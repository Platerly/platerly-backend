using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Platerly.Api.Data;
using Platerly.Api.Helpers;
using Platerly.Api.Models;
using Platerly.Api.Services;
using Platerly.Api.Services.Sitemap;
using Stripe;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 🔑 Stripe
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// 🧩 Services
builder.Services.AddControllers();

// CORS: allow credentialed requests from the SPA
var allowedOrigins = new[]
{
    "http://localhost:5173",
    "https://platerly.com",
    "https://www.platerly.com"
};

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// JWT bearer auth (access tokens)
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var key = builder.Configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Jwt:Key is not configured.");
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            // Always validate signature + expiry
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,

            // Relax issuer/audience for now (you can tighten later)
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"]
        };

        // 🔴 NEW: pull JWT from access-token cookie if Authorization header is missing
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
    });
builder.Services.AddScoped<ISitemapService, SitemapService>();

builder.Services.AddAuthorization();

// Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Platerly.Api", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// EF Core
builder.Services.AddDbContext<PlaterlyDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// DI
builder.Services.AddScoped<JwtTokenGenerator>();
builder.Services.AddScoped<IBlobService, BlobService>();
builder.Services.AddScoped<AuthCookieService>();

builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("SendGrid"));
builder.Services.AddScoped<IEmailSender, SendGridEmailSender>();
builder.Services.AddSingleton<IAllergenResolver, AllergenResolver>();
builder.Services.AddHostedService<TrendingRecomputeService>();

var app = builder.Build();

// 🌐 Middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Ensure host honors SameSite=None cookies when configured
app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.None, // allows cross-site refresh cookie if you set SameSite=None
    Secure = CookieSecurePolicy.Always
});

// CORS BEFORE auth
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
