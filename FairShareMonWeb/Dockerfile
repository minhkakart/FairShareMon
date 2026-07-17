# syntax=docker/dockerfile:1

# ---- Build stage: compile the SPA to static assets ----
FROM node:22-alpine AS build
WORKDIR /app

# pnpm via corepack, pinned to the lockfile major (pnpm-lock.yaml lockfileVersion 9.0).
RUN corepack enable && corepack prepare pnpm@9 --activate

# Install dependencies first so this layer is cached until the manifests change.
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

# Vite inlines VITE_* at BUILD time. src/config/env.ts requires VITE_API_BASE_URL in a prod build
# (the client calls that origin directly + appends /api) and throws if it is empty — so fail the
# build early when it is missing rather than shipping a broken bundle.
ARG VITE_API_BASE_URL
ARG VITE_ENABLE_MOCKS=false
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
ENV VITE_ENABLE_MOCKS=$VITE_ENABLE_MOCKS
RUN test -n "$VITE_API_BASE_URL" \
    || (echo "ERROR: build arg VITE_API_BASE_URL is required (the production API origin)" >&2 && exit 1)

# Build: tsc -b && vite build -> /app/dist
COPY . .
RUN pnpm build

# ---- Runtime stage: serve the static bundle from a minimal nginx ----
FROM nginx:stable-alpine AS runtime

# Replace the stock server with an SPA config: listen on 5173 (matches the compose `web` expose),
# long-cache the hashed build assets, and fall back to index.html for client-side routes.
RUN rm -f /etc/nginx/conf.d/default.conf
COPY <<'NGINX' /etc/nginx/conf.d/app.conf
server {
    listen 5173;
    listen [::]:5173;
    server_name _;
    root /usr/share/nginx/html;
    index index.html;

    # Immutable, content-hashed build assets.
    location /assets/ {
        expires 1y;
        add_header Cache-Control "public, immutable";
        try_files $uri =404;
    }

    # SPA history fallback: unknown paths return the app shell.
    location / {
        try_files $uri /index.html;
    }

    # Never cache the entry document so new deploys are picked up immediately.
    location = /index.html {
        add_header Cache-Control "no-cache";
    }
}
NGINX

COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 5173
# nginx:alpine's base image already runs `nginx -g 'daemon off;'`.
