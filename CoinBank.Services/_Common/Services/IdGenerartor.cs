using System.Security.Cryptography;

namespace CoinBank.Services._Common.Services
{
    public static class IdGenerartor
    { 
        public static string GenerateBytes32HexId()
        {
            var buffer = new byte[32];
            RandomNumberGenerator.Fill(buffer);

            var newId = BitConverter.ToString(buffer)
                .Replace("-", "")
                .ToLowerInvariant();

            return "0x" + newId;
        }
    }
}
