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
      sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'
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

  # Remove the "# Added by Orkeon installer" PATH block that --modify-path
  # appended below, if any -- parity with install.ps1's Remove-UserPathEntry,
  # which always cleans up its own PATH edit on -Uninstall. Only the block
  # whose export line references THIS prefix's bin dir is removed (two
  # prefixes may coexist; uninstalling one must not break the other), and the
  # blank line the installer wrote just above the marker goes with it.
  for rc in "$HOME/.bashrc" "$HOME/.zshrc"; do
    if [ -f "$rc" ] && grep -Fq '# Added by Orkeon installer' "$rc"; then
      tmp_rc=$(mktemp)
      awk -v bin="$BIN_DIR" '
        function flushblank() { if (blank) { print ""; blank = 0 } }
        pending {
          pending = 0
          if (index($0, bin) > 0) { blank = 0; next }
          flushblank(); print "# Added by Orkeon installer"; print; next
        }
        $0 == "# Added by Orkeon installer" { pending = 1; next }
        /^$/  { flushblank(); blank = 1; next }
        { flushblank(); print }
        END { flushblank(); if (pending) print "# Added by Orkeon installer" }
      ' "$rc" > "$tmp_rc"
      if ! cmp -s "$rc" "$tmp_rc"; then
        mv "$tmp_rc" "$rc"
        echo "PATH entry removed from $rc (restart your shell)."
      else
        rm -f "$tmp_rc"
      fi
    fi
  done

  echo "Orkeon uninstalled from $PREFIX."
  exit 0
fi

if [ ! -d "$SRC/bin" ] || [ ! -d "$SRC/libexec" ]; then
  echo "Error: run this script from the extracted archive root (bin/ and libexec/ not found)." >&2
  exit 1
fi

# Refuse to install from the installed copy of ourselves: the delete-and-replace
# below would wipe $SRC first and then have nothing left to copy (parity with
# install.ps1's identical guard).
if [ "$SRC" = "$LIB_DIR" ]; then
  echo "Error: this is the installed copy under $LIB_DIR. Run install.sh from a freshly" >&2
  echo "extracted archive to (re)install, or use --uninstall if that is what you meant." >&2
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
[ -f "$SRC/LICENSE.md" ]               && cp "$SRC/LICENSE.md" "$LIB_DIR/"
[ -f "$SRC/README.md" ]                && cp "$SRC/README.md" "$LIB_DIR/"
[ -f "$SRC/VERSION" ]                  && cp "$SRC/VERSION" "$LIB_DIR/"
[ -f "$SRC/appsettings.sample.json" ]  && cp "$SRC/appsettings.sample.json" "$LIB_DIR/"
# install.sh travels with the rest so it can still be found and re-run with
# --uninstall once the extracted archive it came from is long gone (parity
# with install.ps1, which copies itself for the same reason).
[ -f "$SRC/install.sh" ]               && cp "$SRC/install.sh" "$LIB_DIR/"

# --- macOS: quarantine + ad-hoc signature filet (MAC-01) ----------------------
# Darwin-only; every other line in this script -- above and below this block --
# behaves identically on Linux, byte for byte.
if [ "$(uname -s)" = "Darwin" ]; then
  # Quarantine: a browser-downloaded archive tags its extracted files with
  # com.apple.quarantine, and Gatekeeper then refuses to run them ("cannot be
  # opened because the developer cannot be verified"). Running install.sh is
  # the user's own act of trust, so clearing the attribute here is legitimate
  # (MAC-00-PLAN.md decision #2). It may simply not be set -- a curl download
  # never sets it -- which is not a failure.
  if command -v xattr >/dev/null 2>&1; then
    xattr -dr com.apple.quarantine "$LIB_DIR" 2>/dev/null || true
    echo "macOS: cleared the quarantine attribute from $LIB_DIR."
  fi

  # Ad-hoc signature filet: Apple Silicon refuses to load an unsigned Mach-O.
  # The .NET SDK ad-hoc-signs the apphost even in cross-publish, but bundled
  # native libraries (tree-sitter, onnxruntime, e_sqlite3, esbuild) arrive
  # signed or not depending on their own publisher -- one invalid one kills
  # the process at load time. Re-sign only what codesign actually rejects; a
  # valid publisher signature (e.g. onnxruntime's) must never be replaced with
  # an ad-hoc one (MAC-00-PLAN.md decision #3). A stubborn individual file is
  # a warning, never a reason to abort the install.
  if command -v codesign >/dev/null 2>&1; then
    have_file_cmd=0
    command -v file >/dev/null 2>&1 && have_file_cmd=1
    resigned=0
    find_list=$(mktemp)
    find "$LIB_DIR" -type f > "$find_list" 2>/dev/null || true
    while IFS= read -r f; do
      case "$f" in
        *.dylib) ;;                    # always checked, exec bit or not
        *)
          [ -x "$f" ] || continue      # otherwise only executables
          if [ "$have_file_cmd" = 1 ] && ! file "$f" 2>/dev/null | grep -q 'Mach-O'; then
            continue                   # skip shell wrapper scripts etc.
          fi
          ;;
      esac
      if ! codesign -v "$f" >/dev/null 2>&1; then
        if codesign --force -s - "$f" >/dev/null 2>&1; then
          resigned=$((resigned + 1))
        else
          echo "WARNING: could not ad-hoc sign $f -- it may fail to load." >&2
        fi
      fi
    done < "$find_list"
    rm -f "$find_list"
    if [ "$resigned" -gt 0 ]; then
      echo "macOS: ad-hoc re-signed $resigned file(s) that failed codesign -v."
    fi
  fi

  echo "If \`orkeon\` is ever killed or refused by Gatekeeper despite this, run:"
  echo "  xattr -dr com.apple.quarantine \"$LIB_DIR\""
  echo "See https://github.com/Orkeon/orkeon/blob/main/docs/getting-started/three-ways-to-run-orkeon.md (macOS section) for more."
