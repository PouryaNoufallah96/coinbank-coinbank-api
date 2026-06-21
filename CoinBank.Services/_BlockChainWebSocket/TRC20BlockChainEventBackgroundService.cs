//using CoinBank.Domain.Collections;
//using CoinBank.Services._BlockChain.DTOs.Settings;
//using CoinBank.Services._BlockChainWebSocket.DTOs;
//using CoinBank.Services._Common.DTOs.Settings;
//using CoinBank.Services._Swap;
//using CoinBank.Services._Transaction;
//using CoinBank.Services._Transaction.DTOs.Updates;
//using Microsoft.Extensions.Hosting;
//using Microsoft.Extensions.Logging;
//using Nethereum.Contracts;
//using Nethereum.Hex.HexConvertors.Extensions;
//using Nethereum.Hex.HexTypes;
//using Nethereum.JsonRpc.WebSocketStreamingClient;
//using Nethereum.RPC.Eth.DTOs;
//using Nethereum.RPC.Reactive.Eth.Subscriptions;
//using Nethereum.Util;
//using Nethereum.Web3;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net.Http;
//using System.Numerics;
//using System.Reactive.Linq;
//using System.Security.Cryptography;
//using System.Text;
//using System.Text.Json;
//using System.Threading.Tasks;
//using static Utilities.Constants.RegisterMode;

//namespace CoinBank.Services._BlockChainWebSocket
//{
//    public class TRC20BlockChainEventBackgroundService : BackgroundService, IHostedDependency
//    {
//        private const string SwapLogPrefix = "[TRC20-WS-Swap]";
//        private const string TransferLogPrefix = "[TRC20-WS-Transfer]";
//        private const string CommonLogPrefix = "[TRC20-WS]";
//        private const string NetworkName = "TRC20";

//        private readonly BlockChainSettings _blockChainSettings;
//        private readonly ITransactionLogService _transactionLogService;
//        private readonly ISwapService _swapService;
//        private readonly AvailableTokensSettings _availableTokensSettings;
//        private readonly ILogger<TRC20BlockChainEventBackgroundService> _logger;

//        private Web3 _web3;
//        private StreamingWebSocketClient _webSocketClient;

//        private readonly HttpClient _tronGridHttpClient;

//        private readonly string[] _rpcUrls;
//        private readonly string[] _wsUrls;

//        private int _currentRpcIndex = 0;
//        private int _currentWsIndex = 0;

//        private readonly string _swapContractAddress;

//        private string _swapContractHexAddress;
//        private readonly object _addressLock = new();

//        private readonly object _blockLock = new();

//        private BigInteger _swapLastProcessedBlock = 0;

//        private readonly SemaphoreSlim _reconnectLock = new(1, 1);
//        private readonly SemaphoreSlim _cleanupLock = new(1, 1);

//        private IDisposable _incomingTransferSubscription;
//        private IDisposable _swapContractEventsSubscription;

//        private int _reconnectAttempts = 0;
//        private DateTime _lastEventReceived = DateTime.UtcNow;

//        private bool _isDisposed = false;

//        public TRC20BlockChainEventBackgroundService(
//            BlockChainSettings blockChainSettings,
//            ITransactionLogService transactionLogService,
//            ISwapService swapService,
//            AvailableTokensSettings availableTokensSettings,
//            ILogger<TRC20BlockChainEventBackgroundService> logger)
//        {
//            _blockChainSettings = blockChainSettings;
//            _transactionLogService = transactionLogService;
//            _swapService = swapService;
//            _availableTokensSettings = availableTokensSettings;
//            _logger = logger;

//            _rpcUrls = new[] { _blockChainSettings.TRC20RpcUrl };
//            _wsUrls = new[] { _blockChainSettings.TRC20WsUrl };

//            _swapContractAddress = _blockChainSettings.TRC20SwapContractAddress;

//            _tronGridHttpClient = new HttpClient
//            {
//                BaseAddress = new Uri(GetTronGridBaseUrl()),
//                Timeout = TimeSpan.FromSeconds(20)
//            };
//            _tronGridHttpClient.DefaultRequestHeaders.Add("Accept", "application/json");

//            InitializeClients();
//        }

