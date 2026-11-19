#!/usr/bin/env bash
# Regenerates src/web/openapi.json from the live OpenAPI document.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
: "${DOTNET_SYSTEM_NET_DISABLEIPV6:=1}"
export DOTNET_SYSTEM_NET_DISABLEIPV6

UPDATE_OPENAPI=1 dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~OpenApiSnapshotTests"
