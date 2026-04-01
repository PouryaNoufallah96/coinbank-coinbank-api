using Nethereum.Signer;
using System.Text;
using Utilities.Exceptions.Common;

namespace CoinBank.Services._User
{
    public static class TronAddressHelper
    {

        /// <summary>
        /// Recovers a Tron address from a signed message and its signature.
        /// </summary>
        /// <param name="message">The original signed message.</param>
        /// <param name="signatureHex">The signature in hexadecimal format.</param>
        /// <returns>The recovered Tron address in Base58Check format.</returns>
        /// <exception cref="BadRequestException">Thrown when the address recovery fails.</exception>

        public static string RecoverTronAddress(string message, string signatureHex)
        {
            var signer = new EthereumMessageSigner();

            var recoveredEthAddress = signer.EncodeUTF8AndEcRecover(message, signatureHex);

            if (string.IsNullOrWhiteSpace(recoveredEthAddress))
                throw new BadRequestException("Failed to recover address");

            return ConvertHexToTronAddress(recoveredEthAddress);
        }


        /// <summary>
        /// Converts an Ethereum hex address into a Tron Base58Check encoded address.
        /// </summary>
        /// <param name="hexAddress">The Ethereum hex address.</param>
        /// <returns>The corresponding Tron address.</returns>
        /// <exception cref="BadRequestException">Thrown when the address length is invalid.</exception>

        public static string ConvertHexToTronAddress(string hexAddress)
        {
            if (hexAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hexAddress = hexAddress[2..];

            var addressBytes = Convert.FromHexString(hexAddress);

            if (addressBytes.Length != 20)
                throw new BadRequestException($"Invalid address length: {addressBytes.Length}, expected 20");

            var tronAddress = new byte[21];
            tronAddress[0] = 0x41;
            Array.Copy(addressBytes, 0, tronAddress, 1, 20);

            return Base58CheckEncode(tronAddress);
        }


        /// <summary>
        /// Encodes a byte array using Base58Check encoding (Base58 + checksum).
        /// </summary>
        /// <param name="input">The input byte array.</param>
        /// <returns>The Base58Check encoded string.</returns>

        public static string Base58CheckEncode(byte[] input)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();

            var hash1 = sha256.ComputeHash(input);
            var hash2 = sha256.ComputeHash(hash1);
            var checksum = hash2.Take(4).ToArray();

            var combined = input.Concat(checksum).ToArray();

            return Base58Encode(combined);
        }


        /// <summary>
        /// Encodes a byte array into a Base58 string.
        /// </summary>
        /// <param name="input">The input byte array.</param>
        /// <returns>The Base58 encoded string.</returns>

        public static string Base58Encode(byte[] input)
        {
            const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

            int leadingZeros = 0;
            foreach (var b in input)
            {
                if (b == 0x00)
                    leadingZeros++;
                else
                    break;
            }

            var bytes = input.Reverse().ToArray();
            var value = new System.Numerics.BigInteger(bytes.Concat(new byte[] { 0 }).ToArray());

            var result = new StringBuilder();

            while (value > 0)
            {
                value = System.Numerics.BigInteger.DivRem(value, 58, out var remainder);
                result.Insert(0, alphabet[(int)remainder]);
            }

            for (int i = 0; i < leadingZeros; i++)
            {
                result.Insert(0, alphabet[0]);
            }

            return result.Length == 0 ? alphabet[0].ToString() : result.ToString();
        }
    }
}



//private string PrepareMessageForSigning(string originalMessage)
//{
//    var messageBytes = Encoding.UTF8.GetBytes(originalMessage);
//    var prefix = Encoding.UTF8.GetBytes($"\x19Ethereum Signed Message:\n{messageBytes.Length}");
//    return Encoding.UTF8.GetString(prefix.Concat(messageBytes).ToArray());
//}


///// <summary>
///// Verifies a Tron signature using TronGrid API
///// </summary>
//private async Task<bool> VerifyTronSignatureWithApi(string message, string signatureHex, string walletAddress)
//{
//    try
//    {
//        using (var httpClient = new HttpClient())
//        {
//            httpClient.Timeout = TimeSpan.FromSeconds(10);
//            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

//            // Tron requires the message to be in hex format and hashed with Keccak256
//            // Reference: https://developers.tron.network/reference/verifymessage [citation:7]
//            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
//            byte[] hashedMessage = ComputeKeccak256Hash(messageBytes);
//            string hexMessage = "0x" + BitConverter.ToString(hashedMessage).Replace("-", "").ToLower();

//            var requestBody = new
//            {
//                message = hexMessage,
//                signature = signatureHex,
//                address = walletAddress
//            };

//            var json = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
//            var content = new StringContent(json, Encoding.UTF8, "application/json");

//            // TronGrid API endpoint for signature verification
//            var response = await httpClient.PostAsync("https://api.trongrid.io/wallet/verifymessage", content);

//            if (response.IsSuccessStatusCode)
//            {
//                var result = await response.Content.ReadAsStringAsync();
//                dynamic jsonResponse = Newtonsoft.Json.JsonConvert.DeserializeObject(result);