//        private string GetTronGridBaseUrl()
//        {
//            return "https://api.trongrid.io/";
//        }


//        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//        {
//            _logger.LogInformation("{Prefix} Service started", CommonLogPrefix);

//            while (!stoppingToken.IsCancellationRequested)
//            {
//                try
//                {
//                    await TryConnectWithRetryAsync(stoppingToken);

//                    _lastEventReceived = DateTime.UtcNow;

//                    while (_webSocketClient?.IsStarted == true && !stoppingToken.IsCancellationRequested)
//                    {
//                        var now = DateTime.UtcNow;

//                        if ((now - _lastEventReceived).TotalMinutes > 2)
//                        {
//                            _logger.LogWarning("{Prefix} Heartbeat timeout detected. Reconnecting...", CommonLogPrefix);
//                            await Task.Delay(2000, stoppingToken);
//                            await TryConnectWithRetryAsync(stoppingToken);
//                            _lastEventReceived = DateTime.UtcNow;
//                        }

//                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
//                    }

//                    if (_webSocketClient != null && !_webSocketClient.IsStarted)
//                    {
//                        await Task.Delay(2000, stoppingToken);
//                    }
//                }
//                catch (OperationCanceledException)
//                {
//                    break;
//                }
//                catch (Exception ex)
//                {
//                    _logger.LogError(ex, "{Prefix} Unexpected error in service loop", CommonLogPrefix);

//                    SwitchRpc();
//                    SwitchWs();
//                    InitializeClients();

//                    await Task.Delay(5000, stoppingToken);
//                }
//            }

//            _logger.LogInformation("{Prefix} Service stopped", CommonLogPrefix);
//        }


//        private async Task TryConnectWithRetryAsync(CancellationToken stoppingToken)
//        {
//            if (!await _reconnectLock.WaitAsync(0, stoppingToken))
//            {
//                return;
//            }

//            try
//            {
//                _reconnectAttempts = 0;

//                while (!stoppingToken.IsCancellationRequested && _reconnectAttempts < Math.Max(1, _blockChainSettings.MaxReconnectAttempts))
//                {
//                    try
//                    {
//                        await ConnectAndSubscribe(stoppingToken);
//                        _reconnectAttempts = 0;
//                        return;
//                    }
//                    catch (Exception ex)
//                    {
//                        _reconnectAttempts++;

//                        _logger.LogWarning(ex, "{Prefix} Connection failed. Retry: {Retry}", CommonLogPrefix, _reconnectAttempts);

//                        SwitchRpc();
//                        SwitchWs();
//                        InitializeClients();

//                        await Task.Delay(CalculateReconnectDelay(), stoppingToken);
//                    }
//                }

//                if (_reconnectAttempts >= Math.Max(1, _blockChainSettings.MaxReconnectAttempts))
//                {
//                    await Task.Delay(30000, stoppingToken);
//                    _reconnectAttempts = 0;
//                }
//            }
//            finally
//            {
//                _reconnectLock.Release();
//            }
//        }

//        private TimeSpan CalculateReconnectDelay()
//        {
//            var interval = Math.Max(1, _blockChainSettings.ReconnectInterval);
//            var delaySeconds = Math.Min(Math.Pow(2, _reconnectAttempts) * interval, 300);
//            return TimeSpan.FromSeconds(delaySeconds);
//        }

//        private async Task ConnectAndSubscribe(CancellationToken cancellationToken)
//        {
//            await CleanupConnection();

//            var wsUrl = GetCurrentWsUrl();
//            _webSocketClient = new StreamingWebSocketClient(wsUrl);
//            _web3 = new Web3(GetCurrentRpcUrl());

//            try
//            {
//                await _webSocketClient.StartAsync();

//                await SubscribeToSwapContractEventsAsync(cancellationToken);
//                await SubscribeToIncomingTransfersAsync(cancellationToken);

//                _logger.LogInformation("{Prefix} Subscriptions active", SwapLogPrefix);
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "{Prefix} ConnectAndSubscribe failed", SwapLogPrefix);
//                throw;
//            }
//        }

//        private void InitializeClients()
//        {
//            _web3 = new Web3(GetCurrentRpcUrl());
//        }

//        private string GetCurrentRpcUrl()
//        {
//            return _rpcUrls[_currentRpcIndex];
//        }

