namespace Orkeon.Studio.Core.Localization;

/// <summary>
/// Localization port for every string Core (and the ViewModels compiled against it)
/// fabricates below the view layer (STUDIO-11). The three Studio front-ends share
/// the Core formatters; this port lets each front decide the language: WPF bridges
/// it onto its resx-backed <c>I18n</c> (hot-swappable), the TUIs keep the English
/// default. Keys live in <see cref="StudioStringKeys"/>; the English fallback in
/// <see cref="EnglishStudioStrings"/> is also the key registry of record.
/// </summary>
public interface IStudioStrings
{
    /// <summary>Resolves a key to the current culture's string (the key itself when unknown).</summary>
    string this[string key] { get; }

    /// <summary>Raised when the culture changes, so ViewModels can re-emit their bindings.</summary>
    event EventHandler? CultureChanged;
}

/// <summary>
/// Keys of the strings Core and the ViewModels fabricate. One constant per string —
/// the English text lives in <see cref="EnglishStudioStrings"/>, the French one in
/// each front's resource bridge (WPF: <c>Strings.resx</c>/<c>Strings.fr.resx</c>).
/// <para>
/// Deliberately absent (kept English by policy, like the <c>VALIDATION OK/FAILED</c>
/// verdicts): the bodies of diagnostics that carry a stable code and a remediation —
/// <c>orkeon doctor</c> check names/details, validator message texts, target-detector
/// errors, LLM probe results, and the exit-code descriptions mirroring the CLI table.
/// They are CLI-grade output; translating Studio's copy would desynchronize it from
/// the CLI the user sees in a terminal.
/// </para>
/// </summary>
public static class StudioStringKeys
{
    // ---- Core formatters (tranche 1) ------------------------------------

    /// <summary>"Validation: no findings."</summary>
    public const string ValidationNoFindings = "Core_Validation_NoFindings";

    /// <summary>"Validation: {0} error(s), {1} warning(s), {2} note(s)."</summary>
    public const string ValidationSummary = "Core_Validation_Summary";

    /// <summary>"Nothing ran — {0}"</summary>
    public const string LaunchNothingRan = "Core_Launch_NothingRan";

    /// <summary>"Exit code {0} — {1}"</summary>
    public const string LaunchExitCode = "Core_Launch_ExitCode";

    // ---- LLM preset catalogue (Core Presets/LlmPresets) ------------------

    /// <summary>"Ollama"</summary>
    public const string PresetOllamaTitle = "Core_Preset_Ollama_Title";

    /// <summary>"Local Ollama server."</summary>
    public const string PresetOllamaDescription = "Core_Preset_Ollama_Desc";

    /// <summary>"Docker Model Runner"</summary>
    public const string PresetDmrTitle = "Core_Preset_Dmr_Title";

    /// <summary>"Local llama.cpp engine served by Docker Desktop."</summary>
    public const string PresetDmrDescription = "Core_Preset_Dmr_Desc";

    /// <summary>"OpenAI"</summary>
    public const string PresetOpenAITitle = "Core_Preset_OpenAI_Title";

    /// <summary>"OpenAI cloud API."</summary>
    public const string PresetOpenAIDescription = "Core_Preset_OpenAI_Desc";

    /// <summary>"Other OpenAI-compatible"</summary>
    public const string PresetCustomTitle = "Core_Preset_Custom_Title";

    /// <summary>"DeepSeek, GLM, Mistral, … — base URL and model required."</summary>
    public const string PresetCustomDescription = "Core_Preset_Custom_Desc";

    /// <summary>"None / offline"</summary>
    public const string PresetNoneTitle = "Core_Preset_None_Title";

    /// <summary>"No LLM: runs use the &lt;undefined-llm&gt; echo provider."</summary>
    public const string PresetNoneDescription = "Core_Preset_None_Desc";

    /// <summary>"The custom preset requires both a base URL and a model."</summary>
    public const string PresetErrorCustomIncomplete = "Core_Preset_Error_CustomIncomplete";

    /// <summary>"Unknown preset '{0}'. Supported: {1}."</summary>
    public const string PresetErrorUnknown = "Core_Preset_Error_Unknown";

    /// <summary>"No LLM configured: runs will use the &lt;undefined-llm&gt; echo provider. …"</summary>
    public const string PresetGuidanceNone = "Core_Preset_Guidance_None";

