using System.Globalization;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Storage;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>What a save attempt found before writing anything.</summary>
/// <param name="FieldErrors">Fields that could not be read back (a number field holding letters).</param>
/// <param name="Messages">The document-level findings, including the WIN-01 warning.</param>
internal sealed record SavePreflight(IReadOnlyList<string> FieldErrors, IReadOnlyList<ValidationMessage> Messages)
{
    /// <summary>True when saving must not proceed without the user fixing something.</summary>
    public bool HasBlockingErrors =>
        FieldErrors.Count > 0 || Messages.Any(message => message.Severity == ValidationSeverity.Error);

    /// <summary>True when the user should see the findings before confirming a save.</summary>
    public bool HasWarnings => Messages.Any(message => message.Severity == ValidationSeverity.Warning);

    /// <summary>Every finding as a line, field errors first.</summary>
    public IReadOnlyList<string> Lines =>
        FieldErrors.Select(error => string.Create(CultureInfo.InvariantCulture, $"[ERROR] {error}"))
            .Concat(MessageFormatter.Format(Messages))
            .ToList();
}

/// <summary>
/// The editor session: the document being edited, where it came from, the forms bound to
/// its sections, and the Core operations the screens invoke (validate, preset, save,
/// diagnose). It holds no Terminal.Gui type — the views render it, and the tests drive it
/// without a terminal.
/// </summary>
internal sealed class ConfigEditorModel
{
    private readonly AppSettingsValidator _validator;
    private readonly IDirectoryProbe _directories;
    private readonly OrkeonProcessRunner _runner;
    private readonly ILlmEndpointProbe _llmProbe;

    /// <summary>Creates a session on an empty document.</summary>
    /// <param name="directories">Directory access used to check mount physical paths.</param>
    /// <param name="runner">CLI runner used by the diagnostic button.</param>
    /// <param name="llmProbe">Connectivity probe used by the "Test connection" button.</param>
    public ConfigEditorModel(
        IDirectoryProbe? directories = null,
        OrkeonProcessRunner? runner = null,
        ILlmEndpointProbe? llmProbe = null)
    {
        _directories = directories ?? PhysicalDirectoryProbe.Instance;
        _validator = new AppSettingsValidator(_directories);
        _runner = runner ?? OrkeonProcessRunner.ForCurrentMachine();
        // Lives as long as the editor session, which lives as long as the process.
        _llmProbe = llmProbe ?? HttpLlmEndpointProbe.ForCurrentMachine();

        Mounts = new MountEditorModel(_directories);
        Document = AppSettingsDocument.CreateEmpty();
        LoadForms();
    }

    /// <summary>The document being edited, unknown keys included.</summary>
    public AppSettingsDocument Document { get; private set; }

    /// <summary>Path the document was loaded from or last saved to.</summary>
    public string? CurrentPath { get; private set; }

    /// <summary>The <c>Llm</c> form.</summary>
    public LlmForm Llm { get; } = new();

    /// <summary>The <c>RateLimiting</c> form.</summary>
    public RateLimitingForm RateLimiting { get; } = new();

    /// <summary>The <c>Orkeon:Rag</c> form.</summary>
    public RagForm Rag { get; } = new();

    /// <summary>The <c>Logging:LogLevel</c> form.</summary>
    public LoggingForm Logging { get; } = new();

    /// <summary>The <c>LlmLogging</c> form.</summary>
    public LlmLoggingForm LlmLogging { get; } = new();

    /// <summary>The mount list editor.</summary>
    public MountEditorModel Mounts { get; }

    /// <summary>Directory access shared with the mount forms the screens open.</summary>
    public IDirectoryProbe Directories => _directories;

    /// <summary>Connectivity probe shared with the LLM screen's "Test connection" button.</summary>
    public ILlmEndpointProbe LlmProbe => _llmProbe;

    /// <summary>The section forms, in navigation order.</summary>
    public IReadOnlyList<ISettingsForm> Forms => [Llm, RateLimiting, Rag, Logging, LlmLogging];

    /// <summary>The document as it would be written — the raw, read-only view.</summary>
    public string RawJson => Document.ToJson();

    /// <summary>Fills every form from the current document.</summary>
    public void LoadForms()
    {
        foreach (var form in Forms)
            form.LoadFrom(Document);

        Mounts.LoadFrom(Document);
    }

    /// <summary>
    /// Writes every form back into the document.
    /// </summary>
    /// <returns>Field-level errors; empty when everything was written.</returns>
    public IReadOnlyList<string> ApplyForms()
    {
        var errors = new List<string>();

        foreach (var form in Forms)
            errors.AddRange(form.ApplyTo(Document));

        Mounts.ApplyTo(Document);
        return errors;
    }

    /// <summary>Starts a new, empty document.</summary>
    public void NewDocument()
    {
        Document = AppSettingsDocument.CreateEmpty();
        CurrentPath = null;
        LoadForms();
    }

    /// <summary>Loads a settings file into the session.</summary>
    /// <returns>An error message, or null when the file was loaded.</returns>
    public async Task<string?> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var (document, error) = await AppSettingsFile
            .TryLoadAsync(path, cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
            return error ?? "The file could not be read.";

        Document = document;
        CurrentPath = path;
        LoadForms();
        return null;
    }

    /// <summary>
    /// Applies the forms and validates the resulting document, without writing anything.
    /// </summary>
    /// <param name="scope">
    /// Why the document is being validated. The Save button must pass
    /// <see cref="ValidationScope.Saving"/>: a settings file with no mount cannot start a run
    /// on its own, and writing one is the mistake worth blocking, whereas an empty list is an
    /// ordinary intermediate state while editing.
    /// </param>
    public SavePreflight Preflight(ValidationScope scope = ValidationScope.Editing)
    {
        var fieldErrors = ApplyForms();
        return new SavePreflight(fieldErrors, _validator.Validate(Document, scope));
    }

    /// <summary>
    /// Writes the document to <paramref name="path"/>. The caller decides whether the
    /// findings of <see cref="Preflight"/> allow it; this method only writes.
    /// </summary>
    /// <returns>An error message, or null when the file was written.</returns>
    public async Task<string?> SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await AppSettingsFile.SaveAsync(Document, path, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            return ex.Message;
        }

        CurrentPath = path;
        return null;
    }

    /// <summary>
    /// Applies a preset onto the current document: only the <c>Llm</c> section changes, and
    /// the forms are reloaded so the screens show what was written.
    /// </summary>
    /// <returns>The guidance lines <c>orkeon init</c> prints for the same plan.</returns>
    public IReadOnlyList<string> ApplyPreset(LlmPresetPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        // Forms first: a preset must not be overwritten by stale text still in the fields.
        ApplyForms();
        LlmPresets.Apply(Document, plan);
        LoadForms();

        return LlmPresets.Guidance(plan);
    }

    /// <summary>Where the co-installed <c>orkeon</c> binary was found, for the status line.</summary>
    public BinaryLocation LocateBinary() => _runner.LocateBinary();

    /// <summary>Runs <c>orkeon doctor --json</c> and returns the parsed report.</summary>
    public Task<DoctorReport> RunDoctorAsync(CancellationToken cancellationToken = default) =>
        _runner.RunDoctorAsync(cancellationToken: cancellationToken);
}
