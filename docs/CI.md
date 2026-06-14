# CI / CD

GitLab pipeline defined in [`.gitlab-ci.yml`](../.gitlab-ci.yml). It gates every MR into `main`
on a Release build + a format check (GitLab shared runners), and deploys to the production box
on demand via a **self-hosted runner on that box**. **Zero secret migration** — secrets stay on
the server; no registry, no SSH key in CI.

## Pipeline shape

```
stages: build → quality → deploy
```

| Job | Stage | Runner | Trigger | Blocking? |
|-----|-------|--------|---------|-----------|
| `build` | build | shared (`sdk:8.0`) | MR + `main` | yes |
| `format` | quality | shared (`sdk:8.0`) | MR + `main` | **no** (`allow_failure: true`) — advisory for now |
| `deploy` | deploy | self-hosted `coinbank-prod` | `main` only, `when: manual` | n/a (▶ click) |

`workflow:rules` restricts pipelines to **merge-request events** and the **`main`** branch — this
prevents the common GitLab .NET pitfall of a duplicate *branch* pipeline running alongside every
MR pipeline.

### `build`
`dotnet restore` + `dotnet build CoinBank.Api/CoinBank.Api.csproj -c Release`. Builds the
`.csproj` (not the `.slnx`, which the `sdk:8.0` image's CLI doesn't open). NuGet packages are
cached keyed on the API `.csproj`.

### `format`
`dotnet tool restore` (pins **csharpier `1.2.6`** via [`.config/dotnet-tools.json`](../.config/dotnet-tools.json))
then `dotnet csharpier check .`. The repo has never been csharpier-formatted, so the check
currently reports drift — hence `allow_failure: true` (advisory). [`.csharpierignore`](../.csharpierignore)
keeps it off `bin/`, `obj/`, `publish/`, and the Node `tron-verifier/`.

**Graduation to blocking:** land one dedicated commit running `dotnet csharpier format .` (≈242
of ~271 in-scope `.cs`/`.csproj` files actually change — whitespace/layout only; csharpier does
*not* rename namespaces, so `CoinHalls.Api.*` and `Utilities.*` are untouched), then delete
`allow_failure: true` from the `format` job.

### `deploy`
Mirrors `deploy.sh` and runs on the prod box, so the locally-built image is reachable by compose:

```
dotnet publish → docker build -t coinbank.api . → docker-compose down → up -d --build → ps
```

Uses **`docker-compose` (v1)** to match `deploy.sh` (the proven invocation on that box). `--build`
ensures the `tron.coinbank.com` sidecar is rebuilt from `tron-verifier/`; `cb.coinbank.com` uses
the `coinbank.api` image built in the step before. Secrets are read from `$DEPLOY_ENV_FILE` via
`docker-compose --env-file` (the compose file substitutes `$var` placeholders) — the runner checks
out to a fresh dir, so compose's auto-load of a `.env` in CWD (how `deploy.sh` works today) won't
fire; the explicit `--env-file` replaces it.

## Supporting config

- [`Directory.Build.props`](../Directory.Build.props) — enables the built-in .NET 8 Roslyn analyzers
  (`AnalysisMode=Recommended`) across all 4 projects as **warnings** (`TreatWarningsAsErrors=false`,
  because the codebase is `<Nullable>disable</Nullable>` with intentional house anti-patterns).
- [`Directory.Packages.props`](../Directory.Packages.props) — NuGet **Central Package Management**:
  every package version lives here; the `.csproj` files carry version-less `<PackageReference>`s.

## One-time server setup (manual, on the prod box)

1. `gitlab-runner register` → tag **`coinbank-prod`**, **shell** executor. Shell is required: the
   no-registry design (`docker build` locally + compose `image: coinbank.api`) only works if the
   image lands on the **host** daemon. A vanilla docker executor builds inside a container and the
   image never reaches the host → deploy fails (unless explicitly bound to the host docker socket).
2. `usermod -aG docker gitlab-runner` so the runner user can drive Docker.
3. Set **`DEPLOY_ENV_FILE`** to the real secret-file path (default `/srv/coinbank/.env`) in
   **Settings → CI/CD → Variables**, or fix the default in `.gitlab-ci.yml`. ⚠️ If this path is
   wrong, compose interpolates **empty strings** into every secret and brings up a broken container
   with only warnings — verify before the first deploy.
4. Ensure **shared runners** are enabled for the project (gitlab.com default) so `build`/`format`
   have a runner; the tagged `coinbank-prod` runner serves only `deploy`.
5. **Compose project cutover (one-time).** `docker-compose.yml` pins global `container_name`s
   (`cb.coinbank.com`, `tron-verifier`). The CI deploy runs from the runner's checkout dir, so its
   compose *project name* differs from the manual `deploy.sh`'s — the first CI `up` would hit
   `Conflict: container name "cb.coinbank.com" already in use`. Mitigations (do one): (a) set
   **`COMPOSE_PROJECT_NAME`** (default `coinbank` in `.gitlab-ci.yml`) to match the project the
   running containers were created under, **or** (b) before the first CI deploy, manually run
   `docker-compose down` on the box to drop the old containers and let CI recreate them under its
   pinned project. Subsequent CI deploys are self-consistent.
6. **⚠️ `coinbank-net` network is referenced but not defined.** `docker-compose.yml` puts
   `cb.coinbank.com` on `coinbank-net` (and `tron.coinbank.com` on the default network) but has
   **no top-level `networks:` block**, so a fresh `docker-compose up` errors with *"refers to
   undefined network coinbank-net"*. This is a pre-existing compose gap (it affects `deploy.sh`
   too) — confirm how the network exists on the box today (e.g. created out-of-band, or an
   `external: true` declaration in the box's compose) and either add a top-level
   `networks: { coinbank-net: { external: true } }` (if it's created outside compose) or
   `networks: { coinbank-net: { driver: bridge } }` (if compose should own it) **before the first
   deploy**. Left unedited here on purpose — changing prod network topology blind is risky.

## Verifying a change

There is no test runner; building is the only automated validation. Locally:

```bash
dotnet build CoinBank.Api/CoinBank.Api.csproj -c Release
dotnet tool restore && dotnet csharpier check .    # reports drift until the reformat commit lands
```
