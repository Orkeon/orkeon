# Orkeon.Rag.Onnx

**License**: this package runs [`cross-encoder/ms-marco-MiniLM-L-6-v2`](https://huggingface.co/cross-encoder/ms-marco-MiniLM-L-6-v2)
(**Apache-2.0**, sentence-transformers / UKP Lab) but ships **no weights** itself.
ONNX Runtime and Microsoft.ML.Tokenizers are MIT.

## Install

```
dotnet add package Orkeon.Rag.Onnx --prerelease
```

> This package is published on the [GitHub Packages feed](https://github.com/orgs/Orkeon/packages); add the feed as a NuGet source first — see the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).


**Offline story**: weights are resolved in this order, lazily at first use —

1. **`Orkeon.Rag.Onnx.Model` referenced** → embedded int8 weights (~22 MB), **guaranteed
   offline**, zero download. This is the CI path (RAG-04 acceptance criterion 3).
2. **`OnnxRerankerOptions.ModelPath` / `VocabPath`** — VFS virtual paths read through
   `IFileSystemService`. Provision the files with
   [`tools/download-reranker-model.sh`](tools/download-reranker-model.sh) (SHA-256 verified).
3. Otherwise → loud, actionable error. **The runtime never downloads weights**
   (supply-chain decision: downloading is a host choice, not a library side effect).

## What it does

`OnnxCrossEncoderReranker : IReranker` scores each (query, chunk) pair jointly with the
ms-marco-MiniLM-L-6-v2 cross-encoder — the quality stage of the **50 → 5 cascade**
(`RerankingOptions.DefaultCandidateK` → `RerankingOptions.DefaultTopN`, guide §7).
Deterministic, CPU, millisecond latency, zero token cost. Scores are sigmoid-mapped
logits in (0, 1) with `ScoreOrigin = "cross-encoder"`.

Tokenization is BERT WordPiece (`Microsoft.ML.Tokenizers.BertTokenizer`, the model's own
`vocab.txt`), pairs encoded as `[CLS] query [SEP] passage [SEP]`, batched
(`OnnxRerankerOptions.BatchSize`, default 16, max sequence 512).

## Usage

```csharp
services.AddOrkeonRag(configuration);   // registers the reranker factory (none/noop, llm/listwise)
services.AddOrkeonOnnxReranker();       // contributes onnx/cross-encoder

var reranker = provider.GetRequiredService<RerankerFactory>().Create("onnx");
var top5 = await reranker.RerankAsync(query, candidates, topN: 5, ct);
```

Factory names: `onnx` (canonical), `cross-encoder` (alias).

Native dependency isolation: `Microsoft.ML.OnnxRuntime` is confined to this opt-in
package, same approach as `Orkeon.Tools.Embeddings.Local` (plan RAG §7).
