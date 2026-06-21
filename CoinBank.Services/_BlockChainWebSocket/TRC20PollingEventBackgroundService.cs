using CoinBank.Domain.Collections;
using CoinBank.Services._BlockChain.DTOs.Settings;
using CoinBank.Services._BlockChainWebSocket.DTOs;
using CoinBank.Services._Transaction;
using CoinBank.Services._Transaction.DTOs.Updates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._BlockChainWebSocket
{
    public class TRC20PollingEventBackgroundService : BackgroundService, IHostedDependency
    {
        private const string SwapLogPrefix = "[TRC20-POLLING-Swap]";
        private const string CommonLogPrefix = "[TRC20-POLLING]";
        private const string NetworkName = "TRC20";

        private readonly ITransactionLogService _transactionLogService;
        private readonly ILogger<TRC20PollingEventBackgroundService> _logger;
        private readonly BlockChainSettings _blockChainSettings;
        private readonly object _blockLock = new();

        private Web3 _web3;

        private readonly HttpClient _tronGridHttpClient;

        private readonly string[] _rpcUrls;

        private int _currentRpcIndex = 0;

        private BigInteger _swapLastProcessedBlock = 0;
        private readonly string _swapContractAddress;

        private string _swapContractHexAddress;
        private readonly object _addressLock = new();

        private bool _isDisposed = false;

        public TRC20PollingEventBackgroundService(
            ITransactionLogService transactionLogService,
            ILogger<TRC20PollingEventBackgroundService> logger,
            BlockChainSettings blockChainSettings)
        {
            _transactionLogService = transactionLogService;
            _logger = logger;
            _blockChainSettings = blockChainSettings;

            _rpcUrls = new[] { _blockChainSettings.TRC20RpcUrl };
            _swapContractAddress = _blockChainSettings.TRC20SwapContractAddress;

            _tronGridHttpClient = new HttpClient
            {
                BaseAddress = new Uri(GetTronGridBaseUrl()),
                Timeout = TimeSpan.FromSeconds(20)
            };
            _tronGridHttpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            InitializeClients();
        }

        private string GetTronGridBaseUrl()
        {
            //var rpcUrl = _blockChainSettings.TRC20RpcUrl;

            //if (!string.IsNullOrWhiteSpace(rpcUrl) &&
            //    Uri.TryCreate(rpcUrl, UriKind.Absolute, out var uri))
            //{
            //    return uri.GetLeftPart(UriPartial.Authority) + "/";
            //}

            return "https://api.trongrid.io/";
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    BigInteger latestBlock = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                    _logger.LogInformation(
                     "{Prefix} Checking latest block: {Block}",
                     CommonLogPrefix,
                     latestBlock);

                    var safeBlock = latestBlock - 10;
                    await PollSwapMissingLogsAsync(safeBlock, stoppingToken);

                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Service shutdown requested");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Prefix} Unexpected error in blockchain event service", CommonLogPrefix);
                    await Task.Delay(5000, stoppingToken);
                }
            }

            _logger.LogInformation("Blockchain Event Service stopped.");
        }




        #region Swap 

        private async Task PollSwapMissingLogsAsync(BigInteger latestBlock, CancellationToken cancellationToken)
        {

            if (_swapLastProcessedBlock < 1)
            {
                _swapLastProcessedBlock = await GetSwapLastProcessedBlock(cancellationToken);

            }

            if (_swapLastProcessedBlock >= latestBlock) return;

            var contractHexAddress = await GetSwapContractHexAddressAsync(cancellationToken);
            if (string.IsNullOrEmpty(contractHexAddress))
            {
                _logger.LogWarning("{Prefix} Skipping swap polling because the contract hex address could not be resolved", SwapLogPrefix);
                return;
            }

            const int blockChunk = 2000;
            BigInteger fromBlock = _swapLastProcessedBlock;

            while (fromBlock <= latestBlock)
            {
                BigInteger toBlock = BigInteger.Min(fromBlock + blockChunk - 1, latestBlock);

                var filter = new NewFilterInput
                {
                    FromBlock = new BlockParameter(new HexBigInteger(fromBlock)),
                    ToBlock = new BlockParameter(new HexBigInteger(toBlock)),
                    Address = new[] { contractHexAddress }
                };

                try
                {
                    var logs = await _web3.Eth.Filters.GetLogs.SendRequestAsync(filter);

                    foreach (var log in logs)
                    {
                        var filterLog = log as FilterLog;
                        if (filterLog == null) continue;

                        try
                        {
                            var initiated = log.DecodeEvent<SwapInitiatedEventDTO>();
                            if (initiated != null)
                            {
                                await HandleSwapInitiated(log, initiated, NetworkName);
                                continue;
                            }

                            var executed = log.DecodeEvent<SwapExecutedEventDTO>();
                            if (executed != null)
                            {
                                await HandleSwapExecuted(log, executed, NetworkName);
                                continue;
                            }

                            var failed = log.DecodeEvent<SwapFailedEventDTO>();
                            if (failed != null)
                            {
                                await HandleSwapFailed(log, failed, NetworkName);
                                continue;
                            }

                            var completed = log.DecodeEvent<SwapCompletedEventDTO>();
                            if (completed != null)
                            {
                                await HandleSwapCompleted(log, completed, NetworkName);
                                continue;
                            }

                            var refunded = log.DecodeEvent<SwapRefundedEventDTO>();
                            if (refunded != null)
                            {
                                await HandleSwapRefunded(log, refunded, NetworkName);
                                continue;
                            }

                            var refundClaimed = log.DecodeEvent<TokenRefundClaimedEventDTO>();
                            if (refundClaimed != null)
                            {
                                await HandleTokenRefundClaimed(log, refundClaimed, NetworkName);
                                continue;
                            }

                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "{Prefix} Error decoding polled log", SwapLogPrefix);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Prefix} Error polling logs from {FromBlock} to {ToBlock}", SwapLogPrefix, fromBlock, toBlock);
                }

                fromBlock = toBlock + 1;
                await Task.Delay(3000, cancellationToken);
            }

            lock (_blockLock)
            {
                _swapLastProcessedBlock = BigInteger.Max(_swapLastProcessedBlock, latestBlock);
                //_logger.LogInformation(
                // "{Prefix} Checking latest block: {Block}",
                // CommonLogPrefix,
                // latestBlock);
            }
        }

        private async Task HandleSwapInitiated(FilterLog log, EventLog<SwapInitiatedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);
            _logger.LogInformation("{Prefix} SwapInit | SwapId: {SwapId} | TokenOut: {TokenOut} | AmountOut: {AmountOut} | Receiver: {Receiver}",
              SwapLogPrefix, swapId, ev.Event.TokenOut, ev.Event.AmountOut, ev.Event.Receiver);
            await _transactionLogService.CreateSwapInitiatedLogAsync(new SwapInitiatedLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                SwapId = swapId,
                Buyer = ev.Event.Receiver,
                SourceTokenAddress = ev.Event.TokenIn,
                DestinationTokenAddress = ev.Event.TokenOut,
                DesEid = ev.Event.DstEid,
                SourceTokenAmount = ev.Event.AmountIn,
                DestinationTokenAmount = ev.Event.AmountOut,
                DestinationWallet = ev.Event.Receiver,
                Fee = ev.Event.Fee.ToString(),
                Network = network,
                EventType = BlockchainEventType.SwapInitiated
            });

            lock (_blockLock)
            {
                _swapLastProcessedBlock =
                    BigInteger.Max(_swapLastProcessedBlock, log.BlockNumber.Value + 1);
            }
        }

        private async Task HandleSwapExecuted(FilterLog log, EventLog<SwapExecutedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);
            _logger.LogInformation("{Prefix} SwapExecuted | SwapId: {SwapId} | TokenOut: {TokenOut} | AmountOut: {AmountOut} | Receiver: {Receiver}",
              SwapLogPrefix, swapId, ev.Event.TokenOut, ev.Event.AmountOut, ev.Event.Receiver);
            await _transactionLogService.CreateSwapExecutedLogAsync(new SwapExecutedLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                SwapId = swapId,
                DestinationTokenAddress = ev.Event.TokenOut,
                DestinationTokenAmount = ev.Event.AmountOut,
                DestinationWallet = ev.Event.Receiver,
                Network = network,
                EventType = BlockchainEventType.SwapExecuted
            });

        }

        private async Task HandleSwapFailed(FilterLog log, EventLog<SwapFailedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);
            _logger.LogInformation("{Prefix} SwapFailed | SwapId: {SwapId} | TokenOut: {TokenOut} | AmountOut: {AmountOut} | Receiver: {Receiver}",
              SwapLogPrefix, swapId, ev.Event.TokenOut, ev.Event.AmountOut, ev.Event.Receiver);
            await _transactionLogService.CreateSwapFailedLogAsync(new SwapFailedLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                SwapId = swapId,
                DestinationTokenAddress = ev.Event.TokenOut,
                DestinationTokenAmount = ev.Event.AmountOut,
                DestinationWallet = ev.Event.Receiver,
                Network = network,
                EventType = BlockchainEventType.SwapFailed
            });

        }

        private async Task HandleSwapCompleted(FilterLog log, EventLog<SwapCompletedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);
            _logger.LogInformation("{Prefix} SwapCompleted | SwapId: {SwapId}", SwapLogPrefix, swapId);
            await _transactionLogService.CreateSwapCompletedLogAsync(new SwapCompletedLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                SwapId = swapId,
                Network = network,
                EventType = BlockchainEventType.SwapCompleted
            });
        }

        private async Task HandleSwapRefunded(FilterLog log, EventLog<SwapRefundedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);
            _logger.LogInformation("{Prefix} SwapRefunded | SwapId: {SwapId} | Token: {Token} | Amount: {Amount} | User: {User}",
               SwapLogPrefix, swapId, ev.Event.Token, ev.Event.Amount, ev.Event.User);
            await _transactionLogService.CreateSwapRefundedLogAsync(new SwapRefundedLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                SwapId = swapId,
                Token = ev.Event.Token,
                Amount = ev.Event.Amount,
                User = ev.Event.User,
                Network = network,
                EventType = BlockchainEventType.SwapRefunded
            });
        }
       
        private async Task HandleTokenRefundClaimed(FilterLog log, EventLog<TokenRefundClaimedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);

            _logger.LogInformation(
                "{Prefix} TokenRefundClaimed | SwapId: {SwapId} | Token: {Token} | Recipient: {Recipient} | Amount: {Amount}",
                SwapLogPrefix,
                swapId,
                ev.Event.Token,
                ev.Event.Recipient,
                ev.Event.Amount);

            await _transactionLogService.CreateSwapRefundClaimedLogAsync(
                new SwapRefundClaimedLog
                {
                    Hash = log.TransactionHash,
                    Address = log.Address,
                    BlockNumber = log.BlockNumber.Value,
                    SwapId = swapId,
                    Token = ev.Event.Token,
                    Recipient = ev.Event.Recipient,
                    Amount = ev.Event.Amount,
                    User = ev.Event.Recipient,
                    Network = network,
                    EventType = BlockchainEventType.SwapRefundClaimed
                });
        }

        private async Task<HexBigInteger> GetSwapLastProcessedBlock(CancellationToken cancellationToken)
        {
            try
            {
                lock (_blockLock)
                {
                    if (_swapLastProcessedBlock > 0)
                        return _swapLastProcessedBlock.ToHexBigInteger();
                }

                var lastDbBlock = await _transactionLogService.GetSwapLastCheckedBlockNumberAsync(NetworkName);

                lock (_blockLock)
                {
                    _swapLastProcessedBlock = lastDbBlock;
                }

                if (_swapLastProcessedBlock > 0)
                    return _swapLastProcessedBlock.ToHexBigInteger();

                var latestBlockNumber = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

                lock (_blockLock)
                {
                    _swapLastProcessedBlock = latestBlockNumber;
                    return latestBlockNumber;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "{Prefix} Error getting swap last processed block", SwapLogPrefix);
                throw;
            }
        }


        /// <summary>
        /// Resolves the configured TRON swap contract address to its EVM (0x + 20 bytes) hex form
        /// and caches the result so the conversion only happens once.
        /// The hex form is required by the eth_getLogs compatible RPC endpoint.
        /// </summary>
        private async Task<string> GetSwapContractHexAddressAsync(CancellationToken cancellationToken)
        {
            lock (_addressLock)
            {
                if (!string.IsNullOrEmpty(_swapContractHexAddress))
                    return _swapContractHexAddress;
            }

            var resolved = await ResolveContractHexAddressAsync(_swapContractAddress, cancellationToken);

            lock (_addressLock)
            {
                _logger.LogInformation("{Prefix} Resolved swap contract address '{Original}' to hex address '{Resolved}'", CommonLogPrefix, _swapContractAddress, resolved);
                _swapContractHexAddress = resolved;
            }

            return resolved;
        }

        private async Task<string> ResolveContractHexAddressAsync(string address, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                _logger.LogWarning("{Prefix} Swap contract address is not configured", CommonLogPrefix);
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
                    _logger.LogWarning("{Prefix} TronGrid could not validate address '{Address}', attempting local conversion", CommonLogPrefix, address);

                var evmHex = ConvertTronBase58ToEvmHex(address);

                _logger.LogInformation("{Prefix} Resolved TRON contract '{Tron}' to EVM hex address '{Hex}'", CommonLogPrefix, address, evmHex);

                return evmHex;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} Failed to convert TRON contract address '{Address}' to hex", CommonLogPrefix, address);
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
                _logger.LogWarning(ex, "{Prefix} TronGrid validateaddress call failed for '{Address}'", CommonLogPrefix, address);
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

        #endregion


        private static string ByteArray32ToHex(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            if (bytes.Length != 32)
                throw new ArgumentException("Input must be exactly 32 bytes for bytes32");

            return "0x" + bytes.ToHex();
        }

        private void InitializeClients()
        {
            _web3 = new Web3(GetCurrentRpcUrl());
        }

        private string GetCurrentRpcUrl()
        {
            return _rpcUrls[_currentRpcIndex];
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_isDisposed) return;

            _logger.LogInformation("{Prefix} Stopping polling service...", CommonLogPrefix);

            _isDisposed = true;
            _tronGridHttpClient?.Dispose();
            await base.StopAsync(cancellationToken);
        }
    }
}
