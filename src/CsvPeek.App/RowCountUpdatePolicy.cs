namespace CsvPeek.App;

internal enum RowCountUpdateAction
{
    None,
    StartTimer,
    RestartTimer,
    ApplyDeferred,
    ApplyExact
}

internal sealed class RowCountUpdatePolicy
{
    public bool IsPending { get; private set; }

    public RowCountUpdateAction Request(bool forceExact, bool indexComplete)
    {
        if (forceExact || indexComplete)
        {
            IsPending = false;
            return RowCountUpdateAction.ApplyExact;
        }
        if (IsPending)
            return RowCountUpdateAction.None;
        IsPending = true;
        return RowCountUpdateAction.StartTimer;
    }

    public RowCountUpdateAction DeferForVerticalScroll() =>
        IsPending ? RowCountUpdateAction.RestartTimer : RowCountUpdateAction.None;

    public RowCountUpdateAction TimerElapsed()
    {
        if (!IsPending)
            return RowCountUpdateAction.None;
        IsPending = false;
        return RowCountUpdateAction.ApplyDeferred;
    }

    public void Reset() => IsPending = false;
}
