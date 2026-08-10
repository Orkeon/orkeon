# Orkeon smoke corpus

Orkeon is a multi-agent AI orchestration framework and command line tool for .NET.

The `orkeon doctor` command diagnoses an installation and reports one line per
check: the .NET runtime, the resolved appsettings.json, the LLM configuration and
its reachability, esbuild, the local embedding model, the ONNX reranker, the
tree-sitter grammars and whether the workspace is writable.

The `orkeon init` command writes a per-user configuration file under
`%APPDATA%\Orkeon` on Windows and `~/.config/Orkeon` on Linux and macOS.