    /// <summary>"API key: referenced from the environment — set it with: export {0}=&lt;your-key&gt;"</summary>
    public const string PresetGuidanceApiKeyEnv = "Core_Preset_Guidance_ApiKeyEnv";

    /// <summary>"Note: the Orkeon runtime reads `{0}` natively; `{1}` is only read by …"</summary>
    public const string PresetGuidanceNonDefaultEnv = "Core_Preset_Guidance_NonDefaultEnv";

    /// <summary>"WARNING: the API key is stored in plain text in the generated file. …"</summary>
    public const string PresetGuidanceInlineKeyWarning = "Core_Preset_Guidance_InlineKeyWarning";

    // ---- Settings resolution chain (Core Storage/SettingsLocations) ------

    /// <summary>"Explicit path"</summary>
    public const string ResolutionStep1Title = "Core_Resolution_Step1_Title";

    /// <summary>"The file passed to the runner with --settings. …"</summary>
    public const string ResolutionStep1Description = "Core_Resolution_Step1_Desc";

    /// <summary>"Next to the crew"</summary>
    public const string ResolutionStep2Title = "Core_Resolution_Step2_Title";

    /// <summary>"{0} in the directory holding the crew configuration."</summary>
    public const string ResolutionStep2Description = "Core_Resolution_Step2_Desc";

    /// <summary>"Shared appsettings directory"</summary>
    public const string ResolutionStep3Title = "Core_Resolution_Step3_Title";

    /// <summary>"appsettings/{0}, searched by walking up from the crew directory …"</summary>
    public const string ResolutionStep3Description = "Core_Resolution_Step3_Desc";

    /// <summary>"Global per-user file"</summary>
    public const string ResolutionStep4Title = "Core_Resolution_Step4_Title";

    /// <summary>"The file written by `orkeon init`: %APPDATA%\Orkeon\appsettings.json …"</summary>
    public const string ResolutionStep4Description = "Core_Resolution_Step4_Desc";

    // ---- Mount rights labels (Core FileSystem/MountRightsTokens) ---------

    /// <summary>"Read only"</summary>
    public const string RightsReadOnly = "Core_Rights_ReadOnly";

    /// <summary>"Read / write (create and delete allowed)"</summary>
    public const string RightsReadWrite = "Core_Rights_ReadWrite";

    /// <summary>"Read / write without delete"</summary>
    public const string RightsReadWriteNoDelete = "Core_Rights_ReadWriteNoDelete";

    // ---- Run target prerequisites (Core Targets/RunTargetRequirements) ---

    /// <summary>"This is a multi-file crew directory: running it uses 'orkeon run &lt;directory&gt;' …"</summary>
    public const string TargetDirectoryRunNotice = "Core_Target_DirectoryRunNotice";

    // ---- Mount override semantics (Core Launch/MountOverrideSemantics) ---

    /// <summary>The full statement of the index-based <c>--mount</c> override rule.</summary>
    public const string MountSemanticsExplanation = "Core_MountSemantics_Explanation";

    /// <summary>The statement of what <c>--allow-external-mounts</c> adds.</summary>
    public const string MountSemanticsExternalMounts = "Core_MountSemantics_ExternalMounts";

    /// <summary>" For this launch the runner injects {0} mount(s) ({1}), so the first --mount occupies '{2}'."</summary>
    public const string MountSemanticsThisLaunch = "Core_MountSemantics_ThisLaunch";

    // ---- Config tab (WPF ViewModel statuses) ------------------------------

    /// <summary>"New empty document. Pick a preset to fill in the Llm section."</summary>
    public const string ConfigNewDocument = "Vm_Config_NewDocument";

    /// <summary>"No file selected."</summary>
    public const string ConfigNoFileSelected = "Vm_Config_NoFileSelected";

    /// <summary>"Loaded {0}."</summary>
    public const string ConfigLoaded = "Vm_Config_Loaded";

    /// <summary>"Not saved: {0} error(s) must be fixed first."</summary>
    public const string ConfigNotSavedErrors = "Vm_Config_NotSavedErrors";

    /// <summary>"Not saved: no destination selected."</summary>
    public const string ConfigNotSavedNoDestination = "Vm_Config_NotSavedNoDestination";

