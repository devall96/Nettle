#!/usr/bin/env bash
# Bundles libmpv into a Slopfin linux-x64 publish folder, for the AppImage.
#
#   bundle-libmpv.sh <publish-dir>/mpv
#
# Layout produced (see src/Slopfin.Desktop/Linux/LinuxMpv.cs):
#   mpv/libmpv.so.2     libmpv
#   mpv/fallback/       every library it needs; at runtime the system's copy is preferred,
#                       these are only used when the system doesn't have one
#   mpv/licenses/       copyright files + source package list (GPL/LGPL)
#
# Runs on Ubuntu with libmpv2 installed.
set -euo pipefail

DEST="$1"
LIB=/usr/lib/x86_64-linux-gnu

rm -rf "$DEST"
mkdir -p "$DEST/fallback" "$DEST/licenses"
cp -L "$LIB/libmpv.so.2" "$DEST/"

# Never bundled: the C runtime, the C++ runtime and GPU driver entry points. These must always
# come from the user's own system.
EXCLUDE='^(linux-vdso|ld-linux|libc\.so|libm\.so|libdl\.so|libpthread\.so|librt\.so|libresolv\.so|libutil\.so|libanl\.so|libnsl\.so|libgcc_s\.so|libstdc\+\+\.so|libGL\.so|libGLX|libGLdispatch|libEGL|libOpenGL|libgbm\.so|libvulkan\.so)'

mapfile -t DEPS < <(ldd "$DEST/libmpv.so.2" | awk '/=> \// { print $3 }' | sort -u)
if ldd "$DEST/libmpv.so.2" | grep -q "not found"; then
  echo "libmpv has unresolved dependencies on the build machine:"; ldd "$DEST/libmpv.so.2" | grep "not found"; exit 1
fi
for dep in "${DEPS[@]}"; do
  name="$(basename "$dep")"
  if [[ "$name" =~ $EXCLUDE ]]; then continue; fi
  cp -L "$dep" "$DEST/fallback/$name"
done
echo "bundled libmpv + $(ls "$DEST/fallback" | wc -l) fallback libraries ($(du -sh "$DEST" | cut -f1))"

# Licenses: copyright file of every Ubuntu package we took files from, and their source packages.
{
  echo "Libraries bundled with Slopfin for Linux, taken unmodified from Ubuntu $(. /etc/os-release && echo "$VERSION_ID") packages."
  echo "Corresponding source code: https://launchpad.net/ubuntu/+source/<source package>/<version>"
  echo "(or: apt-get source <source package>=<version>)."
  echo
  printf '%-40s %-40s %s\n' "binary package" "source package" "version"
} > "$DEST/licenses/SOURCES.txt"

for f in "$DEST/libmpv.so.2" "$DEST"/fallback/*; do
  name="$(basename "$f")"
  owner="$(dpkg -S "*/$name" 2>/dev/null | grep -v diversion | head -n1 | cut -d: -f1 || true)"
  if [ -n "$owner" ]; then echo "$owner"; fi
done | sort -u > /tmp/slopfin-pkgs.txt

while read -r pkg; do
  base="${pkg%%:*}"
  src="$(dpkg-query -W -f='${source:Package} ${source:Version}' "$pkg" 2>/dev/null || true)"
  printf '%-40s %-40s %s\n' "$base" "${src% *}" "${src##* }" >> "$DEST/licenses/SOURCES.txt"
  if [ -f "/usr/share/doc/$base/copyright" ]; then cp "/usr/share/doc/$base/copyright" "$DEST/licenses/$base.copyright"; fi
done < /tmp/slopfin-pkgs.txt
echo "license files: $(ls "$DEST/licenses" | wc -l)"
