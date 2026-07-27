# CI pipeline — versioned container images

## Objective

Automate what is today a manual build-host ritual: build the two deployable images
(`fairsharemon-api`, `fairsharemon-web`), tag them from git refs, and push them to the private
registry — so image versions are derived from the repository instead of hand-edited in an untracked
file, and `latest` always resolves to the newest release.

## Background

The repo is a monorepo with two deployables (`FairShareMonApi/`, `FairShareMonWeb/`) and a Docker
Compose production stack in `deployment/`. Both images already have Dockerfiles and are already
registry-qualified in `deployment/docker-compose.yml`:

```
image: ${REGISTRY:-docker-registry.minhkakart.com}/fairsharemon-api:${API_VERSION:-1.0.0}
image: ${REGISTRY:-docker-registry.minhkakart.com}/fairsharemon-web:${WEB_VERSION:-1.0.0}
```

`deployment/Deployment-guild.txt` documents the release model as an "update pack": bump
`API_VERSION` / `WEB_VERSION` in `deployment/.env`, then `docker compose build && docker compose
push` on a build host and `docker compose pull && docker compose up -d` on the deploy host.

Before this change there was no `.github/` directory and no CI of any kind. So:

- Image versions existed only in `deployment/.env`, which is gitignored — nothing tied a published
  tag back to a commit.
- There was no `latest` tag; every deploy had to name an explicit version.
- Nothing gated a push, so a frontend that fails `tsc` or its unit tests could be published.
- Building depended on one person's machine being set up correctly.

## Requirements

- Build both images on GitHub Actions (GitHub-hosted runners) and push to
  `docker-registry.minhkakart.com`.
- Derive versions from git refs. Preserve the existing property that api and web version
  **independently** — releasing one must not re-tag the other.
- Publish a `latest` tag that tracks the newest release.
- Keep a rolling, traceable tag for the tip of `master` that is distinct from `latest`.
- Never push from a pull request; still build there, so a broken Dockerfile or a compile error
  fails the PR.
- Gate each image on its own test suite, without CI ever touching the production database.
- Build + push only. Deployment stays a manual `docker compose pull && up -d` on the host.
- Do not require any new committed secrets; the registry credentials live in GitHub secrets.

## Decisions

### Decision — registry and runner

Keep the existing private registry `docker-registry.minhkakart.com`; build on GitHub-hosted
`ubuntu-latest`.

**Reason:** the registry is publicly reachable with authentication, so a hosted runner can push to
it. Hosted runners need no maintenance and give clean isolation for pull-request builds.

**Alternatives considered:** GHCR (would require changing `REGISTRY` on the deploy host); a
self-hosted runner on the WSL deploy host (faster warm layer cache and LAN-speed pushes, but ongoing
maintenance and a real security concern if the repo ever accepts fork PRs).

### Decision — component-scoped release tags

Releases are cut with `api-vX.Y.Z` and `web-vX.Y.Z`.

**Reason:** preserves the independent-versioning model that `deployment/.env.example` already
documents ("api and web version independently, so a web-only release doesn't re-tag api"). A single
shared `vX.Y.Z` would republish an unchanged image under a new number.

**Consequence:** `docker/metadata-action` must use `type=match` rather than `type=semver` — its
semver parser cannot read the `api-` / `web-` prefix — and `flavor: latest=false` is required so
auto-latest does not fire on the wrong tag.

### Decision — `latest` means newest release, `edge` means tip of master

**Reason:** the standard Docker convention. `latest` only moves when someone deliberately cuts a
release, so a host pinned to `latest` can never pick up an unreleased merge. `edge` exists for
smoke-testing master before tagging.

### Decision — `dotnet test` against throwaway service containers

**Superseded decision (2026-07-27, earlier the same day):** skip `dotnet test` in CI and gate the
API image on compilation only. Requested because the integration suite talks to a real MariaDB +
Redis and the intent was not to point CI at the production database. The recorded trade-off was
that an API regression which still compiles would publish.

**Current decision:** `dotnet test` runs, against `mariadb:11.7` + `redis:7.4` **service
containers** created and destroyed with the job.

**Reason:** the original concern was pointing CI at the real database — which service containers
avoid entirely. The fixtures already have the seams for it (`FSM_TEST_CONNECTION` /
`FSM_TEST_REDIS`), so no test code changes. This closes the "compiles therefore publishes" gap.

**Wiring notes that are non-obvious and were verified against the repo:**
- Steps run on the runner VM, not in a container, so the services must be reached over **mapped
  host ports** at `127.0.0.1`. Using `localhost` risks resolving to `::1`, which MySqlConnector
  fails on. (A job with `container:` would instead address services by name and need no port maps.)
- The CI Redis has **no password**: GitHub's `services` block supports only `image`, `credentials`,
  `env`, `ports`, `volumes`, `options` — there is no `command:`, so the `--requirepass` argument
  used in `docker-compose.yml` cannot be passed.
