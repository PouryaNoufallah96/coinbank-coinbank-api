# CoinBank API

A .NET 8 backend for a multi-chain (BEP20 / ERC20 / TRC20) DeFi/crypto token platform.
In plain terms it provides: wallet-based authentication, token **swaps**, **presales** and
**presale orders**, **staking**, **withdrawals**, on-chain **price feeds**, and blockchain
**event listening** — exposed as a single versioned ASP.NET Core Web API plus four real-time
SignalR hubs.

This is a **classic N-tier Web API on MongoDB** — not microservices, not Clean Architecture,
not DDD/CQRS, and there is no EF. Persistence goes through a hand-rolled MongoDB wrapper
nicknamed **"Monjo"**. DI is **Autofac** by marker-interface convention. EVM chains are accessed
with **Nethereum (Web3)**; TRON message-signature verification is delegated to a small Node.js
sidecar (`tron-verifier`).

## Architecture (N-tier)

One ASP.NET Core host composed of four projects with a strict one-way dependency chain:

```
CoinBank.Api  →  CoinBank.Services  →  CoinBank.Domain  →  CoinBank.Utilities
   (host)          (business logic)      (Mongo entities)     (shared framework — leaf)
```

| Project | SDK | Role |
|---|---|---|
| `CoinBank.Api` | `Microsoft.NET.Sdk.Web` | Host: `Program.cs`, `Controllers/V1/`, API-layer middlewares, DI wiring |
| `CoinBank.Services` | `Microsoft.NET.Sdk` | Business logic in `_<Module>/` feature folders + blockchain integration |
| `CoinBank.Domain` | `Microsoft.NET.Sdk` | Mongo `Collections/` (entities) + `Repositories/` (interface + impl) |
| `CoinBank.Utilities` | `Microsoft.NET.Sdk` | Cross-cutting framework: ApiResult, exceptions, middlewares, filters, Monjo, JWT, permissions |

The reference graph and request lifecycle are documented in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Tech stack

- **Runtime:** .NET 8 (`net8.0`, C# 12), `<ImplicitUsings>enable</ImplicitUsings>`, `<Nullable>disable</Nullable>`.
- **Persistence:** MongoDB via the custom **Monjo** layer over `MongoDB.Driver` 2.28.0. Soft-delete is automatic.
- **DI:** **Autofac** (`Autofac.Extensions.DependencyInjection`) — marker-interface convention scanning.
- **Auth:** encrypted **JWE** tokens (signed HMAC-SHA256 + AES-128 encrypted), a custom `[Authorize]` filter, and an HMAC request-signature gate (`ApplicationId`/`Nonce`/`Signature`).
- **Blockchain:** **Nethereum 5.0.0** (Web3, Signer, Contracts) for EVM (BEP20/ERC20); a Node.js `tron-verifier` sidecar (Express 5 + TronWeb 6) for TRON.
- **Real-time:** ASP.NET Core SignalR (4 hubs).
- **Observability:** Sentry (`Sentry.AspNetCore`).
- **Solution:** `CoinBank.Api.slnx` (XML solution format). Remote: GitLab.

## Service modules (`CoinBank.Services/_<Module>/`)

| Module | Responsibility |
|---|---|
| `_User` | Wallet-signature auth (nonce → sign → JWT), user lifecycle |
| `_Swap` | Token swap creation/state; pushes to `SwapHub` |
| `_PreSale` / `_PreSaleOrder` | Presales, presale orders, release scheduling |
| `_Stake` | Staking plans, accrued-profit preview, early-withdraw rules |
| `_Withdrawal` | Withdrawal flows |
| `_Transaction` | Transaction log orchestration |
| `_Price` | Periodic price fetch (CMC + on-chain pools); pushes to `PriceHub` |
| `_BlockChain` | Core on-chain client (BEP20/ERC20/TRC20 Web3, presale/swap/stake reads) |
| `_BlockChainWebSocket` | On-chain event listeners (WebSocket subscriptions + polling fallback) |
| `_MultiCallService` | Batched contract reads via the Multicall contract |
| `_PancakeSwap` | PancakeSwap quote helpers — **currently commented out / inactive** |
| `_File` | Image upload/serve (`SixLabors.ImageSharp`) |
| `_Log` | Request/app log persistence + cleanup scheduler |
| `_Common` | Shared DTOs/settings/helpers (no root service) |

## Build / run / deploy

```bash
# Build
dotnet build CoinBank.Api/CoinBank.Api.csproj -c Release   # or: dotnet build CoinBank.Api.slnx

# Run locally (expects Mongo at localhost:27017, db coinbankDB)
dotnet run --project CoinBank.Api

# Publish (required before docker build — .dockerignore excludes everything but /publish)
dotnet publish CoinBank.Api/CoinBank.Api.csproj -c Release -o publish

# Deploy the main API (build + publish + docker build + docker-compose up)
./deploy.sh
# Under the hood: docker build -t coinbank.api .  →  docker-compose down/up -d
# Container cb.coinbank.com, host-mapped 127.0.0.1:3013:80, network coinbank-net

# Deploy the TRON sidecar
cd tron-verifier && ./deploy.sh        # docker compose up -d --build (Express on :3001)
```

CI runs on GitLab (see [docs/CI.md](docs/CI.md)): every MR into `main` gets a Release **build**
plus a **csharpier** format check (advisory until a one-time reformat lands), and merges to
`main` expose a **manual** deploy job on the self-hosted `coinbank-prod` runner. Package
versions are centralized in `Directory.Packages.props`; built-in Roslyn analyzers run as
warnings (`Directory.Build.props`). There is still **no test project** — match the surrounding
file's style when editing, and don't rely on a formatter having run.

## Security

Secrets (blockchain operator private key, JWT signature/encryption keys, Mongo connection,
application pre-shared keys + master signatures, CMC API key) are supplied via environment
variables / `.env` (git-protected). See [docs/SECURITY.md](docs/SECURITY.md) for the findings and
remediation. Never commit secret values or read `.env`.
