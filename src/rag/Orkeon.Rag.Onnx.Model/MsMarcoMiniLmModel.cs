using System.Reflection;

namespace Orkeon.Rag.Onnx.Model;

/// <summary>
/// Typed access to the embedded ms-marco-MiniLM-L-6-v2 cross-encoder resources
/// (int8 ONNX weights + WordPiece vocab). The runtime package
/// (<c>Orkeon.Rag.Onnx</c>) does <b>not</b> reference this assembly at compile
/// time — it probes it by name and reads the same resource names via
/// reflection, so this package stays a pure optional weights carrier.
/// </summary>
public static class MsMarcoMiniLmModel
{
    /// <summary>Assembly name probed by the runtime package.</summary>
    public const string AssemblyName = "Orkeon.Rag.Onnx.Model";

    /// <summary>Logical name of the embedded ONNX weights resource.</summary>
    public const string ModelResourceName = "Orkeon.Rag.Onnx.Model.msmarco-minilm-l6-v2.quant.onnx";

    /// <summary>Logical name of the embedded WordPiece vocab resource.</summary>
    public const string VocabResourceName = "Orkeon.Rag.Onnx.Model.msmarco-minilm-l6-v2.vocab.txt";

    /// <summary>
    /// SHA-256 of the embedded ONNX weights
    /// (<c>Xenova/ms-marco-MiniLM-L-6-v2</c>, <c>onnx/model_quantized.onnx</c>).
    /// </summary>
    public const string ModelSha256 = "e9d8ebf845c413e981c175bfe49a3bfa9b3dcce2a3ba54875ee5df5a58639fbe";

    /// <summary>SHA-256 of the embedded vocab file.</summary>
    public const string VocabSha256 = "07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3";

    /// <summary>Opens the embedded ONNX weights stream.</summary>
    public static Stream OpenModelStream() => OpenResource(ModelResourceName);

    /// <summary>Opens the embedded WordPiece vocab stream.</summary>
    public static Stream OpenVocabStream() => OpenResource(VocabResourceName);

    private static Stream OpenResource(string name) =>
        typeof(MsMarcoMiniLmModel).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException(
            $"Embedded resource '{name}' is missing from {AssemblyName} — the package is corrupt.");
}
