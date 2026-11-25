#!/usr/bin/env bash
# Single "is it green" command: build, test, client drift check, web typecheck/lint/build.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
: "${DOTNET_SYSTEM_NET_DISABLEIPV6:=1}"
export DOTNET_SYSTEM_NET_DISABLEIPV6

echo "==> dotnet build"
dotnet build AppTemplate.slnx

echo "==> dotnet test (sqlite)"
dotnet test --project tests/AppTemplate.Architecture.Tests --no-build
dotnet test --project tests/AppTemplate.Api.Tests --no-build

# Postgres tests run by default on a container runtime (rootless Podman here). VERIFY_POSTGRES=0 skips them,
# loudly: a skip is printed, never silent.
: "${VERIFY_POSTGRES:=1}"
if [ "$VERIFY_POSTGRES" = "0" ]; then
  echo "POSTGRES TESTS SKIPPED"
else
  podman_socket="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}/podman/podman.sock"
  if [ -z "${DOCKER_HOST:-}" ] && [ -S "$podman_socket" ]; then
    export DOCKER_HOST="unix://$podman_socket"
  fi
  if [ -z "${DOCKER_HOST:-}" ] || ! curl -sf --max-time 5 --unix-socket "${DOCKER_HOST#unix://}" http://d/_ping >/dev/null; then
    echo "No container runtime: start Podman with 'systemctl --user start podman.socket' and set DOCKER_HOST=unix://\$XDG_RUNTIME_DIR/podman/podman.sock (or run with VERIFY_POSTGRES=0)"
    exit 1
  fi
  echo "==> dotnet test (postgres)"
  dotnet test --project tests/AppTemplate.Api.PostgresTests --no-build
fi

cd src/web

if [ ! -d node_modules ]; then
  echo "==> npm ci"
  npm ci
fi

echo "==> client drift check"
tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT
npx openapi-typescript openapi.json -o "$tmp"
if ! diff -q "$tmp" src/api/schema.d.ts >/dev/null; then
  echo "Client drift: run npm run gen:api in src/web"
  exit 1
fi

echo "==> web typecheck, lint, build"
npm run typecheck
npm run lint
npm run build

echo "VERIFY OK"
