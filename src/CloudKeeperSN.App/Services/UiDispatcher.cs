using System.Windows.Threading;

namespace CloudKeeperSN.App.Services;

public interface IUiDispatcher
{
    void Invoke(Action action);
    void Post(Action action) => Invoke(action);
}

public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Invoke(Action action)
    {
        if (dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    public void Post(Action action) => dispatcher.BeginInvoke(action, DispatcherPriority.DataBind);
}

public sealed class InlineUiDispatcher : IUiDispatcher
{
    public static InlineUiDispatcher Instance { get; } = new();
    private InlineUiDispatcher() { }
    public void Invoke(Action action) => action();
}
