namespace Platerly.Api.Middleware
{
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Prevent MIME type sniffing
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";

            // Prevent clickjacking
            context.Response.Headers["X-Frame-Options"] = "DENY";

            // XSS protection (legacy browsers)
            context.Response.Headers["X-XSS-Protection"] = "1; mode=block";

            // Control referrer information
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Permissions Policy (formerly Feature-Policy)
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // HSTS - only in production (tells browsers to always use HTTPS)
            var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
            if (!env.IsDevelopment())
            {
                // max-age=31536000 = 1 year, includeSubDomains for all subdomains
                context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            await _next(context);
        }
    }

    public static class SecurityHeadersMiddlewareExtensions
    {
        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        {
            return app.UseMiddleware<SecurityHeadersMiddleware>();
        }
    }
}
