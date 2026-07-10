#!/usr/bin/env bash
#
# Launches the local DEV stack (plaintext HTTP/2, no mTLS) for quick smoke-testing: the
# controller and one agent (logs stream here, interleaved), plus the dashboard (its own GUI
# window). Ctrl+C stops all three. Not for production.
#
# Usage:
#   ./Scripts/dev-stack.sh              # build then launch
#   ./Scripts/dev-stack.sh --no-build   # skip the build
#
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ "${1:-}" != "--no-build" ]]; then
    echo "==> Building solution"
    dotnet build "$root/ServerCenter.slnx" -v minimal
fi

pids=()
cleanup() {
    echo
    echo "==> Stopping dev stack"
    kill "${pids[@]}" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

echo "==> Controller (http://localhost:5080)"
Security__RequireClientCertificate=false \
    dotnet run --project "$root/Src/ServerCenter.Controller" --no-build &
pids+=($!)

# Give the controller a moment to bind before the agent and dashboard dial in.
sleep 3

echo "==> Agent (dev-agent)"
SERVERCENTER_CONTROLLER=http://localhost:5080 \
    dotnet run --project "$root/Src/ServerCenter.Agent" --no-build &
pids+=($!)

echo "==> Dashboard"
SERVERCENTER_CONTROLLER=http://localhost:5080 \
    dotnet run --project "$root/Src/ServerCenter.Ui" --no-build &
pids+=($!)

echo "Dev stack running. Ctrl+C to stop. The controller writes servercenter.db (gitignored)."
wait