//                // Check if the signature is valid
//                // API returns: { "result": true/false } or { "valid": true/false }
//                bool isValid = jsonResponse.result ?? jsonResponse.valid ?? false;
//                return isValid;
//            }

//            return false;
//        }
//    }
//    catch
//    {
//        return false;
//    }
//}
//private byte[] ComputeKeccak256Hash(byte[] input)
//{
//    var keccak = new KeccakDigest(256);
//    var output = new byte[32];
//    keccak.BlockUpdate(input, 0, input.Length);
//    keccak.DoFinal(output, 0);
//    return output;
//}


//=============================================================



//private string RecoverTronAddress(string message, string signatureHex)
//{
//    try
//    {
//        // 1. ساخت پیام با فرمت TRON استاندارد
//        var messageBytes = Encoding.UTF8.GetBytes(message);
//        var prefix = $"\x19TRON Signed Message:\n{messageBytes.Length}";
//        var prefixBytes = Encoding.UTF8.GetBytes(prefix);

//        var finalMessage = prefixBytes.Concat(messageBytes).ToArray();

//        // 2. هش کردن با Keccak256
//        var hash = Keccak256Hash(finalMessage);

//        // 3. اعتبارسنجی امضا با استفاده از کتابخانه Nethereum
//        var signer = new EthereumMessageSigner();

//        // توجه: EcRecover هش را به همراه signature گرفته و آدرس را برمی‌گرداند
//        var recoveredAddressHex = signer.EcRecover(hash, signatureHex);

//        if (string.IsNullOrWhiteSpace(recoveredAddressHex))
//            throw new BadRequestException("Failed to recover address");

//        // 4. تبدیل آدرس هگزادسیمال به فرمت TRON Base58
//        return ConvertHexToTronAddress(recoveredAddressHex);
//    }
//    catch (Exception ex)
//    {
//        throw new BadRequestException($"TRON signature recovery failed: {ex.Message}");
//    }
//}



//private byte[] Keccak256Hash(byte[] input)
//{
//    var keccak = new Org.BouncyCastle.Crypto.Digests.KeccakDigest(256);
//    keccak.BlockUpdate(input, 0, input.Length);

//    var result = new byte[32];
//    keccak.DoFinal(result, 0);
//    return result;
//}



//try
//{
//    // گام 1: اضافه کردن پیشوند مخصوص TRON (TIP-191)
//    // فرمت: "\x19TRON Signed Message:\n" + طول پیام + محتوای پیام
//    var messageBytes = Encoding.UTF8.GetBytes(message);
//    var prefix = Encoding.UTF8.GetBytes($"\x19TRON Signed Message:\n{messageBytes.Length}");

//    var prefixedMessage = new byte[prefix.Length + messageBytes.Length];
//    Buffer.BlockCopy(prefix, 0, prefixedMessage, 0, prefix.Length);
//    Buffer.BlockCopy(messageBytes, 0, prefixedMessage, prefix.Length, messageBytes.Length);

//    // گام 2: اعمال Keccak256 روی پیام با پیشوند
//    var sha3 = new Sha3Keccack();
//    var hash = sha3.CalculateHash(prefixedMessage);

//    // گام 3: بازیابی کلید عمومی از امضا
//    var signature = EthECDSASignatureFactory.ExtractECDSASignature(signatureHex);
//    var publicKey = EthECKey.RecoverFromSignature(signature, hash);

//    if (publicKey == null)
//        throw new BadRequestException("Failed to recover public key from signature");

//    // گام 4: تبدیل کلید عمومی به آدرس TRON
//    var publicKeyBytes = publicKey.GetPubKey();
//    var recoveredAddress = TronAddressFromPublicKey(publicKeyBytes);

//    // گام 5: مقایسه آدرس‌ها
//    var isVerified = string.Equals(recoveredAddress, walletAddress, StringComparison.OrdinalIgnoreCase);
//    if (!isVerified)
//        throw new BadRequestException("Invalid signature for Tron wallet");
//}
//catch (Exception ex) when (ex is BadRequestException)
//{
//    throw;
//}
//catch (Exception ex)
//{
//    throw new BaseException($"Error in Tron signature verification: {ex.Message}");
//}












///// <summary>
///// Verifies that the provided cryptographic signature matches the given wallet address for the specified message.
///// </summary>
///// <param name="message">The original message that was signed by the wallet.</param>
///// <param name="signatureHex">The hexadecimal signature generated by the wallet for the message.</param>
///// <param name="walletAddress">The expected wallet address that allegedly signed the message.</param>
///// <exception cref="BadRequestException">
///// Thrown if:
///// <list type="bullet">
///// <item>The message, signature, or wallet address is null, empty, or whitespace.</item>
///// <item>The recovered address from the signature does not match the provided wallet address.</item>
///// </list>
///// </exception>
///// <exception cref="BaseException">
///// Thrown if there is an error during the Web3/Ethereum signature recovery process.
///// </exception>
///// <remarks>
///// This method uses <see cref="EthereumMessageSigner"/> from the Nethereum library to recover the address
///// from the provided message and signature. It ensures that the signature is valid for the given wallet
///// address according to the Ethereum (EVM) signing standard.
///// </remarks>
//private void VerifySignature(string message, string signatureHex, string walletAddress, WalletType walletType)
//{
//    if (string.IsNullOrWhiteSpace(message) ||
//        string.IsNullOrWhiteSpace(signatureHex) ||
//        string.IsNullOrWhiteSpace(walletAddress))
//        throw new BadRequestException("invalid input!");

