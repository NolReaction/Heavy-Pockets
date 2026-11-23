#!/bin/bash
set -e

root="$(cd -- "$(dirname -- "$0")" && pwd)"
exec python3 "$root/Tools/Start-MacRaid.py" "$@"
