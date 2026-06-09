# Contributing to SLT.Manage

Thank you for contributing. This document covers how we work on this repo: commit messages,
merge requests, validation, and a few house rules that differ from typical .NET projects.

For architecture and where code lives, start with [README.md](README.md),
[CODEBASE_MAP.md](CODEBASE_MAP.md), and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## Commit messages

We follow **[Conventional Commits](https://www.conventionalcommits.org/)**, adapted from
[qoomon's cheatsheet](https://gist.github.com/qoomon/5dfcdf8eec66a051ecd85625518cfd13).
A consistent subject line makes history scannable and supports automated changelogs if we add
them later.

### Format

```
<type>(<optional scope>): <description>

<optional body>

<optional footer>
```

Multi-line example:

```bash
git commit -m "feat(treasury): add maturity calendar export" \
  -m "Adds PDF/Excel export for the maturity calendar tab." \
  -m "Closes #123"
```

### Subject line rules

| Part | Rule |
|------|------|
| **type** | Required — see [Types](#types) below |
| **scope** | Optional — logical area of the repo (see [Scopes](#scopes)) |
| **description** | Required — imperative, present tense (`add`, not `added`); lowercase first letter; no trailing period |
| **breaking change** | Append `!` before `:` (e.g. `feat(api)!: remove status endpoint`) and describe impact in the footer |

Think: *"This commit will…"* when writing the description.

### Types

| Type | When to use |
|------|-------------|
| `feat` | New or changed API/UI behavior — new endpoint, report field, permission gate, export format |
| `fix` | Bug fix for behavior introduced by a prior `feat` (wrong aggregation, bad filter, auth edge case) |
| `refactor` | Restructure code without changing API or report output |
| `perf` | Performance improvement (special case of refactor — worth calling out in changelogs) |
| `style` | Formatting, whitespace, layout only — no behavior change |
| `test` | Add or fix tests *(no test project today — reserved for when one exists)* |
| `docs` | Documentation only (`README`, `docs/`, ADRs, handover notes) |
| `build` | Build tooling, dependencies, SDK/project files, `Directory.Packages.props` |
| `ops` | CI/CD, Docker, deploy scripts, runner config, infrastructure |
| `chore` | Maintenance that doesn't fit above — `.gitignore`, tooling pins, repo housekeeping |

#### Which type? (checklist)

Work through in order; stop at the first match:

1. Bug fix? → **`fix`**
2. New or changed API/report behavior? → **`feat`**
3. Performance improvement? → **`perf`**
4. Code restructure, same behavior? → **`refactor`**
5. Formatting only? → **`style`**
6. Tests only? → **`test`**
7. Documentation only? → **`docs`**
8. Build tools or dependencies? → **`build`**
9. CI/CD, Docker, deploy? → **`ops`**
10. Anything else → **`chore`**

### Scopes

Scopes are **optional** but help when scanning history. Prefer a **logical module**, not an issue
ID.

| Scope | Typical use |
|-------|-------------|
| `manage` | Host project — controllers, pipeline, `Program.cs`, filters |
| `services` | `SLT.Services` — business logic, DTOs, feature modules |
| `domain` | `SLT.Domain` — collections, repositories, indexes |
| `utilities` | `SLT.Utilities` — shared framework code |
| `treasury` | `_Treasury` module (reports, calculators, exports) |
| `report` | `_Report` module |
| `user` | `_User` / auth |
| `ci` | `.gitlab-ci.yml`, CI docs |

You may reference an issue in the **description** or **footer** (`Closes #123`), not as the scope.

### Body and footer

- **Body** — motivation and contrast with previous behavior (optional).
- **Footer** — issue references, co-authors, breaking-change details.
- **Breaking changes** must include a footer line starting with `BREAKING CHANGE:` when the
  subject line alone is not enough.

### Special commits

| Situation | Message |
|-----------|---------|
| Initial commit | `chore: init` |
| Merge | Default Git message: `Merge branch '…'` |
| Revert | Default Git message: `Revert "…"` |

### Examples (this repo)

```
feat(treasury): add wallet analysis endpoint
fix(report): correct total overview when wallet is soft-deleted
refactor(domain): extract stake status filter into repository helper
perf(treasury): avoid loading all stakes for overview aggregation
docs: add treasury reporting handover for frontend
build: bump MongoDB.Driver via Directory.Packages.props
ops(ci): make csharpier format check blocking
chore: update .gitignore for publish output
feat(manage)!: require Reporter permission on all report actions

BREAKING CHANGE: unauthenticated callers receive 403 on former open endpoints.
```

### Versioning (if we semver releases)

When tagging releases from conventional commits:

- **Breaking changes** (`!` or `BREAKING CHANGE:`) → **major**
- **`feat` / `fix`** → **minor**
- Everything else → **patch**

---

## Merge requests

We use **GitLab**; target branch is **`main`**.

1. **Branch** — short, kebab-case, prefixed by type when helpful: `feat/treasury-exports`,
   `fix/wallet-overview-totals`, `docs/ci-handover`.
2. **Open an MR** — pipelines run on MR events only (no duplicate branch pipelines).
3. **Keep MRs focused** — one logical change; split large features into reviewable slices.
4. **Description** — what changed, why, and how to verify (build steps, manual API checks).
5. **CI must pass** — at minimum the Release **build** job. The **format** job (csharpier) is
   advisory until the tree is reformatted; see [docs/CI.md](docs/CI.md).
6. **Deploy** — production deploy is **manual** on `main` after merge; never automatic.

There is **no test project** yet — `dotnet build` is the primary automated gate.

---

## Local validation

Before pushing:

```bash
dotnet build SLT.Manage/SLT.Manage.csproj -c Release
# optional — matches CI quality stage (currently advisory):
dotnet tool restore && dotnet csharpier check .
```

Do **not** run `dotnet format` or mass-reformat the tree unless the MR is explicitly for that.
Match the style of neighboring files.

---

## Code conventions (summary)

These are load-bearing; full detail lives in [CLAUDE.md](CLAUDE.md) and
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

| Topic | Convention |
|-------|------------|
| Layering | `SLT.Manage → SLT.Services → SLT.Domain → SLT.Utilities` — one-way only |
| Mongo wrapper | **Monjo** (intentional spelling) — `MonjoRepository<T>`, soft-delete on reads |
| HTTP reads | **`POST`** + `[FromBody]` even for queries |
| DI | Autofac marker interfaces (`IScopedDependency`, etc.) — no manual `services.Add` |
| Nullability | `<Nullable>disable</Nullable>` — guard nulls manually |
| Auth | Custom `[Authorize(Permissions.X)]` on report endpoints |
| Secrets | Never commit or paste real values from `appsettings.json` — see [docs/SECURITY.md](docs/SECURITY.md) |

When adding a feature, use the layer map in [CODEBASE_MAP.md](CODEBASE_MAP.md): collection →
repository → service module → controller action.

---

## Optional: enforce commit format locally

To validate messages before they land, consider
[git-conventional-commits](https://github.com/qoomon/git-conventional-commits) or a local
`commit-msg` hook. This repo does not ship a hook by default.

---

## References

- [Conventional Commits specification](https://www.conventionalcommits.org/)
- [Conventional Commits cheatsheet (qoomon)](https://gist.github.com/qoomon/5dfcdf8eec66a051ecd85625518cfd13)
- [Angular CONTRIBUTING.md](https://github.com/angular/angular/blob/main/CONTRIBUTING.md)
- [docs/CI.md](docs/CI.md) — pipeline stages and deploy
- [docs/WORKFLOW.md](docs/WORKFLOW.md) — planning pipeline for larger work
