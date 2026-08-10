# Homebrew formula for the Orkeon CLI -- a BINARY formula: it downloads the
# self-contained macOS tarball attached to a GitHub Release and installs it. It
# never builds from source (the CLI is a self-contained .NET publish; building
# it would require the SDK and defeat the point of the channel).
#
# `version`, both `url`s and both `sha256`s are rewritten by
# scripts/update-homebrew-formula.sh from a release's SHA256SUMS -- do not
# hand-edit those five values.
#
# This file is authored in the main repository. The published tap is
# Orkeon/homebrew-tap, created and pushed at release time (MAC-00 §8): until
# that first push `brew install orkeon` does not resolve, and the sha256 values
# below are placeholders that deliberately fail verification.
class Orkeon < Formula
  desc "Multi-agent AI orchestration framework and CLI"
  homepage "https://github.com/Orkeon/orkeon"
  version "0.9.2-beta"
  license "MIT"

  # macOS-only: the formula pins the osx tarballs. Linux users take the .deb or
  # the tar.gz + install.sh channel instead.
  depends_on :macos

  on_arm do
    url "https://github.com/Orkeon/orkeon/releases/download/v0.9.2-beta/orkeon-cli-0.9.2-beta-osx-arm64.tar.gz"
    sha256 "PLACEHOLDER_SHA256_ARM64"
  end

  on_intel do
    url "https://github.com/Orkeon/orkeon/releases/download/v0.9.2-beta/orkeon-cli-0.9.2-beta-osx-x64.tar.gz"
    sha256 "PLACEHOLDER_SHA256_X64"
  end

  def install
    # The tarball unpacks to bin/, libexec/ and a few documentation files.
    # Only the payload is kept: the archive's own bin/orkeon walks install
    # symlinks to locate its root, which is not how a Cellar layout works, so
    # it is replaced by the wrapper written below.
    libexec.install "libexec/orkeon", "libexec/esbuild-bin"
    doc.install "README.md"
    pkgshare.install "appsettings.sample.json"

    # Same safety net as install.sh's Darwin block: the payload is
    # cross-published from Linux, so a native library may arrive without a
    # valid (even ad-hoc) signature and Apple silicon would kill the process
    # at load time. Re-sign only what codesign actually rejects -- a valid
    # publisher signature must never be replaced.
    Dir[libexec/"**/*"].each do |f|
      next unless File.file?(f)
      next unless f.end_with?(".dylib") || File.executable?(f)
      unless quiet_system("codesign", "--verify", f)
        system "codesign", "--force", "--sign", "-", f
      end
    end

    # Same contract as scripts/installer-assets/wrapper.sh.tmpl: point the
    # scripting toolchain at the bundled esbuild unless the caller already set
    # it, then exec the apphost. Cellar paths are absolute, so none of the
    # template's symlink walking is needed here.
    (bin/"orkeon").write <<~SH
      #!/bin/sh
      if [ -z "${ORKEON_ESBUILD_PATH:-}" ] && [ -x "#{libexec}/esbuild-bin/esbuild" ]; then
        ORKEON_ESBUILD_PATH="#{libexec}/esbuild-bin/esbuild"
        export ORKEON_ESBUILD_PATH
      fi
      exec "#{libexec}/orkeon/orkeon" "$@"
    SH
    chmod 0755, bin/"orkeon"
  end

  def caveats
    <<~EOS
      Orkeon is self-contained -- no .NET runtime is required.

      First run:
        orkeon init      # pick an LLM provider and model
        orkeon doctor    # verify the install
        orkeon run crew.yaml

      `orkeon init` writes the configuration to ~/.config/Orkeon/appsettings.json.
      A reference configuration is installed at:
        #{opt_pkgshare}/appsettings.sample.json
    EOS
  end

  # `orkeon --version` exits 1 (known gap, WIN-00 §7), so liveness is proven
  # with `doctor` instead: it exits 0 when no check fails, and an unconfigured
  # LLM is a warning rather than a failure.
  test do
    system bin/"orkeon", "doctor"
  end
end
