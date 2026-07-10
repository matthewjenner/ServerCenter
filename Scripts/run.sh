#!/usr/bin/env bash
#
# Build the solution and run one of ServerCenter's entrypoints.
# Usage:  ./Scripts/run.sh [ui|controller|agent] [Debug|Release]
#
# Defaults to the UI in Debug. The controller and agent are long-running (Ctrl+C to stop);
# the controller starts an ASP.NET host, the agent a console host.
#
set -euo pipefail

TARGET="${1:-ui}"
CONFIGURATION="${2:-Debug}"

case "$TARGET" in
    ui|controller|agent) ;;
    *) echo "error: target must be 'ui', 'controller', or 'agent', got '$TARGET'" >&2; exit 1 ;;
esac
case "$CONFIGURATION" in
    Debug|Release) ;;
    *) echo "error: configuration must be 'Debug' or 'Release', got '$CONFIGURATION'" >&2; exit 1 ;;
esac

case "$TARGET" in
    ui)         proj="Src/ServerCenter.Ui" ;;
    controller) proj="Src/ServerCenter.Controller" ;;
    agent)      proj="Src/ServerCenter.Agent" ;;
esac

# Run from the repo root regardless of where the script was invoked.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/.."

step() { printf '\n==> %s\n' "$1"; }

step "Building solution ($CONFIGURATION)"
dotnet build ServerCenter.slnx --configuration "$CONFIGURATION"

step "Running $proj  (Ctrl+C to stop)"
# --no-build: run exactly what was just built, with no implicit rebuild.
dotnet run --project "$proj" --configuration "$CONFIGURATION" --no-build
