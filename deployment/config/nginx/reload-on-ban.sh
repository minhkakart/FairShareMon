#!/bin/sh
# nginx entrypoint: reload nginx whenever fail2ban updates the deny-list, so bans
# apply without a Docker socket. Polls the deny-dir checksum (~10s) and reloads
# only on change, then runs nginx in the foreground.
BLOCK_DIR=/etc/nginx/blocked

checksum() { cat "$BLOCK_DIR"/*.conf 2>/dev/null | md5sum; }

last="$(checksum)"
(
  while true; do
    sleep 10
    cur="$(checksum)"
    if [ "$cur" != "$last" ]; then
      last="$cur"
      nginx -t >/dev/null 2>&1 && nginx -s reload >/dev/null 2>&1
    fi
  done
) &

exec nginx -g 'daemon off;'
