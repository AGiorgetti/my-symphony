#!/usr/bin/env bash
set -eo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
project_root="$repo_root/dotnet"

if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET SDK is required. Install it from https://dotnet.microsoft.com/download" >&2
  exit 1
fi

cd "$project_root"
dotnet restore
