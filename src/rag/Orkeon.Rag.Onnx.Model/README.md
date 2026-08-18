# Orkeon.Rag.Onnx.Model

**License**: the embedded model is [`cross-encoder/ms-marco-MiniLM-L-6-v2`](https://huggingface.co/cross-encoder/ms-marco-MiniLM-L-6-v2)
(**Apache-2.0**, sentence-transformers / UKP Lab), as the int8-quantized ONNX export published by
[`Xenova/ms-marco-MiniLM-L-6-v2`](https://huggingface.co/Xenova/ms-marco-MiniLM-L-6-v2)
(`onnx/model_quantized.onnx`, ~22 MB). See [THIRD-PARTY-NOTICES.md](https://github.com/Orkeon/orkeon/blob/main/src/rag/Orkeon.Rag.Onnx.Model/THIRD-PARTY-NOTICES.md).

## Install

```
dotnet add package Orkeon.Rag.Onnx.Model --prerelease
```

> This package is published on the [GitHub Packages feed](https://github.com/orgs/Orkeon/packages); add the feed as a NuGet source first — see the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).


**Offline story**: the weights and the WordPiece vocab are committed to this repository and
embedded as assembly resources. Referencing this package next to `Orkeon.Rag.Onnx` gives the
cross-encoder reranker a **guaranteed offline** model source — zero network, zero download,
CI-safe. This is the recommended setup for CI (RAG-04 acceptance criterion 3).

## What this package contains

| Resource (logical name) | Content | SHA-256 |
|---|---|---|
| `Orkeon.Rag.Onnx.Model.msmarco-minilm-l6-v2.quant.onnx` | int8 ONNX cross-encoder weights | `e9d8ebf845c413e981c175bfe49a3bfa9b3dcce2a3ba54875ee5df5a58639fbe` |
| `Orkeon.Rag.Onnx.Model.msmarco-minilm-l6-v2.vocab.txt` | BERT WordPiece vocab (30 522 entries) | `07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3` |

`MsMarcoMiniLmModel` exposes the resource names, hashes, and `OpenModelStream()` /
`OpenVocabStream()` helpers.

## How resolution works

`Orkeon.Rag.Onnx` does **not** reference this package. At first use, its
`OnnxCrossEncoderReranker` probes `Assembly.Load("Orkeon.Rag.Onnx.Model")`:

1. **This package referenced** → embedded weights are loaded directly (offline path).
2. Otherwise → `OnnxRerankerOptions.ModelPath` / `VocabPath` (VFS virtual paths) are read
   through `IFileSystemService`.
3. Neither → loud, actionable error. The runtime **never downloads** anything
   (supply-chain decision) — hosts that want the file-based path can use the audited
   script `src/rag/Orkeon.Rag.Onnx/tools/download-reranker-model.sh`.
