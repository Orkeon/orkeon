using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Core.Storage;

namespace Orkeon.Studio.Config.Tests.Presentation;

public class SaveLocationModelTests
{
    [Fact]
    public void The_default_target_is_the_global_file()
    {
        var model = new SaveLocationModel();

        Assert.Equal(SaveLocationMode.Global, model.Mode);
    }

    [Fact]
    public void The_global_target_resolves_to_the_path_the_cli_writes()
    {
        var model = new SaveLocationModel();

        var resolved = model.TryResolve(out var path, out var error);

        if (model.GlobalPath is null)
        {
            // A container without HOME: the chooser reports it instead of throwing.
            Assert.False(resolved);
            Assert.NotNull(error);
            return;
        }

        Assert.True(resolved);
        Assert.Equal(SettingsLocations.GetGlobalSettingsPath(), path);
    }

    [Fact]
    public void A_custom_directory_is_normalized_to_the_appsettings_file_inside_it()
    {
        var directory = Directory.CreateTempSubdirectory("studio-config-tests").FullName;
        try
        {
            var model = new SaveLocationModel
            {
                Mode = SaveLocationMode.CustomPath,
                CustomPath = directory,
            };

            Assert.True(model.TryResolve(out var path, out _));
            Assert.Equal(Path.Combine(directory, "appsettings.json"), path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_custom_file_path_is_made_absolute()
    {
        var model = new SaveLocationModel
        {
            Mode = SaveLocationMode.CustomPath,
            CustomPath = "crew/appsettings.json",
        };

        Assert.True(model.TryResolve(out var path, out _));
        Assert.NotNull(path);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void An_empty_custom_path_is_reported_rather_than_resolved()
    {
        var model = new SaveLocationModel { Mode = SaveLocationMode.CustomPath };

        Assert.False(model.TryResolve(out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Opening_a_file_outside_the_global_path_preselects_the_custom_target()
    {
        var model = new SaveLocationModel();

        model.SelectCurrentFile("/srv/crew/appsettings.json");

        Assert.Equal(SaveLocationMode.CustomPath, model.Mode);
        Assert.Equal("/srv/crew/appsettings.json", model.CustomPath);
    }

    [Fact]
    public void The_resolution_chain_is_shown_in_full_and_in_order()
    {
        var lines = SaveLocationModel.ResolutionChainLines;

        Assert.Equal(4, lines.Count);
        Assert.StartsWith("1. ", lines[0]);
        Assert.StartsWith("4. ", lines[3]);
        Assert.Contains("--settings", lines[0]);
    }
}
