using System.Globalization;
using Orkeon.Studio.Core.Email;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>Where a sign-in stands.</summary>
public enum EmailSignInPhase
{
    /// <summary>The verb runs and has not said yet what to do.</summary>
    Starting,

    /// <summary>A page to open and a code to type there, while the code lives.</summary>
    DeviceCode,

    /// <summary>A page to open in a browser, and a field for the address it ended on.</summary>
    AuthorizationUrl,

    /// <summary>The tokens are stored.</summary>
    Completed,

    /// <summary>The verb refused or failed: what it said shows, and nothing is tried again by itself.</summary>
    Failed,

    /// <summary>The device code outlived its time: a new sign-in is the only way on.</summary>
    Expired,
}

/// <summary>What a sign-in panel needs beside the CLI: the seams of the E-mail tab that only it uses.</summary>
public sealed record EmailSignInServices
{
    /// <summary>Opens an address in the browser, on a click; no "open" button when null.</summary>
    public IBrowserOpener? Browser { get; init; }

    /// <summary>Where "copy the link" puts the address; in-memory when null.</summary>
    public IClipboardService? Clipboard { get; init; }

    /// <summary>The beat the countdown of a device code moves on; one that never beats when null.</summary>
    public IUiTicker? Ticker { get; init; }

    /// <summary>The clock the countdown reads; the system's when null.</summary>
    public TimeProvider? Clock { get; init; }
}

/// <summary>
/// One sign-in of an OAuth e-mail account, from the click to its end (STUDIO-70): the panel under
/// the account's row. Studio runs no OAuth flow — <c>orkeon email login --events jsonl</c> does,
/// and stores the tokens where every run looks for them; this shows what the person has to do
/// meanwhile, and waits. A device sign-in (Microsoft) is a page and a code, counted down on the
/// clock it is given; a browser sign-in (Google) is an address, and a field for the one the
/// browser ended on when it could not come back to this machine — an address the verb rejects
/// comes back to that field with the verb's reason, and the panel goes on waiting.
/// <para>
/// Nothing is opened but on a click, and nothing but an <c>https</c> address: the address is the
/// CLI's word, shown in clear as received. The code is a secret while it lives: it is held here
/// and nowhere else, and leaves with the phase that showed it.
/// </para>
/// <para>
/// The verb waits without a deadline, so every way out of the panel — cancel, the code's expiry,
/// the tab left, the window closed — fires the child's token, which closes its standard input on
/// the caller's thread before the launcher stops it. A refusal of the verb shows as printed and
/// is never tried again.
/// </para>
/// </summary>
public sealed class EmailSignInViewModel : ObservableObject
{
    private readonly EmailSectionViewModel _owner;
    private readonly EmailCliClient _cli;
    private readonly string _settingsPath;
    private readonly IStudioStrings _strings;
    private readonly IUiDispatcher _dispatcher;
    private readonly IBrowserOpener? _browser;
    private readonly IClipboardService _clipboard;
    private readonly TimeProvider _clock;
    private readonly Action _moved;
    private readonly Action<EmailSignInViewModel> _closed;
    private CancellationTokenSource? _cancellation;
    private EmailLoginInput? _input;
    private int _attempt;
    private EmailSignInPhase _phase;
    private string? _address;
    private string? _userCode;
    private DateTimeOffset? _deadline;
    private EmailCliFailure? _failure;
    private string _pastedRedirect = "";
    private bool _redirectNotSent;
    private string? _submittedRedirect;
    private string? _redirectRejection;
    private bool _failedOnDeviceCode;

    /// <summary>
    /// A panel for <paramref name="account"/> as <paramref name="settingsPath"/> declares it.
    /// <paramref name="moved"/> is called whenever the phase changes, <paramref name="closed"/>
    /// once the panel is to leave the row.
    /// </summary>
    internal EmailSignInViewModel(
        string account,
        EmailSectionViewModel owner,
        EmailCliClient cli,
        string settingsPath,
        IStudioStrings strings,
        IUiDispatcher dispatcher,
        EmailSignInServices services,
        Action moved,
        Action<EmailSignInViewModel> closed)
    {
        Account = account;
        _owner = owner;
        _cli = cli;
        _settingsPath = settingsPath;
        _strings = strings;
        _dispatcher = dispatcher;
        _browser = services.Browser;
        _clipboard = services.Clipboard ?? new InMemoryClipboardService();
        _clock = services.Clock ?? TimeProvider.System;
        _moved = moved;
        _closed = closed;

        SubmitRedirectCommand = new RelayCommand(SubmitRedirect, () => IsAuthorizationUrl && !string.IsNullOrWhiteSpace(_pastedRedirect));
        CopyLinkCommand = new RelayCommand(() => { if (_address is { Length: > 0 } address) _clipboard.SetText(address); }, () => HasAddress);
        OpenCommand = new RelayCommand(Open, () => CanOpen);
        CancelCommand = new RelayCommand(Abandon);
        RestartCommand = new RelayCommand(() => { if (CanRestart) Start(); }, () => CanRestart);
    }

