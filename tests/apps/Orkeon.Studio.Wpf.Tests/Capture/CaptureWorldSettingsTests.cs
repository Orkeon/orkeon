using Orkeon.Studio.Wpf.ViewModels.Capture.Fixtures;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;
using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Studio.Wpf.Tests.Capture;

/// <summary>
/// The settings file each capture world is photographed on is kept beside the suite, as the world
/// writes it. Studio does not reference the engine, so nothing here can say whether a key of that
/// file is one a run reads: the engine's own tests judge the kept files against its settings
/// catalogue, with every other settings file of the repository. A seed that writes a key nobody
/// reads — <c>RateLimiting:RequestsPerMinute</c> did — fails there, and a seed that changes fails
/// here until its file is written again.
/// </summary>
public sealed class CaptureWorldSettingsTests
{
    /// <summary>Stands for the world's data folder, which is a new temporary directory at each campaign.</summary>
    private const string Data = "{data}";

    private const string Regenerate =
        "UPDATE_PRODUCED_FILES=1 dotnet test tests/apps/Orkeon.Studio.Wpf.Tests --filter \"FullyQualifiedName~CaptureWorldSettings\"";

    public static TheoryData<string> Worlds() => [StudioFixture.Seeded.Name, StudioFixture.Pristine.Name];

    [Theory]
    [MemberData(nameof(Worlds))]
    public void The_settings_a_world_writes_are_the_ones_kept_beside_the_suite(string world)
    {
        var plan = world == StudioFixture.Pristine.Name ? StudioFixture.Pristine : StudioFixture.Seeded;

        // A Windows path separator, as JSON writes it, is the only thing that depends on the machine.
        var settings = CaptureWorldWriter.Settings(plan, Data).Replace(@"\\", "/", StringComparison.Ordinal);

        ProducedFile.AssertCurrent($"tests/apps/Orkeon.Studio.Wpf.Tests/Capture/Settings/{plan.Name}.appsettings.json", settings, Regenerate);
    }
}
