# Codebase Map

Where things live, and which file to edit to add X. Dependency chain:
`CoinBank.Api → CoinBank.Services → CoinBank.Domain → CoinBank.Utilities`.

## Projects

| Project | Path | Holds |
|---|---|---|
| `CoinBank.Api` | `CoinBank.Api/` | `Program.cs`, `Controllers/V1/`, `Utilities/{Configurations,Middlewares}/`, `appsettings.json` |
| `CoinBank.Services` | `CoinBank.Services/` | `_<Module>/` feature folders, blockchain integration, SignalR hubs, background schedulers |
| `CoinBank.Domain` | `CoinBank.Domain/` | `Collections/` (Mongo entities), `Repositories/` + `Repositories/Contracts/` |
| `CoinBank.Utilities` | `CoinBank.Utilities/` | shared framework (namespace `Utilities.*`) — see table below |
| `tron-verifier` | `tron-verifier/` | Node.js/Express TRON signature sidecar (`server.js`, port 3001) |

## Utilities subfolders (the shared framework)

| Folder | Holds |
|---|---|
| `Api/` | `ApiBaseController` — token/header claim accessors |
| `Attributes/` | `[MonjoCollectionName]`, `[CustomRateLimit]`, input-validation + `[Sanitize]`/`[Security]` |
| `Configuration/` | DI/pipeline/Swagger extension methods, `RegisterSetting<T>` |
| `Constants/` | `RegisterMode` DI marker interfaces, `BaseSettings` |
| `Enums/` | `ApiResultStatusCode`, `Claims`, `UserType` |
| `Exceptions/` | `BaseException` + typed subclasses (`NotFoundException`, `BadRequestException`, …) |
| `Filters/` | `ApiResultFilterAttribute`, custom `AuthorizeAttribute` |
| `Middlewares/` | `CustomExceptionHandler`, `Jwt`, `Signature`, `Firewall`, `CustomRateLimiting` |
| `Models/` | `Results/ApiResult`, `Settings/*`, `Storages/*` |
| `MongoDatabase/` | the **Monjo** layer: `MonjoRepository<T>`, `MonjoConnection`, `Documents/BaseDocument`, `Filter/MonjoQuery` |
| `Permissions/` | permission code consts + `PermissionMeta` registry |
| `Services/` | `JwtService` (JWE), `SignatureService`, `NonceService`, `PasswordService`, `RandomService`, `SchedulerBase` |
| `Swagger/` | security-requirement + version Swagger filters |

## Collections ↔ Repositories (Domain)

`User`, `Swap`, `PreSale`, `PreSaleOrder`, `PreSaleRelease`, `Stake`, `Withdrawal`,
`TransactionLog`, `Log`, `RequestLog` — each has `Collections/<X>.cs`,
`Repositories/<X>Repository.cs`, and `Repositories/Contracts/I<X>Repository.cs`.

## To add X, edit Y

| Task | Do |
|---|---|
| **New Mongo entity** | Add `CoinBank.Domain/Collections/<X>.cs` : `BaseDocument`, tag `[MonjoCollectionName("<Xs>")]`; co-locate its enums/VOs |
| **New repository** | Add `Repositories/Contracts/I<X>Repository : IMonjoRepository<X>` (usually empty) + `Repositories/<X>Repository(IMonjoConnection connection) : MonjoRepository<X>(connection), I<X>Repository, ISingletonDependency` — auto-registered |
| **New service module** | Add `CoinBank.Services/_<Module>/I<Module>Service.cs` + `<Module>Service.cs` (impl a DI marker, e.g. `IScopedDependency`) + `DTOs/{Updates,Results,Settings,Storages}/` |
| **New endpoint** | Add a `[HttpPost("[action]")]` action to a `Controllers/V1/*Controller.cs`; return the raw `*Result` DTO; delegate to the service; add `[Authorize(...)]`/`[CustomRateLimit]` as needed |
| **New permission** | Add a code const + `PermissionMeta` entry in `CoinBank.Utilities/Permissions/Permissions.cs`; reference via `[Authorize(Permissions.<X>)]` |
| **New setting** | Add the POCO; bind in `AddCodeAssistantSettings` (Mongo/Jwt/Firewall/Captcha/AppPool) or `AddSettings` (AvailableTokens/BlockChain/Stake/File/Price/VerifyTron) via `RegisterSetting<T>` |
| **New background job** | Add a `SchedulerBase` subclass implementing `IHostedDependency` |
| **New SignalR hub** | Add a `Hub` under `_<Module>/_Hub/` and map it in `Program.cs` (`/hubs/<name>`) |
| **New middleware** | Add to `CoinBank.Utilities/Middlewares/` (or `CoinBank.Api/Utilities/Middlewares/`) and wire its order in `Program.cs` |
| **Change auth/signature** | `Utilities/Middlewares/{Signature,Jwt}Middleware.cs`, `Filters/AuthorizeAttribute.cs`, `Services/JwtService.cs` |

## Entry points

- `CoinBank.Api/Program.cs` — DI registration + middleware pipeline order + hub mapping.
- `CoinBank.Api/appsettings.json` — config section shapes (secrets via env / `.env`).
- `deploy.sh` / `docker-compose.yml` / `Dockerfile` — build + deploy.
- `tron-verifier/server.js` — the TRON `POST /verify` sidecar.