    /// <summary>The account being signed in, as the saved file names it.</summary>
    public string Account { get; }

    /// <summary>Where the sign-in stands.</summary>
    public EmailSignInPhase Phase => _phase;

    /// <summary>Whether the verb runs and has not said yet what to do.</summary>
    public bool IsStarting => _phase == EmailSignInPhase.Starting;

    /// <summary>Whether the page and the code of a device sign-in show.</summary>
    public bool IsDeviceCode => _phase == EmailSignInPhase.DeviceCode;

    /// <summary>Whether the address of a browser sign-in and its paste field show.</summary>
    public bool IsAuthorizationUrl => _phase == EmailSignInPhase.AuthorizationUrl;

    /// <summary>Whether the tokens are stored.</summary>
    public bool IsCompleted => _phase == EmailSignInPhase.Completed;

    /// <summary>Whether the verb refused or failed.</summary>
    public bool IsFailed => _phase == EmailSignInPhase.Failed;

    /// <summary>Whether the device code outlived its time.</summary>
    public bool IsExpired => _phase == EmailSignInPhase.Expired;

    /// <summary>
    /// Whether "Start again" is offered: the code outlived its time, or the verb failed while
    /// one showed — the verb gives up on the same deadline as the countdown, and its word may
    /// land first. A click, never a retry of the panel's own.
    /// </summary>
    public bool CanRestart => IsExpired || (IsFailed && _failedOnDeviceCode);

    /// <summary>Whether a child is waiting on the person: the panel's one button then cancels, and closes otherwise.</summary>
    public bool IsWaiting => _phase is EmailSignInPhase.Starting or EmailSignInPhase.DeviceCode or EmailSignInPhase.AuthorizationUrl;

    /// <summary>The page to open, in clear and exactly as the verb wrote it; null once the sign-in ended.</summary>
    public string? Address => _address;

    /// <summary>Whether an address shows.</summary>
    public bool HasAddress => _address is { Length: > 0 };

    /// <summary>
    /// Whether "Open in the browser" is offered: a port to open with, and an address that is an
    /// absolute <c>https</c> one. Anything else can only be read and copied.
    /// </summary>
    public bool CanOpen => _browser is not null && HttpsAddress.TryParse(_address, out _);

    /// <summary>The code to type on the page of a device sign-in; null in every other phase.</summary>
    public string? UserCode => _userCode;

