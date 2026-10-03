#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output_dir="${1:-$root_dir/Build-Linux}"

if ! command -v mcs >/dev/null 2>&1; then
  printf '%s\n' 'Mono C# compiler (mcs) was not found.' >&2
  printf '%s\n' 'Install the mono-devel package, then run BUILD_LINUX.sh again.' >&2
  exit 1
fi

mkdir -p "$output_dir"

common_refs=(
  -r:System.dll
  -r:System.Core.dll
  -r:System.Drawing.dll
  -r:System.Windows.Forms.dll
)

mcs -target:winexe -platform:anycpu -optimize+ \
  -win32icon:"$root_dir/Assets/MagicDragon.ico" \
  -out:"$output_dir/MagicDragon_Safeguardian.exe" \
  "${common_refs[@]}" \
  -r:System.Web.Extensions.dll \
  -r:System.IO.Compression.dll \
  -r:System.IO.Compression.FileSystem.dll \
  "$root_dir/Source/MagicDragonSafeguardian.cs"

mcs -target:winexe -platform:anycpu -optimize+ \
  -win32icon:"$root_dir/Assets/MagicDragon.ico" \
  -out:"$output_dir/Uninstall_MagicDragon_Safeguardian.exe" \
  "${common_refs[@]}" \
  "$root_dir/Source/MagicDragonUninstall.cs"

cp "$root_dir/Engine/MagicDragon_Safeguardian.ps1" "$output_dir/"
cp "$root_dir/Assets/MagicDragon.ico" "$output_dir/"
cp "$root_dir/Assets/MagicDragon.png" "$output_dir/"
cp "$root_dir/README.txt" "$output_dir/"
cp "$root_dir/LICENSE" "$output_dir/"

printf '%s\n' 'MagicDragon Safeguardian 1.0.1 Linux cross-build' > "$output_dir/BUILD-INFO.txt"
printf 'Built: %s\n' "$(date --iso-8601=seconds)" >> "$output_dir/BUILD-INFO.txt"
printf '%s\n' 'Target: Windows .NET Framework/Mono-compatible executable' >> "$output_dir/BUILD-INFO.txt"

printf '\nBuild succeeded. Windows executables are in:\n%s\n' "$output_dir"
printf '%s\n' 'These files are Windows-targeted; this does not create a native Linux application.'
