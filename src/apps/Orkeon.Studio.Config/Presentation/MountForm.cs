using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>A sub-path rights override being edited.</summary>
/// <param name="RelativePath">Path relative to the mount root.</param>
/// <param name="Rights">Rights granted on that sub-path.</param>
internal sealed record MountOverrideEntry(string RelativePath, MountRights Rights)
{
    /// <summary>The single line the override list shows.</summary>
    public string Display => string.Create(
        CultureInfo.InvariantCulture,
        $"{RelativePath}  ->  {MountRightsTokens.ToToken(Rights)} ({MountRightsTokens.GetLabel(Rights)})");
}

/// <summary>
/// One mount being created or edited. Rights come from a closed list, and what the form
/// produces is validated by <see cref="MountValidator"/> — the same checks the runtime
/// runs at boot — before the row is accepted.
/// </summary>
internal sealed class MountForm
{
    private readonly List<MountOverrideEntry> _overrides = [];
    private readonly IDirectoryProbe _directories;
    private readonly MountValidator _validator;

    /// <summary>Creates an empty form over a directory probe (defaults to the real disk).</summary>
    public MountForm(IDirectoryProbe? directories = null)
    {
        _directories = directories ?? PhysicalDirectoryProbe.Instance;
        _validator = new MountValidator(_directories);
    }

    /// <summary>Physical directory, picked from the folder browser.</summary>
    public string PhysicalPath { get; set; } = "";

    /// <summary>Virtual path the mount is exposed under.</summary>
    public string VirtualPath { get; set; } = "";

    /// <summary>Default rights of the mount.</summary>
    public MountRights Rights { get; set; } = MountRights.ReadOnly;

    /// <summary>The sub-path overrides currently listed.</summary>
    public IReadOnlyList<MountOverrideEntry> Overrides => _overrides;

    /// <summary>The rights drop-down entries, token first — the closed list of the format.</summary>
    public static IReadOnlyList<string> RightsChoices { get; } = MountRightsTokens.Choices
        .Select(choice => string.Create(CultureInfo.InvariantCulture, $"{choice.Token} — {choice.Label}"))
        .ToList()
        .AsReadOnly();

    /// <summary>Virtual paths offered as suggestions.</summary>
    public static IReadOnlyList<string> VirtualPathSuggestions => MountDefinition.SuggestedVirtualPaths;

    /// <summary>Index of <see cref="Rights"/> in <see cref="RightsChoices"/>.</summary>
    public int RightsChoiceIndex
    {
        get
        {
            for (var i = 0; i < MountRightsTokens.Choices.Count; i++)
            {
                if (MountRightsTokens.Choices[i].Rights == Rights)
                    return i;
            }

            return 0;
        }
        set => Rights = value >= 0 && value < MountRightsTokens.Choices.Count
            ? MountRightsTokens.Choices[value].Rights
            : MountRights.ReadOnly;
    }

    /// <summary>True when the physical path names an existing directory.</summary>
    public bool PhysicalPathExists =>
        !string.IsNullOrWhiteSpace(PhysicalPath) && _directories.Exists(PhysicalPath.Trim());

    /// <summary>Fills a form from an existing mount, for the edit path.</summary>
    public static MountForm FromDefinition(MountDefinition definition, IDirectoryProbe? directories = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var form = new MountForm(directories)
        {
            PhysicalPath = definition.PhysicalPath,
            VirtualPath = definition.VirtualPath,
            Rights = definition.Rights,
        };

        foreach (var item in definition.Overrides)
            form._overrides.Add(new MountOverrideEntry(item.RelativePath, item.Rights));

        return form;
    }

    /// <summary>Adds a sub-path override.</summary>
    public bool TryAddOverride(string? relativePath, MountRights rights, [NotNullWhen(false)] out string? error)
    {
        error = null;

        var path = FieldText.ToStringOrNull(relativePath);
        if (path is null)
        {
            error = "A sub-path override needs a path relative to the mount root.";
            return false;
        }

        if (path.Contains(':', StringComparison.Ordinal) || path.Contains(';', StringComparison.Ordinal))
        {
            error = "A sub-path may not contain ':' or ';' — those separate the fields of a mount string.";
            return false;
        }

        _overrides.Add(new MountOverrideEntry(path, rights));
        return true;
    }

    /// <summary>Removes the override at <paramref name="index"/>.</summary>
    public void RemoveOverrideAt(int index)
    {
        if (index >= 0 && index < _overrides.Count)
            _overrides.RemoveAt(index);
    }

    /// <summary>Creates the physical directory the user picked but has not created yet.</summary>
    public bool TryCreatePhysicalDirectory([NotNullWhen(false)] out string? error)
    {
        error = null;

        var path = FieldText.ToStringOrNull(PhysicalPath);
        if (path is null)
        {
            error = "Pick a physical path first.";
            return false;
        }

        try
        {
            _directories.Create(path);
            return true;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Builds the mount the form describes. The result is what will be written verbatim
    /// into <c>Orkeon:FileSystem:Mounts</c>.
    /// </summary>
    public bool TryBuild([NotNullWhen(true)] out MountDefinition? definition, out IReadOnlyList<string> errors)
    {
        definition = null;
        var problems = new List<string>();

        var physical = FieldText.ToStringOrNull(PhysicalPath);
        if (physical is null)
            problems.Add("The physical path is required.");

        var virtualPath = FieldText.ToStringOrNull(VirtualPath);
        if (virtualPath is null)
        {
            problems.Add("The virtual path is required.");
        }
        else if (!MountDefinition.IsValidVirtualPath(virtualPath))
        {
            problems.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Virtual path '{virtualPath}' must start with '/' or be a Windows drive path."));
        }

        if (problems.Count > 0)
        {
            errors = problems;
            return false;
        }

        definition = new MountDefinition
        {
            PhysicalPath = physical!,
            VirtualPath = virtualPath!,
            Rights = Rights,
            Overrides = _overrides
                .Select(item => new SubPathRightsOverride(item.RelativePath, item.Rights))
                .ToList(),
        };

        errors = [];
        return true;
    }

    /// <summary>
    /// Validates the mount as the runtime will: the string must survive
    /// <c>FileSystemMount.Parse</c> and the physical path must exist. Collisions with the
    /// other mounts are checked by <see cref="MountEditorModel.Validate"/> on the whole list.
    /// </summary>
    public IReadOnlyList<ValidationMessage> Validate()
    {
        if (!TryBuild(out var definition, out var errors))
        {
            return errors
                .Select(text => ValidationMessage.Error(ValidationCodes.MountFormat, text))
                .ToList();
        }

        return _validator.Validate([definition], requireAtLeastOne: false);
    }
}
