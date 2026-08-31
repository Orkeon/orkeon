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
    public const string ValidationNoFindings = "Studio.Diagnostics.NoFindings";

    /// <summary>"Validation: {0} error(s), {1} warning(s), {2} note(s)."</summary>
    public const string ValidationSummary = "Studio.Diagnostics.Summary";

    /// <summary>"Nothing ran — {0}"</summary>
    public const string LaunchNothingRan = "Studio.Run.NothingRan";

    /// <summary>"Exit code {0} — {1}"</summary>
    public const string LaunchExitCode = "Studio.Run.ExitCode";

    // ---- Usage metric chips (Launch/UsageMetricsFormatter, W-08) ---------

    /// <summary>"{0} tokens"</summary>
    public const string UsageTokens = "Studio.Run.Tokens";

    /// <summary>"cache {0} % · {1} tokens"</summary>
    public const string UsageCache = "Studio.Run.Cache";

    /// <summary>"{0} s"</summary>
    public const string UsageSeconds = "Studio.Run.Seconds";

    /// <summary>"{0} min {1} s"</summary>
    public const string UsageMinutesSeconds = "Studio.Run.MinutesSeconds";

    // ---- Wizard ask feedback (Teams/CreateTeamViewModel) -----------------

    /// <summary>"The assistant is not running any more — …"</summary>
    public const string WizardAssistantNotRunning = "Studio.Create.AssistantNotRunning";

    /// <summary>The four stepper labels. They already lived in the WPF resx for the
    /// stepper itself; the registry adopts the same keys so the draft line below the nav
    /// can name a step without a second spelling of it.</summary>
    public const string WizardStep1 = "Studio.Create.Step1";

    /// <summary>"Compose"</summary>
    public const string WizardStep2 = "Studio.Create.Step2";

    /// <summary>"Try"</summary>
    public const string WizardStep3 = "Studio.Create.Step3";

    /// <summary>"Adopt"</summary>
    public const string WizardStep4 = "Studio.Create.Step4";

    /// <summary>"Step {0} of {1} · {2}" — never assembled by hand: Chinese has no
    /// « sur », and a concatenation would ship the French joiner to every culture.</summary>
    public const string WizardDraftStepPattern = "Studio.Create.DraftStepPattern";

    /// <summary>"A creation in progress"</summary>
    public const string WizardDraftTitle = "Studio.Create.DraftTitle";

    /// <summary>"The assistant is waiting for your answer"</summary>
    public const string WizardDraftWaitingTitle = "Studio.Create.DraftWaitingTitle";

    /// <summary>"resume the conversation"</summary>
    public const string WizardDraftResume = "Studio.Create.DraftResume";


    // ---- The assistant conversation (Teams/ChatThreadViewModel, 30/08 mock) ----
    // Every visible word of the thread, its interview, its two running registers and
    // its keyword bank. The regex patterns are localized on purpose: the words a user
    // types to ask about cost or folders are not the same in every language.
    /// <summary>"Assistant conversation"</summary>
    public const string ChatTitle = "Studio.Chat.Title";
    /// <summary>"What I've noted"</summary>
    public const string ChatRecap = "Studio.Chat.Recap";
    /// <summary>"YOUR BRIEF"</summary>
    public const string ChatYourBrief = "Studio.Chat.YourBrief";
    /// <summary>"Edit"</summary>
    public const string ChatEdit = "Studio.Chat.Edit";
    /// <summary>"Stop"</summary>
    public const string ChatStop = "Studio.Chat.Stop";
    /// <summary>"Send"</summary>
    public const string ChatSend = "Studio.Chat.Send";
    /// <summary>"Reply"</summary>
    public const string ChatReply = "Studio.Chat.Reply";
    /// <summary>"Skip this question"</summary>
    public const string ChatSkip = "Studio.Chat.Skip";
    /// <summary>"I don't know — do your best."</summary>
    public const string ChatSkipAnswer = "Studio.Chat.SkipAnswer";
    /// <summary>"Enter to send · Shift+Enter for a new line"</summary>
    public const string ChatKeyHint = "Studio.Chat.KeyHint";
    /// <summary>"Ask the assistant a question…"</summary>
    public const string ChatPlaceholder = "Studio.Chat.Placeholder";

    /// <summary>The composer watermark under an open question.</summary>
    public const string ChatPlaceholderAnswer = "Studio.Chat.PlaceholderAnswer";
    /// <summary>"Ask a question"</summary>
    public const string ChatAsk = "Studio.Chat.Ask";
    /// <summary>"Question"</summary>
    public const string ChatAskShort = "Studio.Chat.AskShort";
    /// <summary>"Ask the assistant a question"</summary>
    public const string ChatAskTip = "Studio.Chat.AskTip";
    /// <summary>"See the conversation"</summary>
    public const string ChatSeeConversation = "Studio.Chat.SeeConversation";
    /// <summary>"Close the conversation"</summary>
    public const string ChatClose = "Studio.Chat.Close";
    /// <summary>"You haven't described anything yet."</summary>
    public const string ChatBriefEmpty = "Studio.Chat.BriefEmpty";
    /// <summary>"The assistant is composing your team"</summary>
    public const string ChatStripBusy = "Studio.Chat.StripBusy";
    /// <summary>"The assistant needs one more detail"</summary>
    public const string ChatStripAsking = "Studio.Chat.StripAsking";
    /// <summary>"brief complete · {0} message(s)"</summary>
    public const string ChatStatusDonePattern = "Studio.Chat.StatusDonePattern";
    /// <summary>"{0} message(s)"</summary>
    public const string ChatStatusMessagesPattern = "Studio.Chat.StatusMessagesPattern";
    /// <summary>"no question yet"</summary>
    public const string ChatStatusNoQuestion = "Studio.Chat.StatusNoQuestion";

    /// <summary>Said in the thread when the engine stops mid-interview.</summary>
    public const string ChatSessionEnded = "Studio.Chat.SessionEnded";

    /// <summary>The status line while the assistant waits on the user.</summary>
    public const string ChatStatusWaiting = "Studio.Chat.StatusWaiting";
    /// <summary>"{0} / {1}"</summary>
    public const string ChatRecapProgressPattern = "Studio.Chat.RecapProgressPattern";
    /// <summary>"{0} · {1}"</summary>
    public const string ChatProfileSuffixPattern = "Studio.Chat.ProfileSuffixPattern";
    /// <summary>"reading the brief"</summary>
    public const string ChatStatus1 = "Studio.Chat.Status1";
    /// <summary>"analysing the source"</summary>
    public const string ChatStatus2 = "Studio.Chat.Status2";
    /// <summary>"composing the roles"</summary>
    public const string ChatStatus3 = "Studio.Chat.Status3";
    /// <summary>"I'm re-reading your description…"</summary>
    public const string ChatThinking1 = "Studio.Chat.Thinking1";
    /// <summary>"I'm looking at where the documents are…"</summary>
    public const string ChatThinking2 = "Studio.Chat.Thinking2";
    /// <summary>"I'm sketching the team's roles…"</summary>
    public const string ChatThinking3 = "Studio.Chat.Thinking3";
    /// <summary>"Thank you — I have what I need to compose the team."</summary>
    public const string ChatWrapBody = "Studio.Chat.WrapBody";
    /// <summary>"I'll show you the composition I propose: the agents, the allowed fo…"</summary>
    public const string ChatWrapDetail = "Studio.Chat.WrapDetail";
    /// <summary>"The work you described"</summary>
    public const string ChatFactBrief = "Studio.Chat.FactBrief";
    /// <summary>"Rhythm"</summary>
    public const string ChatFactRhythm = "Studio.Chat.FactRhythm";
    /// <summary>"Document source"</summary>
    public const string ChatFactSource = "Studio.Chat.FactSource";
    /// <summary>"Shape of the result"</summary>
    public const string ChatFactOutput = "Studio.Chat.FactOutput";
    /// <summary>"Composition instruction"</summary>
    public const string ChatFactComposeNote = "Studio.Chat.FactComposeNote";
    /// <summary>"Trial instruction"</summary>
    public const string ChatFactTryNote = "Studio.Chat.FactTryNote";
    /// <summary>"Adoption instruction"</summary>
    public const string ChatFactAdoptNote = "Studio.Chat.FactAdoptNote";
    /// <summary>"pending"</summary>
    public const string ChatFactPending = "Studio.Chat.FactPending";
    /// <summary>"Ask your question: the assistant answers without changing anything…"</summary>
    public const string ChatEmptyCreate = "Studio.Chat.EmptyCreate";
    /// <summary>"Ask about the run in progress: why a step is slow, what happens on…"</summary>
    public const string ChatEmptyRun = "Studio.Chat.EmptyRun";
    /// <summary>"Ask about a past run: why it failed, what has changed since, how to…"</summary>
    public const string ChatEmptyHistory = "Studio.Chat.EmptyHistory";
    /// <summary>"folder|file|access|permission|read|writ"</summary>
    public const string ChatRuleFolders = "Studio.Chat.RuleFolders";
    /// <summary>"cost|price|paid|bill|token|expensive"</summary>
    public const string ChatRuleCost = "Studio.Chat.RuleCost";
    /// <summary>"time|duration|how long|slow|fast|minute"</summary>
    public const string ChatRuleDuration = "Studio.Chat.RuleDuration";
    /// <summary>"error|fail|crash|break|bug"</summary>
    public const string ChatRuleError = "Studio.Chat.RuleError";
    /// <summary>"schedul|plan|hour|cron|automatic|morning"</summary>
    public const string ChatRuleSchedule = "Studio.Chat.RuleSchedule";
    /// <summary>"confidential|privat|data|gdpr|secur|leave"</summary>
    public const string ChatRulePrivacy = "Studio.Chat.RulePrivacy";
    /// <summary>"Agents only see the folders allowed in Settings. A read-only folder…"</summary>
    public const string ChatAnswerFolders = "Studio.Chat.AnswerFolders";
    /// <summary>"A local setting (Ollama) costs nothing. In the cloud the cost depen…"</summary>
    public const string ChatAnswerCost = "Studio.Chat.AnswerCost";
    /// <summary>"On this dataset, count one to two minutes per run locally. The tech…"</summary>
    public const string ChatAnswerDuration = "Studio.Chat.AnswerDuration";
    /// <summary>"If a step fails the run stops and nothing is written to the output…"</summary>
    public const string ChatAnswerError = "Studio.Chat.AnswerError";
    /// <summary>"A scheduled team runs even with Studio closed, as long as the machi…"</summary>
    public const string ChatAnswerSchedule = "Studio.Chat.AnswerSchedule";
    /// <summary>"With a local setting, no data leaves the machine. With a cloud sett…"</summary>
    public const string ChatAnswerPrivacy = "Studio.Chat.AnswerPrivacy";
    /// <summary>"Describe the work in one sentence, as you would to a colleague: wha…"</summary>
    public const string ChatReplyStep1 = "Studio.Chat.ReplyStep1";
    /// <summary>"The proposed agents share the reading, the writing and the proofrea…"</summary>
    public const string ChatReplyStep2 = "Studio.Chat.ReplyStep2";
    /// <summary>"The trial touches nothing: read-only, on a sample of documents, and…"</summary>
    public const string ChatReplyStep3 = "Studio.Chat.ReplyStep3";
    /// <summary>"After adoption the team works on its own at the chosen moment, even…"</summary>
    public const string ChatReplyStep4 = "Studio.Chat.ReplyStep4";
    /// <summary>"I'm following the run live. If a step stops, tell me: I read the te…"</summary>
    public const string ChatReplyRun = "Studio.Chat.ReplyRun";
    /// <summary>"Every line of the history keeps its full log. Ask me « why did the…"</summary>
    public const string ChatReplyHistory = "Studio.Chat.ReplyHistory";


    // ---- The language picker (Shell/LanguageSelectorViewModel, T-14) ----
    /// <summary>"SYSTEM"</summary>
    public const string LanguageSystem = "Studio.Shell.System";

    /// <summary>"CHOSEN"</summary>
    public const string LanguageChosen = "Studio.Shell.Chosen";

    /// <summary>"system language"</summary>
    public const string LanguageFromSystem = "Studio.Shell.FromSystem";

    /// <summary>"saved choice"</summary>
    public const string LanguageFromChoice = "Studio.Shell.FromChoice";

    /// <summary>"{0} · {1}"</summary>
    public const string LanguageHeaderPattern = "Studio.Shell.HeaderPattern";

    /// <summary>"{0} — {1}"</summary>
    public const string LanguageTooltipPattern = "Studio.Shell.TooltipPattern";


    // ---- Tool chips and the guided tour counter (T-15/T-18) ----
    // The fs.* tools are scoped to one folder each, and that scope is the thing the
    // user has to see — so their labels are patterns, never a bare verb.
    /// <summary>"read {0}"</summary>
    public const string ToolReadScoped = "Studio.Common.ReadScoped";

    /// <summary>"list {0}"</summary>
    public const string ToolListScoped = "Studio.Common.ListScoped";

    /// <summary>"write {0}"</summary>
    public const string ToolWriteScoped = "Studio.Common.WriteScoped";


    /// <summary>"browse a web page"</summary>
    public const string ToolWeb = "Studio.Common.Web";

    /// <summary>"search the index"</summary>
    public const string ToolRag = "Studio.Common.Rag";

    /// <summary>"{0} / {1}"</summary>
    public const string TourCounterPattern = "Studio.Shell.CounterPattern";


    // ---- Diagnostics, in plain words (T-30) ----
    // The CLI prints its detail in English only. Novice reads these instead; the raw
    // line stays, under the mono identifier, where Expert expects it.
    /// <summary>"In place."</summary>
    public const string DoctorStatusOk = "Studio.Diagnostics.StatusOk";

    /// <summary>"Usable, but worth a look."</summary>
    public const string DoctorStatusWarning = "Studio.Diagnostics.StatusWarning";

    /// <summary>"Missing — a team will not run until this is fixed."</summary>
    public const string DoctorStatusFailure = "Studio.Diagnostics.StatusFailure";

    /// <summary>"Not checked."</summary>
    public const string DoctorStatusUnknown = "Studio.Diagnostics.StatusUnknown";


    /// <summary>"{0} · {1}" — the draft line is built from parts, and even the dot
    /// between them is a per-culture decision rather than a C# operator.</summary>
    public const string WizardDraftJoinerPattern = "Studio.Create.DraftJoinerPattern";

    // ---- Import recognition report (Teams/ImportTeamViewModel, audit 04/12) --

    /// <summary>"{0} — {1} agent(s)"</summary>
    public const string ImportRecognizedAgents = "Studio.Import.RecognizedAgents";

    /// <summary>"No secret in the files"</summary>
    public const string ImportSecretsClean = "Studio.Import.SecretsClean";

    /// <summary>"API keys travel through the environment, and these files carry none."</summary>
    public const string ImportSecretsCleanDetail = "Studio.Import.SecretsCleanDetail";

    /// <summary>"Something looks like a pasted key"</summary>
    public const string ImportSecretsFound = "Studio.Import.SecretsFound";

    /// <summary>"{0} file(s) carry what looks like an inline secret — see the warning below."</summary>
    public const string ImportSecretsFoundDetail = "Studio.Import.SecretsFoundDetail";

    /// <summary>"Tools"</summary>
    public const string ImportToolsLater = "Studio.Import.ToolsLater";

    /// <summary>"The team's tools are checked at its first launch."</summary>
    public const string ImportToolsLaterDetail = "Studio.Import.ToolsLaterDetail";

    // ---- Diagnostic verdict card (Config/DiagnosticViewModel, audit 09/20) --

    /// <summary>"Everything is in place."</summary>
    public const string DiagAllGood = "Studio.Diagnostics.AllGood";

    /// <summary>"One point to fix before launching a team."</summary>
    public const string DiagFixNeeded = "Studio.Diagnostics.FixNeeded";

    /// <summary>"{0} checks passed, {1} warning(s), {2} failure(s)."</summary>
    public const string DiagCounts = "Studio.Diagnostics.Counts";

    // ---- History cards (Launch/LaunchHistoryViewModel, audit 06/15) ------

    /// <summary>"Finished without errors."</summary>
    public const string HistOutcomeSuccess = "Studio.History.OutcomeSuccess";

    /// <summary>"Failed (code {0})."</summary>
    public const string HistOutcomeFailed = "Studio.History.OutcomeFailed";

    /// <summary>"Interrupted before the end."</summary>
    public const string HistOutcomeCancelled = "Studio.History.OutcomeCancelled";

    /// <summary>"Never started."</summary>
    public const string HistOutcomeNotStarted = "Studio.History.OutcomeNotStarted";

    // ---- RAG web-fallback status (Config/RagSectionViewModel, T-08) ------

    /// <summary>"Web fallback active: the corrective loop may fetch pages, …"</summary>
    public const string RagWebFallbackActive = "Studio.Settings.WebFallbackActive";

    /// <summary>"Inactive: the corrective policy allows it, but the transport … is still off."</summary>
    public const string RagWebFallbackTransportOff = "Studio.Settings.WebFallbackTransportOff";

    /// <summary>"Inactive: the transport is on, but the corrective policy … is still off."</summary>
    public const string RagWebFallbackPolicyOff = "Studio.Settings.WebFallbackPolicyOff";

    /// <summary>"Off. Both switches must be turned on …"</summary>
    public const string RagWebFallbackOff = "Studio.Settings.WebFallbackOff";

    /// <summary>"already allowed" — the folder picker's faint note on a mounted row.</summary>
    public const string PickerAlreadyMounted = "Studio.Settings.AlreadyMounted";

    /// <summary>Why the run is refused: folders no settings entry allows.</summary>
    public const string RunBlockedUndeclared = "Studio.Run.BlockedUndeclared";

    /// <summary>"{0}: nothing will be bound to it — the agents writing there will fail."</summary>
    public const string WizardDroppedDerived = "Studio.Create.DroppedDerived";

    /// <summary>"already added" — a settings folder the team already carries.</summary>
    public const string AllowedFoldersAlreadyAdded = "Studio.Settings.AlreadyAdded";

    /// <summary>"{0} is already used by another folder" — a virtual root the team already spends.</summary>
    public const string AllowedFoldersConflict = "Studio.Settings.Conflict";

    /// <summary>"{0} folder(s) selected" — the chooser's footer count.</summary>
    public const string AllowedFoldersSummary = "Studio.Settings.Summary";

    /// <summary>"“{0}” added to the authorized folders" — a declaration made from the chooser.</summary>
    public const string AllowedFoldersDeclared = "Studio.Settings.Declared";

    /// <summary>"“{0}” added, but the settings could not be saved — {1}".</summary>
    public const string AllowedFoldersNotSaved = "Studio.Settings.NotSaved";

    /// <summary>"Folders of “{0}”" — the team-mounts modal title.</summary>
    public const string TeamMountsTitle = "Studio.Teams.TeamMountsTitle";

    /// <summary>"Without a folder, this team can neither read nor write any file."</summary>
    public const string TeamMountsNone = "Studio.Teams.None";

    /// <summary>"{0} folders: {1}" — the team-mounts footer summary.</summary>
    public const string TeamMountsSummary = "Studio.Teams.Summary";

    /// <summary>"Add an agent" — the agent editor in add mode.</summary>
    public const string AgentEditorTitleAdd = "Studio.Create.TitleAdd";

    /// <summary>"Edit the agent" — the agent editor in edit mode.</summary>
    public const string AgentEditorTitleEdit = "Studio.Create.TitleEdit";

    /// <summary>"Add to the team" — the agent editor's save label in add mode.</summary>
    public const string AgentEditorAdd = "Studio.Create.Add";

    /// <summary>"Save" — the shared action verb.</summary>
    public const string ActSave = "Studio.Common.Save";

    /// <summary>"Choose where to export the team" — the export destination browser's caption.</summary>
    public const string DialogExportDestination = "Studio.Settings.ExportDestination";

    /// <summary>"{0} (read)" — a team card's read-only mount chip.</summary>
    public const string TeamsMountRo = "Studio.Teams.MountRo";

    /// <summary>"{0} (read, write)" — a team card's writable mount chip.</summary>
    public const string TeamsMountRw = "Studio.Teams.MountRw";

    /// <summary>
    /// "unreadable folder" — a mount string the parser refused. Shown instead of the string
    /// itself: the raw form carries the physical folder, which has no place on an
    /// agent-facing screen.
    /// </summary>
    public const string TeamsMountUnreadable = "Studio.Teams.MountUnreadable";

    /// <summary>"to try" — the badge of a team that never ran and has no schedule.</summary>
    public const string TeamsToTest = "Studio.Teams.ToTest";

    /// <summary>"Last run: {0}, {1}" — the meta line's history part.</summary>
    public const string TeamsLastRun = "Studio.Teams.LastRun";

    /// <summary>"succeeded" — the last run ended well.</summary>
    public const string TeamsRunOk = "Studio.Teams.RunOk";

    /// <summary>"failed" — the last run did not.</summary>
    public const string TeamsRunFail = "Studio.Teams.RunFail";

    /// <summary>"Never ran" — no history entry names this team.</summary>
    public const string TeamsNeverRan = "Studio.Teams.NeverRan";

    /// <summary>"Setting: {0}" — the meta line's model-profile part.</summary>
    public const string TeamsSettingLabel = "Studio.Teams.SettingLabel";

    /// <summary>"Team exported to {0}"</summary>
    public const string TeamsExportedTo = "Studio.Teams.ExportedTo";

    /// <summary>"Export refused — the destination already exists, or the disk said no."</summary>
    public const string TeamsExportFailed = "Studio.Teams.ExportFailed";

    /// <summary>"Declared folders" — the import report's mounts row, when the sidecar has some.</summary>
    public const string ImportMountsDeclared = "Studio.Import.MountsDeclared";

    /// <summary>"{0} folder(s): {1}" — its detail.</summary>
    public const string ImportMountsDeclaredDetail = "Studio.Import.MountsDeclaredDetail";

    /// <summary>"No declared folder" — the sidecar names none (or there is no sidecar).</summary>
    public const string ImportMountsNone = "Studio.Import.MountsNone";

    /// <summary>"Allow its folders after adding — My teams, Change the folders."</summary>
    public const string ImportMountsNoneDetail = "Studio.Import.MountsNoneDetail";

    /// <summary>"reads {0}" — a read-only team mount, in the launcher's meta line.</summary>
    public const string RunMetaReads = "Studio.Run.MetaReads";

    /// <summary>"writes to {0}" — a writable team mount, in the launcher's meta line.</summary>
    public const string RunMetaWrites = "Studio.Run.MetaWrites";

    // ---- LLM preset catalogue (Core Presets/LlmPresets) ------------------

    /// <summary>"Ollama"</summary>
    public const string PresetOllamaTitle = "Studio.Settings.OllamaTitle";

    /// <summary>"Local Ollama server."</summary>
    public const string PresetOllamaDescription = "Studio.Settings.OllamaDesc";

    /// <summary>"Docker Model Runner"</summary>
    public const string PresetDmrTitle = "Studio.Settings.DmrTitle";

    /// <summary>"Local llama.cpp engine served by Docker Desktop."</summary>
    public const string PresetDmrDescription = "Studio.Settings.DmrDesc";

    /// <summary>"OpenAI"</summary>
    public const string PresetOpenAITitle = "Studio.Settings.OpenAITitle";

    /// <summary>"OpenAI cloud API."</summary>
    public const string PresetOpenAIDescription = "Studio.Settings.OpenAIDesc";

    /// <summary>"Other OpenAI-compatible"</summary>
    public const string PresetCustomTitle = "Studio.Settings.CustomTitle";

    /// <summary>"DeepSeek, GLM, Mistral, … — base URL and model required."</summary>
    public const string PresetCustomDescription = "Studio.Settings.CustomDesc";

    /// <summary>"None / offline"</summary>
    public const string PresetNoneTitle = "Studio.Settings.NoneTitle";

    /// <summary>"No LLM: runs use the &lt;undefined-llm&gt; echo provider."</summary>
    public const string PresetNoneDescription = "Studio.Settings.NoneDesc";

    /// <summary>"Anthropic"</summary>
    public const string ProviderAnthropicTitle = "Studio.Settings.AnthropicTitle";

    /// <summary>"Claude"</summary>
    public const string ProviderAnthropicDescription = "Studio.Settings.AnthropicDesc";

    /// <summary>"DeepSeek"</summary>
    public const string ProviderDeepSeekTitle = "Studio.Settings.DeepSeekTitle";

    /// <summary>"budget-friendly, very capable"</summary>
    public const string ProviderDeepSeekDescription = "Studio.Settings.DeepSeekDesc";

    /// <summary>"Gemini"</summary>
    public const string ProviderGeminiTitle = "Studio.Settings.GeminiTitle";

    /// <summary>"Google"</summary>
    public const string ProviderGeminiDescription = "Studio.Settings.GeminiDesc";

    /// <summary>Provider card title: Grok.</summary>
    public const string ProviderGrokTitle = "Studio.Settings.GrokTitle";

    /// <summary>Provider card description: Grok.</summary>
    public const string ProviderGrokDescription = "Studio.Settings.GrokDesc";

    /// <summary>Provider card title: MiniMax.</summary>
    public const string ProviderMiniMaxTitle = "Studio.Settings.MiniMaxTitle";

    /// <summary>Provider card description: MiniMax.</summary>
    public const string ProviderMiniMaxDescription = "Studio.Settings.MiniMaxDesc";


    /// <summary>"HuggingFace"</summary>
    public const string ProviderHuggingFaceTitle = "Studio.Settings.HuggingFaceTitle";

    /// <summary>"multi-model router"</summary>
    public const string ProviderHuggingFaceDescription = "Studio.Settings.HuggingFaceDesc";

    /// <summary>"Kimi"</summary>
    public const string ProviderKimiTitle = "Studio.Settings.KimiTitle";

    /// <summary>"Moonshot AI"</summary>
    public const string ProviderKimiDescription = "Studio.Settings.KimiDesc";

    /// <summary>"Mistral"</summary>
    public const string ProviderMistralTitle = "Studio.Settings.MistralTitle";

    /// <summary>"European"</summary>
    public const string ProviderMistralDescription = "Studio.Settings.MistralDesc";

    /// <summary>"Qwen"</summary>
    public const string ProviderQwenTitle = "Studio.Settings.QwenTitle";

    /// <summary>"Alibaba DashScope"</summary>
    public const string ProviderQwenDescription = "Studio.Settings.QwenDesc";

    /// <summary>"Together AI"</summary>
    public const string ProviderTogetherTitle = "Studio.Settings.TogetherTitle";

    /// <summary>"open models"</summary>
    public const string ProviderTogetherDescription = "Studio.Settings.TogetherDesc";

    /// <summary>"Z.AI (GLM)"</summary>
    public const string ProviderZaiTitle = "Studio.Settings.ZaiTitle";

    /// <summary>"Zhipu"</summary>
    public const string ProviderZaiDescription = "Studio.Settings.ZaiDesc";

    /// <summary>"GPT"</summary>
    public const string ProviderOpenAIShortDescription = "Studio.Settings.OpenAIShortDesc";

    /// <summary>"any endpoint: type the URL and the model"</summary>
    public const string ProviderCustomShortDescription = "Studio.Settings.CustomShortDesc";

    /// <summary>"test the install, no model"</summary>
    public const string ProviderNoneShortDescription = "Studio.Settings.NoneShortDesc";

    /// <summary>"The custom preset requires both a base URL and a model."</summary>
    public const string PresetErrorCustomIncomplete = "Studio.Settings.ErrorCustomIncomplete";

    /// <summary>"Unknown preset '{0}'. Supported: {1}."</summary>
    public const string PresetErrorUnknown = "Studio.Settings.ErrorUnknown";

    /// <summary>"No LLM configured: runs will use the &lt;undefined-llm&gt; echo provider. …"</summary>
    public const string PresetGuidanceNone = "Studio.Settings.GuidanceNone";

    /// <summary>"API key: referenced from the environment — set it with: export {0}=&lt;your-key&gt;"</summary>
    public const string PresetGuidanceApiKeyEnv = "Studio.Settings.GuidanceApiKeyEnv";

    /// <summary>"Note: the Orkeon runtime reads `{0}` natively; `{1}` is only read by …"</summary>
    public const string PresetGuidanceNonDefaultEnv = "Studio.Settings.GuidanceNonDefaultEnv";

    /// <summary>"WARNING: the API key is stored in plain text in the generated file. …"</summary>
    public const string PresetGuidanceInlineKeyWarning = "Studio.Settings.GuidanceInlineKeyWarning";

    // ---- Settings resolution chain (Core Storage/SettingsLocations) ------

    /// <summary>"Explicit path"</summary>
    public const string ResolutionStep1Title = "Studio.Settings.Step1Title";

    /// <summary>"The file passed to the runner with --settings. …"</summary>
    public const string ResolutionStep1Description = "Studio.Settings.Step1Desc";

    /// <summary>"Next to the crew"</summary>
    public const string ResolutionStep2Title = "Studio.Settings.Step2Title";

    /// <summary>"{0} in the directory holding the crew configuration."</summary>
    public const string ResolutionStep2Description = "Studio.Settings.Step2Desc";

    /// <summary>"Shared appsettings directory"</summary>
    public const string ResolutionStep3Title = "Studio.Settings.Step3Title";

    /// <summary>"appsettings/{0}, searched by walking up from the crew directory …"</summary>
    public const string ResolutionStep3Description = "Studio.Settings.Step3Desc";

    /// <summary>"Global per-user file"</summary>
    public const string ResolutionStep4Title = "Studio.Settings.Step4Title";

    /// <summary>"The file written by `orkeon init`: %APPDATA%\Orkeon\appsettings.json …"</summary>
    public const string ResolutionStep4Description = "Studio.Settings.Step4Desc";

    // ---- Mount rights labels (Core FileSystem/MountRightsTokens) ---------

    /// <summary>"Read only"</summary>
    public const string RightsReadOnly = "Studio.Settings.RightsReadOnly";

    /// <summary>"Read / write (create and delete allowed)"</summary>
    public const string RightsReadWrite = "Studio.Settings.RightsReadWrite";

    /// <summary>"Read / write without delete"</summary>
    public const string RightsReadWriteNoDelete = "Studio.Settings.ReadWriteNoDelete";

    // ---- Run target prerequisites (Core Targets/RunTargetRequirements) ---

    /// <summary>"This is a multi-file crew directory: running it uses 'orkeon run &lt;directory&gt;' …"</summary>
    public const string TargetDirectoryRunNotice = "Studio.Run.DirectoryRunNotice";

    // ---- Mount override semantics (Core Launch/MountOverrideSemantics) ---

    /// <summary>The full statement of the index-based <c>--mount</c> override rule.</summary>
    public const string MountSemanticsExplanation = "Studio.Settings.Explanation";

    /// <summary>The statement of what <c>--allow-external-mounts</c> adds.</summary>
    public const string MountSemanticsExternalMounts = "Studio.Settings.ExternalMounts";

    /// <summary>" For this launch the runner injects {0} mount(s) ({1}), so the first --mount occupies '{2}'."</summary>
    public const string MountSemanticsThisLaunch = "Studio.Settings.ThisLaunch";

    // ---- Config tab (WPF ViewModel statuses) ------------------------------

    /// <summary>"New empty document. Pick a preset to fill in the Llm section."</summary>
    public const string ConfigNewDocument = "Studio.Settings.NewDocument";

    /// <summary>"No file selected."</summary>
    public const string ConfigNoFileSelected = "Studio.Settings.NoFileSelected";

    /// <summary>"Loaded {0}."</summary>
    public const string ConfigLoaded = "Studio.Settings.Loaded";

    /// <summary>"Not saved: {0} error(s) must be fixed first."</summary>
    public const string ConfigNotSavedErrors = "Studio.Settings.NotSavedErrors";

    /// <summary>"Not saved: no destination selected."</summary>
    /// <summary>"Not saved — the file could not be written: {0}"</summary>
    public const string ConfigNotSavedWriteFailed = "Studio.Settings.NotSavedWriteFailed";

    public const string ConfigNotSavedNoDestination = "Studio.Settings.NotSavedNoDestination";

    /// <summary>"Not saved yet: authorize at least one folder…"</summary>
    public const string ConfigNotSavedNeedFolder = "Studio.Settings.NotSavedNeedFolder";

    /// <summary>"Saved to {0}."</summary>
    public const string ConfigSaved = "Studio.Settings.Saved";

    /// <summary>"Saved to {0}. Warning {1}: no Llm section, so runs will use the echo provider."</summary>
    public const string ConfigSavedLlmWarning = "Studio.Settings.SavedLlmWarning";

    /// <summary>"No problem found."</summary>
    public const string ConfigNoProblem = "Studio.Settings.NoProblem";

    /// <summary>"{0} error(s), {1} warning(s)."</summary>
    public const string ConfigErrorsWarnings = "Studio.Settings.ErrorsWarnings";

    // ---- Llm section (WPF ViewModel) --------------------------------------

    /// <summary>"No base URL — the runtime falls back to the echo provider."</summary>
    public const string LlmNoBaseUrl = "Studio.Settings.NoBaseUrl";

    /// <summary>"custom (host not in the known-endpoint table)"</summary>
    public const string LlmCustomProvider = "Studio.Settings.CustomProvider";

    /// <summary>"Prefer the {0} environment variable: the runtime reads it with precedence …"</summary>
    public const string LlmApiKeyRecommendation = "Studio.Settings.ApiKeyRecommendation";

    /// <summary>"Testing the connection…"</summary>
    public const string LlmTesting = "Studio.Settings.Testing";

    // ---- Diagnostic panel (WPF ViewModel) ---------------------------------

    /// <summary>"orkeon doctor reported no check."</summary>
    public const string DiagNoCheck = "Studio.Diagnostics.NoCheck";

    /// <summary>"{0} check(s), all green."</summary>
    public const string DiagAllGreen = "Studio.Diagnostics.AllGreen";

    /// <summary>"{0} check(s): {1} failure(s), {2} warning(s)."</summary>
    public const string DiagFindings = "Studio.Diagnostics.Findings";

    // ---- Mounts editor and launch mounts (WPF ViewModels) -----------------

    /// <summary>"{0} mount(s), no error."</summary>
    public const string MountsSummaryOk = "Studio.Settings.SummaryOk";

    /// <summary>"{0} mount(s), {1} error(s)."</summary>
    public const string MountsSummaryErrors = "Studio.Settings.SummaryErrors";

    /// <summary>"Select a crew first: the runner injects its own mounts ahead of every --mount, …"</summary>
    public const string MountsSelectCrewFirst = "Studio.Settings.SelectCrewFirst";

    /// <summary>"Security: this lets a mount point anywhere on the machine, …"</summary>
    public const string MountsExternalWarning = "Studio.Settings.ExternalWarning";

    /// <summary>"{0} effective mount(s); no appsettings entry is replaced."</summary>
    public const string MountsEffectiveNone = "Studio.Settings.EffectiveNone";

    /// <summary>"{0} effective mount(s); {1} appsettings entry(ies) replaced by index."</summary>
    public const string MountsEffectiveReplaced = "Studio.Settings.EffectiveReplaced";

    /// <summary>"auto (injected by the runner)"</summary>
    public const string MountsOriginAuto = "Studio.Settings.OriginAuto";

    /// <summary>"{0} (replaces «{1}»)"</summary>
    public const string MountsOriginReplaces = "Studio.Settings.OriginReplaces";

    // ---- Launch tab (WPF ViewModel) ----------------------------------------

    /// <summary>"The orkeon CLI has not been located yet."</summary>
    public const string LaunchBinaryNotLocated = "Studio.Run.BinaryNotLocated";

    /// <summary>"Ready to work"</summary>
    public const string RunStateIdle = "Studio.Run.StateIdle";

    /// <summary>"Run in progress"</summary>
    public const string RunStateRunning = "Studio.Run.StateRunning";

    /// <summary>"Run finished"</summary>
    public const string RunStateDone = "Studio.Run.StateDone";

    /// <summary>"waiting"</summary>
    public const string RunBadgeIdle = "Studio.Run.BadgeIdle";

    /// <summary>"running"</summary>
    public const string RunBadgeRunning = "Studio.Run.BadgeRunning";

    /// <summary>"succeeded"</summary>
    public const string RunBadgeDone = "Studio.Run.BadgeDone";

    /// <summary>"failed"</summary>
    public const string RunBadgeFailed = "Studio.Run.BadgeFailed";

    /// <summary>"Launch now"</summary>
    public const string RunButtonLaunch = "Studio.Run.BtnLaunch";

    /// <summary>"Running…"</summary>
    public const string RunButtonRunning = "Studio.Run.BtnRunning";

    /// <summary>"Relaunch"</summary>
    public const string RunButtonRelaunch = "Studio.Run.BtnRelaunch";

    /// <summary>"The Orkeon engine was not found — run the Diagnostic."</summary>
    public const string RunCliMissing = "Studio.Run.CliMissing";

    /// <summary>"{0} agents"</summary>
    public const string RunMetaAgents = "Studio.Run.MetaAgents";

    /// <summary>"setting {0}"</summary>
    public const string RunMetaProfile = "Studio.Run.MetaProfile";

    /// <summary>"The orkeon CLI was not found."</summary>
    public const string LaunchBinaryNotFound = "Studio.Run.BinaryNotFound";

    /// <summary>"Ready to launch."</summary>
    public const string LaunchReady = "Studio.Run.Ready";

    /// <summary>"No target resolved."</summary>
    public const string LaunchNoTarget = "Studio.Run.NoTarget";

    /// <summary>"Not launched: {0} error(s) must be fixed first."</summary>
    public const string LaunchNotLaunchedErrors = "Studio.Run.NotLaunchedErrors";

    /// <summary>"Cancelling: the CLI is asked to stop, and is killed if it does not."</summary>
    public const string LaunchCancelling = "Studio.Run.Cancelling";

    /// <summary>"Nothing to replay: the entry for '{0}' recorded no arguments."</summary>
    public const string LaunchNothingToReplay = "Studio.Run.NothingToReplay";

    /// <summary>"Validating…"</summary>
    public const string LaunchValidating = "Studio.Run.Validating";

    /// <summary>"Running…"</summary>
    public const string LaunchRunning = "Studio.Run.Running";

    // ---- Watched-run progress panel (BUS-06) --------------------------------

    /// <summary>"Nothing reported yet."</summary>
    public const string RunProgressNothingYet = "Studio.Run.NothingYet";

    /// <summary>"{0} task(s) finished."</summary>
    public const string RunProgressTasksDone = "Studio.Run.TasksDone";

    /// <summary>"Finished successfully."</summary>
    public const string RunProgressSucceeded = "Studio.Run.Succeeded";

    /// <summary>"Finished with a failure."</summary>
    public const string RunProgressFailed = "Studio.Run.Failed";

    /// <summary>"{0} tokens · {1}"</summary>
    public const string RunProgressCost = "Studio.Run.Cost";

    /// <summary>"Show progress instead of raw output"</summary>
    public const string RunProgressWatch = "Studio.Run.Watch";

    /// <summary>"Stream generated text token by token"</summary>
    public const string RunProgressStream = "Studio.Run.Stream";

    /// <summary>"Answer"</summary>
    public const string RunProgressAnswer = "Studio.Run.Answer";

    /// <summary>"An agent is asking:"</summary>
    public const string RunProgressAgentAsks = "Studio.Run.AgentAsks";

    /// <summary>"Reply"</summary>
    public const string RunProgressReply = "Studio.Run.Reply";

    /// <summary>"Hub messages"</summary>
    public const string RunProgressHubMessages = "Studio.Run.HubMessages";


    // ---- Model profiles (settings screen, design v3) ------------------------

    /// <summary>"New setting" — the freshly created profile's placeholder name.</summary>
    public const string ProfileNewName = "Studio.Settings.NewName";

    /// <summary>"ON YOUR MACHINE · FREE, NO KEY"</summary>
    public const string ProfileGroupLocal = "Studio.Settings.GroupLocal";

    /// <summary>"IN THE CLOUD · API KEY REQUIRED"</summary>
    public const string ProfileGroupCloud = "Studio.Settings.GroupCloud";

    /// <summary>"API key {0}"</summary>
    public const string ProfileKeyTitleFor = "Studio.Settings.KeyTitleFor";

    /// <summary>"The service's API key"</summary>
    public const string ProfileKeyTitleService = "Studio.Settings.KeyTitleService";

    /// <summary>"Studio keeps it in your Windows session — never in a file, never in a shared folder."</summary>
    public const string ProfileKeyExplainer = "Studio.Settings.KeyExplainer";

    /// <summary>"Paste your key here (sk-…)"</summary>
    public const string ProfileKeyPlaceholder = "Studio.Settings.KeyPlaceholder";

    /// <summary>"Remember the key"</summary>
    public const string ProfileKeyStore = "Studio.Settings.KeyStore";

    /// <summary>"key remembered"</summary>
    public const string ProfileKeyStatusSet = "Studio.Settings.KeyStatusSet";

    /// <summary>"no key detected"</summary>
    public const string ProfileKeyStatusMissing = "Studio.Settings.KeyStatusMissing";

    /// <summary>"No key yet?"</summary>
    public const string ProfileKeyNoKeyYet = "Studio.Settings.KeyNoKeyYet";

    /// <summary>"on the provider's site"</summary>
    public const string ProfileKeyOnVendorSite = "Studio.Settings.KeyOnVendorSite";

    /// <summary>The expert-only setx hint naming the runtime's native variable.</summary>
    public const string ProfileKeyExpertHint = "Studio.Settings.KeyExpertHint";

    /// <summary>"No key needed — the model runs on your machine, nothing leaves it."</summary>
    public const string ProfileLocalNote = "Studio.Settings.LocalNote";

    /// <summary>"Without a model, runs answer as an echo — useful to verify the install."</summary>
    public const string ProfileNoneNote = "Studio.Settings.NoneNote";

    /// <summary>"API key missing — remember it first"</summary>
    public const string ProfileKeyMissingTest = "Studio.Settings.KeyMissingTest";

    /// <summary>"copy" — suffix of a duplicated profile's name.</summary>
    public const string ProfileCopySuffix = "Studio.Settings.CopySuffix";


    // ---- Creation wizard (design v3) ----------------------------------------

    /// <summary>"Once"</summary>
    public const string WizardFreqOnce = "Studio.Create.FreqOnce";

    /// <summary>"Every day"</summary>
    public const string WizardFreqDaily = "Studio.Create.FreqDaily";

    /// <summary>"Every week"</summary>
    public const string WizardFreqWeekly = "Studio.Create.FreqWeekly";

    /// <summary>"A folder on this PC"</summary>
    public const string WizardSourceFolder = "Studio.Create.SourceFolder";

    /// <summary>"A website"</summary>
    public const string WizardSourceWeb = "Studio.Create.SourceWeb";

    /// <summary>"I don't know yet"</summary>
    public const string WizardSourceUnknown = "Studio.Create.SourceUnknown";

    /// <summary>"A document"</summary>
    public const string WizardOutputDocument = "Studio.Create.OutputDocument";

    /// <summary>"A table"</summary>
    public const string WizardOutputTable = "Studio.Create.OutputTable";

    /// <summary>"A short message"</summary>
    public const string WizardOutputMessage = "Studio.Create.OutputMessage";

    /// <summary>"Something else"</summary>
    public const string WizardOutputOther = "Studio.Create.OutputOther";

    /// <summary>"How often: {0}."</summary>
    public const string WizardBriefFrequency = "Studio.Create.BriefFrequency";

    /// <summary>"Where the information lives: {0}."</summary>
    public const string WizardBriefSource = "Studio.Create.BriefSource";

    /// <summary>"Expected result: {0}."</summary>
    public const string WizardBriefOutput = "Studio.Create.BriefOutput";

    /// <summary>"The result should look like: {0}"</summary>
    public const string WizardBriefShape = "Studio.Create.BriefShape";

    /// <summary>"Standing instruction for every agent: {0}"</summary>
    public const string WizardBriefConsigne = "Studio.Create.BriefConsigne";

    /// <summary>"Agent" — the card name when the engine named no role.</summary>
    public const string WizardAgentFallback = "Studio.Create.AgentFallback";

    /// <summary>"The save failed — the engine refused the promotion: {0}"</summary>
    public const string WizardPromoteFailed = "Studio.Create.PromoteFailed";

    /// <summary>"The import failed — nothing was copied. Check access to the source and try again."</summary>
    public const string ImportFailed = "Studio.Import.Failed";

    /// <summary>"{0} — done in {1} s"</summary>
    public const string WizardActivityDone = "Studio.Create.ActivityDone";

    /// <summary>"{0} — failed"</summary>
    public const string WizardActivityFailed = "Studio.Create.ActivityFailed";

    /// <summary>"Describe the work to continue."</summary>
    public const string WizardHintDescribe = "Studio.Create.HintDescribe";

    /// <summary>"The format is free: describe the expected result."</summary>
    public const string WizardHintOutcome = "Studio.Create.HintOutcome";

    /// <summary>"Answer the three precisions."</summary>
    public const string WizardHintAnswers = "Studio.Create.HintAnswers";

    /// <summary>"Everything is there — I can compose the team."</summary>
    public const string WizardHintReady = "Studio.Create.HintReady";

    /// <summary>The line under the compose button while the engine interviews.</summary>
    public const string WizardHintComposing = "Studio.Create.HintComposing";


    // ---- My teams screen (design v3) ----------------------------------------

    /// <summary>"Looks good to me"</summary>
    public const string WizardDecisionAccept = "Studio.Create.DecisionAccept";

    /// <summary>"Run the trial again"</summary>
    public const string WizardDecisionRetry = "Studio.Create.DecisionRetry";

    /// <summary>"Fix and retry"</summary>
    public const string WizardDecisionRefine = "Studio.Create.DecisionRefine";

    /// <summary>"Abandon"</summary>
    public const string WizardDecisionAbort = "Studio.Create.DecisionAbort";


    /// <summary>"On demand"</summary>
    public const string TeamsOnDemand = "Studio.Teams.OnDemand";

    /// <summary>"Every day at {0}"</summary>
    public const string TeamsDaily = "Studio.Teams.Daily";

    /// <summary>"Every hour"</summary>
    public const string TeamsHourly = "Studio.Teams.Hourly";


    // ---- Target picker (WPF ViewModel) --------------------------------------

    /// <summary>"No target selected."</summary>
    public const string TargetNone = "Studio.Run.None";

    /// <summary>"{0} — orkeon run {1}"</summary>
    public const string TargetResolved = "Studio.Run.Resolved";

    /// <summary>"{0} script(s) found: pick the one to run."</summary>
    public const string TargetPickScript = "Studio.Run.PickScript";

    /// <summary>"Detection failed."</summary>
    public const string TargetDetectionFailed = "Studio.Run.DetectionFailed";

    /// <summary>"YAML crew file"</summary>
    public const string TargetKindYamlFile = "Studio.Run.KindYamlFile";

    /// <summary>"Scripting crew file"</summary>
    public const string TargetKindScriptFile = "Studio.Run.KindScriptFile";

    /// <summary>"Multi-file crew directory"</summary>
    public const string TargetKindCrewDirectory = "Studio.Run.KindCrewDirectory";

    /// <summary>"Scripting crew directory"</summary>
    public const string TargetKindScriptDirectory = "Studio.Run.KindScriptDirectory";

    // ---- Run log panel (WPF ViewModel) ---------------------------------------

    /// <summary>"{0} line(s)."</summary>
    public const string LogLines = "Studio.Run.LogLines";

    /// <summary>"{0} line(s); {1} older line(s) dropped (cap {2})."</summary>
    public const string LogLinesDropped = "Studio.Run.LinesDropped";

    // ---- File dialogs (titles and filters) ------------------------------------

    /// <summary>"Open appsettings.json"</summary>
    public const string DialogOpenAppSettings = "Studio.Settings.OpenAppSettings";

    /// <summary>"Save appsettings.json"</summary>
    public const string DialogSaveAppSettings = "Studio.Settings.SaveAppSettings";

    /// <summary>"Select an appsettings.json"</summary>
    public const string DialogSelectAppSettings = "Studio.Settings.SelectAppSettings";

    /// <summary>"Select an inputs file"</summary>
    public const string DialogSelectInputsFile = "Studio.Settings.SelectInputsFile";

    /// <summary>"Select the LLM log destination"</summary>
    public const string DialogSelectLlmLogDestination = "Studio.Settings.SelectLlmLogDest";

    /// <summary>"Select a crew definition"</summary>
    public const string DialogSelectCrewDefinition = "Studio.Settings.SelectCrewDefinition";

    /// <summary>"Select a crew directory"</summary>
    public const string DialogSelectCrewDirectory = "Studio.Settings.SelectCrewDirectory";

    /// <summary>"Select the folder to mount"</summary>
    public const string DialogSelectMountFolder = "Studio.Settings.SelectMountFolder";

    /// <summary>"JSON files|*.json|All files|*.*" — the pipe format is the Win32 dialog contract.</summary>
    public const string DialogFilterJson = "Studio.Settings.FilterJson";

    /// <summary>"Crew definitions|*.yaml;*.yml;*.ts;*.js|All files|*.*"</summary>
    public const string DialogFilterCrew = "Studio.Settings.FilterCrew";

    /// <summary>"All files|*.*"</summary>
    public const string DialogFilterAll = "Studio.Settings.FilterAll";

    // ── the Atelier (SPEC-ORKEON-FORGE §12, UX study §2: no framework word at level 1) ──

    /// <summary>"Summarize a site's news every morning"</summary>
    public const string ForgeExample1 = "Studio.Create.Example1";

    /// <summary>"Produce a weekly summary from my files"</summary>
    public const string ForgeExample2 = "Studio.Create.Example2";

    /// <summary>"Compare offers and flag the best one"</summary>
    public const string ForgeExample3 = "Studio.Create.Example3";

    /// <summary>"Turn a folder of documents into a report"</summary>
    public const string ForgeExample4 = "Studio.Create.Example4";

    /// <summary>"I am preparing a proposal…"</summary>
    public const string ForgeStatusPreparing = "Studio.Create.StatusPreparing";

    /// <summary>"Trying it on your example…"</summary>
    public const string ForgeStatusTrying = "Studio.Create.StatusTrying";

    /// <summary>"Checking the result against what you asked…"</summary>
    public const string ForgeStatusJudging = "Studio.Create.StatusJudging";

    /// <summary>"Your solution is ready."</summary>
    public const string ForgeStatusReady = "Studio.Create.StatusReady";

    /// <summary>"Something went wrong — open the details for the technical part."</summary>
    public const string ForgeStatusFailed = "Studio.Create.StatusFailed";

    /// <summary>"Stopped — you can pick it up again from My solutions."</summary>
    public const string ForgeStatusStopped = "Studio.Create.StatusStopped";

    /// <summary>"Attempt {0}"</summary>
    public const string ForgeAttempt = "Studio.Create.Attempt";

    /// <summary>"I could not check this automatically — judge for yourself."</summary>
    public const string ForgeCheckUnverified = "Studio.Create.CheckUnverified";

    /// <summary>"The try is done (score {0})."</summary>
    public const string ForgeResultScore = "Studio.Create.ResultScore";

    /// <summary>"Choose where to store the solution"</summary>
    public const string ForgeStorePickTitle = "Studio.Create.StorePickTitle";
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
        [StudioStringKeys.RunBlockedUndeclared] = "This team uses folders your settings do not allow: {0}. Declare them in Settings › Authorized folders, or remove them from the team.",
        [StudioStringKeys.WizardDroppedDerived] = "{0}: nothing will be bound to it — the agents writing there will fail.",
        [StudioStringKeys.AllowedFoldersAlreadyAdded] = "already added",
        [StudioStringKeys.AllowedFoldersConflict] = "{0} is already used by another folder",
        [StudioStringKeys.AllowedFoldersSummary] = "{0} folder(s) selected",
        [StudioStringKeys.AllowedFoldersDeclared] = "“{0}” added to the authorized folders",
        [StudioStringKeys.AllowedFoldersNotSaved] = "“{0}” added, but the settings could not be saved — {1}",
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
        [StudioStringKeys.TeamsMountUnreadable] = "unreadable folder",
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
        [StudioStringKeys.WizardStep1] = "Describe",
        [StudioStringKeys.WizardStep2] = "Compose",
        [StudioStringKeys.WizardStep3] = "Try",
        [StudioStringKeys.WizardStep4] = "Adopt",
        [StudioStringKeys.WizardDraftStepPattern] = "Step {0} of {1} · {2}",
        [StudioStringKeys.WizardDraftJoinerPattern] = "{0} · {1}",
        [StudioStringKeys.DoctorStatusOk] = "In place.",
        [StudioStringKeys.DoctorStatusWarning] = "Usable, but worth a look.",
        [StudioStringKeys.DoctorStatusFailure] = "Missing — a team will not run until this is fixed.",
        [StudioStringKeys.DoctorStatusUnknown] = "Not checked.",
        [StudioStringKeys.ToolReadScoped] = "read {0}",
        [StudioStringKeys.ToolListScoped] = "list {0}",
        [StudioStringKeys.ToolWriteScoped] = "write {0}",
        [StudioStringKeys.ToolWeb] = "browse a web page",
        [StudioStringKeys.ToolRag] = "search the index",
        [StudioStringKeys.TourCounterPattern] = "{0} / {1}",
        [StudioStringKeys.LanguageSystem] = "SYSTEM",
        [StudioStringKeys.LanguageChosen] = "CHOSEN",
        [StudioStringKeys.LanguageFromSystem] = "system language",
        [StudioStringKeys.LanguageFromChoice] = "saved choice",
        [StudioStringKeys.LanguageHeaderPattern] = "{0} · {1}",
        [StudioStringKeys.LanguageTooltipPattern] = "{0} — {1}",
        [StudioStringKeys.ChatTitle] = "Assistant conversation",
        [StudioStringKeys.ChatRecap] = "What I've noted",
        [StudioStringKeys.ChatYourBrief] = "YOUR BRIEF",
        [StudioStringKeys.ChatEdit] = "Edit",
        [StudioStringKeys.ChatStop] = "Stop",
        [StudioStringKeys.ChatSend] = "Send",
        [StudioStringKeys.ChatReply] = "Reply",
        [StudioStringKeys.ChatSkip] = "Skip this question",
        [StudioStringKeys.ChatSkipAnswer] = "I don't know — do your best.",
        [StudioStringKeys.ChatKeyHint] = "Enter to send · Shift+Enter for a new line",
        [StudioStringKeys.ChatPlaceholder] = "Ask the assistant a question…",
        [StudioStringKeys.ChatAsk] = "Ask a question",
        [StudioStringKeys.ChatAskShort] = "Question",
        [StudioStringKeys.ChatAskTip] = "Ask the assistant a question",
        [StudioStringKeys.ChatSeeConversation] = "See the conversation",
        [StudioStringKeys.ChatClose] = "Close the conversation",
        [StudioStringKeys.ChatBriefEmpty] = "You haven't described anything yet.",
        [StudioStringKeys.ChatStripBusy] = "The assistant is composing your team",
        [StudioStringKeys.ChatStripAsking] = "The assistant needs one more detail",
        [StudioStringKeys.ChatStatusDonePattern] = "brief complete · {0} message(s)",
        [StudioStringKeys.ChatStatusMessagesPattern] = "{0} message(s)",
        [StudioStringKeys.ChatStatusNoQuestion] = "no question yet",
        [StudioStringKeys.ChatSessionEnded] = "The assistant stopped before the brief was finished. Start again when you are ready.",
        [StudioStringKeys.ChatPlaceholderAnswer] = "Answer the assistant…",
        [StudioStringKeys.ChatStatusWaiting] = "waiting for your answer",
        [StudioStringKeys.ChatRecapProgressPattern] = "{0} / {1}",
        [StudioStringKeys.ChatProfileSuffixPattern] = "{0} · {1}",
        [StudioStringKeys.ChatStatus1] = "reading the brief",
        [StudioStringKeys.ChatStatus2] = "analysing the source",
        [StudioStringKeys.ChatStatus3] = "composing the roles",
        [StudioStringKeys.ChatThinking1] = "I'm re-reading your description…",
        [StudioStringKeys.ChatThinking2] = "I'm looking at where the documents are…",
        [StudioStringKeys.ChatThinking3] = "I'm sketching the team's roles…",
        [StudioStringKeys.ChatWrapBody] = "Thank you — I have what I need to compose the team.",
        [StudioStringKeys.ChatWrapDetail] = "I'll show you the composition I propose: the agents, the allowed folders, and what each of them can do.",
        [StudioStringKeys.ChatFactBrief] = "The work you described",
        [StudioStringKeys.ChatFactRhythm] = "Rhythm",
        [StudioStringKeys.ChatFactSource] = "Document source",
        [StudioStringKeys.ChatFactOutput] = "Shape of the result",
        [StudioStringKeys.ChatFactComposeNote] = "Composition instruction",
        [StudioStringKeys.ChatFactTryNote] = "Trial instruction",
        [StudioStringKeys.ChatFactAdoptNote] = "Adoption instruction",
        [StudioStringKeys.ChatFactPending] = "pending",
        [StudioStringKeys.ChatEmptyCreate] = "Ask your question: the assistant answers without changing anything until you ask it to.",
        [StudioStringKeys.ChatEmptyRun] = "Ask about the run in progress: why a step is slow, what happens on a failure, where the result lands.",
        [StudioStringKeys.ChatEmptyHistory] = "Ask about a past run: why it failed, what has changed since, how to keep it from happening again.",
        [StudioStringKeys.ChatRuleFolders] = "folder|file|access|permission|read|writ",
        [StudioStringKeys.ChatRuleCost] = "cost|price|paid|bill|token|expensive",
        [StudioStringKeys.ChatRuleDuration] = "time|duration|how long|slow|fast|minute",
        [StudioStringKeys.ChatRuleError] = "error|fail|crash|break|bug",
        [StudioStringKeys.ChatRuleSchedule] = "schedul|plan|hour|cron|automatic|morning",
        [StudioStringKeys.ChatRulePrivacy] = "confidential|privat|data|gdpr|secur|leave",
        [StudioStringKeys.ChatAnswerFolders] = "Agents only see the folders allowed in Settings. A read-only folder can never be modified, even if an agent asks for it.",
        [StudioStringKeys.ChatAnswerCost] = "A local setting (Ollama) costs nothing. In the cloud the cost depends on how many words are exchanged: Studio shows the tokens spent at the end of every run.",
        [StudioStringKeys.ChatAnswerDuration] = "On this dataset, count one to two minutes per run locally. The technical log gives the duration of each step.",
        [StudioStringKeys.ChatAnswerError] = "If a step fails the run stops and nothing is written to the output folder. The failure appears in the history, with the step and the error message.",
        [StudioStringKeys.ChatAnswerSchedule] = "A scheduled team runs even with Studio closed, as long as the machine is on. You can stop the schedule from My teams.",
        [StudioStringKeys.ChatAnswerPrivacy] = "With a local setting, no data leaves the machine. With a cloud setting, only the text sent to the model leaves — never the files themselves.",
        [StudioStringKeys.ChatReplyStep1] = "Describe the work in one sentence, as you would to a colleague: what you do by hand today, and what you want to receive. I'll take care of the rest — and I'll ask for the details that are missing.",
        [StudioStringKeys.ChatReplyStep2] = "The proposed agents share the reading, the writing and the proofreading. You can drop one: I'll rebalance the roles, and the allowed folders will stay the same.",
        [StudioStringKeys.ChatReplyStep3] = "The trial touches nothing: read-only, on a sample of documents, and nothing is saved. This is the moment to check the tone and the level of detail.",
        [StudioStringKeys.ChatReplyStep4] = "After adoption the team works on its own at the chosen moment, even with Studio closed. Every run leaves a trace in History, and you can stop the schedule from My teams.",
        [StudioStringKeys.ChatReplyRun] = "I'm following the run live. If a step stops, tell me: I read the technical log and explain what blocked, without jargon.",
        [StudioStringKeys.ChatReplyHistory] = "Every line of the history keeps its full log. Ask me « why did the last one fail? » and I'll pick up the offending step and the error message.",
        [StudioStringKeys.WizardDraftTitle] = "A creation in progress",
        [StudioStringKeys.WizardDraftWaitingTitle] = "The assistant is waiting for your answer",
        [StudioStringKeys.WizardDraftResume] = "resume the conversation",
        ["Studio.Diagnostics.Check.appsettings"] = "The settings file is readable",
        ["Studio.Diagnostics.Check.dotnet-runtime"] = "The .NET runtime",
        ["Studio.Diagnostics.Check.esbuild"] = "The script compiler (esbuild)",
        ["Studio.Diagnostics.Check.llm-config"] = "The model configuration",
        ["Studio.Diagnostics.Check.llm-reachability"] = "The connection to the model",
        ["Studio.Diagnostics.Check.local-embeddings"] = "Local embeddings",
        ["Studio.Diagnostics.Check.onnx-reranker"] = "The ONNX reranker",
        ["Studio.Diagnostics.Check.tree-sitter"] = "The code parser (tree-sitter)",
        ["Studio.Diagnostics.Check.workspace-write"] = "Write access to the workspace",
        [StudioStringKeys.ImportRecognizedAgents] = "{0} — {1} agent(s)",
        [StudioStringKeys.ImportSecretsClean] = "No secret in the files",
        [StudioStringKeys.ImportSecretsCleanDetail] = "API keys travel through the environment, and these files carry none.",
        [StudioStringKeys.ImportSecretsFound] = "Something looks like a pasted key",
        [StudioStringKeys.ImportSecretsFoundDetail] = "{0} file(s) carry what looks like an inline secret — see the warning below.",
        [StudioStringKeys.ImportToolsLater] = "Tools",
        [StudioStringKeys.ImportToolsLaterDetail] = "The team's tools are checked at its first launch.",
        ["Studio.Diagnostics.Code.WIN-01"] = "No Llm section: the engine will start on its built-in defaults (Ollama, local).",
        ["Studio.Diagnostics.Code.STUDIO-JSON"] = "The settings file is not valid JSON.",
        ["Studio.Diagnostics.Code.STUDIO-TYPE"] = "A settings field has the wrong type.",
        ["Studio.Diagnostics.Code.STUDIO-LLM-APIKEY"] = "An API key is written inside the file — move it to an environment variable.",
        ["Studio.Diagnostics.Code.STUDIO-RAG-PROFILE"] = "The document-index profile named here is unknown.",
        ["Studio.Diagnostics.Code.STUDIO-MOUNT-EMPTY"] = "No folder is allowed yet: add at least one.",
        ["Studio.Diagnostics.Code.STUDIO-MOUNT-FORMAT"] = "A folder entry is malformed.",
        ["Studio.Diagnostics.Code.STUDIO-MOUNT-PATH"] = "An allowed folder does not exist on this machine.",
        ["Studio.Diagnostics.Code.STUDIO-MOUNT-COLLISION"] = "Two folders share the same internal name.",
        ["Studio.Diagnostics.Code.STUDIO-LAUNCH-OPTION"] = "This option does not apply to the selected team.",
        ["Studio.Diagnostics.Code.STUDIO-LAUNCH-VERBOSE"] = "The verbosity level is not valid.",
        ["Studio.Diagnostics.Code.STUDIO-LAUNCH-VAR"] = "A variable is malformed (expected name=value).",
        ["Studio.Diagnostics.Code.STUDIO-LAUNCH-MOUNT"] = "An added folder line is empty.",
        ["Studio.Diagnostics.Code.STUDIO-LAUNCH-INPUTS"] = "Two conflicting input sources are set — keep one.",
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
        [StudioStringKeys.RunCliMissing] = "The orkeon executable was not located on this machine — launching is disabled. Run the Diagnostic.",
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
        [StudioStringKeys.ProviderGrokTitle] = "Grok",
        [StudioStringKeys.ProviderGrokDescription] = "x.AI",
        [StudioStringKeys.ProviderMiniMaxTitle] = "MiniMax",
        [StudioStringKeys.ProviderMiniMaxDescription] = "M2 family, intl + mainland endpoints",
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
            "The runner injects its own mount first — the crew's configuration directory as " +
            "'/crew' (the script's directory as '/script' for a .ork.ts crew) — then appends each " +
            "--mount argument, and writes the whole list as 'Orkeon:FileSystem:Mounts:{index}'. So " +
            "the appsettings mount at index 0 is always replaced by the auto-injected one, the " +
            "first --mount replaces the appsettings mount at index 1, and the two lists are never " +
            "merged. Appsettings entries past the last written index stay in force. The LLM log " +
            "directory is mounted separately, hidden from agents, and shifts nothing.",
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
        [StudioStringKeys.WizardHintComposing] = "The assistant is composing — answer it in the conversation.",

        [StudioStringKeys.WizardDecisionAccept] = "Looks good to me",
        [StudioStringKeys.WizardDecisionRetry] = "Run the trial again",
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