//        private string GetCurrentWsUrl()
//        {
//            return _wsUrls[_currentWsIndex];
//        }

//        private void SwitchRpc()
//        {
//            _currentRpcIndex = (_currentRpcIndex + 1) % _rpcUrls.Length;
//        }

//        private void SwitchWs()
//        {
//            _currentWsIndex = (_currentWsIndex + 1) % _wsUrls.Length;
//        }

//        private async Task CleanupConnection()
//        {
//            if (!await _cleanupLock.WaitAsync(0))
//            {
//                _logger.LogInformation("{Prefix} Cleanup already running", CommonLogPrefix);
//                return;
//            }

//            try
//            {
//                _incomingTransferSubscription?.Dispose();
//                _incomingTransferSubscription = null;

//                _swapContractEventsSubscription?.Dispose();
//                _swapContractEventsSubscription = null;

//                if (_webSocketClient != null)
//                {
//                    try
//                    {
//                        if (_webSocketClient.IsStarted)
//                        {
//                            await _webSocketClient.StopAsync();
//                        }
//                    }
//                    catch { }

//                    try
//                    {
//                        _webSocketClient.Dispose();
//                    }
//                    catch { }

//                    _webSocketClient = null;
//                }

//                _logger.LogInformation("{Prefix} Cleanup completed", CommonLogPrefix);
//            }
//            finally
//            {
//                _cleanupLock.Release();
//            }
//        }



//        #region Swap 

//        private async Task SubscribeToSwapContractEventsAsync(CancellationToken cancellationToken)
//        {
//            var contractHexAddress = await GetSwapContractHexAddressAsync(cancellationToken);
//            if (string.IsNullOrEmpty(contractHexAddress))
//            {
//                _logger.LogWarning("{Prefix} Skipping swap subscription because the contract hex address could not be resolved", SwapLogPrefix);
//                return;
//            }

//            var subscription = new EthLogsObservableSubscription(_webSocketClient);

//            var contracts = new[] { contractHexAddress };

//            var observable = subscription.GetSubscriptionDataResponsesAsObservable()
//                .Where(log => contracts.Any(c => log.Address.IsTheSameAddress(c)))
//                .Select(log => Observable.FromAsync(() => SwapProcessContractEventLogAsync(log, cancellationToken)))
//                .Concat();

//            _swapContractEventsSubscription = observable.Subscribe(
//                _ => { },
//                ex => _logger.LogError(ex, "{Prefix} Swap subscription error", SwapLogPrefix),
//                () => _logger.LogWarning("{Prefix} Swap subscription closed", SwapLogPrefix)
//            );

//            var filter = new NewFilterInput
//            {
//                Address = contracts,
//                FromBlock = new BlockParameter(await GetSwapLastProcessedBlock(cancellationToken))
//            };

//            await subscription.SubscribeAsync(filter);
//        }

//        private async Task SwapProcessContractEventLogAsync(FilterLog log, CancellationToken cancellationToken)
//        {
//            try
//            {
//                _lastEventReceived = DateTime.UtcNow;
//                var network = NetworkName;

//                var initiated = log.DecodeEvent<SwapInitiatedEventDTO>();
//                if (initiated != null)
//                {
//                    await HandleSwapInitiated(log, initiated, network);
//                    UpdateSwapLastBlock(log);
//                    return;
//                }

//                var executed = log.DecodeEvent<SwapExecutedEventDTO>();
//                if (executed != null)
//                {
//                    await HandleSwapExecuted(log, executed, network);
//                    //UpdateSwapLastBlock(log);
//                    return;
//                }

//                var failed = log.DecodeEvent<SwapFailedEventDTO>();
//                if (failed != null)
//                {
//                    await HandleSwapFailed(log, failed, network);
//                    //UpdateSwapLastBlock(log);
//                    return;
//                }

//                var completed = log.DecodeEvent<SwapCompletedEventDTO>();
//                if (completed != null)
//                {
//                    await HandleSwapCompleted(log, completed, network);
//                    //UpdateSwapLastBlock(log);
//                    return;
//                }