    /// <summary>"Saved to {0}."</summary>
    public const string ConfigSaved = "Vm_Config_Saved";

    /// <summary>"Saved to {0}. Warning {1}: no Llm section, so runs will use the echo provider."</summary>
    public const string ConfigSavedLlmWarning = "Vm_Config_SavedLlmWarning";

    /// <summary>"No problem found."</summary>
    public const string ConfigNoProblem = "Vm_Config_NoProblem";

    /// <summary>"{0} error(s), {1} warning(s)."</summary>
    public const string ConfigErrorsWarnings = "Vm_Config_ErrorsWarnings";

    // ---- Llm section (WPF ViewModel) --------------------------------------

    /// <summary>"No base URL — the runtime falls back to the echo provider."</summary>
    public const string LlmNoBaseUrl = "Vm_Llm_NoBaseUrl";

    /// <summary>"custom (host not in the known-endpoint table)"</summary>
    public const string LlmCustomProvider = "Vm_Llm_CustomProvider";

    /// <summary>"Prefer the {0} environment variable: the runtime reads it with precedence …"</summary>
    public const string LlmApiKeyRecommendation = "Vm_Llm_ApiKeyRecommendation";

    /// <summary>"Testing the connection…"</summary>
    public const string LlmTesting = "Vm_Llm_Testing";

    // ---- Diagnostic panel (WPF ViewModel) ---------------------------------

    /// <summary>"orkeon doctor reported no check."</summary>
    public const string DiagNoCheck = "Vm_Diag_NoCheck";

    /// <summary>"{0} check(s), all green."</summary>
    public const string DiagAllGreen = "Vm_Diag_AllGreen";

    /// <summary>"{0} check(s): {1} failure(s), {2} warning(s)."</summary>
    public const string DiagFindings = "Vm_Diag_Findings";

    // ---- Mounts editor and launch mounts (WPF ViewModels) -----------------

    /// <summary>"{0} mount(s), no error."</summary>
    public const string MountsSummaryOk = "Vm_Mounts_SummaryOk";

    /// <summary>"{0} mount(s), {1} error(s)."</summary>
    public const string MountsSummaryErrors = "Vm_Mounts_SummaryErrors";

    /// <summary>"Select a crew first: the runner injects its own mounts ahead of every --mount, …"</summary>
    public const string MountsSelectCrewFirst = "Vm_Mounts_SelectCrewFirst";

    /// <summary>"Security: this lets a mount point anywhere on the machine, …"</summary>
    public const string MountsExternalWarning = "Vm_Mounts_ExternalWarning";

    /// <summary>"{0} effective mount(s); no appsettings entry is replaced."</summary>
    public const string MountsEffectiveNone = "Vm_Mounts_EffectiveNone";

    /// <summary>"{0} effective mount(s); {1} appsettings entry(ies) replaced by index."</summary>
    public const string MountsEffectiveReplaced = "Vm_Mounts_EffectiveReplaced";

    /// <summary>"auto (injected by the runner)"</summary>
    public const string MountsOriginAuto = "Vm_Mounts_OriginAuto";

    /// <summary>"{0} (replaces «{1}»)"</summary>
    public const string MountsOriginReplaces = "Vm_Mounts_OriginReplaces";

    // ---- Launch tab (WPF ViewModel) ----------------------------------------

    /// <summary>"The orkeon CLI has not been located yet."</summary>
    public const string LaunchBinaryNotLocated = "Vm_Launch_BinaryNotLocated";

    /// <summary>"The orkeon CLI was not found."</summary>
    public const string LaunchBinaryNotFound = "Vm_Launch_BinaryNotFound";

    /// <summary>"Ready to launch."</summary>
    public const string LaunchReady = "Vm_Launch_Ready";

    /// <summary>"No target resolved."</summary>
    public const string LaunchNoTarget = "Vm_Launch_NoTarget";

    /// <summary>"Not launched: {0} error(s) must be fixed first."</summary>
    public const string LaunchNotLaunchedErrors = "Vm_Launch_NotLaunchedErrors";

    /// <summary>"Cancelling: the CLI is asked to stop, and is killed if it does not."</summary>
    public const string LaunchCancelling = "Vm_Launch_Cancelling";

