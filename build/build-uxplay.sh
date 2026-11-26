#!/usr/bin/env bash
# Builds the DXMirror receiver: UxPlay (pinned commit + DXMirror patch) and a self-contained
# GStreamer runtime, staged in artifacts/receiver/.
#
# Run inside the MSYS2 *UCRT64* environment (build.ps1 does this for you):
#   C:\msys64\usr\bin\bash.exe -lc "cd /c/Users/<you>/Documents/DXMirror && MSYSTEM=UCRT64 ./build/build-uxplay.sh"
#
# Layout produced:
#   artifacts/receiver/bin/uxplay.exe (+ all DLLs, gst-plugin-scanner.exe, gst-inspect-1.0.exe)
#   artifacts/receiver/lib/gstreamer-1.0/*.dll   (only the plugins DXMirror needs)
#   artifacts/receiver/licenses/                 (license texts of bundled components)
set -euo pipefail

UXPLAY_REPO="https://github.com/FDH2/UxPlay.git"
UXPLAY_COMMIT="fbd040e70e754e01fa7e6df810fecd08b6484ada"   # UxPlay 1.74, 2026-11-23
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="$ROOT/build/work"
STAGE="$ROOT/artifacts/receiver"
PATCH="$ROOT/third_party/uxplay/dxmirror-uxplay.patch"

if [[ "${MSYSTEM:-}" != "UCRT64" ]]; then
  echo "ERROR: run this script in the MSYS2 UCRT64 environment (MSYSTEM=UCRT64)." >&2
  exit 1
fi
PREFIX="/ucrt64"

echo "==> Installing build dependencies (pacman)"
# Mirrors sometimes answer "429 Too Many Requests": retry, already downloaded packages stay cached.
pacman_install() {
  local attempt
  for attempt in 1 2 3 4 5; do
    if pacman -S --needed --noconfirm "$@"; then return 0; fi
    echo "pacman failed (attempt $attempt of 5) - waiting $((attempt * 20)) s and retrying..." >&2
    sleep $((attempt * 20))
  done
  return 1
}
pacman_install \
  git \
  mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-cmake mingw-w64-ucrt-x86_64-ninja mingw-w64-ucrt-x86_64-pkgconf \
  mingw-w64-ucrt-x86_64-openssl mingw-w64-ucrt-x86_64-libplist \
  mingw-w64-ucrt-x86_64-gstreamer mingw-w64-ucrt-x86_64-gst-plugins-base \
  mingw-w64-ucrt-x86_64-gst-plugins-good mingw-w64-ucrt-x86_64-gst-plugins-bad mingw-w64-ucrt-x86_64-gst-libav

echo "==> Fetching UxPlay $UXPLAY_COMMIT"
mkdir -p "$WORK"
if [[ ! -d "$WORK/UxPlay/.git" ]]; then
  git clone "$UXPLAY_REPO" "$WORK/UxPlay"
fi
cd "$WORK/UxPlay"
git fetch --quiet origin
git reset --hard --quiet
git clean -fdxq
git checkout --quiet "$UXPLAY_COMMIT"

echo "==> Applying DXMirror patch"
git apply --whitespace=nowarn "$PATCH"

echo "==> Building UxPlay (service discovery via Apple Bonjour)"
# UxPlay's experimental built-in mDNS responder (1.74) did not open its UDP 5353 socket on Windows
# in our tests, so DXMirror uses the long-proven Bonjour backend (-DUSE_DNS_SD=1).
# UxPlay loads dnssd.dll of the installed Bonjour Service at runtime (LoadLibrary), so only the
# header is needed to compile; an empty archive satisfies UxPlay's CMake link step.
BONJOUR_DIR="$WORK/bonjour-sdk"
rm -rf "$BONJOUR_DIR"
mkdir -p "$BONJOUR_DIR/Include" "$BONJOUR_DIR/Lib/x64"
cp "$ROOT/third_party/bonjour/dns_sd.h" "$BONJOUR_DIR/Include/"
ar rc "$BONJOUR_DIR/Lib/x64/dnssd.lib"
export BONJOUR_SDK_HOME="$(cygpath -m "$BONJOUR_DIR")"
if [[ ! -f /c/Windows/System32/dnssd.dll ]]; then
  echo "WARNING: Apple Bonjour Service is not installed on this PC (no System32\\dnssd.dll)." >&2
  echo "         The receiver will build, but needs Bonjour at runtime to be found by iPhones." >&2