//                var refunded = log.DecodeEvent<SwapRefundedEventDTO>();
//                if (refunded != null)
//                {
//                    await HandleSwapRefunded(log, refunded, network);
//                    //UpdateSwapLastBlock(log);
//                    return;
//                }

//                var refundClaimed = log.DecodeEvent<TokenRefundClaimedEventDTO>();
//                if (refundClaimed != null)
//                {
//                    await HandleTokenRefundClaimed(log, refundClaimed, NetworkName);
//                    return;
//                }
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "{Prefix} Swap decode error", SwapLogPrefix);
//            }
//        }

//        private async Task HandleSwapInitiated(FilterLog log, EventLog<SwapInitiatedEventDTO> ev, string network)
//        {
//            var swapId = ByteArray32ToHex(ev.Event.SwapId);

//            _logger.LogInformation("{Prefix} SwapInit | SwapId: {SwapId} | TokenOut: {TokenOut} | AmountOut: {AmountOut} | Receiver: {Receiver}",
//              SwapLogPrefix, swapId, ev.Event.TokenOut, ev.Event.AmountOut, ev.Event.Receiver);

//            await _transactionLogService.CreateSwapInitiatedLogAsync(new SwapInitiatedLog
//            {
//                Hash = log.TransactionHash,
//                Address = log.Address,
//                BlockNumber = log.BlockNumber.Value,
//                SwapId = swapId,
//                Buyer = ev.Event.Receiver,
//                SourceTokenAddress = ev.Event.TokenIn,
//                DestinationTokenAddress = ev.Event.TokenOut,
//                DesEid = ev.Event.DstEid,
//                SourceTokenAmount = ev.Event.AmountIn,
//                DestinationTokenAmount = ev.Event.AmountOut,
//                DestinationWallet = ev.Event.Receiver,
//                Fee = ev.Event.Fee.ToString(),
//                Network = network,
//                EventType = BlockchainEventType.SwapInitiated
//            });
//        }

//        private async Task HandleSwapExecuted(FilterLog log, EventLog<SwapExecutedEventDTO> ev, string network)
//        {
//            var swapId = ByteArray32ToHex(ev.Event.SwapId);
//            _logger.LogInformation("{Prefix} SwapExecuted | SwapId: {SwapId} | TokenOut: {TokenOut} | AmountOut: {AmountOut} | Receiver: {Receiver}",
//               SwapLogPrefix, swapId, ev.Event.TokenOut, ev.Event.AmountOut, ev.Event.Receiver);

//            await _transactionLogService.CreateSwapExecutedLogAsync(new SwapExecutedLog
//            {
//                Hash = log.TransactionHash,
//                Address = log.Address,
//                BlockNumber = log.BlockNumber.Value,
//                SwapId = swapId,
//                DestinationTokenAddress = ev.Event.TokenOut,
//                DestinationTokenAmount = ev.Event.AmountOut,
//                DestinationWallet = ev.Event.Receiver,
//                Network = network,
//                EventType = BlockchainEventType.SwapExecuted
//            });
//        }

//        private async Task HandleSwapFailed(FilterLog log, EventLog<SwapFailedEventDTO> ev, string network)
//        {
//            var swapId = ByteArray32ToHex(ev.Event.SwapId);
//            _logger.LogInformation("{Prefix} SwapFailed | SwapId: {SwapId} | TokenOut: {TokenOut} | AmountOut: {AmountOut} | Receiver: {Receiver}",
//              SwapLogPrefix, swapId, ev.Event.TokenOut, ev.Event.AmountOut, ev.Event.Receiver);
//            await _transactionLogService.CreateSwapFailedLogAsync(new SwapFailedLog
//            {
//                Hash = log.TransactionHash,
//                Address = log.Address,
//                BlockNumber = log.BlockNumber.Value,
//                SwapId = swapId,
//                DestinationTokenAddress = ev.Event.TokenOut,
//                DestinationTokenAmount = ev.Event.AmountOut,
//                DestinationWallet = ev.Event.Receiver,
//                Network = network,
//                EventType = BlockchainEventType.SwapFailed
//            });
//        }

