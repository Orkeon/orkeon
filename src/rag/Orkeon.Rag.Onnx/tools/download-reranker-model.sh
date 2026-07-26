#!/usr/bin/env bash
# Downloads the ms-marco-MiniLM-L-6-v2 cross-encoder weights (int8 ONNX export,
# Apache-2.0) for hosts using OnnxRerankerOptions.ModelPath/VocabPath instead of
# the Orkeon.Rag.Onnx.Model package. SHA-256 is verified: the Orkeon runtime
# NEVER downloads weights by itself — running this script is the host's explicit
# supply-chain decision.
#
# Usage: download-reranker-model.sh [target-directory]   (default: ./models)
set -euo pipefail

TARGET_DIR="${1:-./models}"
BASE_URL="https://huggingface.co/Xenova/ms-marco-MiniLM-L-6-v2/resolve/main"

MODEL_FILE="msmarco-minilm-l6-v2.quant.onnx"
MODEL_URL="${BASE_URL}/onnx/model_quantized.onnx"
MODEL_SHA256="e9d8ebf845c413e981c175bfe49a3bfa9b3dcce2a3ba54875ee5df5a58639fbe"

VOCAB_FILE="msmarco-minilm-l6-v2.vocab.txt"
VOCAB_URL="${BASE_URL}/vocab.txt"
VOCAB_SHA256="07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3"

mkdir -p "${TARGET_DIR}"

download_and_verify() {
    local url="$1" file="$2" sha="$3"
    local path="${TARGET_DIR}/${file}"

    if [[ -f "${path}" ]] && echo "${sha}  ${path}" | sha256sum --check --status; then
        echo "OK (cached): ${path}"
        return 0
    fi

    echo "Downloading ${url} -> ${path}"
    curl --fail --location --silent --show-error --output "${path}.tmp" "${url}"

    if ! echo "${sha}  ${path}.tmp" | sha256sum --check --status; then
        rm -f "${path}.tmp"
        echo "ERROR: SHA-256 mismatch for ${file} — refusing the artifact." >&2
        exit 1
    fi

    mv "${path}.tmp" "${path}"
    echo "OK (verified): ${path}"
}

download_and_verify "${MODEL_URL}" "${MODEL_FILE}" "${MODEL_SHA256}"
download_and_verify "${VOCAB_URL}" "${VOCAB_FILE}" "${VOCAB_SHA256}"

echo
echo "Done. Point OnnxRerankerOptions at these files through a VFS mount, e.g.:"
echo "  ModelPath = /models/${MODEL_FILE}"
echo "  VocabPath = /models/${VOCAB_FILE}"
