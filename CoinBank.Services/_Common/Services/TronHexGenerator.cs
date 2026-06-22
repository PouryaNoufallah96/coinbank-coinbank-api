using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._Common.Services
{
    public class TronHexGenerator (ILogger<TronHexGenerator> _logger, HttpClient _tronGridHttpClient) : ITronHexGenerator ,ISingletonDependency
    {
        /// <summary>
        /// Resolves the configured TRON swap contract address to its EVM (0x + 20 bytes) hex form
        /// and caches the result so the conversion only happens once.
        /// The hex form is required by the eth_getLogs compatible RPC endpoint.
        /// </summary>
        private async Task<string> GetSwapContractHexAddressAsync(string address ,CancellationToken cancellationToken)
        {      
            var resolved = await ResolveContractHexAddressAsync(address, cancellationToken);

            return resolved;
        }

        private async Task<string> ResolveContractHexAddressAsync(string address, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                _logger.LogWarning("address is not configured");
                return null;
            }

            address = address.Trim();

            // Already an EVM hex address (0x + 40 hex chars)
            if (address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                var hex = address[2..];
                if (hex.Length == 40 && IsHex(hex))
                    return "0x" + hex.ToLowerInvariant();

                _logger.LogWarning("{Prefix} Configured swap contract address '{Address}' is not a valid 20-byte hex address", CommonLogPrefix, address);
                return null;
            }

            // TRON hex form (41 + 40 hex chars)
            if (address.Length == 42 && address.StartsWith("41", StringComparison.OrdinalIgnoreCase) && IsHex(address))
                return "0x" + address[2..].ToLowerInvariant();

            // TRON Base58Check form (starts with 'T') -> validate via TronGrid, then convert to EVM hex
            try
            {
                var isValid = await ValidateAddressWithTronGridAsync(address, cancellationToken);
                if (!isValid)
                    _logger.LogWarning("TronGrid could not validate address '{Address}', attempting local conversion", address);

                var evmHex = ConvertTronBase58ToEvmHex(address);

                _logger.LogInformation(" Resolved TRON contract '{Tron}' to EVM hex address '{Hex}'",  address, evmHex);

                return evmHex;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, " Failed to convert TRON contract address '{Address}' to hex", address);
                return null;
            }
        }

        /// <summary>
        /// Uses the TronGrid wallet/validateaddress endpoint to confirm the address is a valid TRON address.
        /// Best-effort: returns false if the call fails so the caller can decide how to proceed.
        /// </summary>
        private async Task<bool> ValidateAddressWithTronGridAsync(string address, CancellationToken cancellationToken)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { address, visible = true });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await _tronGridHttpClient.PostAsync("wallet/validateaddress", content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return false;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("result", out var result))
                {
                    return result.ValueKind == JsonValueKind.True ||
                           (result.ValueKind == JsonValueKind.String &&
                            bool.TryParse(result.GetString(), out var parsed) && parsed);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TronGrid validateaddress call failed for '{Address}'", address);
                return false;
            }
        }

        /// <summary>
        /// Converts a TRON Base58Check address (T...) into an EVM hex address (0x + 20 bytes)
        /// by decoding Base58, validating the checksum and stripping the 0x41 TRON prefix.
        /// </summary>
        private static string ConvertTronBase58ToEvmHex(string base58Address)
        {
            var decoded = Base58Decode(base58Address);

            if (decoded.Length != 25)
                throw new FormatException($"Invalid TRON address length: {decoded.Length}, expected 25 bytes");

            var payload = decoded[..^4];
            var checksum = decoded[^4..];

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(sha256.ComputeHash(payload));

            if (!hash.Take(4).SequenceEqual(checksum))
                throw new FormatException("Invalid TRON address checksum");

            if (payload.Length != 21 || payload[0] != 0x41)
                throw new FormatException("Invalid TRON address payload");

            var addressBytes = payload[1..];
            return "0x" + Convert.ToHexString(addressBytes).ToLowerInvariant();
        }

        private static byte[] Base58Decode(string input)
        {
            const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

            BigInteger value = 0;
            foreach (var c in input)
            {
                var digit = alphabet.IndexOf(c);
                if (digit < 0)
                    throw new FormatException($"Invalid Base58 character '{c}'");

                value = value * 58 + digit;
            }

            var leadingZeros = 0;
            foreach (var c in input)
            {
                if (c == '1')
                    leadingZeros++;
                else
                    break;
            }

            var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            var result = new byte[leadingZeros + bytes.Length];
            Array.Copy(bytes, 0, result, leadingZeros, bytes.Length);

            return result;
        }

        private static bool IsHex(string value) => value.All(Uri.IsHexDigit);
    }
}