//        private async Task HandleSwapCompleted(FilterLog log, EventLog<SwapCompletedEventDTO> ev, string network)
//        {
//            var swapId = ByteArray32ToHex(ev.Event.SwapId);
//            _logger.LogInformation("{Prefix} SwapCompleted | SwapId: {SwapId}", SwapLogPrefix, swapId);
//            await _transactionLogService.CreateSwapCompletedLogAsync(new SwapCompletedLog
//            {
//                Hash = log.TransactionHash,
//                Address = log.Address,
//                BlockNumber = log.BlockNumber.Value,
//                SwapId = swapId,
//                Network = network,
//                EventType = BlockchainEventType.SwapCompleted
//            });
//        }

//        private async Task HandleSwapRefunded(FilterLog log, EventLog<SwapRefundedEventDTO> ev, string network)
//        {
//            var swapId = ByteArray32ToHex(ev.Event.SwapId);
//            _logger.LogInformation("{Prefix} SwapRefunded | SwapId: {SwapId} | Token: {Token} | Amount: {Amount} | User: {User}",
//               SwapLogPrefix, swapId, ev.Event.Token, ev.Event.Amount, ev.Event.User);
//            await _transactionLogService.CreateSwapRefundedLogAsync(new SwapRefundedLog
//            {
//                Hash = log.TransactionHash,
//                Address = log.Address,
//                BlockNumber = log.BlockNumber.Value,
//                SwapId = swapId,
//                Token = ev.Event.Token,
//                Amount = ev.Event.Amount,
//                User = ev.Event.User,
//                Network = network,
//                EventType = BlockchainEventType.SwapRefunded
//            });
//        }

//        private async Task HandleTokenRefundClaimed(FilterLog log, EventLog<TokenRefundClaimedEventDTO> ev, string network)
//        {
//            var swapId = ByteArray32ToHex(ev.Event.SwapId);

//            _logger.LogInformation(
//                "{Prefix} TokenRefundClaimed | SwapId: {SwapId} | Token: {Token} | Recipient: {Recipient} | Amount: {Amount}",
//                SwapLogPrefix,
//                swapId,
//                ev.Event.Token,
//                ev.Event.Recipient,
//                ev.Event.Amount);

//            await _transactionLogService.CreateSwapRefundClaimedLogAsync(
//                new SwapRefundClaimedLog
//                {
//                    Hash = log.TransactionHash,
//                    Address = log.Address,
//                    BlockNumber = log.BlockNumber.Value,
//                    SwapId = swapId,
//                    Token = ev.Event.Token,
//                    Recipient = ev.Event.Recipient,
//                    Amount = ev.Event.Amount,
//                    User = ev.Event.Recipient,
//                    Network = network,
//                    EventType = BlockchainEventType.SwapRefundClaimed
//                });
//        }

//        private async Task<HexBigInteger> GetSwapLastProcessedBlock(CancellationToken cancellationToken)
//        {
//            lock (_blockLock)
//            {
//                if (_swapLastProcessedBlock > 0)
//                    return _swapLastProcessedBlock.ToHexBigInteger();
//            }

//            var dbBlock = await _transactionLogService.GetSwapLastCheckedBlockNumberAsync(NetworkName);

//            lock (_blockLock)
//            {
//                _swapLastProcessedBlock = dbBlock;
//            }

//            if (_swapLastProcessedBlock > 0)
//                return _swapLastProcessedBlock.ToHexBigInteger();

//            var latestBlockNumber = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

//            lock (_blockLock)
//            {
//                _swapLastProcessedBlock = latestBlockNumber;
//                return latestBlockNumber;
//            }
//        }

//        private void UpdateSwapLastBlock(FilterLog log)
//        {
//            if (log?.BlockNumber == null) return;

//            lock (_blockLock)
//            {
//                _swapLastProcessedBlock = BigInteger.Max(_swapLastProcessedBlock, log.BlockNumber.Value + 1);
//            }
//        }

//        #endregion


//        #region Incoming Transfers

//        private async Task SubscribeToIncomingTransfersAsync(CancellationToken cancellationToken)
//        {
//            var tokens = _availableTokensSettings.Select(t => t.Address.ToLower()).ToArray();

//            var subscription = new EthLogsObservableSubscription(_webSocketClient);

//            var observable = subscription.GetSubscriptionDataResponsesAsObservable()
//                .Where(log => tokens.Contains(log.Address.ToLower()))
//                .Select(log => Observable.FromAsync(() => ProcessIncomingTransferLogAsync(log)))
//                .Concat();

