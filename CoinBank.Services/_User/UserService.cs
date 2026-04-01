using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._User.DTOs.Results;
using CoinBank.Services._User.DTOs.Settings;
using CoinBank.Services._User.DTOs.Storages;
using CoinBank.Services._User.DTOs.Updates;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Nethereum.Signer;
using Nethereum.Util;
using Org.BouncyCastle.Crypto.Digests;
using System.Security.Claims;
using System.Text;
using Utilities.Constants;
using Utilities.Enums;
using Utilities.Exceptions;
using Utilities.Exceptions.Common;
using Utilities.Services.Contracts;
using Utilities.Utilities;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._User
{
    public class UserService(
    IRandomService _randomService,
    JwtServiceSettings _jwtSettings,
    IJwtService _jwtService,
    ILogger<UserService> _logger,
    IUserRepository _userRepository,
    UserAuthStorage _userAuthStorage) : IUserService, IScopedDependency
    {

        /// <summary>
        /// used for create one time nonce
        /// </summary>
        /// <param name="update"></param>
        /// <param name="ip"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public NonceResult GetNonce(NonceRequest update, string ip)
        {
            ValidateClientInfo(update.ClientId, update.ClientSecret);
            var walletAddress = ValidateAndConvertToChecksumAddress(update.WalletAddress,update.WalletType);
            var walletType = update.WalletType;
            var random = _randomService.GetSecureAlphaNumericString(6);
            var newNonce = random + Guid.NewGuid().ToString("N");
            var newUserAuthData = new UserAuthData
            {
                Nonce = newNonce,
                WalletAddress = walletAddress,
                WalletType = walletType,
                GeneratedMoment = DateTime.UtcNow,
                IP = ip,
            };

            _userAuthStorage.AddItem(newNonce, newUserAuthData);

            return new NonceResult
            {
                ExpireMoment = newUserAuthData.GeneratedMoment.AddMinutes(2),
                Nonce = newNonce,
                Message = $"Please sign this message to authenticate with CoinBank: {newNonce}"
            };
        }


        /// <summary>
        /// this method is for get jwt token
        /// here check the nonce and wallet and signature with  nethereium
        /// throw error if data is not valid
        /// generate jwt token for valid data for login and update nonce storage
        /// </summary>
        /// <param name="update"></param>
        /// <param name="ip"></param>
        /// <returns></returns>
        public async Task<ActionResult> GetToken(NonceVerification update, string ip)
        {
            ValidateClientInfo(update.ClientId, update.ClientSecret);

            var userAuthData = ValidateNonce(update.Nonce, update.WalletAddress, update.WalletType);

            var message = $"Please sign this message to authenticate with CoinBank: {update.Nonce}";
            VerifySignature(message, update.Signature, userAuthData.WalletAddress, update.WalletType);

            var user = await GetOrCreateUserAsync(userAuthData.WalletAddress, update.WalletType);

            _userAuthStorage.RemoveItem(update.Nonce);

            return Authenticate(user);
        }


        /// <summary>
        /// for get user data 
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="NotFoundException"></exception>
        public async Task<GetUserResult> GetUserAsync(string whois)
        {
            var user = await _userRepository.AsQueryable()
                .Where(u => u.UserPublicKey == whois)
                .FirstOrDefaultAsync();

            return user == null
                ? throw new NotFoundException("User Not Found!")
                : new GetUserResult
                {
                    CreateMoment = user.CreatedMoment,
                    EVMWalletAddress = user.EVMWalletAddress,
                    TronWalletAddress = user.TronWalletAddress,
                    LoginHistories = user.LoginDates
                }; 
        }



        /// <summary>
        /// this method is for get jwt token
        /// here check the nonce and wallet and signature with  nethereium
        /// throw error if data is not valid
        /// generate jwt token for valid data for login and update nonce storage
        /// </summary>
        /// <param name="update"></param>
        /// <param name="ip"></param>
        /// <returns></returns>
        public async Task<ActionResult> GetTokenWithPureWalletAddress(GetTokenWithPureWalletAddress update, string ip)
        {
            ValidateClientInfo(update.ClientId, update.ClientSecret);
            var walletAddress = ValidateAndConvertToChecksumAddress(update.WalletAddress,update.WalletType);

            var user = new User
            {
                Status = UserStatus.NotVerified,
                SecurityStamp = "-",
                Role = UserRole.Customer,
                Id = "guess"
            };

            switch (update.WalletType)
            {
                case WalletType.EVM:
                    user.EVMWalletAddress = update.WalletAddress;
                    break;
                case WalletType.TRON:
                    user.TronWalletAddress = update.WalletAddress;
                    break;
                default:
                    throw new BadRequestException("Invalid Wallet type");
            }

            return Authenticate(user);
        }

        /// <summary>
        /// this method use for get user stats in each available stage
        /// </summary>
        /// <param name="userPublicKey"></param>
        /// <param name="walletAddress"></param>
        /// <returns></returns>
        public async Task<GetUserStatsResult> GetUserStatsAsync(string whois)
        {

            var result = new List<GetUserStatsResult>();

            if (whois == "guess") return new GetUserStatsResult
            {
                UserStatus = UserStatus.NotVerified,
            };

            var user = await _userRepository.AsQueryable()
                .Where(q => q.UserPublicKey == whois)
                .FirstOrDefaultAsync();

            if (user != null)
            {
                return new GetUserStatsResult
                {
                    UserStatus = user.Status,
                };
            }

            throw new NotFoundException("User not found!");
        }


        #region Private Methods
        /// <summary>
        /// Validates the provided nonce and associated wallet address.
        /// </summary>
        /// <param name="Nonce">The unique nonce value issued to the user for authentication.</param>
        /// <param name="walletAddress">The wallet address provided by the client.</param>
        /// <returns>The <see cref="UserAuthData"/> associated with the nonce if validation succeeds.</returns>
        /// <exception cref="NonceNotFoundException">Thrown if the nonce does not exist in the storage.</exception>
        /// <exception cref="BadRequestException">
        /// Thrown if the nonce has expired or if the provided wallet address does not match the one stored for this nonce.
        /// </exception>
        /// <remarks>
        /// This method ensures:
        /// 1. The nonce exists in the <see cref="UserAuthStorage"/>.
        /// 2. The nonce has not expired (valid for 10 seconds from creation).
        /// 3. The provided wallet address matches the one originally associated with the nonce.
        /// </remarks>
        private UserAuthData ValidateNonce(string Nonce, string walletAddress, WalletType walletType)
        {
            var userAuthData = _userAuthStorage.GetItem(Nonce) ?? throw new NonceNotFoundException();

            if (DateTime.UtcNow - userAuthData.GeneratedMoment > TimeSpan.FromSeconds(300))
            {
                _userAuthStorage.RemoveItem(Nonce);
                throw new BadRequestException("nonce expired!");
            }

            if (!string.Equals(userAuthData.WalletAddress, walletAddress, StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("Wallet address mismatch for nonce");

            if (userAuthData.WalletType != walletType)
                throw new BadRequestException("Wallet type mismatch for nonce");

            return userAuthData;
        }


        /// <summary>
        /// Verifies that the provided cryptographic signature matches the given wallet address for the specified message.
        /// </summary>
        /// <param name="message">The original message that was signed by the wallet.</param>
        /// <param name="signatureHex">The hexadecimal signature generated by the wallet for the message.</param>
        /// <param name="walletAddress">The expected wallet address that allegedly signed the message.</param>
        /// <exception cref="BadRequestException">
        /// Thrown if:
        /// <list type="bullet">
        /// <item>The message, signature, or wallet address is null, empty, or whitespace.</item>
        /// <item>The recovered address from the signature does not match the provided wallet address.</item>
        /// </list>
        /// </exception>
        /// <exception cref="BaseException">
        /// Thrown if there is an error during the Web3/Ethereum signature recovery process.
        /// </exception>
        /// <remarks>
        /// This method uses <see cref="EthereumMessageSigner"/> from the Nethereum library to recover the address
        /// from the provided message and signature. It ensures that the signature is valid for the given wallet
        /// address according to the Ethereum (EVM) signing standard.
        /// </remarks>
        private void VerifySignature(string message, string signatureHex, string walletAddress, WalletType walletType)
        {
            // 1. Basic validation
            if (string.IsNullOrWhiteSpace(message) ||
                string.IsNullOrWhiteSpace(signatureHex) ||
                string.IsNullOrWhiteSpace(walletAddress))
            {
                throw new BadRequestException("Invalid input!");
            }

            signatureHex = NormalizeHex(signatureHex);
            walletAddress = walletAddress.Trim();

            if (!IsValidSignature(signatureHex))
                throw new BadRequestException("Invalid signature format");

            try
            {
                string recoveredAddress = walletType switch
                {
                    WalletType.EVM => RecoverEvmAddress(message, signatureHex),
                    WalletType.TRON => RecoverTronAddress(message, signatureHex),
                    _ => throw new BadRequestException("Invalid wallet type")
                };

                recoveredAddress = recoveredAddress.Trim();

                if (!string.Equals(recoveredAddress, walletAddress, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BadRequestException("Invalid signature for wallet");
                }
            }
            catch (BadRequestException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new BaseException("Signature verification failed");
            }
        }


        /// <summary>
        /// Recovers the Ethereum address from a signed message and its signature.
        /// </summary>
        /// <param name="message">The original signed message.</param>
        /// <param name="signatureHex">The signature in hexadecimal format.</param>
        /// <returns>The recovered Ethereum address.</returns>
        private string RecoverEvmAddress(string message, string signatureHex)
        {
            var signer = new EthereumMessageSigner();
            return signer.EncodeUTF8AndEcRecover(message, signatureHex);
        }


        /// <summary>
        /// Recovers a Tron address from a signed message and its signature.
        /// </summary>
        /// <param name="message">The original signed message.</param>
        /// <param name="signatureHex">The signature in hexadecimal format.</param>
        /// <returns>The recovered Tron address in Base58Check format.</returns>
        /// <exception cref="BadRequestException">Thrown when the address recovery fails.</exception>
        private string RecoverTronAddress(string message, string signatureHex)
        {
            return TronAddressHelper.RecoverTronAddress(message, signatureHex);
        }


        /// <summary>
        /// Ensures that a hexadecimal string starts with the "0x" prefix.
        /// </summary>
        /// <param name="hex">The hexadecimal string.</param>
        /// <returns>The normalized hexadecimal string with "0x" prefix.</returns>
        private string NormalizeHex(string hex)
        {
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return hex;

            return "0x" + hex;
        }


        /// <summary>
        /// Validates whether a given signature string is a valid hexadecimal signature.
        /// </summary>
        /// <param name="signature">The signature string.</param>
        /// <returns>True if the signature is valid; otherwise, false.</returns>
        private bool IsValidSignature(string signature)
        {
            if (!signature.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return false;

            var hex = signature[2..];

            // Signature length should be 65 or 66 bytes (130 or 132 hex characters)
            return (hex.Length == 130 || hex.Length == 132) && hex.All(c => Uri.IsHexDigit(c));
        }


        /// <summary>
        /// for validate the ClientInformation , OAuth2 verification
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="clientSecret"></param>
        /// <exception cref="BadRequestException"></exception>
        private void ValidateClientInfo(string clientId, string clientSecret)
        {
            if (!clientId.HasValue() ||
                !clientSecret.HasValue() ||
                !_jwtSettings.ClientInfo.ContainsKey(clientId.ToLower()) ||
                !_jwtSettings.ClientInfo[clientId.ToLower()].Equals(clientSecret, StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException(ApiResultStatusCode.OAuth.ToDisplay());
        }


        /// <summary>
        /// this method use for creating jwt
        /// </summary>
        /// <param name="tabletUniqeId"></param>
        /// <param name="tabletData"></param>
        /// <returns></returns>
        private ActionResult Authenticate(User user)
           => new JsonResult(_jwtService.Generate(GetClaimsAsync(user)));


        /// <summary>
        /// for create the cliams of jwt
        /// </summary>
        /// <param name="tabletUniqeId"></param>
        /// <param name="tabletData"></param>
        /// <returns></returns>
        /// <exception cref="BaseException"></exception>
        private IEnumerable<Claim> GetClaimsAsync(User user)
        {
            try
            {
                var claims = new List<Claim>
             {
                 new(Claims.EVMWalletAddress.ToDisplay(),user.EVMWalletAddress ?? "no wallet"),
                 new(Claims.TronWalletAddress.ToDisplay(),user.TronWalletAddress ?? "no wallet"),
                 new(Claims.PublicKey.ToDisplay(),user.UserPublicKey.ToString()),
                 new(Claims.SecurityStamp.ToDisplay(),user.SecurityStamp.ToString()),
                 new(Claims.UserStatus.ToDisplay(),user.Status.ToString()),
                 new(Claims.UserType.ToDisplay(),user.Role == UserRole.Customer ? UserType.User.ToString() : UserType.Admin.ToString()),
             };

                claims.AddRange(user.Permissions.Select(permission =>
                    new Claim(Claims.Permission.ToDisplay(), permission)));

                return claims;
            }
            catch (Exception ex)
            {
                throw new BaseException(ex.Message);
            }
        }


        /// <summary>
        /// for get or create user
        /// if wallet exists in db that means user is exists
        /// if does not exists should create a new user
        /// </summary>
        /// <param name="walletAddress"></param>
        /// <returns></returns>
        private async Task<User> GetOrCreateUserAsync(string walletAddress, WalletType walletType)
        {
            walletAddress = walletAddress.Trim();

            var query = _userRepository.AsQueryable();
            if (walletType == WalletType.EVM)
            {
                query.Where(u => u.EVMWalletAddress.ToLower() == walletAddress.ToLower());
            }
            else if (walletType == WalletType.TRON)
            {
                query.Where(u => u.TronWalletAddress.ToLower() == walletAddress.ToLower());
            }
            else throw new BadRequestException("Invalid wallet type!");

            var user = await query.FirstOrDefaultAsync();

            if (user == null)
            {
                user = new User
                {
                    Role = UserRole.Customer,
                    Permissions = [],
                    UserName = null,
                    PasswordHash = null,
                    Status = UserStatus.Active,
                    LoginDates = []
                };
                if (walletType == WalletType.EVM) user.EVMWalletAddress = walletAddress;
                else if (walletType == WalletType.TRON) user.TronWalletAddress = walletAddress;
                else throw new BadRequestException("Invalid wallet type!");
                await _userRepository.InsertOneAsync(user);
            }

            await AddLoginDateToUser(user);

            return user;
        }


        /// <summary>
        /// for adding last login time, just keep last 20 record
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        /// <exception cref="BaseException"></exception>
        private async Task<Domain.Collections.User> AddLoginDateToUser(Domain.Collections.User user)
        {
            try
            {
                if (user.LoginDates == null || user.LoginDates.Count == 0)
                {
                    user.LoginDates = new List<DateTime> { DateTime.UtcNow };
                    return user;
                }

                user.LoginDates.Add(DateTime.UtcNow);

                var orderedDates = user.LoginDates
                    .OrderByDescending(x => x)
                    .Take(20)
                    .ToList();

                user.LoginDates = orderedDates;

                await _userRepository.ReplaceOneAsync(user);

                return user;
            }
            catch (Exception ex)
            {
                throw new BaseException(ex.Message);
            }

        }

        private string ValidateAndConvertToChecksumAddress(string address, WalletType walletType)
        {
            if (walletType == WalletType.EVM)
            {
                var addressUtil = new AddressUtil();
                if (!addressUtil.IsValidAddressLength(address) || !addressUtil.IsChecksumAddress(address) && !address.ToLower().Equals(address))
                {
                    if (!addressUtil.IsValidEthereumAddressHexFormat(address))
                    {
                        throw new BadRequestException($"Invalid address format: {address}");
                    }
                    return addressUtil.ConvertToChecksumAddress(address);
                }
                return address;
            }
            else if (walletType == WalletType.TRON)
            {
                try
                {
                    var decoded = Base58CheckDecodeForValidateTronAddress(address);

                    if (decoded.Length != 21)
                        throw new BadRequestException($"Invalid address format: {address}");
                    if (decoded[0] != 0x41)
                        throw new BadRequestException($"Invalid address format: {address}");

                    return address;
                }
                catch
                {
                    throw new BadRequestException($"Invalid address format: {address}");
                }
            }
            else throw new BadRequestException("Invalid wallet type");
        }

        public static byte[] Base58CheckDecodeForValidateTronAddress(string input)
        {
            var decoded = Base58DecodeForValidateAddress(input);

            if (decoded.Length < 4)
                throw new Exception("Invalid input");

            var data = decoded.Take(decoded.Length - 4).ToArray();
            var checksum = decoded.Skip(decoded.Length - 4).ToArray();

            using var sha256 = System.Security.Cryptography.SHA256.Create();

            var hash1 = sha256.ComputeHash(data);
            var hash2 = sha256.ComputeHash(hash1);

            var calculatedChecksum = hash2.Take(4).ToArray();

            if (!checksum.SequenceEqual(calculatedChecksum))
                throw new Exception("Invalid checksum");

            return data;
        }
        public static byte[] Base58DecodeForValidateAddress(string input)
        {
            const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

            var value = System.Numerics.BigInteger.Zero;

            foreach (var c in input)
            {
                var index = alphabet.IndexOf(c);
                if (index < 0)
                    throw new Exception("Invalid Base58 character");

                value = value * 58 + index;
            }

            var bytes = value.ToByteArray().Reverse().ToArray();

            // Handle leading zeros
            int leadingZeros = input.TakeWhile(c => c == '1').Count();

            var result = new byte[leadingZeros + bytes.Length];
            Array.Copy(bytes, 0, result, leadingZeros, bytes.Length);

            return result;
        }

        #endregion
    }
}