//    if (walletType == WalletType.EVM)
//    {
//        try
//        {
//            var signer = new EthereumMessageSigner();
//            var recovered = signer.EncodeUTF8AndEcRecover(message, signatureHex);

//            var isVerified = string.Equals(recovered, walletAddress, StringComparison.OrdinalIgnoreCase);
//            if (!isVerified) throw new BadRequestException("Invalid signature for wallet");
//        }
//        catch
//        {
//            throw new BaseException("error in web3 network!");
//        }
//    }
//    else if (walletType == WalletType.TRON)
//    {

//        try
//        {
//            // Step 1: Format message according to Tron standard (similar to Ethereum but with "TRON" prefix)
//            const string TRON_MESSAGE_PREFIX = "\u0019TRON Signed Message:\n";
//            var messageWithPrefix = $"{TRON_MESSAGE_PREFIX}{message.Length}{message}";
//            var messageBytes = Encoding.UTF8.GetBytes(messageWithPrefix);

//            // Step 2: Apply Keccak256 hash
//            var sha3 = new Sha3Keccack();
//            var hash = sha3.CalculateHash(messageBytes);

//            // Step 3: Recover public key from signature
//            var signature = EthECDSASignatureFactory.ExtractECDSASignature(signatureHex);
//            var publicKey = EthECKey.RecoverFromSignature(signature, hash);

//            if (publicKey == null)
//                throw new BadRequestException("Failed to recover public key from signature");

//            // Step 4: Convert public key to Tron address
//            var publicKeyBytes = publicKey.GetPubKey(); // Get uncompressed public key (65 bytes with 0x04 prefix)
//            var recoveredAddress = TronAddressFromPublicKey(publicKeyBytes);

//            // Step 5: Compare addresses
//            var isVerified = string.Equals(recoveredAddress, walletAddress, StringComparison.OrdinalIgnoreCase);
//            if (!isVerified)
//                throw new BadRequestException("Invalid signature for Tron wallet");
//        }
//        catch (Exception ex) when (ex is BadRequestException)
//        {
//            throw;
//        }
//        catch (Exception ex)
//        {
//            throw new BaseException($"Error in Tron signature verification: {ex.Message}");
//        }
//    }
//    else throw new BadRequestException("Invalid wallet type");
//}



//private string TronAddressFromPublicKey(byte[] publicKeyBytes)
//{
//    // Remove the first byte (0x04) from uncompressed public key
//    // Uncompressed public key starts with 0x04 followed by X (32 bytes) and Y (32 bytes)
//    var publicKeyWithoutPrefix = new byte[publicKeyBytes.Length - 1];
//    Array.Copy(publicKeyBytes, 1, publicKeyWithoutPrefix, 0, publicKeyWithoutPrefix.Length);

//    // Apply Keccak256 hash
//    var sha3 = new Sha3Keccack();
//    var hash = sha3.CalculateHash(publicKeyWithoutPrefix);

//    // Take the last 20 bytes for the address and add Tron prefix (0x41)
//    var addressBytes = new byte[21];
//    addressBytes[0] = 0x41; // Tron address prefix
//    Array.Copy(hash, hash.Length - 20, addressBytes, 1, 20);

//    // Convert to Base58 format
//    return TronBase58Encode(addressBytes);
//}

//private string TronBase58Encode(byte[] addressBytes)
//{
//    // For .NET 5+ or .NET Core
//    var hash1 = SHA256.HashData(addressBytes);
//    var hash2 = SHA256.HashData(hash1);

//    // Add 4-byte checksum
//    var checksum = new byte[4];
//    Array.Copy(hash2, 0, checksum, 0, 4);

//    var combined = new byte[addressBytes.Length + checksum.Length];
//    Array.Copy(addressBytes, 0, combined, 0, addressBytes.Length);
//    Array.Copy(checksum, 0, combined, addressBytes.Length, checksum.Length);

//    // Convert to Base58
//    return Base58Encode(combined);
//}

//private string Base58Encode(byte[] input)
//{
//    const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
//    var result = new List<char>();
//    var bytes = input.ToArray();
//    int zeroCount = 0;

//    while (zeroCount < bytes.Length && bytes[zeroCount] == 0)
//        zeroCount++;

//    var number = new System.Numerics.BigInteger(1);
//    for (int i = bytes.Length - 1; i >= 0; i--)
//    {
//        number = number * 256 + bytes[i];
//    }

//    while (number > 0)
//    {
//        var remainder = (int)(number % 58);
//        number /= 58;
//        result.Add(alphabet[remainder]);
//    }

//    for (int i = 0; i < zeroCount; i++)
//        result.Add(alphabet[0]);

//    result.Reverse();
//    return new string(result.ToArray());
//}
