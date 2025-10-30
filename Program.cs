using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Platerly.Api.Data;
using Platerly.Api.Helpers;
using Platerly.Api.Models;
using Platerly.Api.Seeding;
using Platerly.Api.Services;
using System.Text;
using Stripe;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Contracts.Billing;

var builder = WebApplication.CreateBuilder(args);

// ----------------------
// 🔑 STRIPE CONFIG
// ----------------------
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// ----------------------
// 🧩 SERVICES
// ----------------------
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
              .AllowCredentials(); // <— required for HttpOnly refresh cookie
    });
});

// JWT bearer auth (access tokens)
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // For local dev you can set RequireHttpsMetadata = false; keep true in prod
        // options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)
            )
        };
    });

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
                Reference = new OpenApiReference
                { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            new string[] { }
        }
    });
});

// EF Core
builder.Services.AddDbContext<PlaterlyDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// DI
builder.Services.AddScoped<JwtTokenGenerator>();
builder.Services.AddScoped<IBlobService, BlobService>();
builder.Services.AddScoped<AuthCookieService>(); // <— for HttpOnly refresh cookie

builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("SendGrid"));
builder.Services.AddScoped<IEmailSender, SendGridEmailSender>();
builder.Services.AddSingleton<IAllergenResolver, AllergenResolver>();
builder.Services.AddHostedService<TrendingRecomputeService>();

var app = builder.Build();

// ----------------------
// 🌐 MIDDLEWARE PIPELINE
// ----------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// CORS BEFORE auth
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
