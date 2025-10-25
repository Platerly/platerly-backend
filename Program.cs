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
using Stripe.Checkout;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Contracts.Billing;

// ... same usings ...

var builder = WebApplication.CreateBuilder(args);

// StripeConfiguration
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// Add services
builder.Services.AddControllers();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
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
        { new OpenApiSecurityScheme { Reference = new OpenApiReference
            { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, new string[] {} }
    });
});

builder.Services.AddDbContext<PlaterlyDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<JwtTokenGenerator>();
builder.Services.AddScoped<IBlobService, BlobService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "https://www.platerly.com", "https://platerly.com")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("SendGrid"));
builder.Services.AddScoped<IEmailSender, SendGridEmailSender>();
builder.Services.AddSingleton<IAllergenResolver, AllergenResolver>();
builder.Services.AddHostedService<TrendingRecomputeService>();


var app = builder.Build();


// Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ----------------------
// 🔌 STRIPE ENDPOINTS
// ----------------------

// Create Checkout Session
app.MapPost("/billing/create-checkout-session",
([FromBody] CreateCheckoutRequest req, IConfiguration cfg) =>
{
    var domain = "http://localhost:5173";
    var options = new SessionCreateOptions
    {
        Mode = "subscription",
        SuccessUrl = $"{domain}/account?sub=success",
        CancelUrl = $"{domain}/account?sub=cancel",
        LineItems = new List<SessionLineItemOptions>
        {
            new SessionLineItemOptions { Price = cfg["Stripe:PriceIdPremium"], Quantity = 1 }
        },
        ClientReferenceId = req.UserId
    };
    var session = new SessionService().Create(options);
    return Results.Ok(new { url = session.Url });
});

// Webhook
app.MapPost("/stripe/webhook", async (HttpRequest req, IConfiguration cfg, PlaterlyDbContext db) =>
{
    var log = app.Logger;
    var json = await new StreamReader(req.Body).ReadToEndAsync();
    var sig = req.Headers["Stripe-Signature"].ToString();
    var wh = cfg["Stripe:WebhookSecret"];

    if (string.IsNullOrWhiteSpace(json)) return Results.BadRequest("Empty");
    if (string.IsNullOrWhiteSpace(sig)) return Results.BadRequest("NoSig");
    if (string.IsNullOrWhiteSpace(wh)) return Results.BadRequest("NoSecret");

    Event evt;
    try
    {
        // disable version mismatch check for local dev
        evt = EventUtility.ConstructEvent(json, sig, wh, throwOnApiVersionMismatch: false);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Invalid Stripe signature");
        return Results.BadRequest("BadSig");
    }

    try
    {
        switch (evt.Type)
        {
            case "checkout.session.completed":
                var s = (Stripe.Checkout.Session)evt.Data.Object;
                log.LogInformation("checkout.session.completed: customer={Customer}, subscription={Sub}, ref={Ref}",
                    s.CustomerId, s.SubscriptionId, s.ClientReferenceId);

                if (Guid.TryParse(s.ClientReferenceId, out _))
                {
                    await SetUserPremiumAsync(db, s.ClientReferenceId!, s.CustomerId ?? s.Customer?.Id, s.SubscriptionId ?? s.Subscription?.Id, "active");
                }
                break;

            case "customer.subscription.updated":
                var sub = (Subscription)evt.Data.Object;
                log.LogInformation("customer.subscription.updated: {Status}", sub.Status);
                await SyncSubscriptionAsync(db, sub.CustomerId, sub.Id, sub.Status);
                break;

            case "customer.subscription.deleted":
                var subDel = (Subscription)evt.Data.Object;
                log.LogInformation("customer.subscription.deleted");
                await SetPremiumStatusByCustomerAsync(db, subDel.CustomerId, "canceled");
                break;

            case "invoice.payment_failed":
                var inv = (Invoice)evt.Data.Object;
                log.LogInformation("invoice.payment_failed");
                await SetPremiumStatusByCustomerAsync(db, inv.CustomerId, "past_due");
                break;

            default:
                log.LogInformation("Unhandled event type: {Type}", evt.Type);
                break;
        }
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Error processing webhook {Type}", evt.Type);
        // Swallow error so Stripe always sees 200
    }

    return Results.Ok();
});

// Helpers (safe versions)
static async Task SetUserPremiumAsync(PlaterlyDbContext db, string userIdString, string customerId, string? subId, string status)
{
    if (!Guid.TryParse(userIdString, out var userId)) return;
    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
    if (user is null) return;

    user.IsPremium = status == "active";
    user.PremiumStatus = status;
    user.PremiumSince ??= DateTimeOffset.UtcNow;
    user.StripeCustomerId = customerId;
    user.StripeSubscriptionId = subId;
    await db.SaveChangesAsync();
}

static async Task SyncSubscriptionAsync(PlaterlyDbContext db, string customerId, string subId, string status)
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == customerId);
    if (user is null) return;

    user.StripeSubscriptionId = subId;
    user.PremiumStatus = status;
    user.IsPremium = status == "active";
    await db.SaveChangesAsync();
}

static async Task SetPremiumStatusByCustomerAsync(PlaterlyDbContext db, string customerId, string status)
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == customerId);
    if (user is null) return;

    user.PremiumStatus = status;
    user.IsPremium = status == "active";
    await db.SaveChangesAsync();
}

app.Run();
