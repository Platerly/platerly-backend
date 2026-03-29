namespace Platerly.Api.Middleware
{
    /// <summary>
    /// Injects the mandatory safety disclaimer as an HTTP response header on EVERY response
    /// without exception, satisfying the Phase 2 requirement for universal disclaimer injection.
    ///
    /// Full disclaimer text is also available in the SafetyMeta.Disclaimer field on every
    /// safety-layer endpoint response body.
    /// </summary>
    public class SafetyDisclaimerMiddleware
    {
        private const string HeaderName = "X-Safety-Disclaimer";
        private const string DisclaimerValue =
            "Allergen and nickel data is for informational purposes only and does not constitute " +
            "medical advice. Source: Rebelytics R&D Inc. v9.1.1 (2025), 257 studies, 20575 measurements. " +
            "Consult a healthcare professional for allergy management decisions. " +
            "Recipzee/Platerly Inc. accepts no liability for adverse reactions from reliance on this data.";

        private readonly RequestDelegate _next;

        public SafetyDisclaimerMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Register callback so the header is set before the response body is written.
            context.Response.OnStarting(() =>
            {
                if (!context.Response.Headers.ContainsKey(HeaderName))
                    context.Response.Headers[HeaderName] = DisclaimerValue;

                return Task.CompletedTask;
            });

            await _next(context);
        }
    }

    public static class SafetyDisclaimerMiddlewareExtensions
    {
        public static IApplicationBuilder UseSafetyDisclaimer(this IApplicationBuilder app)
            => app.UseMiddleware<SafetyDisclaimerMiddleware>();
    }
}
