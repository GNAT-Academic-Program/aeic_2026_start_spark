#!/usr/bin/env bash
#
# dump_code — concatenate source files into one text file for sharing.
#
# Usage:
#   dump_code [--exclude <path>] ... <folder> <pattern> [pattern ...] <output_file>
#
# Options:
#   --exclude <path>    Exclude folder or file path (can be used multiple times)
#
# Example:
#   dump_code . '*.ads' '*.adb' ada_src_code.txt
#   dump_code --exclude awesome_ada_slidev --exclude node_modules . '*.sh' '*.md' output.txt
#
# Quote the patterns so the shell doesn't expand them before the script sees
# them — you want them matched recursively inside <folder>, not in your CWD.

set -euo pipefail

# Parse exclusions
excludes=()
while [[ "$#" -gt 0 ]]; do
    case "$1" in
        --exclude)
            if [ "$#" -lt 2 ]; then
                echo "error: --exclude requires an argument" >&2
                exit 1
            fi
            excludes+=("$2")
            shift 2
            ;;
        *)
            break
            ;;
    esac
done

if [ "$#" -lt 3 ]; then
    echo "usage: dump_code [--exclude <path>] ... <folder> <pattern> [pattern ...] <output_file>" >&2
    exit 1
fi

folder="$1"
shift

# Last argument is the output file; everything between is a pattern.
output="${!#}"                       # last positional arg
patterns=( "${@:1:$#-1}" )           # all args except the last

if [ ! -d "$folder" ]; then
    echo "error: '$folder' is not a directory" >&2
    exit 1
fi

# Build exclusion expression: -path ./exclude1 -prune -o -path ./exclude2 -prune -o ...
exclude_expr=()
for exclude in "${excludes[@]}"; do
    # Normalize the path relative to folder
    exclude_path="$folder/$exclude"
    exclude_expr+=( -path "$exclude_path" -prune -o )
done

# Build a find expression: -name p1 -o -name p2 -o ...
find_expr=()
for i in "${!patterns[@]}"; do
    if [ "$i" -gt 0 ]; then
        find_expr+=( -o )
    fi
    find_expr+=( -name "${patterns[$i]}" )
done

# Truncate / create the output fresh.
: > "$output"

count=0
# -print0 / read -d '' handles spaces and odd characters in filenames.
while IFS= read -r -d '' file; do
    {
        echo "============================================================"
        echo "FILE: $file"
        echo "============================================================"
        cat "$file"
        echo                         # ensure trailing newline before next header
        echo
    } >> "$output"
    count=$((count + 1))
done < <(find "$folder" "${exclude_expr[@]}" -type f \( "${find_expr[@]}" \) -print0 | sort -z)

echo "Wrote $count file(s) into '$output'."