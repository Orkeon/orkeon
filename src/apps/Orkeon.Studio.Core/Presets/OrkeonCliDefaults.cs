using Orkeon.Constants.Llm;
using System.Diagnostics.CodeAnalysis;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Studio.Core.Presets;

/// <summary>
/// The endpoint and model defaults the Orkeon runtime itself uses, copied here on purpose.
/// <para>
/// Studio Core is referenced by three self-contained front-ends. Referencing
/// <c>Orkeon.Infrastructure</c> for a handful of string constants dragged the whole runtime —
/// ONNX runtimes, tree-sitter grammars, the local embedding model — into every published app,
/// for roughly 230 MB each. Copying the constants and pinning them with a test is the trade
/// STUDIO-01 §7 chose: the copy is checked against
/// <c>Orkeon.Infrastructure.Constants.Llm.LlmEndpoints</c>,
/// <c>ProviderDefaults</c> and <c>DockerModelRunnerDefaults</c> by
/// <c>ConstantDriftTests</c> in <c>Orkeon.Studio.Core.Tests</c>, which does reference
/// Infrastructure. Change one side and that test fails.
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1054",
    Justification = "These are JSON string field values shown in and edited from text fields, " +
                    "and they are compared verbatim against the runtime's own string constants.")]
public static class OrkeonCliDefaults
{
#pragma warning disable S1075 // URIs should not be hardcoded — official, stable public endpoints (copies of LlmEndpoints)












#pragma warning restore S1075





    // Cloud default models — copies of ProviderDefaults.ForProvider(<id>), same drift pinning.











}
