using CsvPeek.Core;

namespace CsvPeek.Application;

internal sealed class CsvIndexCoordinator(
    string path,
    CsvDialect dialect,
    CsvFileFingerprint fingerprint,
    CsvSparseIndex index,
    ICsvRecordSourceFactory sourceFactory,
    ICsvIndexStore indexStore,
    Action<CsvRecord> recordObserved) : IAsyncDisposable
{
    private readonly CsvScanEngine _scanEngine = new(sourceFactory);
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _backgroundCancellation;
    private Task? _backgroundTask;
    private bool _disposed;

    public event EventHandler<ScanProgress>? Progress;

    public void StartBackgroundIndexing()
    {
        if (_disposed || index.IsComplete || _backgroundTask is { IsCompleted: false })
            return;
        _backgroundCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _backgroundTask = BuildIndexAsync(_backgroundCancellation.Token);
    }

    public async Task CancelBackgroundIndexingAsync()
    {
        if (_backgroundCancellation is null)
            return;
        _backgroundCancellation.Cancel();
        try
        {
            if (_backgroundTask is not null)
                await _backgroundTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        _backgroundCancellation.Dispose();
        _backgroundCancellation = null;
        _backgroundTask = null;
    }

    public async Task SearchAsync(
        string query,
        IReadOnlyList<string> columnNames,
        IProgress<IReadOnlyList<SearchMatch>> matches,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        await CancelBackgroundIndexingAsync().ConfigureAwait(false);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCancellation.Token);
        CancellationToken linkedToken = linkedCancellation.Token;
        await _scanGate.WaitAsync(linkedToken).ConfigureAwait(false);
        try
        {
            index.Reset(dialect.BomLength);
            var search = new CsvSearchService(dialect, columnNames, query, matches, progress);
            ScanProgress finalProgress = await _scanEngine.ScanAsync(
                path,
                dialect,
                fingerprint,
                query,
                record =>
                {
                    index.Observe(record);
                    recordObserved(record);
                    return search.Observe(record);
                },
                () => search.MatchCount,
                search.Report,
                linkedToken).ConfigureAwait(false);

            index.Complete(finalProgress.RecordsScanned);
            search.Report(finalProgress with { MatchesFound = search.MatchCount });
            await SaveIndexAsync(linkedToken).ConfigureAwait(false);
        }
        finally
        {
            _scanGate.Release();
            if (!index.IsComplete && !_disposed)
                StartBackgroundIndexing();
        }
    }

    private async Task BuildIndexAsync(CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            index.Reset(dialect.BomLength);
            ScanProgress finalProgress = await _scanEngine.ScanAsync(
                path,
                dialect,
                fingerprint,
                query: null,
                record =>
                {
                    index.Observe(record);
                    recordObserved(record);
                    return false;
                },
                static () => 0,
                value => Progress?.Invoke(this, value),
                cancellationToken).ConfigureAwait(false);

            index.Complete(finalProgress.RecordsScanned);
            Progress?.Invoke(this, finalProgress);
            await SaveIndexAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private Task SaveIndexAsync(CancellationToken cancellationToken) =>
        sourceFactory.StillMatches(fingerprint)
            ? indexStore.SaveAsync(fingerprint, dialect, index, cancellationToken)
            : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetimeCancellation.Cancel();
        await CancelBackgroundIndexingAsync().ConfigureAwait(false);
        await _scanGate.WaitAsync().ConfigureAwait(false);
        _scanGate.Release();
        _lifetimeCancellation.Dispose();
        _scanGate.Dispose();
    }
}
