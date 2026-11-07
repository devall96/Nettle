#!/usr/bin/env bash
# Bundles libVLC into a Slopfin linux-x64 publish folder, for the AppImage.
#
#   bundle-libvlc.sh <publish-dir>/vlc
#
# Layout produced (see src/Slopfin.Desktop/Linux/LinuxVlc.cs):
#   vlc/libvlc.so.5, vlc/libvlccore.so.9   libVLC
#   vlc/vlc/plugins/                       plugins (libVLC looks for <its dir>/vlc/plugins)
#   vlc/fallback/                          every library those need; at runtime the system's
#                                          copy is preferred, these are only used if missing
#   vlc/licenses/                          copyright files + source package list (GPL/LGPL)
#
# Runs on Ubuntu with: libvlc5 libvlccore9 vlc-plugin-base vlc-plugin-video-output vlc-bin
set -euo pipefail

DEST="$1"
LIB=/usr/lib/x86_64-linux-gnu

rm -rf "$DEST"
mkdir -p "$DEST/vlc" "$DEST/fallback" "$DEST/licenses"

cp -L "$LIB/libvlc.so.5" "$LIB/libvlccore.so.9" "$DEST/"
cp -rL "$LIB/vlc/plugins" "$DEST/vlc/plugins"

# Not used by Slopfin: Lua scripts (disabled with --no-lua), VLC's own GUIs, the build box's cache.
rm -rf "$DEST/vlc/plugins/lua" "$DEST/vlc/plugins/gui" "$DEST/vlc/plugins/plugins.dat"

# Drop plugins whose dependencies aren't installed here (they could never load anyway).
while IFS= read -r -d '' so; do
  if ldd "$so" | grep -q "not found"; then
    echo "skip plugin (missing deps): ${so#$DEST/}"
    rm -f "$so"
  fi
done < <(find "$DEST/vlc/plugins" -name '*.so' -print0)

# Never bundled: the C runtime, the C++ runtime and GPU driver entry points. These must always
# come from the user's own system.
EXCLUDE='^(linux-vdso|ld-linux|libc\.so|libm\.so|libdl\.so|libpthread\.so|librt\.so|libresolv\.so|libutil\.so|libanl\.so|libnsl\.so|libgcc_s\.so|libstdc\+\+\.so|libGL\.so|libGLX|libGLdispatch|libEGL|libOpenGL|libgbm\.so)'

# Every library that libVLC or any plugin needs (ldd already follows the whole chain).
mapfile -t DEPS < <(
  { ldd "$DEST/libvlc.so.5"; ldd "$DEST/libvlccore.so.9"; find "$DEST/vlc/plugins" -name '*.so' -exec ldd {} \; ; } \
    | awk '/=> \// { print $3 }' | sort -u
)

for dep in "${DEPS[@]}"; do
  name="$(basename "$dep")"
  if [[ "$name" =~ $EXCLUDE ]]; then continue; fi
  case "$name" in libvlc.so.5|libvlccore.so.9) continue ;; esac
  cp -L "$dep" "$DEST/fallback/$name"
done
echo "bundled $(ls "$DEST/fallback" | wc -l) fallback libraries, $(find "$DEST/vlc/plugins" -name '*.so' | wc -l) plugins"

# Plugin cache, so the first play doesn't have to scan ~300 plugins.
if [ -x "$LIB/vlc/vlc-cache-gen" ]; then "$LIB/vlc/vlc-cache-gen" "$DEST/vlc/plugins" || true; fi

# Licenses: copyright file of every Ubuntu package we took files from, and their source packages.
{
  echo "Libraries bundled with Slopfin for Linux, taken from Ubuntu $(. /etc/os-release && echo "$VERSION_ID") packages."
  echo "Corresponding source code: https://launchpad.net/ubuntu/+source/<source package>/<version>"
  echo "(or: apt-get source <source package>=<version>)."
  echo
  printf '%-40s %-40s %s\n' "binary package" "source package" "version"
} > "$DEST/licenses/SOURCES.txt"

for f in "$DEST/libvlc.so.5" "$DEST/libvlccore.so.9" "$DEST"/fallback/*; do
  name="$(basename "$f")"
  owner="$(dpkg -S "*/$name" 2>/dev/null | grep -v diversion | head -n1 | cut -d: -f1 || true)"
  if [ -n "$owner" ]; then echo "$owner"; fi
done | sort -u > /tmp/slopfin-pkgs.txt
for p in vlc-plugin-base vlc-plugin-video-output; do echo "$p"; done >> /tmp/slopfin-pkgs.txt

sort -u /tmp/slopfin-pkgs.txt | while read -r pkg; do
  base="${pkg%%:*}"
  src="$(dpkg-query -W -f='${source:Package} ${source:Version}' "$pkg" 2>/dev/null || true)"
  printf '%-40s %-40s %s\n' "$base" "${src% *}" "${src##* }" >> "$DEST/licenses/SOURCES.txt"
  if [ -f "/usr/share/doc/$base/copyright" ]; then cp "/usr/share/doc/$base/copyright" "$DEST/licenses/$base.copyright"; fi
done
echo "license files: $(ls "$DEST/licenses" | wc -l)"
