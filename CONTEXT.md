# CoinBank API

Multi-chain DeFi backend (BEP20/ERC20/TRC20): wallet auth, swaps, presales, staking, withdrawals, price feeds, on-chain listeners. N-tier MongoDB via **Monjo**, Autofac marker DI.

## Domain

**Swap**:
A token-for-token exchange request tracked through `SwapState` with on-chain `SwapTransaction`s. Lives in the `Swap` collection / `_Swap` module.
_Avoid_: trade, exchange (generic)

**PreSale**:
A token presale campaign configurable on-chain. `PreSale` collection / `_PreSale` module.

**PreSaleOrder**:
A user's order placed against a presale. `PreSaleOrder` collection / `_PreSaleOrder` module.

**PreSaleRelease**:
A scheduled release/vesting tranche of presale-purchased tokens. `PreSaleRelease` collection.

**Stake**:
A staking position with a plan (`DurationInMonths`, `MonthlyProfitPercent`) and early-withdraw rules. `Stake` collection / `_Stake` module.

**Withdrawal**:
A user withdrawal request/flow. `Withdrawal` collection / `_Withdrawal` module.

**TransactionLog**:
Record of on-chain/business transactions. `TransactionLog` collection / `_Transaction` module.

**Wallet auth**:
Public-API login: server issues a nonce, user signs with wallet, server verifies and issues JWE. EVM via Nethereum; TRON via `tron-verifier` sidecar.
_Avoid_: login, OAuth

**NetworkType**:
The chain a wallet/operation belongs to — **BEP20** (BSC), **ERC20** (Ethereum), or **TRC20** (TRON).

**AvailableToken**:
A configured tradable token in `AvailableTokensSettings`: name, network, contract, pool, decimals, swap limits, price-sync flag.

**Price feed**:
Periodic (60s) token prices from CoinMarketCap + on-chain pools, pushed via `PriceHub`.

**Multicall**:
Contract that batches many on-chain reads into one RPC call (`_MultiCallService`).

## Framework / platform

**Monjo**:
Hand-rolled MongoDB abstraction (`MonjoRepository<T>`, `IMonjoConnection`, `MonjoQuery`, `[MonjoCollectionName]`). Intentional spelling — never "Mongo".
_Avoid_: Mongo repository, EF

**BaseDocument**:
Base for every Mongo entity: `Id`, `CreatedMoment`/`ModifiedMoment`, soft-delete `IsDeleted`/`DeletedMoment`.

**Soft-delete**:
Deletes set `IsDeleted=true`; reads auto-filter `!IsDeleted`. `RealDeleteManyAsync` is the only hard delete.

**ApiResult**:
Universal response envelope `{ IsSuccess, StatusCode, Message, Data }`, applied by `[ApiResultFilter]`.

**JWE**:
Encrypted JWT used here: signed (HMAC-SHA256) and encrypted (AES-128).

**Signature middleware**:
Request gate requiring `ApplicationId` + `Nonce` + `Signature` headers (HMAC-SHA256), replay protection, `MasterSignature` bypass.

**Permission**:
Short opaque code (e.g. `"U1A#"`) checked by custom `[Authorize]` against token `Permission` claims.

**RegisterMode marker**:
DI opt-in: `IScopedDependency`, `ITransientDependency`, `ISingletonDependency`, `ISelfSingletonDependency`, `IHostedDependency`.

**tron-verifier**:
Node.js sidecar (`POST /verify`, port 3001) for TRON message signatures Nethereum cannot verify.
