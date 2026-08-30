#!/usr/bin/env bash
set -euo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd -- "$script_directory/.." && pwd)"
configuration="${CONFIGURATION:-Release}"
dotnet_command="${DOTNET:-dotnet}"
project_path="$repository_root/src/BluetoothAudioReceiver.App/BluetoothAudioReceiver.App.csproj"
artifact_directory="$repository_root/artifacts/release"
publish_directory="$repository_root/artifacts/publish-win-x64"
version="${VERSION:-$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$project_path" | head -n 1)}"
informational_version="${INFORMATIONAL_VERSION:-$version}"
file_version="$version.0"

if ! command -v "$dotnet_command" >/dev/null 2>&1; then
    echo "dotnet was not found. Set DOTNET=/path/to/dotnet." >&2
    exit 1
fi

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "VERSION must use X.Y.Z." >&2
    exit 1
fi

IFS=. read -r version_major version_minor version_patch <<< "$version"
for component in "$version_major" "$version_minor" "$version_patch"; do
    if (( 10#$component > 65535 )); then
        echo "Each version component must be between 0 and 65535." >&2
        exit 1
    fi
done

for directory in "$artifact_directory" "$publish_directory"; do
    if [[ -d "$directory" ]]; then
        find "$directory" -mindepth 1 -delete
    else
        mkdir -p "$directory"
    fi
done

"$dotnet_command" publish "$project_path" \
    --configuration "$configuration" \
    --runtime win-x64 \
    --self-contained true \
    --output "$publish_directory" \
    -p:Version="$version" \
    -p:AssemblyVersion="$file_version" \
    -p:FileVersion="$file_version" \
    -p:InformationalVersion="$informational_version" \
    -p:IncludeSourceRevisionInInformationalVersion=false \
    -p:DebugSymbols=false \
    -p:DebugType=None

mapfile -d '' published_files < <(find "$publish_directory" -type f -print0)
if [[ ${#published_files[@]} -ne 1 ]] || [[ ${published_files[0]##*/} != BluetoothAudioReceiver.App.exe ]]; then
    echo "Expected exactly one published executable." >&2
    printf 'Found: %s\n' "${published_files[@]}" >&2
    exit 1
fi

mv "${published_files[0]}" "$artifact_directory/BluetoothAudioReceiver.exe"
(
    cd -- "$artifact_directory"
    sha256sum BluetoothAudioReceiver.exe > SHA256SUMS.txt
)

echo "Executable: $artifact_directory/BluetoothAudioReceiver.exe"
echo "Checksum:   $artifact_directory/SHA256SUMS.txt"
