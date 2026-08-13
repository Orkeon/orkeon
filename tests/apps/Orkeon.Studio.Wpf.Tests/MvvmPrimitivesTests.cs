using System.ComponentModel;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.Tests;

public sealed class ObservableObjectTests
{
    private sealed class Probe : ObservableObject
    {
        private string _text = "";

        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }

        public void RaiseMany() => OnPropertiesChanged("A", "B");
    }

    [Fact]
    public void Should_Notify_When_ValueChanges()
    {
        var probe = new Probe();
        var raised = new List<string?>();
        probe.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        probe.Text = "changed";

        Assert.Equal([nameof(Probe.Text)], raised);
    }

    [Fact]
    public void Should_NotNotify_When_ValueIsUnchanged()
    {
        var probe = new Probe { Text = "same" };
        var raised = new List<string?>();
        probe.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        probe.Text = "same";

        Assert.Empty(raised);
    }

    [Fact]
    public void Should_NotifyEachName_When_SeveralComputedPropertiesChange()
    {
        var probe = new Probe();
        var raised = new List<string?>();
        probe.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        probe.RaiseMany();

        Assert.Equal(["A", "B"], raised);
    }

    [Fact]
    public void Should_ImplementInpc_So_WpfBindingCanSubscribe()
    {
        // The whole MVVM contract rests on this: the view never polls.
        Assert.IsType<INotifyPropertyChanged>(new Probe(), exactMatch: false);
    }
}

public sealed class RelayCommandTests
{
    [Fact]
    public void Should_Execute_When_CanExecuteIsTrue()
    {
        var calls = 0;
        var command = new RelayCommand(() => calls++);

        command.Execute(null);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Should_NotExecute_When_CanExecuteIsFalse()
    {
        var calls = 0;
        var command = new RelayCommand(() => calls++, () => false);

        command.Execute(null);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Should_PassParameter_When_CommandTakesOne()
    {
        object? received = null;
        var command = new RelayCommand(p => received = p);

        command.Execute("payload");

        Assert.Equal("payload", received);
    }

    [Fact]
    public void Should_RaiseCanExecuteChanged_When_Asked()
    {
        var command = new RelayCommand(() => { });
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        command.RaiseCanExecuteChanged();

        Assert.Equal(1, raised);
    }
}

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task Should_AwaitTheAction_When_ExecutedAsync()
    {
        var completed = false;
        var command = new AsyncRelayCommand(async () =>
        {
            await Task.Yield();
            completed = true;
        });

        await command.ExecuteAsync();

        Assert.True(completed);
    }

    [Fact]
    public async Task Should_RefuseReentry_While_Running()
    {
        var gate = new TaskCompletionSource();
        var starts = 0;
        var command = new AsyncRelayCommand(async () =>
        {
            starts++;
            await gate.Task;
        });

        var first = command.ExecuteAsync();
        Assert.False(command.CanExecute(null));

        await command.ExecuteAsync();
        Assert.Equal(1, starts);

        gate.SetResult();
        await first;

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task Should_ClearIsRunning_When_TheActionThrows()
    {
        var command = new AsyncRelayCommand(() => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync());

        Assert.False(command.IsRunning);
    }
}

public sealed class ImmediateUiDispatcherTests
{
    [Fact]
    public void Should_RunInline_So_TestsStayDeterministic()
    {
        var ran = false;

        ImmediateUiDispatcher.Instance.Post(() => ran = true);

        Assert.True(ran);
    }
}
