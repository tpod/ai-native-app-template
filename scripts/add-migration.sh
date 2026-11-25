#!/usr/bin/env bash
# Adds an EF Core migration for both providers: Postgres (the app) and SQLite (the fast tests).
# Usage: scripts/add-migration.sh <Name>
set -euo pipefail

if [ $# -ne 1 ]; then
  echo "Usage: scripts/add-migration.sh <Name>" >&2
  exit 2
fi

cd "$(dirname "${BASH_SOURCE[0]}")/.."
: "${DOTNET_SYSTEM_NET_DISABLEIPV6:=1}"
export DOTNET_SYSTEM_NET_DISABLEIPV6

dotnet tool restore >/dev/null
dotnet tool run dotnet-ef migrations add "$1" -p src/AppTemplate.Api --context PostgresAppDbContext -o Data/Migrations/Postgres
dotnet tool run dotnet-ef migrations add "$1" -p src/AppTemplate.Api --context SqliteAppDbContext -o Data/Migrations/Sqlite