fi
rm -rf build && mkdir build && cd build
cmake -G Ninja -DCMAKE_BUILD_TYPE=Release -DUSE_DNS_SD=1 ..
ninja
test -f uxplay.exe

echo "==> Staging receiver in $STAGE"
rm -rf "$STAGE"
mkdir -p "$STAGE/bin" "$STAGE/lib/gstreamer-1.0" "$STAGE/licenses"
cp uxplay.exe "$STAGE/bin/"
strip "$STAGE/bin/uxplay.exe" || true
cp "$PREFIX/libexec/gstreamer-1.0/gst-plugin-scanner.exe" "$STAGE/bin/"
cp "$PREFIX/bin/gst-inspect-1.0.exe" "$STAGE/bin/"

# GStreamer plugins used by UxPlay on Windows (see renderers/video_renderer.c, audio_renderer.c)
PLUGINS=(
  coreelements          # queue, capsfilter, ...
  app                   # appsrc
  playback              # decodebin
  typefindfunctions
  videoconvertscale     # videoconvert, videoscale
  audioconvert audioresample volume
  level                 # good: level
  autodetect            # good: autovideosink/autoaudiosink (fallbacks)
  videofilter           # good: videoflip (rotation options)
  directsound           # good: directsoundsink (fallback)
  videoparsersbad       # bad: h264parse, h265parse
  d3d11                 # bad: d3d11h264dec/d3d11h265dec + d3d11videosink (hardware path)
  d3d12                 # bad: d3d12 decoders + d3d12videosink
  wasapi wasapi2        # bad: WASAPI audio output
  libav                 # avdec_h264 (software fallback), avdec_aac, avdec_alac
  audioparsers          # good: aacparse (MP4 recording)
  isomp4                # good: mp4mux (MP4 recording)
  audiorate             # base: audiorate (MP4 recording, gap filling)
)
for p in "${PLUGINS[@]}"; do
  src="$PREFIX/lib/gstreamer-1.0/libgst${p}.dll"
  if [[ -f "$src" ]]; then
    cp "$src" "$STAGE/lib/gstreamer-1.0/"
  else
    echo "WARNING: plugin libgst${p}.dll not found in $PREFIX/lib/gstreamer-1.0" >&2
  fi
done