//            _incomingTransferSubscription = observable.Subscribe(
//                _ => { },
//                ex => _logger.LogError(ex, "{Prefix} Transfer subscription error", TransferLogPrefix),
//                () => _logger.LogWarning("{Prefix} Transfer subscription closed", TransferLogPrefix)
//            );

//            var filter = new NewFilterInput
//            {
//                Address = tokens
//            };

//            await subscription.SubscribeAsync(filter);
//        }

//        private async Task ProcessIncomingTransferLogAsync(FilterLog log)
//        {
//            try
//            {
//                _lastEventReceived = DateTime.UtcNow;

//                var transfer = log.DecodeEvent<TransferEventDTO>();
//                if (transfer == null) return;

//                var token = _availableTokensSettings.FirstOrDefault(t =>
//                    t.Address.IsTheSameAddress(log.Address));

//                if (token == null)
//                {
//                    _logger.LogWarning("{Prefix} Token not found: {Address}", TransferLogPrefix, log.Address);
//                    return;
//                }

//                var amount = Web3.Convert.FromWei(transfer.Event.Value);

//                var swapContractHexAddress = await GetSwapContractHexAddressAsync(CancellationToken.None);

//                if (!string.IsNullOrEmpty(swapContractHexAddress) &&
//                    transfer.Event.To.IsTheSameAddress(swapContractHexAddress))
//                {
//                    _logger.LogInformation(
//                        "{Prefix} Swap deposit: {Token} {Amount} from {From}",
//                        SwapLogPrefix, token.Name, amount, transfer.Event.From);

//                    await _swapService.UpdateSingleTokenInStorageAsync(token.Address, token.Network);

//                }
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "{Prefix} Transfer processing error", TransferLogPrefix);
//            }
//        }

//        #endregion


//        private static string ByteArray32ToHex(byte[] bytes)
//        {
//            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
//            if (bytes.Length != 32) throw new ArgumentException("Must be 32 bytes");

//            return "0x" + bytes.ToHex();
//        }

//        /// <summary>
//        /// Resolves the configured TRON swap contract address to its EVM (0x + 20 bytes) hex form
//        /// and caches the result so the conversion only happens once.
//        /// The hex form is required by the eth_getLogs compatible RPC endpoint.
//        /// </summary>
//        private async Task<string> GetSwapContractHexAddressAsync(CancellationToken cancellationToken)
//        {
//            lock (_addressLock)
//            {
//                if (!string.IsNullOrEmpty(_swapContractHexAddress))
//                    return _swapContractHexAddress;
//            }

//            var resolved = await ResolveContractHexAddressAsync(_swapContractAddress, cancellationToken);

//            lock (_addressLock)
//            {
//                _swapContractHexAddress = resolved;
//            }

//            return resolved;
//        }

//        private async Task<string> ResolveContractHexAddressAsync(string address, CancellationToken cancellationToken)
//        {
//            if (string.IsNullOrWhiteSpace(address))
//            {
//                _logger.LogWarning("{Prefix} Swap contract address is not configured", CommonLogPrefix);
//                return null;
//            }

//            address = address.Trim();

//            // Already an EVM hex address (0x + 40 hex chars)
//            if (address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
//            {
//                var hex = address[2..];
//                if (hex.Length == 40 && IsHex(hex))
//                    return "0x" + hex.ToLowerInvariant();

//                _logger.LogWarning("{Prefix} Configured swap contract address '{Address}' is not a valid 20-byte hex address", CommonLogPrefix, address);
//                return null;
//            }

//            // TRON hex form (41 + 40 hex chars)
//            if (address.Length == 42 && address.StartsWith("41", StringComparison.OrdinalIgnoreCase) && IsHex(address))
//                return "0x" + address[2..].ToLowerInvariant();

//            // TRON Base58Check form (starts with 'T') -> validate via TronGrid, then convert to EVM hex
//            try
//            {
//                var isValid = await ValidateAddressWithTronGridAsync(address, cancellationToken);
//                if (!isValid)
//                    _logger.LogWarning("{Prefix} TronGrid could not validate address '{Address}', attempting local conversion", CommonLogPrefix, address);

