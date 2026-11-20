using Orkeon.Scripting.Toolchain;

namespace Orkeon.Scripting.Tests.Toolchain;

/// <summary>The <c>orkeon.d.ts</c> roll-up this build embeds, the one <see cref="TscTypeChecker"/> hands to tsc.</summary>
internal static class TypingsRollup
{
    public static string Read()
    {
        using var stream = typeof(TscTypeChecker).Assembly.GetManifestResourceStream("Orkeon.Scripting.Typings.orkeon.d.ts")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
