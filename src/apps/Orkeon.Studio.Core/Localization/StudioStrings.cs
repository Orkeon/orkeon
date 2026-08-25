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
/// <c>orkeon doctor</c> check names/details, target-detector errors, LLM probe results,
/// and the exit-code descriptions mirroring the CLI table. They are CLI-grade output;
/// translating Studio's copy would desynchronize it from the CLI the user sees in a
/// terminal. Validator message texts follow a revised split (T-08): the raw English
/// line keeps its role as the expert detail, and a per-code plain-language overlay
/// (<c>Vm_ValMsg_&lt;code&gt;</c> keys, resolved by <c>ValidationMessageViewModel</c>)
/// is what the lists show first.
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

    // ---- Usage metric chips (Launch/UsageMetricsFormatter, W-08) ---------

    /// <summary>"{0} tokens"</summary>
    public const string UsageTokens = "Core_Usage_Tokens";

    /// <summary>"cache {0} % · {1} tokens"</summary>
    public const string UsageCache = "Core_Usage_Cache";

    /// <summary>"{0} s"</summary>
    public const string UsageSeconds = "Core_Usage_Seconds";

    /// <summary>"{0} min {1} s"</summary>
    public const string UsageMinutesSeconds = "Core_Usage_MinutesSeconds";

    // ---- Wizard ask feedback (Teams/CreateTeamViewModel) -----------------

    /// <summary>"The assistant is not running any more — …"</summary>
    public const string WizardAssistantNotRunning = "Vm_Wiz_AssistantNotRunning";

    // ---- Import recognition report (Teams/ImportTeamViewModel, audit 04/12) --

    /// <summary>"{0} — {1} agent(s)"</summary>
    public const string ImportRecognizedAgents = "Vm_Import_RecognizedAgents";

    /// <summary>"No secret in the files"</summary>
    public const string ImportSecretsClean = "Vm_Import_SecretsClean";

    /// <summary>"API keys travel through the environment, and these files carry none."</summary>
    public const string ImportSecretsCleanDetail = "Vm_Import_SecretsCleanDetail";

    /// <summary>"Something looks like a pasted key"</summary>
    public const string ImportSecretsFound = "Vm_Import_SecretsFound";

    /// <summary>"{0} file(s) carry what looks like an inline secret — see the warning below."</summary>
    public const string ImportSecretsFoundDetail = "Vm_Import_SecretsFoundDetail";

    /// <summary>"Tools"</summary>
    public const string ImportToolsLater = "Vm_Import_ToolsLater";

    /// <summary>"The team's tools are checked at its first launch."</summary>
    public const string ImportToolsLaterDetail = "Vm_Import_ToolsLaterDetail";

    // ---- Diagnostic verdict card (Config/DiagnosticViewModel, audit 09/20) --

    /// <summary>"Everything is in place."</summary>
    public const string DiagAllGood = "Vm_Diag_AllGood";

    /// <summary>"One point to fix before launching a team."</summary>
    public const string DiagFixNeeded = "Vm_Diag_FixNeeded";

    /// <summary>"{0} checks passed, {1} warning(s), {2} failure(s)."</summary>
    public const string DiagCounts = "Vm_Diag_Counts";

    // ---- History cards (Launch/LaunchHistoryViewModel, audit 06/15) ------

    /// <summary>"Finished without errors."</summary>
    public const string HistOutcomeSuccess = "Vm_Hist_OutcomeSuccess";

    /// <summary>"Failed (code {0})."</summary>
    public const string HistOutcomeFailed = "Vm_Hist_OutcomeFailed";

    /// <summary>"Interrupted before the end."</summary>
    public const string HistOutcomeCancelled = "Vm_Hist_OutcomeCancelled";

    /// <summary>"Never started."</summary>
    public const string HistOutcomeNotStarted = "Vm_Hist_OutcomeNotStarted";

    // ---- RAG web-fallback status (Config/RagSectionViewModel, T-08) ------

    /// <summary>"Web fallback active: the corrective loop may fetch pages, …"</summary>
    public const string RagWebFallbackActive = "Vm_Rag_WebFallbackActive";

    /// <summary>"Inactive: the corrective policy allows it, but the transport … is still off."</summary>
    public const string RagWebFallbackTransportOff = "Vm_Rag_WebFallbackTransportOff";

    /// <summary>"Inactive: the transport is on, but the corrective policy … is still off."</summary>
    public const string RagWebFallbackPolicyOff = "Vm_Rag_WebFallbackPolicyOff";

    /// <summary>"Off. Both switches must be turned on …"</summary>
    public const string RagWebFallbackOff = "Vm_Rag_WebFallbackOff";

    /// <summary>"already allowed" — the folder picker's faint note on a mounted row.</summary>
    public const string PickerAlreadyMounted = "Vm_Picker_AlreadyMounted";

    /// <summary>"Folders of “{0}”" — the team-mounts modal title.</summary>
    public const string TeamMountsTitle = "Vm_TeamMounts_Title";

    /// <summary>"Without a folder, this team can neither read nor write any file."</summary>
    public const string TeamMountsNone = "Vm_TeamMounts_None";

    /// <summary>"{0} folders: {1}" — the team-mounts footer summary.</summary>
    public const string TeamMountsSummary = "Vm_TeamMounts_Summary";

    /// <summary>"Add an agent" — the agent editor in add mode.</summary>
    public const string AgentEditorTitleAdd = "Vm_AgentEditor_TitleAdd";

    /// <summary>"Edit the agent" — the agent editor in edit mode.</summary>
    public const string AgentEditorTitleEdit = "Vm_AgentEditor_TitleEdit";

    /// <summary>"Add to the team" — the agent editor's save label in add mode.</summary>
    public const string AgentEditorAdd = "Vm_AgentEditor_Add";

    /// <summary>"Save" — the shared action verb.</summary>
    public const string ActSave = "Act_Save";

    /// <summary>"Choose where to export the team" — the export destination browser's caption.</summary>
    public const string DialogExportDestination = "Vm_Dialog_ExportDestination";

    /// <summary>"{0} (read)" — a team card's read-only mount chip.</summary>
    public const string TeamsMountRo = "Vm_Teams_MountRo";

    /// <summary>"{0} (read, write)" — a team card's writable mount chip.</summary>
    public const string TeamsMountRw = "Vm_Teams_MountRw";

    /// <summary>"to try" — the badge of a team that never ran and has no schedule.</summary>
    public const string TeamsToTest = "Vm_Teams_ToTest";

    /// <summary>"Last run: {0}, {1}" — the meta line's history part.</summary>
    public const string TeamsLastRun = "Vm_Teams_LastRun";

    /// <summary>"succeeded" — the last run ended well.</summary>
    public const string TeamsRunOk = "Vm_Teams_RunOk";

    /// <summary>"failed" — the last run did not.</summary>
    public const string TeamsRunFail = "Vm_Teams_RunFail";

    /// <summary>"Never ran" — no history entry names this team.</summary>
    public const string TeamsNeverRan = "Vm_Teams_NeverRan";

    /// <summary>"Setting: {0}" — the meta line's model-profile part.</summary>
    public const string TeamsSettingLabel = "Teams_SettingLabel";

    /// <summary>"Team exported to {0}"</summary>
    public const string TeamsExportedTo = "Vm_Teams_ExportedTo";

    /// <summary>"Export refused — the destination already exists, or the disk said no."</summary>
    public const string TeamsExportFailed = "Vm_Teams_ExportFailed";

    /// <summary>"Declared folders" — the import report's mounts row, when the sidecar has some.</summary>
    public const string ImportMountsDeclared = "Vm_Import_MountsDeclared";

    /// <summary>"{0} folder(s): {1}" — its detail.</summary>
    public const string ImportMountsDeclaredDetail = "Vm_Import_MountsDeclaredDetail";

    /// <summary>"No declared folder" — the sidecar names none (or there is no sidecar).</summary>
    public const string ImportMountsNone = "Vm_Import_MountsNone";

    /// <summary>"Allow its folders after adding — Mes équipes, Changer les dossiers."</summary>
    public const string ImportMountsNoneDetail = "Vm_Import_MountsNoneDetail";

    /// <summary>"reads {0}" — a read-only team mount, in the launcher's meta line.</summary>
    public const string RunMetaReads = "Vm_Run_MetaReads";

    /// <summary>"writes to {0}" — a writable team mount, in the launcher's meta line.</summary>
    public const string RunMetaWrites = "Vm_Run_MetaWrites";

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

    /// <summary>"Anthropic"</summary>
    public const string ProviderAnthropicTitle = "Core_Provider_Anthropic_Title";

    /// <summary>"Claude"</summary>
    public const string ProviderAnthropicDescription = "Core_Provider_Anthropic_Desc";

    /// <summary>"DeepSeek"</summary>
    public const string ProviderDeepSeekTitle = "Core_Provider_DeepSeek_Title";

    /// <summary>"budget-friendly, very capable"</summary>
    public const string ProviderDeepSeekDescription = "Core_Provider_DeepSeek_Desc";

    /// <summary>"Gemini"</summary>
    public const string ProviderGeminiTitle = "Core_Provider_Gemini_Title";

    /// <summary>"Google"</summary>
    public const string ProviderGeminiDescription = "Core_Provider_Gemini_Desc";

    /// <summary>"Groq"</summary>
    public const string ProviderGroqTitle = "Core_Provider_Groq_Title";

    /// <summary>"very fast responses"</summary>
    public const string ProviderGroqDescription = "Core_Provider_Groq_Desc";

    /// <summary>"HuggingFace"</summary>
    public const string ProviderHuggingFaceTitle = "Core_Provider_HuggingFace_Title";

    /// <summary>"multi-model router"</summary>
    public const string ProviderHuggingFaceDescription = "Core_Provider_HuggingFace_Desc";

    /// <summary>"Kimi"</summary>
    public const string ProviderKimiTitle = "Core_Provider_Kimi_Title";

    /// <summary>"Moonshot AI"</summary>
    public const string ProviderKimiDescription = "Core_Provider_Kimi_Desc";

    /// <summary>"Mistral"</summary>
    public const string ProviderMistralTitle = "Core_Provider_Mistral_Title";

    /// <summary>"European"</summary>
    public const string ProviderMistralDescription = "Core_Provider_Mistral_Desc";

    /// <summary>"Qwen"</summary>
    public const string ProviderQwenTitle = "Core_Provider_Qwen_Title";

    /// <summary>"Alibaba DashScope"</summary>
    public const string ProviderQwenDescription = "Core_Provider_Qwen_Desc";

    /// <summary>"Together AI"</summary>
    public const string ProviderTogetherTitle = "Core_Provider_Together_Title";

    /// <summary>"open models"</summary>
    public const string ProviderTogetherDescription = "Core_Provider_Together_Desc";

    /// <summary>"Z.AI (GLM)"</summary>
    public const string ProviderZaiTitle = "Core_Provider_Zai_Title";

    /// <summary>"Zhipu"</summary>
    public const string ProviderZaiDescription = "Core_Provider_Zai_Desc";

    /// <summary>"GPT"</summary>
    public const string ProviderOpenAIShortDescription = "Core_Provider_OpenAI_ShortDesc";

    /// <summary>"any endpoint: type the URL and the model"</summary>
    public const string ProviderCustomShortDescription = "Core_Provider_Custom_ShortDesc";

    /// <summary>"test the install, no model"</summary>
    public const string ProviderNoneShortDescription = "Core_Provider_None_ShortDesc";

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
    /// <summary>"Not saved — the file could not be written: {0}"</summary>
    public const string ConfigNotSavedWriteFailed = "Vm_Config_NotSavedWriteFailed";

    public const string ConfigNotSavedNoDestination = "Vm_Config_NotSavedNoDestination";

    /// <summary>"Not saved yet: authorize at least one folder…"</summary>
    public const string ConfigNotSavedNeedFolder = "Vm_Config_NotSavedNeedFolder";

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

    /// <summary>"Ready to work"</summary>
    public const string RunStateIdle = "Vm_Run_StateIdle";

    /// <summary>"Run in progress"</summary>
    public const string RunStateRunning = "Vm_Run_StateRunning";

    /// <summary>"Run finished"</summary>
    public const string RunStateDone = "Vm_Run_StateDone";

    /// <summary>"waiting"</summary>
    public const string RunBadgeIdle = "Vm_Run_BadgeIdle";

    /// <summary>"running"</summary>
    public const string RunBadgeRunning = "Vm_Run_BadgeRunning";

    /// <summary>"succeeded"</summary>
    public const string RunBadgeDone = "Vm_Run_BadgeDone";

    /// <summary>"failed"</summary>
    public const string RunBadgeFailed = "Vm_Run_BadgeFailed";

    /// <summary>"Launch now"</summary>
    public const string RunButtonLaunch = "Vm_Run_BtnLaunch";

    /// <summary>"Running…"</summary>
    public const string RunButtonRunning = "Vm_Run_BtnRunning";

    /// <summary>"Relaunch"</summary>
    public const string RunButtonRelaunch = "Vm_Run_BtnRelaunch";

    /// <summary>"The Orkeon engine was not found — run the Diagnostic."</summary>
    public const string RunCliMissing = "Vm_Run_CliMissing";

    /// <summary>"{0} agents"</summary>
    public const string RunMetaAgents = "Vm_Run_MetaAgents";

    /// <summary>"setting {0}"</summary>
    public const string RunMetaProfile = "Vm_Run_MetaProfile";

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

    /// <summary>"ON YOUR MACHINE · FREE, NO KEY"</summary>
    public const string ProfileGroupLocal = "Profile_GroupLocal";

    /// <summary>"IN THE CLOUD · API KEY REQUIRED"</summary>
    public const string ProfileGroupCloud = "Profile_GroupCloud";

    /// <summary>"API key {0}"</summary>
    public const string ProfileKeyTitleFor = "Profile_KeyTitleFor";

    /// <summary>"The service's API key"</summary>
    public const string ProfileKeyTitleService = "Profile_KeyTitleService";

    /// <summary>"Studio keeps it in your Windows session — never in a file, never in a shared folder."</summary>
    public const string ProfileKeyExplainer = "Profile_KeyExplainer";

    /// <summary>"Paste your key here (sk-…)"</summary>
    public const string ProfileKeyPlaceholder = "Profile_KeyPlaceholder";

    /// <summary>"Remember the key"</summary>
    public const string ProfileKeyStore = "Profile_KeyStore";

    /// <summary>"key remembered"</summary>
    public const string ProfileKeyStatusSet = "Profile_KeyStatusSet";

    /// <summary>"no key detected"</summary>
    public const string ProfileKeyStatusMissing = "Profile_KeyStatusMissing";

    /// <summary>"No key yet?"</summary>
    public const string ProfileKeyNoKeyYet = "Profile_KeyNoKeyYet";

    /// <summary>"on the provider's site"</summary>
    public const string ProfileKeyOnVendorSite = "Profile_KeyOnVendorSite";

    /// <summary>The expert-only setx hint naming the runtime's native variable.</summary>
    public const string ProfileKeyExpertHint = "Profile_KeyExpertHint";

    /// <summary>"No key needed — the model runs on your machine, nothing leaves it."</summary>
    public const string ProfileLocalNote = "Profile_LocalNote";

    /// <summary>"Without a model, runs answer as an echo — useful to verify the install."</summary>
    public const string ProfileNoneNote = "Profile_NoneNote";

    /// <summary>"API key missing — remember it first"</summary>
    public const string ProfileKeyMissingTest = "Profile_KeyMissingTest";

    /// <summary>"copy" — suffix of a duplicated profile's name.</summary>
    public const string ProfileCopySuffix = "Vm_Profiles_CopySuffix";


    // ---- Creation wizard (design v3) ----------------------------------------

    /// <summary>"Once"</summary>
    public const string WizardFreqOnce = "Vm_Wizard_Freq_Once";

    /// <summary>"Every day"</summary>
    public const string WizardFreqDaily = "Vm_Wizard_Freq_Daily";

    /// <summary>"Every week"</summary>
    public const string WizardFreqWeekly = "Vm_Wizard_Freq_Weekly";

    /// <summary>"A folder on this PC"</summary>
    public const string WizardSourceFolder = "Vm_Wizard_Source_Folder";

    /// <summary>"A website"</summary>
    public const string WizardSourceWeb = "Vm_Wizard_Source_Web";

    /// <summary>"I don't know yet"</summary>
    public const string WizardSourceUnknown = "Vm_Wizard_Source_Unknown";

    /// <summary>"A document"</summary>
    public const string WizardOutputDocument = "Vm_Wizard_Output_Document";

    /// <summary>"A table"</summary>
    public const string WizardOutputTable = "Vm_Wizard_Output_Table";

    /// <summary>"A short message"</summary>
    public const string WizardOutputMessage = "Vm_Wizard_Output_Message";

    /// <summary>"Something else"</summary>
    public const string WizardOutputOther = "Vm_Wizard_Output_Other";

    /// <summary>"How often: {0}."</summary>
    public const string WizardBriefFrequency = "Vm_Wizard_Brief_Frequency";

    /// <summary>"Where the information lives: {0}."</summary>
    public const string WizardBriefSource = "Vm_Wizard_Brief_Source";

    /// <summary>"Expected result: {0}."</summary>
    public const string WizardBriefOutput = "Vm_Wizard_Brief_Output";

    /// <summary>"The result should look like: {0}"</summary>
    public const string WizardBriefShape = "Vm_Wizard_Brief_Shape";

    /// <summary>"Standing instruction for every agent: {0}"</summary>
    public const string WizardBriefConsigne = "Vm_Wizard_Brief_Consigne";

    /// <summary>"Agent" — the card name when the engine named no role.</summary>
    public const string WizardAgentFallback = "Vm_Wizard_AgentFallback";

    /// <summary>"The save failed — the engine refused the promotion: {0}"</summary>
    public const string WizardPromoteFailed = "Vm_Wizard_PromoteFailed";

    /// <summary>"The import failed — nothing was copied. Check access to the source and try again."</summary>
    public const string ImportFailed = "Vm_Import_Failed";

    /// <summary>"{0} — done in {1} s"</summary>
    public const string WizardActivityDone = "Vm_Wizard_ActivityDone";

    /// <summary>"{0} — failed"</summary>
    public const string WizardActivityFailed = "Vm_Wizard_ActivityFailed";

    /// <summary>"Describe the work to continue."</summary>
    public const string WizardHintDescribe = "Vm_Wizard_Hint_Describe";

    /// <summary>"The format is free: describe the expected result."</summary>
    public const string WizardHintOutcome = "Vm_Wizard_Hint_Outcome";

    /// <summary>"Answer the three precisions."</summary>
    public const string WizardHintAnswers = "Vm_Wizard_Hint_Answers";

    /// <summary>"Everything is there — I can compose the team."</summary>
    public const string WizardHintReady = "Vm_Wizard_Hint_Ready";


    // ---- Mes équipes (design v3) --------------------------------------------

    /// <summary>"Looks good to me"</summary>
    public const string WizardDecisionAccept = "Vm_Wizard_Decision_Accept";

    /// <summary>"Fix and retry"</summary>
    public const string WizardDecisionRefine = "Vm_Wizard_Decision_Refine";

    /// <summary>"Abandon"</summary>
    public const string WizardDecisionAbort = "Vm_Wizard_Decision_Abort";


    /// <summary>"On demand"</summary>
    public const string TeamsOnDemand = "Vm_Teams_OnDemand";

    /// <summary>"Every day at {0}"</summary>
    public const string TeamsDaily = "Vm_Teams_Daily";

    /// <summary>"Every hour"</summary>
    public const string TeamsHourly = "Vm_Teams_Hourly";


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
        [StudioStringKeys.RagWebFallbackActive] = "Web fallback active: the corrective loop may fetch pages, each one validated against prompt injection.",
        [StudioStringKeys.RagWebFallbackTransportOff] = "Inactive: the corrective policy allows it, but the transport (Orkeon:Rag:WebFallback) is still off.",
        [StudioStringKeys.RagWebFallbackPolicyOff] = "Inactive: the transport is on, but the corrective policy (Orkeon:Rag:Corrective:WebFallback) is still off.",
        [StudioStringKeys.RagWebFallbackOff] = "Off. Both switches must be turned on for the corrective loop to reach the web.",
        [StudioStringKeys.PickerAlreadyMounted] = "already allowed",
        [StudioStringKeys.TeamMountsTitle] = "Folders of “{0}”",
        [StudioStringKeys.TeamMountsNone] = "Without a folder, this team can neither read nor write any file.",
        [StudioStringKeys.TeamMountsSummary] = "{0} folders: {1}",
        [StudioStringKeys.AgentEditorTitleAdd] = "Add an agent",
        [StudioStringKeys.AgentEditorTitleEdit] = "Edit the agent",
        [StudioStringKeys.AgentEditorAdd] = "Add to the team",
        [StudioStringKeys.ActSave] = "Save",
        [StudioStringKeys.DialogExportDestination] = "Choose where to export the team",
        [StudioStringKeys.TeamsMountRo] = "{0} (read)",
        [StudioStringKeys.TeamsMountRw] = "{0} (read, write)",
        [StudioStringKeys.TeamsToTest] = "to try",
        [StudioStringKeys.TeamsLastRun] = "Last run: {0}, {1}",
        [StudioStringKeys.TeamsRunOk] = "succeeded",
        [StudioStringKeys.TeamsRunFail] = "failed",
        [StudioStringKeys.TeamsNeverRan] = "Never ran",
        [StudioStringKeys.TeamsSettingLabel] = "Setting: {0}",
        [StudioStringKeys.ConfigNotSavedWriteFailed] = "Not saved — the file could not be written: {0}",
        [StudioStringKeys.TeamsExportedTo] = "Team exported to {0}",
        [StudioStringKeys.TeamsExportFailed] = "Export refused — the destination already exists, or the disk said no.",
        [StudioStringKeys.ImportMountsDeclared] = "Declared folders",
        [StudioStringKeys.ImportMountsDeclaredDetail] = "{0} folder(s): {1}",
        [StudioStringKeys.ImportMountsNone] = "No declared folder",
        [StudioStringKeys.ImportMountsNoneDetail] = "Allow its folders after adding — Mes équipes, Changer les dossiers.",
        [StudioStringKeys.RunMetaReads] = "reads {0}",
        [StudioStringKeys.RunMetaWrites] = "writes to {0}",
        [StudioStringKeys.HistOutcomeSuccess] = "Finished without errors.",
        [StudioStringKeys.HistOutcomeFailed] = "Failed (code {0}).",
        [StudioStringKeys.HistOutcomeCancelled] = "Interrupted before the end.",
        [StudioStringKeys.HistOutcomeNotStarted] = "Never started.",
        [StudioStringKeys.DiagAllGood] = "Everything is in place.",
        [StudioStringKeys.DiagFixNeeded] = "One point to fix before launching a team.",
        [StudioStringKeys.DiagCounts] = "{0} checks passed, {1} warning(s), {2} failure(s).",
        [StudioStringKeys.ConfigNotSavedNeedFolder] = "Not saved yet: authorize at least one folder (Dossiers autorisés tab) — your changes will be saved as soon as one is in place.",
        [StudioStringKeys.WizardAssistantNotRunning] = "The assistant is not running — start the composition (step 1, Composer) or resume the session; your question was kept.",
        ["Vm_Doctor_appsettings"] = "The settings file is readable",
        ["Vm_Doctor_dotnet-runtime"] = "The .NET runtime",
        ["Vm_Doctor_esbuild"] = "The script compiler (esbuild)",
        ["Vm_Doctor_llm-config"] = "The model configuration",
        ["Vm_Doctor_llm-reachability"] = "The connection to the model",
        ["Vm_Doctor_local-embeddings"] = "Local embeddings",
        ["Vm_Doctor_onnx-reranker"] = "The ONNX reranker",
        ["Vm_Doctor_tree-sitter"] = "The code parser (tree-sitter)",
        ["Vm_Doctor_workspace-write"] = "Write access to the workspace",
        [StudioStringKeys.ImportRecognizedAgents] = "{0} — {1} agent(s)",
        [StudioStringKeys.ImportSecretsClean] = "No secret in the files",
        [StudioStringKeys.ImportSecretsCleanDetail] = "API keys travel through the environment, and these files carry none.",
        [StudioStringKeys.ImportSecretsFound] = "Something looks like a pasted key",
        [StudioStringKeys.ImportSecretsFoundDetail] = "{0} file(s) carry what looks like an inline secret — see the warning below.",
        [StudioStringKeys.ImportToolsLater] = "Tools",
        [StudioStringKeys.ImportToolsLaterDetail] = "The team's tools are checked at its first launch.",
        ["Vm_ValMsg_WIN-01"] = "No Llm section: the engine will start on its built-in defaults (Ollama, local).",
        ["Vm_ValMsg_STUDIO-JSON"] = "The settings file is not valid JSON.",
        ["Vm_ValMsg_STUDIO-TYPE"] = "A settings field has the wrong type.",
        ["Vm_ValMsg_STUDIO-LLM-APIKEY"] = "An API key is written inside the file — move it to an environment variable.",
        ["Vm_ValMsg_STUDIO-RAG-PROFILE"] = "The document-index profile named here is unknown.",
        ["Vm_ValMsg_STUDIO-MOUNT-EMPTY"] = "No folder is allowed yet: add at least one.",
        ["Vm_ValMsg_STUDIO-MOUNT-FORMAT"] = "A folder entry is malformed.",
        ["Vm_ValMsg_STUDIO-MOUNT-PATH"] = "An allowed folder does not exist on this machine.",
        ["Vm_ValMsg_STUDIO-MOUNT-COLLISION"] = "Two folders share the same internal name.",
        ["Vm_ValMsg_STUDIO-LAUNCH-OPTION"] = "This option does not apply to the selected team.",
        ["Vm_ValMsg_STUDIO-LAUNCH-VERBOSE"] = "The verbosity level is not valid.",
        ["Vm_ValMsg_STUDIO-LAUNCH-VAR"] = "A variable is malformed (expected name=value).",
        ["Vm_ValMsg_STUDIO-LAUNCH-MOUNT"] = "An added folder line is empty.",
        ["Vm_ValMsg_STUDIO-LAUNCH-INPUTS"] = "Two conflicting input sources are set — keep one.",
        [StudioStringKeys.ValidationSummary] = "Validation: {0} error(s), {1} warning(s), {2} note(s).",
        [StudioStringKeys.LaunchNothingRan] = "Nothing ran — {0}",
        [StudioStringKeys.LaunchExitCode] = "Exit code {0} — {1}",
        [StudioStringKeys.UsageTokens] = "{0} tokens",
        [StudioStringKeys.UsageCache] = "cache {0} % · {1} tokens",
        [StudioStringKeys.UsageSeconds] = "{0} s",
        [StudioStringKeys.UsageMinutesSeconds] = "{0} min {1} s",
        [StudioStringKeys.RunStateIdle] = "Ready to work",
        [StudioStringKeys.RunStateRunning] = "Run in progress",
        [StudioStringKeys.RunStateDone] = "Run finished",
        [StudioStringKeys.RunBadgeIdle] = "waiting",
        [StudioStringKeys.RunBadgeRunning] = "running",
        [StudioStringKeys.RunBadgeDone] = "succeeded",
        [StudioStringKeys.RunBadgeFailed] = "failed",
        [StudioStringKeys.RunButtonLaunch] = "Launch now",
        [StudioStringKeys.RunButtonRunning] = "Running…",
        [StudioStringKeys.RunButtonRelaunch] = "Relaunch",
        [StudioStringKeys.RunCliMissing] = "The Orkeon engine was not found — run the Diagnostic.",
        [StudioStringKeys.RunMetaAgents] = "{0} agents",
        [StudioStringKeys.RunMetaProfile] = "setting {0}",

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
        [StudioStringKeys.ProviderAnthropicTitle] = "Anthropic",
        [StudioStringKeys.ProviderAnthropicDescription] = "Claude",
        [StudioStringKeys.ProviderDeepSeekTitle] = "DeepSeek",
        [StudioStringKeys.ProviderDeepSeekDescription] = "budget-friendly, very capable",
        [StudioStringKeys.ProviderGeminiTitle] = "Gemini",
        [StudioStringKeys.ProviderGeminiDescription] = "Google",
        [StudioStringKeys.ProviderGroqTitle] = "Groq",
        [StudioStringKeys.ProviderGroqDescription] = "very fast responses",
        [StudioStringKeys.ProviderHuggingFaceTitle] = "HuggingFace",
        [StudioStringKeys.ProviderHuggingFaceDescription] = "multi-model router",
        [StudioStringKeys.ProviderKimiTitle] = "Kimi",
        [StudioStringKeys.ProviderKimiDescription] = "Moonshot AI",
        [StudioStringKeys.ProviderMistralTitle] = "Mistral",
        [StudioStringKeys.ProviderMistralDescription] = "European",
        [StudioStringKeys.ProviderQwenTitle] = "Qwen",
        [StudioStringKeys.ProviderQwenDescription] = "Alibaba DashScope",
        [StudioStringKeys.ProviderTogetherTitle] = "Together AI",
        [StudioStringKeys.ProviderTogetherDescription] = "open models",
        [StudioStringKeys.ProviderZaiTitle] = "Z.AI (GLM)",
        [StudioStringKeys.ProviderZaiDescription] = "Zhipu",
        [StudioStringKeys.ProviderOpenAIShortDescription] = "GPT",
        [StudioStringKeys.ProviderCustomShortDescription] = "any endpoint: type the URL and the model",
        [StudioStringKeys.ProviderNoneShortDescription] = "test the install, no model",
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
        [StudioStringKeys.ProfileGroupLocal] = "ON YOUR MACHINE · FREE, NO KEY",
        [StudioStringKeys.ProfileGroupCloud] = "IN THE CLOUD · API KEY REQUIRED",
        [StudioStringKeys.ProfileKeyTitleFor] = "API key {0}",
        [StudioStringKeys.ProfileKeyTitleService] = "The service's API key",
        [StudioStringKeys.ProfileKeyExplainer] = "Studio keeps it in your Windows session — never in a file, never in a shared folder.",
        [StudioStringKeys.ProfileKeyPlaceholder] = "Paste your key here (sk-…)",
        [StudioStringKeys.ProfileKeyStore] = "Remember the key",
        [StudioStringKeys.ProfileKeyStatusSet] = "key remembered",
        [StudioStringKeys.ProfileKeyStatusMissing] = "no key detected",
        [StudioStringKeys.ProfileKeyNoKeyYet] = "No key yet?",
        [StudioStringKeys.ProfileKeyOnVendorSite] = "on the provider's site",
        [StudioStringKeys.ProfileKeyExpertHint] = "setx ORKEON_Llm__ApiKey \"sk-…\" — read natively by the runtime, wins over any file",
        [StudioStringKeys.ProfileLocalNote] = "No key needed — the model runs on your machine, nothing leaves it.",
        [StudioStringKeys.ProfileNoneNote] = "Without a model, runs answer as an echo — useful to verify the install.",
        [StudioStringKeys.ProfileKeyMissingTest] = "API key missing — remember it first",
        [StudioStringKeys.ProfileCopySuffix] = "copy",

        [StudioStringKeys.WizardFreqOnce] = "Once",
        [StudioStringKeys.WizardFreqDaily] = "Every day",
        [StudioStringKeys.WizardFreqWeekly] = "Every week",
        [StudioStringKeys.WizardSourceFolder] = "A folder on this PC",
        [StudioStringKeys.WizardSourceWeb] = "A website",
        [StudioStringKeys.WizardSourceUnknown] = "I don't know yet",
        [StudioStringKeys.WizardOutputDocument] = "A document",
        [StudioStringKeys.WizardOutputTable] = "A table",
        [StudioStringKeys.WizardOutputMessage] = "A short message",
        [StudioStringKeys.WizardOutputOther] = "Something else",
        [StudioStringKeys.WizardBriefFrequency] = "How often: {0}.",
        [StudioStringKeys.WizardBriefSource] = "Where the information lives: {0}.",
        [StudioStringKeys.WizardBriefOutput] = "Expected result: {0}.",
        [StudioStringKeys.WizardBriefShape] = "The result should look like: {0}",
        [StudioStringKeys.WizardBriefConsigne] = "Standing instruction for every agent: {0}",
        [StudioStringKeys.WizardAgentFallback] = "Agent",
        [StudioStringKeys.WizardPromoteFailed] = "The save failed — the engine refused the promotion: {0}",
        [StudioStringKeys.ImportFailed] = "The import failed — nothing was copied. Check access to the source and try again.",
        [StudioStringKeys.WizardActivityDone] = "{0} — done in {1} s",
        [StudioStringKeys.WizardActivityFailed] = "{0} — failed",
        [StudioStringKeys.WizardHintDescribe] = "Describe the work to continue.",
        [StudioStringKeys.WizardHintOutcome] = "The format is free: describe the expected result.",
        [StudioStringKeys.WizardHintAnswers] = "Answer the three precisions.",
        [StudioStringKeys.WizardHintReady] = "Everything is there — I can compose the team.",

        [StudioStringKeys.WizardDecisionAccept] = "Looks good to me",
        [StudioStringKeys.WizardDecisionRefine] = "Fix and retry",
        [StudioStringKeys.WizardDecisionAbort] = "Abandon",

        [StudioStringKeys.TeamsOnDemand] = "On demand",
        [StudioStringKeys.TeamsDaily] = "Every day at {0}",
        [StudioStringKeys.TeamsHourly] = "Every hour",

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
