#!/bin/sh
# Run once before `docker compose up`. Creates the external network and the host
# data directories the bind-mounts need. Idempotent — safe to re-run.
# Linux / WSL2. On Windows without WSL, create the network + dirs manually.
set -e
cd "$(dirname "$0")"

# External network shared with the host proxy / tunnel that fronts these containers.
docker network inspect wsl-net >/dev/null 2>&1 || docker network create wsl-net

# Bind-mount targets: nginx logs (fail2ban tails these), the shared deny-list,
# static error pages, and the DB/redis/fail2ban data dirs.
mkdir -p data/blocked data/nginx/logs data/nginx/html data/mariadb data/redis data/fail2ban

# Pre-create the nginx log files so fail2ban's jails have a file to tail from the first boot
# (fail2ban may start before nginx first opens them).
touch data/nginx/logs/access.log data/nginx/logs/error.log

echo "Prerequisites ready. Now run:  docker compose up -d --build"
