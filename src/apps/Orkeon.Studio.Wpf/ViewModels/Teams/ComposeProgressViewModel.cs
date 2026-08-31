using System.Globalization;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Teams;

/// <summary>
/// One reading of where the composition stands — everything the progress card needs, and
/// nothing it has to go looking for.
/// </summary>
/// <param name="Stage">The engine's wire spelling of the stage, null before the first one.</param>
/// <param name="EngineRunning">Whether the engine process is alive.</param>
/// <param name="WaitingOnUser">Whether it is alive but blocked on an answer of yours.</param>
/// <param name="Finished">Whether the session reached its end, however it ended.</param>
/// <param name="Narration">The model's own last words, when it said any.</param>
/// <param name="FileCount">Crew files written so far.</param>
/// <param name="ValidationOk">The last validation outcome; null before the first one.</param>
/// <param name="PromptTokens">Ascending — everything sent to the models.</param>
/// <param name="CompletionTokens">Descending — everything the models sent back.</param>
/// <param name="Estimated">Whether any part of the two figures was approximated.</param>
public sealed record ComposeProgress(
    string? Stage,
    bool EngineRunning,
    bool WaitingOnUser,
    bool Finished,
    string? Narration,
    int FileCount,
    bool? ValidationOk,
    long PromptTokens,
    long CompletionTokens,
    bool Estimated);

/// <summary>
/// The card that stands between «Composer» and the proposal, and between «Essayer» and the
/// verdict.
/// <para>
/// Those two stretches used to be blank. The engine was working — sometimes for a minute —
/// and the screen showed an empty column with a small immobile arc beside «Arrêter»: no way
/// to tell a session that is thinking from one that has died. This card is what fills them,
/// and it says three things only, all of which the stream already carries: WHAT the engine
/// is doing, what the model last SAID, and what it has COST so far, up and down.
/// </para>
/// <para>
/// It borrows <c>RunProgressViewModel</c>'s doctrine word for word: «nothing reported yet»
/// rather than a sentence that implies movement. No news is not the same as going well, and
/// a card that invents reassurance is worse than the blank column it replaced.
/// </para>
/// </summary>
public sealed class ComposeProgressViewModel : ObservableObject
{
    private readonly IStudioStrings _strings;
    private ComposeProgress _reading = new(null, false, false, false, null, 0, null, 0, 0, false);

