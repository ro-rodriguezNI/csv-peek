using CsvPeek.Application;

namespace CsvPeek.App;

internal sealed class DocumentWorkspaceController(ICsvDocumentSessionFactory documentFactory) : IAsyncDisposable
{
    public ICsvDocumentSession? Current { get; private set; }

    public async Task<ICsvDocumentSession> OpenAsync(string path, CancellationToken cancellationToken)
    {
        ICsvDocumentSession next = await documentFactory.OpenAsync(path, cancellationToken);
        ICsvDocumentSession? previous = Current;
        Current = next;
        if (previous is not null)
            await previous.DisposeAsync();
        return next;
    }

    public Task ReloadAsync(CancellationToken cancellationToken) =>
        Current is null ? Task.CompletedTask : OpenAsync(Current.Path, cancellationToken);

    public bool HasExternalChange() => Current?.HasChanged() == true;

    public Task StopBackgroundWorkAsync() =>
        Current?.CancelBackgroundIndexingAsync() ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (Current is null)
            return;
        ICsvDocumentSession document = Current;
        Current = null;
        await document.DisposeAsync();
    }
}
