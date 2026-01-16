using System.Security.Claims;

namespace Platerly.Api.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Safely extracts the user ID from the ClaimsPrincipal.
        /// Returns null if the claim is missing or invalid.
        /// </summary>
        public static Guid? GetUserIdOrNull(this ClaimsPrincipal user)
        {
            var claim = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim))
                return null;

            return Guid.TryParse(claim, out var userId) ? userId : null;
        }

        /// <summary>
        /// Extracts the user ID from the ClaimsPrincipal.
        /// Throws InvalidOperationException if the claim is missing or invalid.
        /// Use this only when you're certain the user is authenticated.
        /// </summary>
        public static Guid GetUserId(this ClaimsPrincipal user)
        {
            var userId = user.GetUserIdOrNull();
            if (userId == null)
                throw new InvalidOperationException("User ID claim is missing or invalid.");
            return userId.Value;
        }

        /// <summary>
        /// Tries to get the user ID from the ClaimsPrincipal.
        /// Returns true if successful, false otherwise.
        /// </summary>
        public static bool TryGetUserId(this ClaimsPrincipal user, out Guid userId)
        {
            var result = user.GetUserIdOrNull();
            if (result.HasValue)
            {
                userId = result.Value;
                return true;
            }
            userId = Guid.Empty;
            return false;
        }
    }
}
