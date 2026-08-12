using CsvPeek.Core;
using System.Text;

namespace CsvPeek.Application;

public sealed class CsvDocumentSession : ICsvDocumentSession
{
    private readonly ICsvRecordSourceFactory _sourceFactory;
    private readonly ICsvIndexStore _indexStore;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CsvSparseIndex _index = new();
    private CsvPageProvider _pages = null!;
    private CsvIndexCoordinator _indexing = null!;
    private string[] _columnNames = [];
    private bool _disposed;
    private int _hasTruncatedCells;

    private CsvDocumentSession(
        string path,
        CsvDialect dialect,
        CsvFileFingerprint fingerprint,
        ICsvRecordSourceFactory sourceFactory,
        ICsvIndexStore indexStore)
    {
        Path = path;
        Dialect = dialect;
        Fingerprint = fingerprint;
        _sourceFactory = sourceFactory;
        _indexStore = indexStore;
    }

    public string Path { get; }
    public CsvDialect Dialect { get; private set; }
    public CsvFileFingerprint Fingerprint { get; private set; }
    public IReadOnlyList<string> ColumnNames => _columnNames;
    public long KnownDataRows => Math.Max(0, (_index.IsComplete ? _index.RecordCount : _index.RecordsScanned) - (Dialect.FirstRowIsHeader ? 1 : 0));
    public bool IsIndexComplete => _index.IsComplete;
    public bool HasTruncatedCells => Volatile.Read(ref _hasTruncatedCells) != 0;

    public event EventHandler<ScanProgress>? IndexProgress;
    public event EventHandler<long>? PageLoaded;

    public static async Task<CsvDocumentSession> OpenAsync(
        string path,
        ICsvRecordSourceFactory sourceFactory,
        ICsvIndexStore indexStore,
        CancellationToken cancellationToken = default)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        CsvDialect dialect = await sourceFactory.DetectDialectAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var session = new CsvDocumentSession(fullPath, dialect, sourceFactory.GetFingerprint(fullPath), sourceFactory, indexStore);
        try
        {
            await session.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public CsvRecord? TryGetRecord(long dataRow) => _pages.TryGet(dataRow);

    public async Task EnsurePageAsync(long dataRow, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCancellation.Token);
        await _pages.EnsurePageAsync(dataRow, linked.Token).ConfigureAwait(false);
    }

    public void StartBackgroundIndexing() => _indexing.StartBackgroundIndexing();

    public Task CancelBackgroundIndexingAsync() => _indexing.CancelBackgroundIndexingAsync();

    public Task SearchAsync(
        string query,
        IProgress<IReadOnlyList<SearchMatch>> matches,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.CompletedTask;
        return _indexing.SearchAsync(query, _columnNames, matches, progress, cancellationToken);
    }

    public async Task ReconfigureAsync(
        char delimiter,
        Encoding encoding,
        bool firstRowIsHeader,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await DisposeWorkersAsync().ConfigureAwait(false);
        Dialect = new CsvDialect(
            delimiter,
            encoding,
            _sourceFactory.GetBomLength(Path, encoding),
            firstRowIsHeader);
        Fingerprint = _sourceFactory.GetFingerprint(Path);
        Interlocked.Exchange(ref _hasTruncatedCells, 0);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool HasChanged() => !_sourceFactory.StillMatches(Fingerprint);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _index = new CsvSparseIndex();
        _index.Reset(Dialect.BomLength);
        _pages = new CsvPageProvider(Path, Dialect, _index, _sourceFactory, ObserveRecord);
        _pages.PageLoaded += OnPageLoaded;
        CsvPageProvider.InitialPage initialPage = await _pages.LoadInitialPageAsync(cancellationToken).ConfigureAwait(false);
        _columnNames = initialPage.ColumnNames;
        await _indexStore.TryLoadAsync(Fingerprint, Dialect, _index, cancellationToken).ConfigureAwait(false);
        _indexing = new CsvIndexCoordinator(Path, Dialect, Fingerprint, _index, _sourceFactory, _indexStore, ObserveRecord);
        _indexing.Progress += OnIndexProgress;
    }

    private void ObserveRecord(CsvRecord record)
    {
        if (record.WasTruncated)
            Interlocked.Exchange(ref _hasTruncatedCells, 1);
    }

    private void OnIndexProgress(object? sender, ScanProgress progress) => IndexProgress?.Invoke(this, progress);
    private void OnPageLoaded(object? sender, long page) => PageLoaded?.Invoke(this, page);

    private async Task DisposeWorkersAsync()
    {
        if (_indexing is not null)
        {
            _indexing.Progress -= OnIndexProgress;
            await _indexing.DisposeAsync().ConfigureAwait(false);
        }
        if (_pages is not null)
        {
            _pages.PageLoaded -= OnPageLoaded;
            _pages.Clear();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetimeCancellation.Cancel();
        await DisposeWorkersAsync().ConfigureAwait(false);
        _lifetimeCancellation.Dispose();
    }
}
