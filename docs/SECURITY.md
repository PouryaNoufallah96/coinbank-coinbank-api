# Security

Security findings for CoinBank API, grouped by category with remediation. Secret **values** are
never reproduced here — only categories and locations. Severity reflects impact + exposure.

> Quick rule: secrets belong in env vars / a secret store, never in source control. `.env` is
> git-protected by a project hook — do not read it or commit its contents.

> **Latest full audit:** [`docs/reviews/security-perf-review-FINAL.md`](reviews/security-perf-review-FINAL.md)
> — extends this baseline with new criticals (a `.env` blob in pushed
> history, live permissive CORS, an unauthenticated file-read, a no-signature token mint) plus
> performance + architecture findings. This file remains the canonical KF-xx baseline.

## CRITICAL — committed secrets / backdoors

These are the highest priority: fix by **rotating the value, moving it to env/secret store, and
purging it from git history** (`git rm --cached <file>` + a history scrub if it was ever committed).

- **`test` application in `ApplicationPoolSettings` ships with `PreSharedKey = "test"` and
  `MasterSignature = "test"`** (`CoinBank.Api/appsettings.json`). `MasterSignature` is a nonce-less
  bypass of the entire signature middleware — anyone holding it skips replay protection and request
  authenticity. **Remediate:** remove the `test` application from committed config entirely; never
  ship dev applications/master signatures to a deployed environment; rotate any value that leaked.
- **Hard-coded Sentry DSN in `CoinBank.Api/Program.cs`.** A DSN in source control lets anyone send
  (or spoof) events to your Sentry project. **Remediate:** move the DSN to an env var; rotate the
  DSN; `git rm --cached` is not enough on its own if it was committed — scrub history.

## CRITICAL — high-value operational secrets (env-supplied)

These are supplied via env / `.env` today; the risk is exposure if `.env` or compose files leak.

- **Blockchain operator private key** (`BlockChainSettings.PrivateKey`) — signs BEP20 transactions
  in `_BlockChain/BlockChainService`. Compromise = direct loss of funds. **Remediate:** store in a
  dedicated secret manager / HSM; never log it; rotate on any suspicion; scope the signing wallet.
- **JWT signature key + encryption key** (`JwtServiceSettings.SignatureKey` / `EncryptionKey`) and
  **client-info secrets** (`ClientInfo`). Compromise = forge/decrypt JWE tokens. **Remediate:** env
  /secret store only; rotate (invalidates issued tokens).
- **Application pre-shared keys + master signatures** (`ApplicationPoolSettings.Applications[].*`) —
  request-authenticity HMAC keys. **Remediate:** env/secret store; rotate; never commit.
- **Mongo connection string** (`MonjoSettings.ConnectionString`) and **CMC price API key**
  (`PriceSetting.CMCApiKey`). **Remediate:** env/secret store; rotate.

## HIGH — configuration / hardening

- **Allow-all firewall by default.** Default `FirewallSettings` / docker-compose firewall rules are
  effectively `Allow *`. **Remediate:** ship a restrictive default; allow-list IPs/paths explicitly;
  treat `Allow *` as a misconfiguration in deployed environments.
- **`NotFoundException(string)` returns HTTP 500 instead of 404.** A correctness bug with security
  relevance: incorrect status codes mask "not found" as "server error" and can leak internal state
  in dev (see below). **Remediate:** fix the ctor chain so the HTTP status matches the intended 404.

## MEDIUM — information disclosure

- **Verbose error leakage in Development.** Sentry is configured with `SendDefaultPii = true`, and in
  Development `CustomExceptionHandlerMiddleware` serializes full Exception/StackTrace/InnerException/
  `AdditionalData` into the response body. **Remediate:** set `SendDefaultPii = false`; ensure the dev
  detail branch can never run in a deployed environment; redact PII from captured events.

## Practices to keep

- Secrets via env vars / `.env` (git-protected) — keep `.env` out of git and out of images.
- `.dockerignore` already restricts the image build context to `/publish/**` — keep it that way so
  source, `.env`, and configs are not baked into the image.
- Encrypted (JWE) tokens with `ClockSkew = TimeSpan.Zero`, HMAC request signatures + replay nonces,
  and per-key rate limiting are good edge controls — preserve them.

## If a secret was exposed

1. **Rotate** the credential at its source immediately.
2. **Move** it to an env var / secret store; remove it from `appsettings.json`/`Program.cs`.
3. **Purge** it from git: `git rm --cached <file>` for tracked files, and scrub history
   (e.g. `git filter-repo`) if it was ever committed; force-push and notify collaborators.
4. **Audit** access logs (Sentry, Mongo, RPC provider, exchange) for misuse during the exposure window.

## Optional — deep AI security audit (deepsec)

For a periodic *deep* pass beyond the `secret-scan` / `dependency-audit` skills and the
diff-scoped `review` skill, consider [`vercel-labs/deepsec`](https://github.com/vercel-labs/deepsec)
— an AI-agent vulnerability scanner that reasons over the whole repo for auth gaps, broken
access control, crypto misuse (IV reuse, missing constant-time compares, algorithm confusion),
SSRF, and injection in *your own* logic. It has real .NET / ASP.NET Core support and is
host-agnostic (scans a local tree — no GitHub/GitLab dependency).

Use it as a **one-shot (then occasional) audit — NOT standing CI, NOT a wrapped skill** (that
would be over-engineering for these repos). It does **not** replace `secret-scan` (deepsec does
NOT catch secrets committed in `appsettings.json`) or `dependency-audit` (it does no CVE/SCA);
it complements them by finding logic/auth/crypto flaws those miss — high value on hot-wallet,
JWE, and on-chain code.

```bash
npx deepsec init                      # scaffolds .deepsec/ (config + data/<id>/INFO.md + SETUP.md)
cd .deepsec && pnpm install           # deepsec is a Node tool; this is its own dep install
# add an AI credential per .deepsec/SETUP.md (Vercel AI Gateway key or an Anthropic token)
```

Then fill `.deepsec/data/<id>/INFO.md` (≤100 lines) with the project primitives the regex layer
can't anchor — **hot-wallet key handling, the custom JWE auth flow, Monjo/MongoDB data access,
Nethereum on-chain paths, and (coinbank only) the Node `tron-verifier` sidecar** — then:

```bash
pnpm deepsec scan                     # fast, no AI
pnpm deepsec process --limit 50       # CALIBRATE cost first (Opus ≈ $25–60 / 100 files)
pnpm deepsec process --concurrency 5  # full pass once satisfied
pnpm deepsec revalidate --min-severity HIGH
pnpm deepsec export --format md-dir --out ./findings
```

Caveats: cost scales with file count (always `--limit` first); it runs a coding agent with shell
access (your source is trusted; prefer its sandbox mode if any vendored third-party code is present).
`.deepsec/` is gitignored by this repo's AI-layer setup.