- `deployment/config/mariadb/init_database.sql` **cannot be mounted** — service containers start
  before `actions/checkout`, so the workspace is empty. `MARIADB_DATABASE: fairsharemon` replaces
  it; the database-level charset is irrelevant because the migrations pin
  `utf8mb4`/`utf8mb4_unicode_ci` per column (`AppDbContext.cs:52-53`).
- **Both** env pairs are required: `FSM_TEST_*` for the fixture probes, and
  `ConnectionStrings__Default` / `Redis__Configuration` for the `WebApplicationFactory` tests, which
  boot real `Program.cs` and read configuration normally.
- `dotnet ef database update` must run before `dotnet test`: `IntegrationTestBase` opens a per-test
  transaction and rolls it back, but never creates the schema. It works without a `--connection`
  flag because `AppDbContextDesignTimeFactory.cs:20` calls `AddEnvironmentVariables()`.
- The health checks double as the anti-false-green guard. `SkippableFact` means an unreachable
  dependency yields a *green* run that tested nothing; because the runner blocks until both health
  checks pass, connectivity is proven before the first step.

**Cost:** roughly 3–4 minutes added to API builds, and ~25s of service-container startup.

### Decision — web gate

`pnpm lint`, `pnpm build` (which runs `tsc -b`), and `pnpm test` gate the web image.

**Reason:** matches the quality bar in `FairShareMonWeb/CLAUDE.md`. These are MSW-mocked and need
no database or backend at all. Playwright E2E is not included (not requested) — see Future
Improvements.

## Assumptions

- `docker-registry.minhkakart.com` accepts pushes from GitHub-hosted runners with basic auth.
- Repository variables `REGISTRY` and `VITE_API_BASE_URL` and secrets `REGISTRY_USERNAME` /
  `REGISTRY_PASSWORD` are configured by the owner in GitHub Settings (see Impact Analysis).
- The default branch stays `master` and releases are cut from it.
- Playwright E2E is not part of the gate (not requested); `deployment/**`-only changes publish
  nothing, because they do not change image content.

## Implementation plan

1. Add `.github/workflows/ci.yml` with five jobs:
   - `scope` — resolves which components to build (release-tag prefix, `workflow_dispatch` input,
     or a plain `git diff --name-only` path check). Falls back to building both when the diff range
     cannot be resolved, so a force-push never silently skips a component.
   - `web-checks` — pnpm install (frozen lockfile) → lint → build → vitest.
   - `api-tests` — `mariadb:11.7` + `redis:7.4` service containers (health-gated, host-port mapped)
     → `dotnet tool restore` → `dotnet ef database update` → `dotnet test` → upload the `.trx`.
   - `image-api` — needs `api-tests`. Buildx, conditional registry login,
     `docker/metadata-action` tag derivation, `docker/build-push-action` with `push: false` on PRs.
   - `image-web` — needs `web-checks`. Same, plus the `VITE_API_BASE_URL` build arg and a fail-fast
     guard for it.
2. Rewrite the "Images" block in `deployment/.env.example` to document the tag scheme CI publishes.
3. Update `deployment/Deployment-guild.txt`: CI publishes, the host only pulls; add a RELEASING
   section and a rollback note.
4. Fix a pre-existing documentation error found while doing (3) — see Impact Analysis.

## Impact analysis

**Infrastructure** — new `.github/workflows/ci.yml`. Requires one-time configuration in GitHub
Settings:

| Kind | Name | Value |
|---|---|---|
| Variable | `REGISTRY` | `docker-registry.minhkakart.com` (falls back to this if unset) |
| Variable | `VITE_API_BASE_URL` | `https://fairsharemon-api.minhkakart.com` |
| Secret | `REGISTRY_USERNAME` | registry user |
| Secret | `REGISTRY_PASSWORD` | registry password / token |

**APIs / Database / UI / Services** — none. No application code is touched; no schema change; no
runtime behavior change. The images CI produces are byte-for-byte the same builds the Dockerfiles
already described.

**Documentation** — `deployment/.env.example` and `deployment/Deployment-guild.txt` updated.

**Pre-existing bug fixed in passing:** `Deployment-guild.txt` section A claimed
`App__RunMigrationsOnStartup=true` is set in `docker-compose.yml`. It is not — the flag only comes
from the host-mounted `appsettings.Production.local.json`, and the committed default in
`appsettings.json` is `false`. A reader following the guide as written would get an API that never
applies migrations.

**Known constraint carried forward:** the web image is environment-specific. `VITE_API_BASE_URL` is
inlined by Vite at build time and asserted in `FairShareMonWeb/Dockerfile`, so one image serves
exactly one API origin and cannot be promoted between environments.

## Progress log

### 2026-07-27

