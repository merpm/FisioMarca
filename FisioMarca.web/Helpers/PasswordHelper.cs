using System.Security.Cryptography;
using System.Text;

namespace FisioMarca.Helpers
{
    public static class PasswordHelper
    {
        public static string Hash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }
    }
}
