#!/usr/bin/env sh
# Compatibility helper; never embeds credentials.
set -eu
cd "$(dirname "$0")/.."
docker compose build api
docker compose up -d api