- Explored the repo: confirmed no existing CI, two Dockerfiles, compose already registry-qualified,
  no git tags, no `Directory.Build.props`, no `<Version>` in either csproj, `package.json` version
  a `0.0.0` placeholder — `deployment/.env` was the only version source of truth.
- Confirmed the .NET test harness reads `FSM_TEST_CONNECTION` / `FSM_TEST_REDIS`
  (`FairShareMonApi.Tests/Infrastructure/DatabaseFixture.cs`, `RedisFixture.cs`) and that
  `IntegrationTestBase` only opens a per-test transaction — it never creates the schema, so any
  future CI test job must apply migrations first.
- Resolved the open questions with the owner: keep the private registry, GitHub-hosted runners,
  component-scoped tags, `latest` = newest release, drop `dotnet test`, build + push only.
- Added `.github/workflows/ci.yml` (4 jobs at this point).
- Updated `deployment/.env.example` and `deployment/Deployment-guild.txt`; fixed the
  `RunMigrationsOnStartup` documentation error.
- Verified: workflow YAML parses; the `scope` decision logic exercised against real history —
  web-only (`ebedc62`), api-only (`069c4c8`), both (`1777b56`), deployment-only (`04b5bc8`, builds
  nothing), release tags, dispatch inputs, force-push fallback; tag regexes produce the expected
  versions with no cross-component bleed. Web gate confirmed green on master (lint clean, build OK,
  114 files / 949 tests).
- **Reversed the drop-`dotnet test` decision** after establishing that service containers keep CI
  away from the production database entirely. Added the `api-tests` job and made `image-api` depend
  on it; updated the gate description in `Deployment-guild.txt`. Re-validated the workflow: job
  graph, service definitions, env pairs, and every referenced path resolve.
- Not verified locally: the image builds and the registry push (no `docker`), and the `api-tests`
  job (no `dotnet` on this machine). First CI run exercises all three.
- Landed the pipeline on `master` by fast-forward (`fccd952`) and cut the first CI-published
  release, **2.0.0 for both components**. Three runs, all green:

  | Run | Ref | Outcome |
  |---|---|---|
  | #1 | `master` | all 5 jobs success → `:edge`, `:sha-fccd952` for both images |
  | #2 | `web-v2.0.0` | success; api jobs correctly skipped → web `:2.0.0 :2.0 :latest :sha-fccd952` |
  | #3 | `api-v2.0.0` | success; web jobs correctly skipped → api `:2.0.0 :2.0 :latest :sha-fccd952` |

  Confirmed by this: registry authentication and push work from a GitHub-hosted runner; the registry
  accepts the manifests with `provenance: false`; the Cloudflare layer-size concern did not
  materialise; component scoping means a release tag rebuilds only its own image; and the
  service-container test gate passes on both a master push and a release tag. Test-log review
  cleared the `SkippableFact` false-green risk — the integration tests really ran.

  Note on content: `fccd952` changes only CI config and documentation, so 2.0.0 is functionally
  identical to the previously deployed build — a repackage, not a feature release, and it carries no
  new migrations.

## Final outcome

CI is in place. Merges to `master` publish `:edge` and `:sha-<short>`; pushing `api-vX.Y.Z` or
`web-vX.Y.Z` publishes `:X.Y.Z`, `:X.Y`, `:latest`, and `:sha-<short>` for that component only.
Pull requests build both images without pushing. Each image is gated on its own suite — web on
lint + type-check + vitest, api on `dotnet test` against throwaway MariaDB/Redis service containers.
Deployment is unchanged: the host still runs `docker compose pull && docker compose up -d`, and
rollback is re-pinning a previously published version tag.

**Verified in production on 2026-07-27** by the first three runs (see Progress Log). Registry
authentication, the push path, component scoping, and the service-container test gate all worked on
first use. The `SkippableFact` false-green risk was checked against the run log and cleared — the
integration tests genuinely executed.

## Future improvements

- Add Playwright E2E to the web gate (`pnpm exec playwright install --with-deps chromium` then
  `pnpm test:e2e`; MSW-mocked, no backend needed) and upload the HTML report as an artifact.
- Trim the redundant `dotnet build` from `FairShareMonApi/FairShareMonApi/Dockerfile` — it runs both
  `build` and `publish`, roughly doubling compile time.
- Speed up `api-tests` by adding `--tmpfs=/var/lib/mysql` to the MariaDB service `options`, putting
  the datadir in RAM. Durability is irrelevant for a per-run container. Do this only once the job is
  reliably green, so any failure has a single cause.
- Auto-create a GitHub Release with generated notes on each component tag.
- Move the web app to runtime config injection so one web image can be promoted across
  environments.
- Add container `HEALTHCHECK` directives for `api` and `web` (compose currently has none for them).
- Optionally pin third-party actions to commit SHAs rather than major tags.
