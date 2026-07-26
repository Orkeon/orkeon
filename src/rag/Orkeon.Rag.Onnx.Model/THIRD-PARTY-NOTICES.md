# Third-party notices — Orkeon.Rag.Onnx.Model

This package embeds a third-party machine-learning model.

## ms-marco-MiniLM-L-6-v2 (cross-encoder)

- **Original model**: [`cross-encoder/ms-marco-MiniLM-L-6-v2`](https://huggingface.co/cross-encoder/ms-marco-MiniLM-L-6-v2)
  — sentence-transformers, UKP Lab (Technische Universität Darmstadt).
- **License**: Apache License 2.0 (<https://www.apache.org/licenses/LICENSE-2.0>).
- **Embedded artifact**: int8-quantized ONNX export from
  [`Xenova/ms-marco-MiniLM-L-6-v2`](https://huggingface.co/Xenova/ms-marco-MiniLM-L-6-v2),
  file `onnx/model_quantized.onnx` (same Apache-2.0 license as the original model),
  plus the matching `vocab.txt` (BERT WordPiece vocabulary).
- **Retrieved**: 2026-07-26 from `https://huggingface.co/Xenova/ms-marco-MiniLM-L-6-v2`.
- **Integrity** (SHA-256):
  - `msmarco-minilm-l6-v2.quant.onnx` — `e9d8ebf845c413e981c175bfe49a3bfa9b3dcce2a3ba54875ee5df5a58639fbe`
  - `msmarco-minilm-l6-v2.vocab.txt` — `07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3`
- **Training data**: the model was trained on the MS MARCO passage ranking dataset
  (Microsoft, non-commercial research license for the dataset itself; the trained
  model is distributed under Apache-2.0 by its authors).

No modification was made to the artifacts other than renaming the files.