    /// <summary>"Open {page} and enter this code:", localized; null outside a device sign-in.</summary>
    public string? DeviceCodeLine => IsDeviceCode
        ? string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.MailDeviceCode], _address)
        : null;

    /// <summary>Whether the device code has a known lifetime to count down.</summary>
    public bool HasExpiry => IsDeviceCode && _deadline is not null;

    /// <summary>"The code expires in 14:59", localized, read on the clock at each beat; null without a lifetime.</summary>
    public string? ExpiresInText
    {
        get
        {
            if (!IsDeviceCode || _deadline is not { } deadline)
                return null;

            var remaining = deadline - _clock.GetUtcNow();
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            var clock = string.Create(CultureInfo.InvariantCulture, $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}");
            return string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.MailDeviceCodeExpiresIn], clock);
        }
    }

    /// <summary>The address the browser ended on, as pasted; sent by <see cref="SubmitRedirectCommand"/>.</summary>
    public string PastedRedirect
    {
        get => _pastedRedirect;
        set
        {
            if (!SetProperty(ref _pastedRedirect, value ?? ""))
                return;

            SetRedirectNotSent(false);
            SubmitRedirectCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Whether the last address pasted could not be handed to the verb — its input not open yet,
    /// or closed already: the field keeps it, and says so until it is typed in or sent again.
    /// </summary>
    public bool RedirectNotSent => _redirectNotSent;

    /// <summary>
    /// Whether the verb rejected the last address it was handed — cut short, another address,
    /// plain text: the address is back in the field to be corrected, the verb still waits, and
    /// the notice stays until an address is sent again.
    /// </summary>
    public bool RedirectRejected => _redirectRejection is not null;

    /// <summary>Why the verb rejected the address: its sentence, in English, as printed; null when it rejected none.</summary>
    public string? RedirectRejectedDetail => _redirectRejection;

    /// <summary>
    /// Why the sign-in failed: the verb's sentence, in English, as printed — nothing is read out
    /// of it —, or the sentence of the home screen when the CLI is not on this machine.
    /// </summary>
    public string? FailureDetail => _failure is { } failure ? _owner.FailureText(failure) : null;

    /// <summary>
    /// Writes <see cref="PastedRedirect"/> to the verb, which judges it; the field is then cleared.
    /// An address nothing read stays in the field, and <see cref="RedirectNotSent"/> says so; one
    /// the verb rejects comes back to it, and <see cref="RedirectRejected"/> says so.
    /// </summary>
    public RelayCommand SubmitRedirectCommand { get; }

    /// <summary>Puts <see cref="Address"/> on the clipboard — the address, never the code.</summary>
    public RelayCommand CopyLinkCommand { get; }

    /// <summary>Opens <see cref="Address"/> in the browser: on this click only, and only an <c>https</c> address.</summary>
    public RelayCommand OpenCommand { get; }

    /// <summary>"Cancel" while the verb waits — the child is stopped —, "Close" once it ended: the panel leaves the row.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>"Start again" once the code expired, or the verb failed while one showed: a new sign-in, with a new code.</summary>
    public RelayCommand RestartCommand { get; }

    /// <summary>
    /// Completes when the child of the current attempt ended and what followed — the states read
    /// again after a sign-in that stored its tokens — is done. What the tests await.
    /// </summary>
    internal Task Ended { get; private set; } = Task.CompletedTask;

    /// <summary>Whether the panel counts a device code down: the tab keeps its beat while one does.</summary>
    internal bool CountsDown => HasExpiry;

    /// <summary>Starts the verb. Its first words may land before this returns.</summary>
    internal void Start()
    {
        var attempt = ++_attempt;
        var cancellation = new CancellationTokenSource();
        var input = new EmailLoginInput();
        _cancellation = cancellation;
        _input = input;
        Show(EmailSignInPhase.Starting);
        Ended = RunAsync(attempt, cancellation, input);
    }

    /// <summary>One second passed: the countdown is said again, and a code past its time ends the sign-in.</summary>
    internal void Tick()
    {
        if (!IsDeviceCode || _deadline is not { } deadline)
            return;

        if (_clock.GetUtcNow() < deadline)
        {
            OnPropertyChanged(nameof(ExpiresInText));
            return;
        }

        StopChild();
        Show(EmailSignInPhase.Expired);
    }

    /// <summary>
    /// Stops the child when one waits, and takes the panel off the row. Synchronous up to the
    /// child's token: when this returns, its standard input is closed.
    /// </summary>
    internal void Abandon()
    {
        StopChild();
        Show(_phase, keep: false);
        _closed(this);
    }

    /// <summary>A language change: what the panel fabricates is said again; the address, the code and the verb's sentence stay.</summary>
    internal void RefreshTexts() =>
        OnPropertiesChanged(nameof(DeviceCodeLine), nameof(ExpiresInText), nameof(FailureDetail));

    private async Task RunAsync(int attempt, CancellationTokenSource cancellation, EmailLoginInput input)
    {
        EmailLoginStep ended;
        try
        {
            ended = await _cli.LoginAsync(
                Account, _settingsPath, step => _dispatcher.Post(() => OnStep(attempt, step)), input, cancellation.Token);
        }
        finally
        {
            cancellation.Dispose();
        }

        var landed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.Post(() => landed.TrySetResult(OnEnded(attempt, ended)));

        // The tokens are stored: what the engine makes of the account changed, the file did not.
        if (await landed.Task)
            await _owner.RefreshStatesAsync();
    }

    /// <summary>The verb said what to do. A word of an attempt that was stopped since is not this panel's.</summary>
    private void OnStep(int attempt, EmailLoginStep step)
    {
        if (attempt != _attempt || !IsWaiting)
            return;

        switch (step.Kind)
        {
            case EmailLoginStepKind.DeviceCode:
                _address = step.Address;
                _userCode = step.UserCode;
                _deadline = step.ExpiresIn is { } life ? _clock.GetUtcNow() + life : null;
                Show(EmailSignInPhase.DeviceCode, keep: true);
                break;
            case EmailLoginStepKind.AuthorizationUrl:
                _address = step.Address;
                Show(EmailSignInPhase.AuthorizationUrl, keep: true);
                break;
            case EmailLoginStepKind.RedirectRejected when IsAuthorizationUrl:
                OnRedirectRejected(step.Message ?? "");
                break;
        }
    }

    /// <summary>
    /// The verb rejected the address it was handed, and still waits: its sentence shows, and the
    /// address comes back in the field it left — unless something was typed there since.
    /// </summary>
    private void OnRedirectRejected(string reason)
    {
        var submitted = _submittedRedirect;
        _submittedRedirect = null;
        _redirectRejection = reason;
        if (_pastedRedirect.Length == 0 && submitted is { Length: > 0 })
        {
            _pastedRedirect = submitted;
            OnPropertyChanged(nameof(PastedRedirect));
            SubmitRedirectCommand.RaiseCanExecuteChanged();
        }

        OnPropertiesChanged(nameof(RedirectRejected), nameof(RedirectRejectedDetail));
    }

    /// <summary>The child ended; true when it stored the tokens.</summary>
    private bool OnEnded(int attempt, EmailLoginStep ended)
    {
        // Cancelled, expired or started again since: that end was asked for, and says nothing.
        if (attempt != _attempt || !IsWaiting)
            return false;

        _cancellation = null;
        _input = null;
        if (ended.Kind == EmailLoginStepKind.Completed)
        {
            Show(EmailSignInPhase.Completed);
            return true;
        }

        _failure = ended.Failure;
        _failedOnDeviceCode = IsDeviceCode;
        Show(EmailSignInPhase.Failed, keepFailure: true);
        return false;
    }

    private void SubmitRedirect()
    {
        if (!IsAuthorizationUrl || string.IsNullOrWhiteSpace(_pastedRedirect))
            return;

        // The notice of a rejection is of the address sent before: it leaves with this one.
        if (_redirectRejection is not null)
        {
            _redirectRejection = null;
            OnPropertiesChanged(nameof(RedirectRejected), nameof(RedirectRejectedDetail));
        }

        // Kept while the verb judges it: an address it rejects goes back to the field.
        var submitted = _pastedRedirect;
        _submittedRedirect = submitted;

        // Clearing the field says the verb took the address: only what it was handed leaves it.
        if (_input?.PasteRedirect(submitted) != true)
        {
            _submittedRedirect = null;
            SetRedirectNotSent(true);
            return;
        }

        // The verb may have answered within the write: an address it rejected stays where it is.
        if (_redirectRejection is null)
            PastedRedirect = "";
    }

    private void SetRedirectNotSent(bool value)
    {
        if (_redirectNotSent == value)
            return;

        _redirectNotSent = value;
        OnPropertyChanged(nameof(RedirectNotSent));
    }

    private void Open()
    {
        if (_browser is { } browser && HttpsAddress.TryParse(_address, out var address))
            browser.Open(address);
    }

    /// <summary>Fires the token of the child in flight, if any: nothing it says from here on is listened to.</summary>
    private void StopChild()
    {
        _attempt++;
        var cancellation = _cancellation;
        _cancellation = null;
        _input = null;
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The child ended between the click and the cancel; nothing left to stop.
        }
    }

    /// <summary>
    /// Moves to <paramref name="phase"/>. Unless <paramref name="keep"/>, the address, the code
    /// and its deadline leave the screen with the phase that showed them; the failure stays only
    /// for the phase that says it.
    /// </summary>
    private void Show(EmailSignInPhase phase, bool keep = false, bool keepFailure = false)
    {
        _phase = phase;
        if (!keep)
        {
            _address = null;
            _userCode = null;
            _deadline = null;
            _pastedRedirect = "";
            _redirectNotSent = false;
            _submittedRedirect = null;
            _redirectRejection = null;
        }

        if (!keepFailure)
        {
            _failure = null;
            _failedOnDeviceCode = false;
        }

        OnPropertiesChanged(
            nameof(Phase), nameof(IsStarting), nameof(IsDeviceCode), nameof(IsAuthorizationUrl), nameof(IsCompleted),
            nameof(IsFailed), nameof(IsExpired), nameof(CanRestart), nameof(IsWaiting), nameof(Address), nameof(HasAddress), nameof(CanOpen),
            nameof(UserCode), nameof(DeviceCodeLine), nameof(HasExpiry), nameof(ExpiresInText), nameof(PastedRedirect),
            nameof(RedirectNotSent), nameof(RedirectRejected), nameof(RedirectRejectedDetail), nameof(FailureDetail));
        SubmitRedirectCommand.RaiseCanExecuteChanged();
        CopyLinkCommand.RaiseCanExecuteChanged();
        OpenCommand.RaiseCanExecuteChanged();
        RestartCommand.RaiseCanExecuteChanged();
        _moved();
    }
}
