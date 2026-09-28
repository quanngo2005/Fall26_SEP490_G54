#!/usr/bin/env sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
pwsh -NoProfile -File "$ROOT/scripts/codereview-backend.ps1"
