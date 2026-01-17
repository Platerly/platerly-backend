using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Platerly.Api.Data;
using Platerly.Api.Helpers;
using Platerly.Api.Models;
using Platerly.Api.Services;
using Platerly.Api.Services.Sitemap;
using Platerly.Api.Services.Allergy;
using Platerly.Api.Services.Ingredients;
using Platerly.Api.Services.Notifications;
using Platerly.Api.Services.Admin;
using Platerly.Api.Services.Shopping;
using Platerly.Api.Services.ApiKeys;
using Platerly.Api.Middleware;
using Stripe;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);

// health check
builder.Services.AddHealthChecks();

// 🔑 Stripe
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// 🧩 Services
builder.Services.AddControllers();

// CORS: allow credentialed requests from the SPA
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "https://platerly.com", "https://www.platerly.com" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .WithHeaders(
                  "Content-Type",
                  "Authorization",
                  "X-Requested-With",
                  "Accept",
                  "Origin",
                  "X-API-Key" // For B2B API key authentication
              )
              .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS")
              .AllowCredentials();
    });
});

// JWT bearer auth (access tokens) + API Key auth for B2B
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var key = builder.Configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Jwt:Key is not configured.");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,

            ValidateIssuer = true,
            ValidateAudience = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"]
        };

        // 🔴 pull JWT from access-token cookie if Authorization header is missing
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token))
                {
                    if (context.Request.Cookies.TryGetValue("platerly.a", out var cookieToken) &&
                        !string.IsNullOrEmpty(cookieToken))
                    {
                        context.Token = cookieToken;
                    }
                }

                return Task.CompletedTask;
            }
        };
    })
    .AddApiKeyAuthentication(); // API Key auth for B2B customers

builder.Services.AddAuthorization();

// Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Platerly.Api", Version = "v1" });

    // JWT Bearer auth
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    // API Key auth for B2B
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API Key for B2B access. Use X-API-Key header or 'ApiKey {key}' in Authorization header.",
        Name = "X-API-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            Array.Empty<string>()
        }
    });
});

// ✅ EF Core (Scoped DbContext)
// IMPORTANT: Do NOT also register AddDbContextFactory<PlaterlyDbContext>().
builder.Services.AddDbContext<PlaterlyDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// DI
builder.Services.AddHttpClient(); // For IHttpClientFactory (used by AuthController for Google OAuth)
builder.Services.AddMemoryCache();
builder.Services.AddScoped<JwtTokenGenerator>();
builder.Services.AddScoped<IBlobService, BlobService>();
builder.Services.AddScoped<AuthCookieService>();

builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("SendGrid"));
builder.Services.AddScoped<IEmailSender, SendGridEmailSender>();

builder.Services.AddSingleton<IAllergenResolver, AllergenResolver>();
builder.Services.Configure<TrendingConfig>(builder.Configuration.GetSection(TrendingConfig.SectionName));
builder.Services.AddHostedService<TrendingRecomputeService>();
builder.Services.AddScoped<ISitemapService, SitemapService>();

// Allergy alerts
builder.Services.AddScoped<IAllergyAlertService, AllergyAlertService>();

// Notifications
builder.Services.AddScoped<INotificationService, NotificationService>();

// Admin audit logging
builder.Services.AddScoped<IAdminAuditService, AdminAuditService>();

// Billing email orchestrator
builder.Services.AddScoped<BillingEmailOrchestrator>();

// Ingredient validation service (checks for prohibited/haram ingredients)
builder.Services.AddScoped<IIngredientValidationService, IngredientValidationService>();
builder.Services.AddScoped<ProhibitedIngredientSeeder>();
builder.Services.AddScoped<IngredientCategorySeeder>();
builder.Services.AddScoped<IngredientSubstitutionSeeder>();

// Allergy seeders
builder.Services.AddScoped<Platerly.Api.Services.Allergy.AllergySeeder>();
builder.Services.AddScoped<Platerly.Api.Services.Allergy.AllergyLexiconSeeder>();

// Ingredient substitution service (allergy-based substitutions)
builder.Services.AddScoped<IIngredientSubstitutionService, IngredientSubstitutionService>();

// Ingredient usage tracking (B2B analytics)
builder.Services.AddScoped<IIngredientUsageService, IngredientUsageService>();

// B2B Analytics API service
builder.Services.AddScoped<IIngredientAnalyticsService, IngredientAnalyticsService>();

// Shopping list and meal planning
builder.Services.AddScoped<IQuantityParserService, QuantityParserService>();
builder.Services.AddScoped<IShoppingListService, ShoppingListService>();
builder.Services.AddScoped<IMealPlanService, MealPlanService>();

// API Keys for B2B customers
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();

// Recipe seeder for populating database with halal recipes
builder.Services.AddScoped<IRecipeSeederService, RecipeSeederService>();

// Rate limiting (configurable via appsettings.json)
var rateLimitConfig = builder.Configuration.GetSection("RateLimiting");
var generalLimit = rateLimitConfig.GetValue("GeneralPermitLimit", 100);
var anonymousLimit = rateLimitConfig.GetValue("AnonymousPermitLimit", 30);
var authLimit = rateLimitConfig.GetValue("AuthPermitLimit", 10);
var windowMinutes = rateLimitConfig.GetValue("WindowMinutes", 1);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // General API rate limit per IP
    options.AddPolicy("general", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = generalLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                SegmentsPerWindow = 4,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // Stricter limit for anonymous/public endpoints
    options.AddPolicy("anonymous", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = anonymousLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // Very strict limit for auth endpoints (prevent brute force)
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// Response compression for better performance
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/json",
        "application/javascript",
        "text/css",
        "text/plain",
        "image/svg+xml"
    });
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

var app = builder.Build();

// Seed data on startup (safe to call multiple times - skips if already seeded)
using (var scope = app.Services.CreateScope())
{
    var prohibitedSeeder = scope.ServiceProvider.GetRequiredService<ProhibitedIngredientSeeder>();
    await prohibitedSeeder.SeedAsync();

    var categorySeeder = scope.ServiceProvider.GetRequiredService<IngredientCategorySeeder>();
    await categorySeeder.SeedAsync();

    var substitutionSeeder = scope.ServiceProvider.GetRequiredService<IngredientSubstitutionSeeder>();
    await substitutionSeeder.SeedAsync();

    // Seed allergen data (FDA Big 9 + EU 14 + Nickel)
    var allergySeeder = scope.ServiceProvider.GetRequiredService<Platerly.Api.Services.Allergy.AllergySeeder>();
    await allergySeeder.SeedAsync();

    // Seed allergen keyword lexicon (comprehensive ingredient-to-allergen mappings)
    var lexiconSeeder = scope.ServiceProvider.GetRequiredService<Platerly.Api.Services.Allergy.AllergyLexiconSeeder>();
    await lexiconSeeder.SeedAsync();
}

// 🌐 Middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Response compression (Brotli/Gzip)
app.UseResponseCompression();

// Security headers (XSS, clickjacking, MIME sniffing protection)
app.UseSecurityHeaders();

// Request logging - track all requests with timing
app.UseRequestLogging();

// Global exception handler - catches all unhandled exceptions
app.UseGlobalExceptionHandler();

// Rate limiting
app.UseRateLimiter();

app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.None,
    Secure = CookieSecurePolicy.Always
});

// CORS BEFORE auth
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

// API Key middleware (after auth, before controllers)
app.UseApiKeyRateLimiting();
app.UseApiKeyUsageLogging();

app.UseHealthChecks("/health");
app.MapControllers();

app.Run();