fi

LINKED=""
for cmd in "$LIB_DIR"/bin/*; do
  name=$(basename "$cmd")
  ln -sf "$cmd" "$BIN_DIR/$name"
  LINKED="$LINKED$name "
done

echo "Orkeon installed to $LIB_DIR"
echo "Commands linked in $BIN_DIR: $LINKED"

# --- .NET 10 runtime check ----------------------------------------------------
# Only framework-dependent payloads need a runtime on the machine: a
# self-contained publish bundles its own, libhostfxr included. Its presence next
# to an installed app is how the two are told apart (the POSIX mirror of the
# hostfxr.dll test in install.ps1). The multi-app archive mixes both kinds --
# `orkeon` is self-contained while `orkeon-slim` and `orkeon-repl` are not -- so the check looks
# at every app and warns as soon as one of them needs
# a runtime that isn't there.

RUNTIME_MISSING=0

# True when at least one installed app is framework-dependent. Iterates the glob
# directly (no command substitution) so a --prefix containing spaces still works;
# esbuild-bin is skipped, it holds no .NET app. An unrecognizable libexec/ warns
# about nothing rather than guessing.
needs_dotnet_runtime() {
  for app in "$LIB_DIR"/libexec/*/; do
    [ -d "$app" ] || continue
    case "$app" in *"/esbuild-bin/") continue ;; esac
    if [ ! -f "${app}libhostfxr.so" ] && [ ! -f "${app}libhostfxr.dylib" ]; then
      return 0
    fi
  done
  return 1
}

# True when a Microsoft.NETCore.App 10.x runtime is reachable, via DOTNET_ROOT
# (which a --runtime install under $HOME leaves off the PATH) or via the PATH.
has_dotnet_10() {
  if [ -n "${DOTNET_ROOT:-}" ] && [ -x "$DOTNET_ROOT/dotnet" ] &&
     "$DOTNET_ROOT/dotnet" --list-runtimes 2>/dev/null | grep -q '^Microsoft\.NETCore\.App 10\.'; then
    return 0
  fi
  if command -v dotnet >/dev/null 2>&1 &&
     dotnet --list-runtimes 2>/dev/null | grep -q '^Microsoft\.NETCore\.App 10\.'; then
    return 0
  fi
  return 1
}

# Diagnostic only -- never installs anything, never adds a repository, never
# calls sudo on the user's behalf.
print_runtime_help() {
  echo ""
  echo "WARNING: the .NET 10 runtime was not found."
  echo "         Some commands installed here are framework-dependent and will not"
  echo "         start without it. The files were installed all the same."
  echo ""
  if [ "$(uname -s)" = "Darwin" ]; then
    echo "  Install the runtime (macOS), then re-run the command:"
    echo ""
    echo "  Official installer or archive:"
    echo "    https://dotnet.microsoft.com/download/dotnet/10.0"
    echo ""
    echo "  Or without administrator rights, under your home directory:"
  else
    echo "  Install the runtime, then re-run the command:"
    echo ""
    echo "  Ubuntu 25.10 and newer -- straight from the distribution:"
    echo "    sudo apt install dotnet-runtime-10.0"
    echo ""
    echo "  Debian, and Ubuntu LTS -- register the Microsoft repository first"
    echo "  (pick the .deb matching your distribution and release at"
    echo "  https://packages.microsoft.com/config/ ):"
    echo "    wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \\"
    echo "      -O /tmp/packages-microsoft-prod.deb"
    echo "    sudo dpkg -i /tmp/packages-microsoft-prod.deb && sudo apt update"
    echo "    sudo apt install dotnet-runtime-10.0"
    echo ""
    echo "  Or without sudo, under your home directory:"
  fi
  echo "    curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh"
  echo "    sh dotnet-install.sh --runtime dotnet --channel 10.0"
  # shellcheck disable=SC2016  # printed verbatim for the user to copy, not expanded here
  echo '    export DOTNET_ROOT="$HOME/.dotnet"'
  # shellcheck disable=SC2016
  echo '    export PATH="$DOTNET_ROOT:$PATH"'
  echo "  (add those two exports to your shell rc file to make them permanent)"
  echo ""
  echo "  Note: the \`orkeon\` command itself is self-contained and already works --"
  echo "  only the other launchers in this archive need the runtime. Distributions"
  echo "  that carry the CLI alone need none of the above: the Debian package"
  echo "  (orkeon_<version>_amd64.deb) and, on Windows, orkeon-cli-<version>-win-x64.zip."
}

if needs_dotnet_runtime && ! has_dotnet_10; then
  RUNTIME_MISSING=1
  print_runtime_help
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

# Repeated last: the PATH block above can be long enough to scroll the runtime
# diagnostic out of sight, and it is the one thing standing between the user and
# a working command.
if [ "$RUNTIME_MISSING" = 1 ]; then
  echo ""
  echo "REMINDER: the .NET 10 runtime is still missing. The framework-dependent"
  echo "          commands will fail to start until you install it -- see the"
  echo "          instructions printed above, or"
  echo "          https://dotnet.microsoft.com/download/dotnet/10.0"
fi
