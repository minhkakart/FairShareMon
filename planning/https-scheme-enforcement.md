# HTTPS Scheme Enforcement (HTTP→HTTPS redirect + HSTS)

## Objective

Make a plaintext HTTP page load impossible for the browser-facing hosts, so the SPA's
`Origin` is always `https://…` and can never fall outside `App:AllowedOrigins`. Secondarily,
make a rejected CORS preflight visible in the application log instead of silent.

## Background

Safari (and Chrome-on-iOS) could not log in. `logs/cloudflare-tunnel/tunnel-logs-from-safari-with-2-tries.csv`
records two `OPTIONS /api/v1/auth/login → 204` and **zero POSTs** — the browser rejected the
CORS preflight and never sent the real request. The two PC captures in the same directory
show the normal shape (`OPTIONS → 204` then `POST → 200`); the `204` there is the preflight,
not a failure, and the "auto retry" the user observed is just the real request following it.

The difference is the page's scheme:

| Capture | Page request |
|---|---|
| `tunnel-logs-from-safari-with-2-tries.csv:6` | `GET **http**://fairsharemon.minhkakart.com/` |
| `tunnel-logs-from-pc-browser-with-one-login.csv:27` | `GET **https**://fairsharemon.minhkakart.com/login` |
| `tunnel-logs-from-pc-browser-with-auto-refresh.csv:27` | `GET **https**://fairsharemon.minhkakart.com/dashboard` |

The Safari device loaded the SPA over plain HTTP, so its `Origin` was
`http://fairsharemon.minhkakart.com`. `App:AllowedOrigins` is
`["https://fairsharemon.minhkakart.com"]`, and `CorsExtensions.NormalizeOrigin` compares
`Uri.GetLeftPart(UriPartial.Authority)`, which **includes the scheme** — so the origin was
rejected.

Two things made this hard to see:

1. **ASP.NET Core answers a rejected preflight with a bare `204`.** `CorsMiddleware` sets
   `204` for any preflight once a policy exists and simply omits the
   `Access-Control-Allow-Origin` header when the origin is not allowed. In an access log a
   rejection and a success are byte-identical.
2. **The framework's own rejection message is filtered out twice.**
   `Microsoft.AspNetCore.Cors.Infrastructure.CorsService` logs `OriginNotAllowed` at
   **Information**, which is dropped by `"Microsoft.AspNetCore": "Warning"` in
   `Logging:LogLevel` and again by the NLog rule
   `{"logger": "Microsoft.*", "maxLevel": "Info", "final": true}` — a blackhole rule with no
   `writeTo`.

Nothing in the stack redirects HTTP→HTTPS. Every `server` block listens on `:80` only (TLS
terminates at Cloudflare) and no `Strict-Transport-Security` header is emitted anywhere.
Desktop Chrome masks this by auto-upgrading typed URLs (HTTPS-First is on by default);
Safari does not, and the HSTS pin that would normally cover it was erased when browser data
was wiped. Chrome-on-iOS is WebKit and behaves the same — so this is a property of the
device, not of the browser engine.

## Requirements

- A plaintext request to a browser-facing host is redirected to HTTPS.
- HTTPS responses carry `Strict-Transport-Security` so the second visit never uses plaintext.
- Internal / container-to-container requests must **not** be redirected.
- Non-browser hosts (the Docker registry) must not be redirected.
- A rejected CORS origin must produce an application log line naming the origin.

## Open Questions

- None outstanding.

## Assumptions

- Cloudflare sends `X-Forwarded-Proto` through the tunnel. The existing `$fwd_proto` map and
  the API's `UseForwardedHeaders()` already depend on this. If it turns out Cloudflare only
  sends `CF-Visitor`, both new maps rekey to `$http_cf_visitor` (`~*"scheme":"http"`) —
  see Verification step 6.

## Decision Log

### Decision: key the redirect on `$http_x_forwarded_proto`, not the existing `$fwd_proto`

`$fwd_proto` is defined as:

```nginx
map $http_x_forwarded_proto $fwd_proto {
    default $http_x_forwarded_proto;
    ""      $scheme;     # <-- fallback
}
```

nginx only listens on `:80`, so when the header is absent `$scheme` is **always** `"http"`.
Keying the redirect on `$fwd_proto` would therefore 301 every request that did not come from
the edge — internal probes, container-to-container calls, anything hitting nginx directly.
An absent `X-Forwarded-Proto` means "not from the edge" and must be left alone, so the new
maps read the raw header.

**Alternatives considered:** keying on `$scheme` (same defect, worse); adding a separate
internal-only `listen` port (more moving parts for no gain).

### Decision: HSTS `max-age=31536000`, no `includeSubDomains`, no `preload`

HSTS is effectively irreversible once a browser caches the pin. `includeSubDomains` would
pin **every** `*.minhkakart.com` host — including ones this project does not own and any
future plaintext subdomain — across both projects in the zone. Scoping the header to the
hosts that opt in keeps the blast radius to what we control.

