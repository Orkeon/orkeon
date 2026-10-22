using Orkeon.Domain.Agent;

namespace Orkeon.Domain.Tests.Agent;

public class GuardrailPresetsEditFileTests
{
    [Fact]
    [Trait("BR", "RT-09a")]
    public void ShouldApplyFileWriteRules_WhenToolIsEditFile()
    {
        var presets = new[] { GuardrailPresets.Analysis, GuardrailPresets.Strict, GuardrailPresets.Creative };
        var carriers = 0;

        foreach (var preset in presets)
        {
            if (preset.ToolRules is null || !preset.ToolRules.TryGetValue("file_write", out var writeRules))
                continue;

            carriers++;
            Assert.True(preset.ToolRules.TryGetValue("edit_file", out var editRules));
            Assert.Equal(writeRules, editRules);
        }

        Assert.True(carriers >= 2);
    }
}