    /// <summary>"Nothing to replay: the entry for '{0}' recorded no arguments."</summary>
    public const string LaunchNothingToReplay = "Vm_Launch_NothingToReplay";

    /// <summary>"Validating…"</summary>
    public const string LaunchValidating = "Vm_Launch_Validating";

    /// <summary>"Running…"</summary>
    public const string LaunchRunning = "Vm_Launch_Running";

    // ---- Watched-run progress panel (BUS-06) --------------------------------

    /// <summary>"Nothing reported yet."</summary>
    public const string RunProgressNothingYet = "Vm_RunProgress_NothingYet";

    /// <summary>"{0} task(s) finished."</summary>
    public const string RunProgressTasksDone = "Vm_RunProgress_TasksDone";

    /// <summary>"Finished successfully."</summary>
    public const string RunProgressSucceeded = "Vm_RunProgress_Succeeded";

    /// <summary>"Finished with a failure."</summary>
    public const string RunProgressFailed = "Vm_RunProgress_Failed";

    /// <summary>"{0} tokens · {1}"</summary>
    public const string RunProgressCost = "Vm_RunProgress_Cost";

    /// <summary>"Show progress instead of raw output"</summary>
    public const string RunProgressWatch = "Vm_RunProgress_Watch";

    /// <summary>"Stream generated text token by token"</summary>
    public const string RunProgressStream = "Vm_RunProgress_Stream";

    /// <summary>"Answer"</summary>
    public const string RunProgressAnswer = "Vm_RunProgress_Answer";

    /// <summary>"An agent is asking:"</summary>
    public const string RunProgressAgentAsks = "Vm_RunProgress_AgentAsks";

    /// <summary>"Reply"</summary>
    public const string RunProgressReply = "Vm_RunProgress_Reply";

    /// <summary>"Hub messages"</summary>
    public const string RunProgressHubMessages = "Vm_RunProgress_HubMessages";


    // ---- Model profiles (Réglages, design v3) -------------------------------

    /// <summary>"New setting" — the freshly created profile's placeholder name.</summary>
    public const string ProfileNewName = "Vm_Profiles_NewName";

    /// <summary>"copy" — suffix of a duplicated profile's name.</summary>
    public const string ProfileCopySuffix = "Vm_Profiles_CopySuffix";


    // ---- Target picker (WPF ViewModel) --------------------------------------

    /// <summary>"No target selected."</summary>
    public const string TargetNone = "Vm_Target_None";

    /// <summary>"{0} — orkeon run {1}"</summary>
    public const string TargetResolved = "Vm_Target_Resolved";

    /// <summary>"{0} script(s) found: pick the one to run."</summary>
    public const string TargetPickScript = "Vm_Target_PickScript";

    /// <summary>"Detection failed."</summary>
    public const string TargetDetectionFailed = "Vm_Target_DetectionFailed";

    /// <summary>"YAML crew file"</summary>
    public const string TargetKindYamlFile = "Vm_Target_KindYamlFile";

    /// <summary>"Scripting crew file"</summary>
    public const string TargetKindScriptFile = "Vm_Target_KindScriptFile";

    /// <summary>"Multi-file crew directory"</summary>
    public const string TargetKindCrewDirectory = "Vm_Target_KindCrewDirectory";

    /// <summary>"Scripting crew directory"</summary>
    public const string TargetKindScriptDirectory = "Vm_Target_KindScriptDirectory";

    // ---- Run log panel (WPF ViewModel) ---------------------------------------

    /// <summary>"{0} line(s)."</summary>
    public const string LogLines = "Vm_Log_Lines";

    /// <summary>"{0} line(s); {1} older line(s) dropped (cap {2})."</summary>
    public const string LogLinesDropped = "Vm_Log_LinesDropped";

    // ---- File dialogs (titles and filters) ------------------------------------

    /// <summary>"Open appsettings.json"</summary>
    public const string DialogOpenAppSettings = "Vm_Dialog_OpenAppSettings";

    /// <summary>"Save appsettings.json"</summary>
    public const string DialogSaveAppSettings = "Vm_Dialog_SaveAppSettings";

    /// <summary>"Select an appsettings.json"</summary>
    public const string DialogSelectAppSettings = "Vm_Dialog_SelectAppSettings";