### Decision: log rejected origins from our own middleware rather than unmuting `Microsoft.*`

Unfiltering `Microsoft.AspNetCore.Cors` would also emit a "policy execution successful" line
for every preflight — five or more per page load. A dedicated middleware logs only the
rejections, at Warning, and reuses `CorsExtensions.IsAllowedOrigin` so its verdict cannot
drift from the policy's.

## Implementation Plan

1. `deployment/config/nginx/nginx.conf`: add the `$redirect_to_https` and `$hsts_header`
   maps beside the existing `$fwd_proto` map.
2. New `deployment/config/nginx/snippets/https_only.conf` holding the redirect + HSTS lines.
3. `include` that snippet at the top of the `server` blocks in `conf.d/web.conf` and
   `conf.d/api.conf`.
4. New `FairShareMonApi/Middlewares/CorsOriginLoggingMiddleware.cs`, registered in
   `Program.cs` between `UseRouting()` and `UseCors(...)`.
5. Tests in `FairShareMonApi.Tests/CorsOriginLoggingMiddlewareTests.cs`.
6. Apply the equivalent nginx change to the live `app-root` proxy (below) and enable
   Cloudflare's **Always Use HTTPS**.

## ⚠️ The repo's nginx config is not what runs in production

Per `deployment-server.txt`, production serves **every** project from one shared proxy —
`app-root` at `/home/minhkakart/projects/app-root/` (container `nginx-root`), alongside
`cloudflared-tunnel-root` and `fail2ban-root`. It fronts fairsharemon, order-qr,
docker-registry and nso.

`deployment/` in this repo describes a different, **non-running** topology:

| Repo (`deployment/`) | Production (`app-root` + `fairsharemon`) |
|---|---|
| 6 services incl. `fsm-nginx`, `fsm-fail2ban` | fairsharemon runs **4** (api, web, mariadb, redis); nginx/fail2ban/cloudflared live in `app-root` |
| `conf.d/api.conf` + `conf.d/web.conf` | one `conf.d/fairsharemon.conf` holding both server blocks |
| `proxy_set_header` repeated per server | shared `snippets/proxy_headers.conf`, included per location |
| upstreams `api:8080`, `web:5173` | `fairsharemon-api-1:8080`, `fairsharemon-web-1:5173` |
| has a `= /api/v1/health` location | no health location; no container healthcheck on the API |
| `docker-compose.yml` | server uses `docker-compose.yaml` |

**Editing only this repo's nginx config fixes nothing in production.** The change below is
the one that matters; the repo copy is kept in step with it so it stops teaching the wrong
thing on this specific point. Resyncing (or retiring) the rest of `deployment/` is a
separate job — see Future Improvements.

## Apply on the deploy host (`~/projects/app-root`)

Because one proxy fronts everything, this single change fixes **FairShareMon and
quick-ordering at once**.

**1. New `config/nginx/snippets/https_only.conf`:**

```nginx
# Force HTTPS for browser-facing hosts. `include` this at the TOP of a server block
# (server context, not inside a location).
#
# Opt-in per host on purpose: docker-registry is excluded, because Docker client
# tooling should not be pushed through a redirect.
if ($redirect_to_https) { return 301 https://$host$request_uri; }

# HSTS. $hsts_header is empty on non-HTTPS requests, and nginx emits nothing for an
# empty add_header value — so this never lands on the 301 above.
add_header Strict-Transport-Security $hsts_header always;
```

**2. `config/nginx/nginx.conf`** — add beside the existing `$fwd_proto` map:

```nginx
# Redirect only when the EDGE explicitly reported a plaintext client connection.
# Do NOT key this on $fwd_proto: it falls back to $scheme when the header is absent,
# and nginx only listens on :80, so $scheme is ALWAYS "http" — every internal or
# direct container-to-container request would 301 into a loop. An absent header
# means "did not come from the edge" and must not redirect.
map $http_x_forwarded_proto $redirect_to_https {
    default 0;
    "http"  1;
}

# HSTS only on genuinely-HTTPS responses. Empty value => nginx emits no header.
# 1 year, no includeSubDomains / no preload: scoped to the hosts that opt in below,
# so other *.minhkakart.com hosts are unaffected and this stays reversible.
map $http_x_forwarded_proto $hsts_header {
    default "";
    "https" "max-age=31536000";
}
```

**3.** Add `include /etc/nginx/snippets/https_only.conf;` at the top of each browser-facing
`server` block:

- `conf.d/fairsharemon.conf` — `fairsharemon-api.minhkakart.com`, `fairsharemon.minhkakart.com`
- `conf.d/order-qr.conf` — `order-qr.minhkakart.com`, `admin-order.minhkakart.com`,
  `api-order.minhkakart.com`, `redis-order-qr.minhkakart.com`

