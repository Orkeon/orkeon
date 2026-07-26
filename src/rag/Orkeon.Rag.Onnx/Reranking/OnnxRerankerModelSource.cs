using System.Reflection;

namespace Orkeon.Rag.Onnx.Reranking;

/// <summary>
/// Probes the optional companion assembly <c>Orkeon.Rag.Onnx.Model</c> for the
/// embedded ms-marco-MiniLM-L-6-v2 weights. This package deliberately has no
/// compile-time reference to the model package — the weights carrier stays
/// optional and is resolved by name at runtime.
/// </summary>
internal static class OnnxRerankerModelSource
{
    /// <summary>Assembly name of the optional weights package.</summary>
    internal const string ModelAssemblyName = "Orkeon.Rag.Onnx.Model";

    // Logical resource names — public contract of Orkeon.Rag.Onnx.Model
    // (see MsMarcoMiniLmModel in that project; keep in sync).
    internal const string ModelResourceName = "Orkeon.Rag.Onnx.Model.msmarco-minilm-l6-v2.quant.onnx";
    internal const string VocabResourceName = "Orkeon.Rag.Onnx.Model.msmarco-minilm-l6-v2.vocab.txt";

    /// <summary>
    /// Attempts to load the embedded weights and vocab from the companion
    /// assembly. Returns <c>false</c> when the assembly is not referenced by the
    /// host (the VFS-path fallback applies then).
    /// </summary>
    internal static bool TryLoadEmbedded(
        string assemblyName,
        out byte[]? modelBytes,
        out Stream? vocabStream)
    {
        modelBytes = null;
        vocabStream = null;

        Assembly assembly;
        try
        {
            assembly = Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return false;
        }

        using var modelStream = assembly.GetManifestResourceStream(ModelResourceName);
        var vocab = assembly.GetManifestResourceStream(VocabResourceName);
        if (modelStream is null || vocab is null)
        {
            vocab?.Dispose();
            throw new InvalidOperationException(
                $"Assembly '{assemblyName}' is loaded but does not carry the expected embedded resources " +
                $"('{ModelResourceName}', '{VocabResourceName}') — package version mismatch or corrupt package.");
        }

        using var buffer = new MemoryStream(capacity: checked((int)Math.Max(0, modelStream.Length)));
        modelStream.CopyTo(buffer);
        modelBytes = buffer.ToArray();
        vocabStream = vocab;
        return true;
    }
}
