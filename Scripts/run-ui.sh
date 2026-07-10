#!/usr/bin/env bash
#
# Convenience shortcut: build the solution and run the ServerCenter operator UI from source.
# Usage:  ./Scripts/run-ui.sh [Debug|Release]   (defaults to Debug)
#
# The UI is a thin Avalonia client - point it at the controller from its own settings. This
# delegates to `./Scripts/run.sh ui [...]` so the build/run logic lives in exactly one place.
#
set -euo pipefail

CONFIGURATION="${1:-Debug}"

# Resolve the repo's Scripts dir regardless of where this was invoked from, then hand off.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$SCRIPT_DIR/run.sh" ui "$CONFIGURATION"
