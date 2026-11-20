#!/usr/bin/env bash
# Renames the template: scripts/rename.sh <NewName>
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

new="${1:-}"
if [[ ! "$new" =~ ^[A-Z][A-Za-z0-9]+$ ]]; then
  echo "Usage: scripts/rename.sh <NewName>  (must match ^[A-Z][A-Za-z0-9]+\$)" >&2
  exit 1
fi
new_lower="$(printf '%s' "$new" | tr '[:upper:]' '[:lower:]')"

# Old names are built from parts so this script never rewrites itself.
old="App""Template"
old_lower="app""template"

# 1. Contents: tracked text files only.
# git grep exit 1 means "no match" and is fine; any other failure aborts.
{ git grep -Ilzi -e "$old_lower" || [ $? -eq 1 ]; } \
  | while IFS= read -r -d '' f; do
      sed -i -e "s/$old/$new/g" -e "s/$old_lower/$new_lower/g" "$f"
    done

# 2. Paths: deepest first, via git mv.
{ git ls-files | grep -F "$old" || [ $? -eq 1 ]; } \
  | awk -F/ '{print NF "\t" $0}' | sort -rn | cut -f2- \
  | while IFS= read -r p; do
      [ -e "$p" ] || continue
      dir="$(dirname "$p")"; base="$(basename "$p")"
      [[ "$base" == *"$old"* ]] || continue
      git mv "$p" "$dir/${base//$old/$new}"
    done

# Directories, deepest first.
find . -path ./.git -prune -o -type d -name "*$old*" -print \
  | awk -F/ '{print NF "\t" $0}' | sort -rn | cut -f2- \
  | while IFS= read -r d; do
      dir="$(dirname "$d")"; base="$(basename "$d")"
      git mv "$d" "$dir/${base//$old/$new}"
    done

echo "Renamed to $new. Next steps:"
echo "  dotnet build $new.slnx"
echo "  scripts/verify.sh"