    /// <summary>Builds the card over the localization port.</summary>
    /// <param name="strings">The catalogue; the English default when omitted.</param>
    public ComposeProgressViewModel(IStudioStrings? strings = null)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        _strings.CultureChanged += (_, _) => RaiseAll();
    }

    /// <summary>Takes a new reading and raises everything that depends on it.</summary>
    /// <param name="reading">Where the composition stands now.</param>
    public void Update(ComposeProgress reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        _reading = reading;
        RaiseAll();
    }

    /// <summary>
    /// Whether the card has anything to say at all. A session that never started shows
    /// nothing rather than an empty frame.
    /// </summary>
    public bool IsVisible => _reading.EngineRunning || _reading.Stage is not null;

    /// <summary>Whether the engine is working — the only state in which the spinner turns.</summary>
    public bool IsWorking => _reading.EngineRunning && !_reading.WaitingOnUser && !_reading.Finished;

    /// <summary>Whether the engine is alive and waiting on you rather than on itself.</summary>
    public bool IsWaiting => _reading.EngineRunning && _reading.WaitingOnUser;

    /// <summary>
    /// What the engine is doing, in one line. Waiting outranks the stage: «I am waiting for
    /// you» is the only sentence that matters while it is true.
    /// </summary>
    public string Title => _strings[TitleKey()];

    private string TitleKey()
    {
        if (IsWaiting)
            return StudioStringKeys.ComposeWaitingOnYou;
        if (_reading.Finished || !_reading.EngineRunning)
            return StudioStringKeys.ComposeStageIdle;

        return _reading.Stage switch
        {
            "brief" => StudioStringKeys.ComposeStageBrief,
            "blueprint" => StudioStringKeys.ComposeStageBlueprint,
            "render" => StudioStringKeys.ComposeStageRender,
            "validate" => StudioStringKeys.ComposeStageValidate,
            "test" => StudioStringKeys.ComposeStageTest,
            "diagnose" => StudioStringKeys.ComposeStageDiagnose,
            "verdict" => StudioStringKeys.ComposeStageVerdict,
            "promote" => StudioStringKeys.ComposeStagePromote,
            // A stage this build has not heard of still says that something is happening,
            // which is true and useful, rather than naming a stage it cannot name.
            _ => StudioStringKeys.ComposeStageWorking,
        };
    }

    /// <summary>
    /// The model's own last words, or the sentence that admits there are none. Deliberately
    /// NOT a reassurance: an engine that has said nothing gets «nothing reported yet».
    /// </summary>
    public string Detail => _reading.Narration is { Length: > 0 } narration
        ? Clip(narration)
        : _strings[StudioStringKeys.RunProgressNothingYet];

    /// <summary>Ascending tokens, grouped for reading; empty while nothing was spent.</summary>
    public string TokensUp => Format(_reading.PromptTokens);

    /// <summary>Descending tokens, grouped for reading; empty while nothing was spent.</summary>
    public string TokensDown => Format(_reading.CompletionTokens);

    /// <summary>Whether the meter has moved at all — no movement shows no figures, never zeros.</summary>
    public bool HasTokens => _reading.PromptTokens > 0 || _reading.CompletionTokens > 0;

    /// <summary>
    /// Whether the figures are the runtime's own approximation because the provider
    /// reported none. The card marks them «≈» rather than passing an estimate off as a count.
    /// </summary>
    public bool TokensEstimated => HasTokens && _reading.Estimated;

    /// <summary>What «≈» means, for the tooltip.</summary>
    public string TokensNote => _strings[StudioStringKeys.ComposeTokensEstimated];

    /// <summary>The ascending chip's accessible name.</summary>
    public string TokensUpLabel => _strings[StudioStringKeys.ComposeTokensUp];

    /// <summary>The descending chip's accessible name.</summary>
    public string TokensDownLabel => _strings[StudioStringKeys.ComposeTokensDown];

    /// <summary>
    /// What is already acquired: files written, and whether the definition passed. Facts
    /// only — a step that produced nothing contributes no line.
    /// </summary>
    public IReadOnlyList<string> Facts
    {
        get
        {
            var facts = new List<string>(2);
            if (_reading.FileCount > 0)
            {
                facts.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    _strings[StudioStringKeys.ComposeFilesWritten],
                    _reading.FileCount));
            }

            if (_reading.ValidationOk is { } ok)
            {
                facts.Add(_strings[ok
                    ? StudioStringKeys.ComposeDefinitionValid
                    : StudioStringKeys.ComposeDefinitionInvalid]);
            }

            return facts;
        }
    }

    /// <summary>Whether anything was acquired yet — gates the facts row.</summary>
    public bool HasFacts => Facts.Count > 0;

    private static string Format(long tokens) =>
        tokens > 0 ? tokens.ToString("N0", CultureInfo.CurrentCulture) : string.Empty;

    /// <summary>
    /// The card is one line tall by design: a narration that runs on is cut here rather
    /// than pushing the proposal below the fold. The whole text stays in the conversation.
    /// </summary>
    private static string Clip(string text)
    {
        const int Max = 160;
        var single = text.ReplaceLineEndings(" ").Trim();
        return single.Length > Max ? string.Concat(single.AsSpan(0, Max).TrimEnd(), "…") : single;
    }

    private void RaiseAll() => OnPropertiesChanged(
        nameof(IsVisible), nameof(IsWorking), nameof(IsWaiting), nameof(Title), nameof(Detail),
        nameof(TokensUp), nameof(TokensDown), nameof(HasTokens), nameof(TokensEstimated),
        nameof(TokensNote), nameof(TokensUpLabel), nameof(TokensDownLabel),
        nameof(Facts), nameof(HasFacts));
}
