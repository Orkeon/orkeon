#!/bin/sh
# Orkeon installer (Linux / macOS).
#
# Installs the archive contents to $PREFIX/lib/orkeon and symlinks the
# launchers into $PREFIX/bin. Default prefix: ~/.local (no sudo needed).
#
# Usage:
#   ./install.sh [--prefix DIR] [--modify-path] [--uninstall]
set -eu

PREFIX="${HOME}/.local"
MODIFY_PATH=0
UNINSTALL=0

while [ $# -gt 0 ]; do
  case "$1" in
    --prefix)      PREFIX="$2"; shift 2 ;;
    --prefix=*)    PREFIX="${1#--prefix=}"; shift ;;
    --modify-path) MODIFY_PATH=1; shift ;;
    --uninstall)   UNINSTALL=1; shift ;;
    -h|--help)
      sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'
      exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

LIB_DIR="$PREFIX/lib/orkeon"
BIN_DIR="$PREFIX/bin"
SRC=$(cd "$(dirname "$0")" && pwd)

if [ "$UNINSTALL" = 1 ]; then
  if [ -d "$LIB_DIR/bin" ]; then
    for cmd in "$LIB_DIR"/bin/*; do
      name=$(basename "$cmd")
      # Only remove symlinks we own (pointing into $LIB_DIR).
      if [ -h "$BIN_DIR/$name" ]; then
        target=$(readlink "$BIN_DIR/$name")
        case "$target" in "$LIB_DIR"/*) rm -f "$BIN_DIR/$name" ;; esac
      fi
    done
  fi
  rm -rf "$LIB_DIR"
  echo "Orkeon uninstalled from $PREFIX."
  exit 0
fi

if [ ! -d "$SRC/bin" ] || [ ! -d "$SRC/libexec" ]; then
  echo "Error: run this script from the extracted archive root (bin/ and libexec/ not found)." >&2
  exit 1
fi

if ! mkdir -p "$LIB_DIR" "$BIN_DIR" 2>/dev/null || [ ! -w "$LIB_DIR" ]; then
  echo "Error: cannot write to $PREFIX. Re-run with a writable --prefix, or with sudo for system-wide install:" >&2
  echo "  sudo ./install.sh --prefix /usr/local" >&2
  exit 1
fi

# Delete-and-replace for clean upgrades.
rm -rf "$LIB_DIR"
mkdir -p "$LIB_DIR"
cp -R "$SRC/bin" "$SRC/libexec" "$LIB_DIR/"
[ -f "$SRC/LICENSE.md" ] && cp "$SRC/LICENSE.md" "$LIB_DIR/"
[ -f "$SRC/README.md" ]  && cp "$SRC/README.md"  "$LIB_DIR/"

for cmd in "$LIB_DIR"/bin/*; do
  name=$(basename "$cmd")
  ln -sf "$cmd" "$BIN_DIR/$name"
done

echo "Orkeon installed to $LIB_DIR"
echo "Commands linked in $BIN_DIR: $(ls "$LIB_DIR/bin" | tr '\n' ' ')"

# .NET 10 runtime check (framework-dependent binaries).
if command -v dotnet >/dev/null 2>&1 && dotnet --list-runtimes 2>/dev/null | grep -q "Microsoft.NETCore.App 10\."; then
  :
else
  echo ""
  echo "WARNING: .NET 10 runtime not found. These binaries require it."
  echo "         Install it from https://dotnet.microsoft.com/download/dotnet/10.0"
fi

# PATH handling.
case ":$PATH:" in
  *":$BIN_DIR:"*) ;;
  *)
    if [ "$MODIFY_PATH" = 1 ]; then
      line="export PATH=\"$BIN_DIR:\$PATH\""
      for rc in "$HOME/.bashrc" "$HOME/.zshrc"; do
        if [ -f "$rc" ] && ! grep -Fq "$line" "$rc"; then
          printf '\n# Added by Orkeon installer\n%s\n' "$line" >> "$rc"
          echo "PATH updated in $rc (restart your shell)."
        fi
      done
    else
      echo ""
      echo "NOTE: $BIN_DIR is not on your PATH. Add it with:"
      echo "  export PATH=\"$BIN_DIR:\$PATH\""
      echo "(or re-run with --modify-path to append it to your shell rc files)"
    fi ;;
esac
