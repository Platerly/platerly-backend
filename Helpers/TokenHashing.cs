using System.Security.Cryptography;
using System.Text;

namespace Platerly.Api.Helpers
{
    public static class TokenHashing
    {
        public static string Hash(string token)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes); // uppercase hex
        }

        public static string NewRandomToken(int bytes = 64)
        {
            var data = RandomNumberGenerator.GetBytes(bytes);
            return Convert.ToHexString(data);
        }
    }
}
