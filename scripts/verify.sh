#!/usr/bin/env bash
# Single "is it green" command: build, test, client drift check, web typecheck/lint/build.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
: "${DOTNET_SYSTEM_NET_DISABLEIPV6:=1}"
export DOTNET_SYSTEM_NET_DISABLEIPV6

echo "==> dotnet build"
dotnet build AppTemplate.slnx

echo "==> dotnet test"
dotnet test --solution AppTemplate.slnx --no-build

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