//                var evmHex = ConvertTronBase58ToEvmHex(address);

//                _logger.LogInformation("{Prefix} Resolved TRON contract '{Tron}' to EVM hex address '{Hex}'", CommonLogPrefix, address, evmHex);

//                return evmHex;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "{Prefix} Failed to convert TRON contract address '{Address}' to hex", CommonLogPrefix, address);
//                return null;
//            }
//        }

//        /// <summary>
//        /// Uses the TronGrid wallet/validateaddress endpoint to confirm the address is a valid TRON address.
//        /// Best-effort: returns false if the call fails so the caller can decide how to proceed.
//        /// </summary>
//        private async Task<bool> ValidateAddressWithTronGridAsync(string address, CancellationToken cancellationToken)
//        {
//            try
//            {
//                var payload = JsonSerializer.Serialize(new { address, visible = true });
//                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
//                using var response = await _tronGridHttpClient.PostAsync("wallet/validateaddress", content, cancellationToken);

//                if (!response.IsSuccessStatusCode)
//                    return false;

//                var json = await response.Content.ReadAsStringAsync(cancellationToken);
//                using var doc = JsonDocument.Parse(json);

//                if (doc.RootElement.TryGetProperty("result", out var result))
//                {
//                    return result.ValueKind == JsonValueKind.True ||
//                           (result.ValueKind == JsonValueKind.String &&
//                            bool.TryParse(result.GetString(), out var parsed) && parsed);
//                }

//                return false;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogWarning(ex, "{Prefix} TronGrid validateaddress call failed for '{Address}'", CommonLogPrefix, address);
//                return false;
//            }
//        }

//        /// <summary>
//        /// Converts a TRON Base58Check address (T...) into an EVM hex address (0x + 20 bytes)
//        /// by decoding Base58, validating the checksum and stripping the 0x41 TRON prefix.
//        /// </summary>
//        private static string ConvertTronBase58ToEvmHex(string base58Address)
//        {
//            var decoded = Base58Decode(base58Address);

//            if (decoded.Length != 25)
//                throw new FormatException($"Invalid TRON address length: {decoded.Length}, expected 25 bytes");

//            var payload = decoded[..^4];
//            var checksum = decoded[^4..];

//            using var sha256 = SHA256.Create();
//            var hash = sha256.ComputeHash(sha256.ComputeHash(payload));

//            if (!hash.Take(4).SequenceEqual(checksum))
//                throw new FormatException("Invalid TRON address checksum");

//            if (payload.Length != 21 || payload[0] != 0x41)
//                throw new FormatException("Invalid TRON address payload");

//            var addressBytes = payload[1..];
//            return "0x" + Convert.ToHexString(addressBytes).ToLowerInvariant();
//        }

//        private static byte[] Base58Decode(string input)
//        {
//            const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

//            BigInteger value = 0;
//            foreach (var c in input)
//            {
//                var digit = alphabet.IndexOf(c);
//                if (digit < 0)
//                    throw new FormatException($"Invalid Base58 character '{c}'");

//                value = value * 58 + digit;
//            }

//            var leadingZeros = 0;
//            foreach (var c in input)
//            {
//                if (c == '1')
//                    leadingZeros++;
//                else
//                    break;
//            }

//            var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
//            var result = new byte[leadingZeros + bytes.Length];
//            Array.Copy(bytes, 0, result, leadingZeros, bytes.Length);

//            return result;
//        }

//        private static bool IsHex(string value) => value.All(Uri.IsHexDigit);

//        public override async Task StopAsync(CancellationToken cancellationToken)
//        {
//            if (_isDisposed) return;

//            _logger.LogInformation("{Prefix} Stopping service...", SwapLogPrefix);

//            await CleanupConnection();

//            _isDisposed = true;
//            await base.StopAsync(cancellationToken);
//        }

//        public void Dispose()
//        {
//            if (_isDisposed) return;
//            _incomingTransferSubscription?.Dispose();
//            _swapContractEventsSubscription?.Dispose();
//            _webSocketClient?.Dispose();
//            _tronGridHttpClient?.Dispose();

//            _reconnectLock.Dispose();
//            _cleanupLock.Dispose();

//            _isDisposed = true;
//        }

//    }
//}