    /// <summary>"Select an inputs file"</summary>
    public const string DialogSelectInputsFile = "Vm_Dialog_SelectInputsFile";

    /// <summary>"Select the LLM log destination"</summary>
    public const string DialogSelectLlmLogDestination = "Vm_Dialog_SelectLlmLogDest";

    /// <summary>"Select a crew definition"</summary>
    public const string DialogSelectCrewDefinition = "Vm_Dialog_SelectCrewDefinition";

    /// <summary>"Select a crew directory"</summary>
    public const string DialogSelectCrewDirectory = "Vm_Dialog_SelectCrewDirectory";

    /// <summary>"Select the folder to mount"</summary>
    public const string DialogSelectMountFolder = "Vm_Dialog_SelectMountFolder";

    /// <summary>"JSON files|*.json|All files|*.*" — the pipe format is the Win32 dialog contract.</summary>
    public const string DialogFilterJson = "Vm_Dialog_FilterJson";

    /// <summary>"Crew definitions|*.yaml;*.yml;*.ts;*.js|All files|*.*"</summary>
    public const string DialogFilterCrew = "Vm_Dialog_FilterCrew";

    /// <summary>"All files|*.*"</summary>
    public const string DialogFilterAll = "Vm_Dialog_FilterAll";

    // ── the Atelier (SPEC-ORKEON-FORGE §12, UX study §2: no framework word at level 1) ──

    /// <summary>"Summarize a site's news every morning"</summary>
    public const string ForgeExample1 = "Vm_Forge_Example1";

    /// <summary>"Produce a weekly summary from my files"</summary>
    public const string ForgeExample2 = "Vm_Forge_Example2";

    /// <summary>"Compare offers and flag the best one"</summary>
    public const string ForgeExample3 = "Vm_Forge_Example3";

    /// <summary>"Turn a folder of documents into a report"</summary>
    public const string ForgeExample4 = "Vm_Forge_Example4";

    /// <summary>"I am preparing a proposal…"</summary>
    public const string ForgeStatusPreparing = "Vm_Forge_StatusPreparing";

    /// <summary>"Trying it on your example…"</summary>
    public const string ForgeStatusTrying = "Vm_Forge_StatusTrying";

    /// <summary>"Checking the result against what you asked…"</summary>
    public const string ForgeStatusJudging = "Vm_Forge_StatusJudging";

    /// <summary>"Your solution is ready."</summary>
    public const string ForgeStatusReady = "Vm_Forge_StatusReady";

    /// <summary>"Something went wrong — open the details for the technical part."</summary>
    public const string ForgeStatusFailed = "Vm_Forge_StatusFailed";

    /// <summary>"Stopped — you can pick it up again from My solutions."</summary>
    public const string ForgeStatusStopped = "Vm_Forge_StatusStopped";

    /// <summary>"Attempt {0}"</summary>
    public const string ForgeAttempt = "Vm_Forge_Attempt";

    /// <summary>"I could not check this automatically — judge for yourself."</summary>
    public const string ForgeCheckUnverified = "Vm_Forge_CheckUnverified";

    /// <summary>"The try is done (score {0})."</summary>
    public const string ForgeResultScore = "Vm_Forge_ResultScore";

    /// <summary>"Choose where to store the solution"</summary>
    public const string ForgeStorePickTitle = "Vm_Forge_StorePickTitle";
}

/// <summary>
/// English defaults — the culture-neutral fallback every front starts from, and the
/// single place a new Core string is declared before its translations exist.
/// </summary>
public sealed class EnglishStudioStrings : IStudioStrings
{
    /// <summary>The shared instance (the class is immutable).</summary>
    public static EnglishStudioStrings Instance { get; } = new();

