namespace Platerly.Api.Exceptions
{
    /// <summary>
    /// Thrown when a user tries to access a premium feature without a premium subscription.
    /// </summary>
    public class PremiumRequiredException : Exception
    {
        /// <summary>
        /// A machine-readable code identifying the specific premium limit hit
        /// </summary>
        public string LimitCode { get; }

        public PremiumRequiredException(string message, string limitCode)
            : base(message)
        {
            LimitCode = limitCode;
        }

        public PremiumRequiredException(string message, string limitCode, Exception innerException)
            : base(message, innerException)
        {
            LimitCode = limitCode;
        }
    }
}
