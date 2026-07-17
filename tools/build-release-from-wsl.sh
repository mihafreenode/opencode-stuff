#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
script_path="$(wslpath -w "$repo_root/tools/build-release.ps1")"

powershell.exe \
  -NoProfile \
  -ExecutionPolicy Bypass \
  -File "$script_path" \
  "$@"
