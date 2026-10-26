#!/usr/bin/env sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
# PowerShell 7 (pwsh) on Linux/macOS/CI; Windows PowerShell when only that exists (e.g. Git Bash on Windows).
PS=$(command -v pwsh || command -v powershell.exe || command -v powershell || true)
if [ -z "$PS" ]; then
  echo "PowerShell is required: install PowerShell 7 (pwsh)." >&2
  exit 1
fi
exec "$PS" -NoProfile -ExecutionPolicy Bypass -File "$ROOT/scripts/codereview-frontend.ps1"