Not changed: `docker-registry.conf` (Docker client tooling, not a browser) and `default.conf`
(the `return 444` catch-all).

**4. Cloudflare** → SSL/TLS → Edge Certificates → **Always Use HTTPS**. Stops the plaintext
request at the edge before it costs a tunnel round-trip. The nginx change is the backstop,
not a replacement.

Caveats worth keeping in mind when editing these files later:

- A server-level `add_header` is silently dropped in any `location` that declares its own
  `add_header`. No current location does — keep it that way, or repeat the include there.
- Browsers do not follow redirects on a preflight `OPTIONS`. This is not a regression: an
  https page calling an http API is already blocked as mixed content before it leaves the
  browser, so a plaintext preflight is already dead today. The 301 helps non-browser clients.

## Impact Analysis

- **Infrastructure**: two new nginx maps, one new snippet, six `include` lines on the live
  proxy (two mirrored in this repo). No container or compose changes.
- **APIs**: one new middleware in the pipeline, before `UseCors`. It never modifies the
  response — `UseCors` still owns the decision. No endpoint changes.
- **UI**: none. The SPA is unchanged; `VITE_API_BASE_URL` is already the https API origin.
- **Database / Services**: none.
- **Security**: plaintext page loads eliminated for the covered hosts; HSTS prevents the
  first-request downgrade on repeat visits. CORS remains as strict as before — no origin was
  added to the allowlist.

## Progress Log

### 2026-08-10

- Diagnosed from the three tunnel-log captures; identified the http page load as the cause
  and the silent 204 as the reason it was invisible.
- Created planning doc, recording the `$http_x_forwarded_proto`-vs-`$fwd_proto` trap and the
  HSTS scoping decision.
- Documented the repo-vs-production nginx drift found in `deployment-server.txt`.
- Added the maps + `snippets/https_only.conf` to `deployment/config/nginx/`, included from
  `conf.d/web.conf` and `conf.d/api.conf`.
- Added `Middlewares/CorsOriginLoggingMiddleware.cs` and registered it in `Program.cs`.
- Added `FairShareMonApi.Tests/CorsOriginLoggingMiddlewareTests.cs`.

## Verification

On the server, `cd ~/projects/app-root` (compose service `nginx`, container `nginx-root`):

1. `docker compose exec nginx nginx -t` — config parses.
2. **The trap test:** `docker compose exec nginx curl -sI -H 'Host: fairsharemon.minhkakart.com' http://localhost/`
   → must **not** be `301`. Proves a request with no `X-Forwarded-Proto` is left alone.
3. `curl -sI http://fairsharemon.minhkakart.com/` → `301` +
   `Location: https://fairsharemon.minhkakart.com/`. Repeat for `order-qr` and `admin-order`.
4. `curl -sI https://fairsharemon.minhkakart.com/ | grep -i strict-transport` →
   `max-age=31536000`. Confirm it is **absent** from the step-3 http response.
5. `curl -sI http://docker-registry.minhkakart.com/v2/` → **not** redirected (exclusion held).
6. If step 3 returns 200 instead of 301, Cloudflare is sending `CF-Visitor` rather than
   `X-Forwarded-Proto` — rekey both maps on `$http_cf_visitor` (`~*"scheme":"http"`) and
   re-test. Check this before assuming the map is wrong.
7. Rejected preflight is now audible:
   `curl -i -X OPTIONS https://fairsharemon-api.minhkakart.com/api/v1/auth/login -H 'Origin: http://fairsharemon.minhkakart.com' -H 'Access-Control-Request-Method: POST'`
   → still `204` with no `Access-Control-Allow-Origin`, but
   `docker compose -p fairsharemon logs fsm-api` now shows a Warning naming the origin.
8. Same call with `Origin: https://fairsharemon.minhkakart.com` → `204` **with**
   `Access-Control-Allow-Origin`, and no Warning.
9. `dotnet test .\FairShareMonApi.sln`.
10. **End-to-end, the original repro:** on the Safari device, clear website data, type the
    bare domain (no scheme), log in. Expect the 301, then a successful login.

## Final Outcome

In-repo work complete: nginx maps + `https_only.conf` snippet mirrored into
`deployment/config/nginx/`, `CorsOriginLoggingMiddleware` added and registered, tests added.

Two steps remain outside the repo and are the ones that actually fix production: applying
the "Apply on the deploy host" section to `~/projects/app-root`, and enabling Cloudflare's
**Always Use HTTPS**. Until those land, the Safari symptom persists.

## Future Improvements

- **Resync or retire `deployment/`.** It describes a topology production replaced with the
  shared `app-root` proxy. A deployment directory that does not match production is a trap.
- Consider `includeSubDomains` + HSTS preload once every `*.minhkakart.com` host is
  confirmed HTTPS-only.
- Port the middleware's approach to a shared package if a third project appears.
