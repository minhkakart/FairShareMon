# PWA standalone home-screen app (minimal)

Make the web app launch as a **standalone app** (its own window, no browser
chrome) when a user adds it to their iPhone/Android home screen, and use the
existing favicon as the shortcut icon. Today the shortcut just reopens a Safari
tab at the URL because the site ships no web app manifest, no Apple meta tags,
and no PNG icons.

## Objective

Add the three pieces a manual "Add to Home Screen" flow needs to launch
full-screen standalone on iOS + Android:

1. A **web app manifest** (`public/manifest.webmanifest`) with
   `display: "standalone"`, `start_url`/`scope` `/`, brand `theme_color`
   `#863bff`, white `background_color`, and PNG icon entries (192, 512, maskable).
2. **`<head>` tags** in `index.html`: `<link rel="manifest">`, `theme-color`,
   `description`, and the iOS `apple-mobile-web-app-*` meta set + a raster
   `apple-touch-icon` (iOS will not use an SVG for the home-screen icon).
3. **PNG icons** generated from `public/favicon.svg`, committed to `public/`.

## Locked decisions

- **Scope = minimal standalone only.** No service worker, no offline support, no
  Android auto-install prompt, no in-app "add to home screen" guidance banner.
  Rationale: manual A2HS launches standalone on both platforms with just
  manifest + Apple meta + icons; a service worker only adds offline/auto-prompt
  (not requested) and risks stale-cache bugs against the current
  `no-cache` index / `1y immutable` assets nginx setup.
- **No new npm dependency** (per `FairShareMonWeb/CLAUDE.md` — a new dep is an
  Open Question). Icons were generated **one-time** with a throwaway
  `pnpm dlx @vite-pwa/assets-generator@latest --preset minimal-2023 public/favicon.svg`
  and the PNGs committed. `package.json` and `vite.config.ts` are **unchanged**.
- **Theming:** `theme_color` = brand purple `#863bff` (tints the standalone
  status bar); splash `background_color` = white. The Apple touch icon is baked
  onto a **solid white** background (favicon is transparent purple; iOS would
  otherwise composite transparency onto black).

## Files

- `public/manifest.webmanifest` — new.
- `public/pwa-192x192.png`, `public/pwa-512x512.png`,
  `public/maskable-icon-512x512.png`, `public/apple-touch-icon-180x180.png` — new
  (generated). `public/` is copied verbatim into `dist/`, so no build config change.
- `index.html` — added manifest/theme-color/description + iOS meta tags after the
  existing SVG `<link rel="icon">`, before the pre-paint script (left untouched).
- `Dockerfile` — added `location = /manifest.webmanifest { default_type
  application/manifest+json; }` to the inline nginx config so the manifest ships
  with the correct content-type regardless of the base image's `mime.types`.

## Verification

- `pnpm build` succeeds; `dist/` contains `manifest.webmanifest` + the 4 PNGs.
- `pnpm preview` → Chrome DevTools → Application → Manifest: standalone, colors,
  all icons load. (SW-related installability warnings are expected/fine.)
- Real device (deployed HTTPS): iPhone Safari Share → Add to Home Screen shows
  the purple-on-white icon and launches full-screen standalone; same via Android
  Chrome menu → Add to Home screen.

## Progress log

- 2026-07-20 — Implemented. Generated 4 PNG icons from `favicon.svg` (removed the
  generator's extra `pwa-64x64.png`/`favicon.ico`), verified the apple-touch-icon
  has a solid white background, added the manifest, `index.html` head tags, and
  the nginx manifest MIME block. No product code paths touched.
