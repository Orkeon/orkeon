using Orkeon.Studio.Config.Presentation;
using Terminal.Gui.Views;

namespace Orkeon.Studio.Config.Views;

/// <summary>The <c>RateLimiting</c> screen: five request budgets, all optional.</summary>
internal sealed class RateLimitingSectionView : SectionView
{
    private readonly RateLimitingForm _form;
    private readonly TextField _maxConcurrent;
    private readonly TextField _global;
    private readonly TextField _provider;
    private readonly TextField _agent;
    private readonly TextField _queue;

    /// <summary>Builds the screen over <paramref name="form"/>.</summary>
    public RateLimitingSectionView(RateLimitingForm form)
        : base("Rate limiting")
    {
        _form = form ?? throw new ArgumentNullException(nameof(form));

        FormLayout.AddNote(this, 0, "Leave a field empty to drop the key and let the runtime default apply.");
        _maxConcurrent = FormLayout.AddField(this, 2, "Max concurrent requests", _form.MaxConcurrentRequests);
        _global = FormLayout.AddField(this, 3, "Global requests / minute", _form.GlobalRequestsPerMinute);
        _provider = FormLayout.AddField(this, 4, "Provider requests / minute", _form.ProviderRequestsPerMinute);
        _agent = FormLayout.AddField(this, 5, "Agent requests / minute", _form.AgentRequestsPerMinute);
        _queue = FormLayout.AddField(this, 6, "Queue limit", _form.QueueLimit);
    }

    /// <inheritdoc />
    public override void Load()
    {
        _maxConcurrent.Text = _form.MaxConcurrentRequests;
        _global.Text = _form.GlobalRequestsPerMinute;
        _provider.Text = _form.ProviderRequestsPerMinute;
        _agent.Text = _form.AgentRequestsPerMinute;
        _queue.Text = _form.QueueLimit;
    }

    /// <inheritdoc />
    public override void Apply()
    {
        _form.MaxConcurrentRequests = _maxConcurrent.Text ?? "";
        _form.GlobalRequestsPerMinute = _global.Text ?? "";
        _form.ProviderRequestsPerMinute = _provider.Text ?? "";
        _form.AgentRequestsPerMinute = _agent.Text ?? "";
        _form.QueueLimit = _queue.Text ?? "";
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _maxConcurrent.Dispose();
            _global.Dispose();
            _provider.Dispose();
            _agent.Dispose();
            _queue.Dispose();
        }

        base.Dispose(disposing);
    }
}