    private static readonly Dictionary<string, string> Strings = new(StringComparer.Ordinal)
    {
        [StudioStringKeys.ValidationNoFindings] = "Validation: no findings.",
        [StudioStringKeys.ValidationSummary] = "Validation: {0} error(s), {1} warning(s), {2} note(s).",
        [StudioStringKeys.LaunchNothingRan] = "Nothing ran — {0}",
        [StudioStringKeys.LaunchExitCode] = "Exit code {0} — {1}",

        [StudioStringKeys.PresetOllamaTitle] = "Ollama",
        [StudioStringKeys.PresetOllamaDescription] = "Local Ollama server.",
        [StudioStringKeys.PresetDmrTitle] = "Docker Model Runner",
        [StudioStringKeys.PresetDmrDescription] = "Local llama.cpp engine served by Docker Desktop.",
        [StudioStringKeys.PresetOpenAITitle] = "OpenAI",
        [StudioStringKeys.PresetOpenAIDescription] = "OpenAI cloud API.",
        [StudioStringKeys.PresetCustomTitle] = "Other OpenAI-compatible",
        [StudioStringKeys.PresetCustomDescription] = "DeepSeek, GLM, Mistral, … — base URL and model required.",
        [StudioStringKeys.PresetNoneTitle] = "None / offline",
        [StudioStringKeys.PresetNoneDescription] = "No LLM: runs use the <undefined-llm> echo provider.",
        [StudioStringKeys.PresetErrorCustomIncomplete] = "The custom preset requires both a base URL and a model.",
        [StudioStringKeys.PresetErrorUnknown] = "Unknown preset '{0}'. Supported: {1}.",
        [StudioStringKeys.PresetGuidanceNone] =
            "No LLM configured: runs will use the <undefined-llm> echo provider. " +
            "Configure one when you are ready.",
        [StudioStringKeys.PresetGuidanceApiKeyEnv] =
            "API key: referenced from the environment — set it with: export {0}=<your-key>",
        [StudioStringKeys.PresetGuidanceNonDefaultEnv] =
            "Note: the Orkeon runtime reads `{0}` natively; " +
            "`{1}` is only read by the `orkeon init` / `orkeon llm` probes.",
        [StudioStringKeys.PresetGuidanceInlineKeyWarning] =
            "WARNING: the API key is stored in plain text in the generated file. " +
            "Prefer referencing it from the {0} environment variable.",

        [StudioStringKeys.ResolutionStep1Title] = "Explicit path",
        [StudioStringKeys.ResolutionStep1Description] =
            "The file passed to the runner with --settings. If it does not exist, resolution stops and " +
            "the runtime falls back to environment variables only.",
        [StudioStringKeys.ResolutionStep2Title] = "Next to the crew",
        [StudioStringKeys.ResolutionStep2Description] =
            "{0} in the directory holding the crew configuration.",
        [StudioStringKeys.ResolutionStep3Title] = "Shared appsettings directory",
        [StudioStringKeys.ResolutionStep3Description] =
            "appsettings/{0}, searched by walking up from the crew directory " +
            "(the legacy _shared/ location is still accepted for one release).",
        [StudioStringKeys.ResolutionStep4Title] = "Global per-user file",
        [StudioStringKeys.ResolutionStep4Description] =
            "The file written by `orkeon init`: %APPDATA%\\Orkeon\\appsettings.json on Windows, " +
            "$XDG_CONFIG_HOME/Orkeon/appsettings.json (else ~/.config/Orkeon/appsettings.json) elsewhere.",

        [StudioStringKeys.RightsReadOnly] = "Read only",
        [StudioStringKeys.RightsReadWrite] = "Read / write (create and delete allowed)",
        [StudioStringKeys.RightsReadWriteNoDelete] = "Read / write without delete",

        [StudioStringKeys.TargetDirectoryRunNotice] =
            "This is a multi-file crew directory: running it uses 'orkeon run <directory>', which " +
            "requires Orkeon >= {0}. The CLI installed alongside Studio supports this.",

        [StudioStringKeys.MountSemanticsExplanation] =
            "The runner injects its own mounts first — the crew's configuration directory (the " +
            "script's directory as '/script' for a .ork.ts crew), plus the log directory when LLM " +
            "logging is on — then appends each --mount argument, and writes the whole list as " +
            "'Orkeon:FileSystem:Mounts:{index}'. So the appsettings mount at index 0 is always " +
            "replaced by the auto-injected one, the first --mount replaces the appsettings mount at " +
            "index 1 (index 2 with --llm-log), and the two lists are never merged. Appsettings " +
            "entries past the last written index stay in force.",
        [StudioStringKeys.MountSemanticsExternalMounts] =
            "--allow-external-mounts additionally whitelists each --mount base path under " +
            "'PathSecurity:AdditionalAllowedDirectories', letting mounts point outside the working " +
            "directory (same effect as ORKEON_ALLOW_EXTERNAL_MOUNTS=1).",
        [StudioStringKeys.MountSemanticsThisLaunch] =
            " For this launch the runner injects {0} mount(s) ({1}), so the first --mount occupies '{2}'.",

        [StudioStringKeys.ConfigNewDocument] = "New empty document. Pick a preset to fill in the Llm section.",
        [StudioStringKeys.ConfigNoFileSelected] = "No file selected.",
        [StudioStringKeys.ConfigLoaded] = "Loaded {0}.",
        [StudioStringKeys.ConfigNotSavedErrors] = "Not saved: {0} error(s) must be fixed first.",
        [StudioStringKeys.ConfigNotSavedNoDestination] = "Not saved: no destination selected.",
        [StudioStringKeys.ConfigSaved] = "Saved to {0}.",
        [StudioStringKeys.ConfigSavedLlmWarning] =
            "Saved to {0}. Warning {1}: no Llm section, so runs will use the echo provider.",
        [StudioStringKeys.ConfigNoProblem] = "No problem found.",
        [StudioStringKeys.ConfigErrorsWarnings] = "{0} error(s), {1} warning(s).",

        [StudioStringKeys.LlmNoBaseUrl] = "No base URL — the runtime falls back to the echo provider.",
        [StudioStringKeys.LlmCustomProvider] = "custom (host not in the known-endpoint table)",
        [StudioStringKeys.LlmApiKeyRecommendation] =
            "Prefer the {0} environment variable: the runtime reads it with " +
            "precedence over this file, so the key never has to be stored in clear text.",
        [StudioStringKeys.LlmTesting] = "Testing the connection…",

        [StudioStringKeys.DiagNoCheck] = "orkeon doctor reported no check.",
        [StudioStringKeys.DiagAllGreen] = "{0} check(s), all green.",
        [StudioStringKeys.DiagFindings] = "{0} check(s): {1} failure(s), {2} warning(s).",

        [StudioStringKeys.MountsSummaryOk] = "{0} mount(s), no error.",
        [StudioStringKeys.MountsSummaryErrors] = "{0} mount(s), {1} error(s).",
        [StudioStringKeys.MountsSelectCrewFirst] =
            "Select a crew first: the runner injects its own mounts ahead of every --mount, so which " +
            "configuration key each mount occupies depends on the crew being launched.",
        [StudioStringKeys.MountsExternalWarning] =
            "Security: this lets a mount point anywhere on the machine, outside the working directory. " +
            "Only enable it for a path you chose deliberately.",
        [StudioStringKeys.MountsEffectiveNone] = "{0} effective mount(s); no appsettings entry is replaced.",
        [StudioStringKeys.MountsEffectiveReplaced] =
            "{0} effective mount(s); {1} appsettings entry(ies) replaced by index.",
        [StudioStringKeys.MountsOriginAuto] = "auto (injected by the runner)",
        [StudioStringKeys.MountsOriginReplaces] = "{0} (replaces «{1}»)",

        [StudioStringKeys.LaunchBinaryNotLocated] = "The orkeon CLI has not been located yet.",
        [StudioStringKeys.LaunchBinaryNotFound] = "The orkeon CLI was not found.",
        [StudioStringKeys.LaunchReady] = "Ready to launch.",
        [StudioStringKeys.LaunchNoTarget] = "No target resolved.",
        [StudioStringKeys.LaunchNotLaunchedErrors] = "Not launched: {0} error(s) must be fixed first.",
        [StudioStringKeys.LaunchCancelling] = "Cancelling: the CLI is asked to stop, and is killed if it does not.",
        [StudioStringKeys.LaunchNothingToReplay] = "Nothing to replay: the entry for '{0}' recorded no arguments.",
        [StudioStringKeys.LaunchValidating] = "Validating…",
        [StudioStringKeys.LaunchRunning] = "Running…",
        [StudioStringKeys.RunProgressNothingYet] = "Nothing reported yet.",
        [StudioStringKeys.RunProgressTasksDone] = "{0} task(s) finished.",
        [StudioStringKeys.RunProgressSucceeded] = "Finished successfully.",
        [StudioStringKeys.RunProgressFailed] = "Finished with a failure.",
        [StudioStringKeys.RunProgressCost] = "{0} tokens · {1}",
        [StudioStringKeys.RunProgressWatch] = "Show progress instead of raw output",
        [StudioStringKeys.RunProgressStream] = "Stream generated text token by token",
        [StudioStringKeys.RunProgressAnswer] = "Answer",
        [StudioStringKeys.RunProgressAgentAsks] = "An agent is asking:",
        [StudioStringKeys.RunProgressReply] = "Reply",
        [StudioStringKeys.RunProgressHubMessages] = "Hub messages",

        [StudioStringKeys.ProfileNewName] = "New setting",
        [StudioStringKeys.ProfileCopySuffix] = "copy",

        [StudioStringKeys.TargetNone] = "No target selected.",
        [StudioStringKeys.TargetResolved] = "{0} — orkeon run {1}",
        [StudioStringKeys.TargetPickScript] = "{0} script(s) found: pick the one to run.",
        [StudioStringKeys.TargetDetectionFailed] = "Detection failed.",
        [StudioStringKeys.TargetKindYamlFile] = "YAML crew file",
        [StudioStringKeys.TargetKindScriptFile] = "Scripting crew file",
        [StudioStringKeys.TargetKindCrewDirectory] = "Multi-file crew directory",
        [StudioStringKeys.TargetKindScriptDirectory] = "Scripting crew directory",

        [StudioStringKeys.LogLines] = "{0} line(s).",
        [StudioStringKeys.LogLinesDropped] = "{0} line(s); {1} older line(s) dropped (cap {2}).",

        [StudioStringKeys.DialogOpenAppSettings] = "Open appsettings.json",
        [StudioStringKeys.DialogSaveAppSettings] = "Save appsettings.json",
        [StudioStringKeys.DialogSelectAppSettings] = "Select an appsettings.json",
        [StudioStringKeys.DialogSelectInputsFile] = "Select an inputs file",
        [StudioStringKeys.DialogSelectLlmLogDestination] = "Select the LLM log destination",
        [StudioStringKeys.DialogSelectCrewDefinition] = "Select a crew definition",
        [StudioStringKeys.DialogSelectCrewDirectory] = "Select a crew directory",
        [StudioStringKeys.DialogSelectMountFolder] = "Select the folder to mount",
        [StudioStringKeys.DialogFilterJson] = "JSON files|*.json|All files|*.*",
        [StudioStringKeys.DialogFilterCrew] = "Crew definitions|*.yaml;*.yml;*.ts;*.js|All files|*.*",
        [StudioStringKeys.DialogFilterAll] = "All files|*.*",
        [StudioStringKeys.ForgeExample1] = "Summarize a site's news every morning",
        [StudioStringKeys.ForgeExample2] = "Produce a weekly summary from my files",
        [StudioStringKeys.ForgeExample3] = "Compare offers and flag the best one",
        [StudioStringKeys.ForgeExample4] = "Turn a folder of documents into a report",
        [StudioStringKeys.ForgeStatusPreparing] = "I am preparing a proposal…",
        [StudioStringKeys.ForgeStatusTrying] = "Trying it on your example…",
        [StudioStringKeys.ForgeStatusJudging] = "Checking the result against what you asked…",
        [StudioStringKeys.ForgeStatusReady] = "Your solution is ready.",
        [StudioStringKeys.ForgeStatusFailed] = "Something went wrong — open the details for the technical part.",
        [StudioStringKeys.ForgeStatusStopped] = "Stopped — you can pick it up again from My solutions.",
        [StudioStringKeys.ForgeAttempt] = "Attempt {0}",
        [StudioStringKeys.ForgeCheckUnverified] = "I could not check this automatically — judge for yourself.",
        [StudioStringKeys.ForgeResultScore] = "The try is done (score {0}).",
        [StudioStringKeys.ForgeStorePickTitle] = "Choose where to store the solution",
    };

    /// <summary>Every declared key with its English text, for the front-ends' drift tests.</summary>
    public static IReadOnlyDictionary<string, string> All => Strings;

    /// <inheritdoc />
    public string this[string key] => Strings.GetValueOrDefault(key, key);

    /// <inheritdoc />
    public event EventHandler? CultureChanged
    {
        add { }     // English defaults never change culture.
        remove { }
    }
}