echo "==> Collecting DLL dependencies"
collect_deps() {
  # ldd lists every DLL; keep only those from the UCRT64 prefix (system DLLs ship with Windows)
  ldd "$1" | awk '{print $3}' | grep -i "^$PREFIX/bin/" || true
}
declare -A SEEN=()
queue=("$STAGE/bin/uxplay.exe" "$STAGE/bin/gst-plugin-scanner.exe" "$STAGE/bin/gst-inspect-1.0.exe" "$STAGE"/lib/gstreamer-1.0/*.dll)
while ((${#queue[@]})); do
  f="${queue[0]}"; queue=("${queue[@]:1}")
  while read -r dep; do
    [[ -z "$dep" ]] && continue
    name="$(basename "$dep")"
    if [[ -z "${SEEN[$name]:-}" ]]; then
      SEEN[$name]=1
      cp -u "$dep" "$STAGE/bin/"
      queue+=("$STAGE/bin/$name")
    fi
  done < <(collect_deps "$f")
done
echo "    ${#SEEN[@]} DLLs bundled"

echo "==> Licenses"
cp "$WORK/UxPlay/LICENSE" "$STAGE/licenses/UxPlay-GPL-3.0.txt"
for pkg in gstreamer gst-plugins-base gst-plugins-good gst-plugins-bad gst-libav ffmpeg openssl libplist glib2; do
  d="$PREFIX/share/licenses/$pkg"
  [[ -d "$d" ]] && cp -r "$d" "$STAGE/licenses/$pkg" || true
done
pacman -Q > "$STAGE/licenses/msys2-package-versions.txt"
cat > "$STAGE/licenses/SOURCES.txt" <<EOF
UxPlay source: $UXPLAY_REPO commit $UXPLAY_COMMIT
DXMirror changes to UxPlay: third_party/uxplay/dxmirror-uxplay.patch (GPL-3.0)
All other components are unmodified MSYS2 UCRT64 packages (see msys2-package-versions.txt);
their sources are available from https://github.com/msys2/MINGW-packages and the upstream projects.
EOF

echo "==> Verifying the staged runtime in isolation"
REG="$(mktemp -d)/registry.bin"
export GST_PLUGIN_SYSTEM_PATH_1_0="$(cygpath -w "$STAGE/lib/gstreamer-1.0")"
export GST_PLUGIN_PATH_1_0=""
export GST_PLUGIN_SCANNER_1_0="$(cygpath -w "$STAGE/bin/gst-plugin-scanner.exe")"
export GST_REGISTRY_1_0="$(cygpath -w "$REG")"
# only Windows system directories on PATH: every other DLL must come from the bundle
ISOLATED_PATH="/c/Windows/System32:/c/Windows"
missing=0
for element in appsrc queue h264parse h265parse decodebin avdec_h264 avdec_aac avdec_alac videoconvert videoscale \
               audioconvert audioresample volume level d3d11videosink d3d11h264dec d3d12videosink wasapisink autoaudiosink \
               aacparse mp4mux filesink avenc_aac audiorate; do
  if PATH="$ISOLATED_PATH" "$STAGE/bin/gst-inspect-1.0.exe" --exists "$element"; then
    echo "    ok      $element"
  else
    echo "    MISSING $element"
    case "$element" in d3d11h264dec|d3d12videosink) ;; *) missing=1 ;; esac   # GPU-specific elements may be absent on build PCs
  fi
done
version_output="$(PATH="$ISOLATED_PATH" "$STAGE/bin/uxplay.exe" -v)"
printf '%s\n' "$version_output"
uxplay_version="$(sed -n 's/^UxPlay version \([0-9.]*\);.*/\1/p' <<< "$version_output")"
if [[ ! "$uxplay_version" =~ ^[0-9]+(\.[0-9]+){1,3}$ ]]; then
  echo "ERROR: could not determine the bundled UxPlay version." >&2
  exit 1
fi
if (( missing )); then
  echo "ERROR: required GStreamer elements are missing from the bundle." >&2
  exit 1
fi

echo "==> Recording the native packages actually shipped"
runtime_inventory="$STAGE/licenses/native-inventory.tsv"
printf 'UxPlay\t%s\t%s\n' "$uxplay_version" "$UXPLAY_COMMIT" > "$runtime_inventory"
{
  for file in "$STAGE"/bin/* "$STAGE"/lib/gstreamer-1.0/*.dll; do
    case "$file" in
      */bin/uxplay.exe) continue ;;
      */bin/gst-plugin-scanner.exe) original="$PREFIX/libexec/gstreamer-1.0/gst-plugin-scanner.exe" ;;
      *) original="$PREFIX/${file#"$STAGE"/}" ;;
    esac
    owner="$(pacman -Qqo "$original")"
    read -r package version <<< "$(pacman -Q "$owner")"
    base="$(awk '/^%BASE%$/ { getline; print; exit }' "/var/lib/pacman/local/$package-$version/desc")"
    if [[ -z "$base" ]]; then
      base="${package/mingw-w64-ucrt-x86_64-/mingw-w64-}"
    fi
    printf '%s\t%s\t%s\n' "$package" "$base" "$version"
  done
} | sort -u >> "$runtime_inventory"

du -sh "$STAGE"
echo "==> Receiver ready: $STAGE"
