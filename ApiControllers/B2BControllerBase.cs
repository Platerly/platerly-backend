using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// Abstract base class for all B2B API controllers.
    ///
    /// Authentication:
    ///   B2B controllers use <c>ApiKeyAuthenticationOptions.DefaultScheme</c> — a custom
    ///   <c>ApiKeyAuthenticationHandler</c> that reads the API key from the
    ///   <c>X-Api-Key</c> request header and populates the following claims on success:
    ///     <c>ApiKeyId</c>    — the Guid of the resolved API key record.
    ///     <c>ApiKeyScope</c> — one claim per scope assigned to the key (e.g. "reports").
    ///   JWT bearer tokens (<c>[Authorize]</c> without scheme) are NOT accepted on B2B endpoints;
    ///   the two auth schemes are completely separate.
    ///
    /// Phase 2 safety layer:
    ///   All successful B2B responses must go through <see cref="SafetyOk{T}"/> to be wrapped
    ///   in a <see cref="SafetyApiResponse{T}"/> envelope that contains a <c>Safety</c> block
    ///   (disclaimer, confidence score, data freshness, contextual alerts).
    ///   Individual controllers append domain-specific alerts before calling SafetyOk.
    ///
    /// Error format:
    ///   <see cref="ApiError"/> returns RFC 7807-inspired structured errors that match the shape
    ///   produced by GlobalExceptionMiddleware, ensuring all 4xx and 5xx responses have the same
    ///   <c>{ traceId, status, title, detail }</c> shape.
    ///
    /// Scope-gating (Professional tier):
    ///   Use <see cref="HasScope"/> to gate endpoints behind a named scope. Scope names are
    ///   case-insensitive. Return <see cref="ApiError"/> with status 403 when scope is missing.
    /// </summary>
    public abstract class B2BControllerBase : ControllerBase
    {
        protected readonly ISafetyMetaService _safetyMeta;

        protected B2BControllerBase(ISafetyMetaService safetyMeta)
        {
            _safetyMeta = safetyMeta;
        }

        /// <summary>
        /// Wraps a successful payload in the standard SafetyApiResponse envelope.
        /// All B2B success responses must go through this method.
        /// </summary>
        protected OkObjectResult SafetyOk<T>(T data)
        {
            return base.Ok(new SafetyApiResponse<T>
            {
                Data = data,
                Safety = _safetyMeta.BuildBaseline()
            });
        }

        /// <summary>
        /// Returns a structured error response matching the RFC 7807 format
        /// used by GlobalExceptionMiddleware, ensuring consistent error shapes
        /// for both 4xx and 5xx responses.
        /// </summary>
        protected ObjectResult ApiError(string detail, int statusCode = 400)
        {
            var title = statusCode switch
            {
                400 => "Bad Request",
                401 => "Unauthorized",
                403 => "Forbidden",
                404 => "Resource Not Found",
                409 => "Conflict",
                422 => "Unprocessable Entity",
                _ => "An error occurred."
            };

            return StatusCode(statusCode, new ErrorResponse
            {
                TraceId = HttpContext.TraceIdentifier,
                Status = statusCode,
                Title = title,
                Detail = detail
            });
        }

        /// <summary>
        /// Extracts the API key ID from claims populated by ApiKeyAuthenticationHandler.
        /// Returns Guid.Empty if the claim is absent (should not happen on authenticated routes).
        /// </summary>
        protected Guid GetApiKeyId()
        {
            var claim = User.FindFirst("ApiKeyId");
            return claim != null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
        }

        /// <summary>
        /// Returns true when the authenticated API key includes the given scope.
        /// Scope names are case-insensitive. Use this to gate Professional-tier endpoints.
        /// </summary>
        protected bool HasScope(string scope) =>
            User.FindAll("ApiKeyScope")
                .Any(c => string.Equals(c.Value, scope, StringComparison.OrdinalIgnoreCase));
    }
}
