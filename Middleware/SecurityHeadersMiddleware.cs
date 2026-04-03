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

            // Content-Security-Policy:
            //   default-src 'self'                  — only same-origin resources by default
            //   script-src  'self'                  — JS only from same origin (no inline, no eval)
            //   style-src   'self' 'unsafe-inline'  — inline styles needed by some ASP.NET helpers
            //   img-src     'self' data: https:     — images from same origin, data URIs, and any HTTPS
            //   font-src    'self' data:             — fonts from same origin or data URIs
            //   connect-src 'self'                  — XHR/fetch only to same origin
            //   frame-ancestors 'none'               — equivalent to X-Frame-Options: DENY
            //   form-action 'self'                  — form posts only to same origin
            //   base-uri    'self'                  — prevent base-tag injection
            //
            // Adjust script-src / connect-src if you add CDN assets or third-party APIs
            // that are loaded client-side (Stripe.js, Google Fonts, etc.) — those belong in
            // the SPA's own CSP, not here.  This header covers the API's own HTML responses
            // (Swagger UI, OG share pages, health endpoint).
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: https:; " +
                "font-src 'self' data:; " +
                "connect-src 'self'; " +
                "frame-ancestors 'none'; " +
                "form-action 'self'; " +
                "base-uri 'self'";

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
