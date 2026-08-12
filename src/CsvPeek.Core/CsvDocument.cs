using System.Text;

namespace CsvPeek.Core;

public sealed class CsvDocument : IAsyncDisposable
{
    private readonly CsvIndexStore _indexStore;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private CancellationTokenSource? _backgroundScanCancellation;
    private Task? _backgroundScanTask;
    private bool _disposed;

    private CsvDocument(string path, CsvDialect dialect, CsvFileFingerprint fingerprint, CsvIndexStore indexStore)
    {
        Path = path;
        Dialect = dialect;
        Fingerprint = fingerprint;
        Index = new CsvSparseIndex();
        Index.Reset(dialect.BomLength);
        Cache = new CsvPageCache();
        _indexStore = indexStore;
    }

    public string Path { get; }
    public CsvDialect Dialect { get; private set; }
    public CsvFileFingerprint Fingerprint { get; private set; }
    public CsvSparseIndex Index { get; }
    public CsvPageCache Cache { get; }
    public string[] ColumnNames { get; private set; } = [];
    public long KnownDataRows => Math.Max(0, (Index.IsComplete ? Index.RecordCount : Index.RecordsScanned) - (Dialect.FirstRowIsHeader ? 1 : 0));
    public bool HasTruncatedCells { get; private set; }

    public event EventHandler<ScanProgress>? IndexProgress;

    public static async Task<CsvDocument> OpenAsync(string path, CsvIndexStore? indexStore = null, CancellationToken cancellationToken = default)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("No se encontró el archivo CSV.", fullPath);

        var dialect = await CsvDialectDetector.DetectAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var document = new CsvDocument(fullPath, dialect, CsvFileFingerprint.Create(fullPath), indexStore ?? new CsvIndexStore());
        await document.LoadInitialPageAsync(cancellationToken).ConfigureAwait(false);
        await document._indexStore.TryLoadAsync(document.Fingerprint, dialect, document.Index, cancellationToken).ConfigureAwait(false);
        return document;
    }

    public async Task ReconfigureAsync(CsvDialect dialect, CancellationToken cancellationToken = default)
    {
        await CancelBackgroundScanAsync().ConfigureAwait(false);
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dialect = dialect;
            Fingerprint = CsvFileFingerprint.Create(Path);
            Index.Reset(dialect.BomLength);
            Cache.Clear();
            await LoadInitialPageAsync(cancellationToken).ConfigureAwait(false);
            await _indexStore.TryLoadAsync(Fingerprint, Dialect, Index, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private Task LoadInitialPageAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var reader = new CsvRecordReader(Path, Dialect);
        var rawRecords = new List<CsvRecord>(CsvPageCache.PageSize + 1);
        while (rawRecords.Count < CsvPageCache.PageSize + (Dialect.FirstRowIsHeader ? 1 : 0))
        {
            var record = reader.ReadRecord(cancellationToken);
            if (record is null)
                break;
            rawRecords.Add(record);
            Index.Observe(record);
            HasTruncatedCells |= record.WasTruncated;
        }

        CsvRecord? header = Dialect.FirstRowIsHeader && rawRecords.Count > 0 ? rawRecords[0] : null;
        var data = header is null ? rawRecords : rawRecords.Skip(1).ToList();
        int columnCount = Math.Max(header?.Fields.Length ?? 0, data.Count == 0 ? 0 : data.Max(record => record.Fields.Length));
        ColumnNames = BuildColumnNames(header?.Fields, columnCount);
        Cache.Seed(data);
        if (reader.ReadRecord(cancellationToken) is null)
            Index.Complete(rawRecords.Count);
    }, cancellationToken);

    public void StartBackgroundIndexing()
    {
        if (_disposed || Index.IsComplete || _backgroundScanTask is { IsCompleted: false })
            return;
        _backgroundScanCancellation = new CancellationTokenSource();
        _backgroundScanTask = BuildIndexAsync(_backgroundScanCancellation.Token);
    }

    public async Task CancelBackgroundScanAsync()
    {
        if (_backgroundScanCancellation is null)
            return;
        _backgroundScanCancellation.Cancel();
        try
        {
            if (_backgroundScanTask is not null)
                await _backgroundScanTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        _backgroundScanCancellation.Dispose();
        _backgroundScanCancellation = null;
        _backgroundScanTask = null;
    }

    private async Task BuildIndexAsync(CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => ScanCore(null, null, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    public async Task SearchAsync(string query, IProgress<IReadOnlyList<SearchMatch>> matches, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        await CancelBackgroundScanAsync().ConfigureAwait(false);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancellation.Token);
        CancellationToken linkedToken = linkedCancellation.Token;
        await _scanGate.WaitAsync(linkedToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => ScanCore(query, (batch, scanProgress) =>
            {
                if (batch.Count > 0)
                    matches.Report(batch);
                progress?.Report(scanProgress);
            }, linkedToken), linkedToken).ConfigureAwait(false);
        }
        finally
        {
            _scanGate.Release();
            if (!Index.IsComplete && !_disposed)
                StartBackgroundIndexing();
        }
    }

    private void ScanCore(string? query, Action<IReadOnlyList<SearchMatch>, ScanProgress>? searchProgress, CancellationToken cancellationToken)
    {
        Index.Reset(Dialect.BomLength);
        long matchCount = 0;
        long recordCount = 0;
        var batch = new List<SearchMatch>(100);
        var comparison = StringComparison.OrdinalIgnoreCase;
        int fieldLimit = query is null ? 0 : CsvRecordReader.DefaultMaxFieldChars;
        using var reader = new CsvRecordReader(Path, Dialect, maxFieldChars: fieldLimit, captureFields: query is not null, matchQuery: query);
        CsvRecord? record;

        while ((record = reader.ReadRecord(cancellationToken)) is not null)
        {
            Index.Observe(record);
            recordCount = record.Number + 1;
            HasTruncatedCells |= record.WasTruncated;

            bool isHeader = Dialect.FirstRowIsHeader && record.Number == 0;
            if (query is not null && !isHeader)
            {
                foreach (int column in record.MatchedColumns ?? [])
                {
                    string value = column < record.Fields.Length ? record.Fields[column] : string.Empty;
                    int foundAt = value.IndexOf(query, comparison);

                    matchCount++;
                    if (matchCount <= 10_000)
                    {
                        batch.Add(new SearchMatch(
                            record.Number - (Dialect.FirstRowIsHeader ? 1 : 0),
                            column,
                            column < ColumnNames.Length ? ColumnNames[column] : $"Columna {column + 1}",
                            foundAt >= 0 ? CreatePreview(value, foundAt, query.Length) : "Coincidencia en un valor grande (vista previa truncada)",
                            record.StartOffset));
                    }
                }
            }

            if (recordCount % 4096 == 0 || batch.Count >= 100)
            {
                var scanProgress = new ScanProgress(recordCount, reader.BytePosition, Fingerprint.Length, matchCount);
                if (searchProgress is not null)
                {
                    searchProgress(batch.ToArray(), scanProgress);
                    batch.Clear();
                }
                else
                {
                    IndexProgress?.Invoke(this, scanProgress);
                }
            }
        }

        Index.Complete(recordCount);
        var finalProgress = new ScanProgress(recordCount, Fingerprint.Length, Fingerprint.Length, matchCount);
        if (searchProgress is not null)
            searchProgress(batch.ToArray(), finalProgress);
        else
            IndexProgress?.Invoke(this, finalProgress);

        _indexStore.SaveAsync(Fingerprint, Dialect, Index, cancellationToken).GetAwaiter().GetResult();
    }

    public async Task<CsvRecord[]> LoadPageAsync(long dataPage, CancellationToken cancellationToken = default)
    {
        long firstDataRow = dataPage * CsvPageCache.PageSize;
        long firstRawRecord = firstDataRow + (Dialect.FirstRowIsHeader ? 1 : 0);
        var (checkpointRecord, checkpointOffset) = Index.FindCheckpoint(firstRawRecord);

        return await Task.Run(() =>
        {
            using var reader = new CsvRecordReader(Path, Dialect, checkpointOffset, checkpointRecord);
            CsvRecord? record;
            do
            {
                record = reader.ReadRecord(cancellationToken);
            } while (record is not null && record.Number < firstRawRecord);

            var records = new List<CsvRecord>(CsvPageCache.PageSize);
            while (record is not null && records.Count < CsvPageCache.PageSize)
            {
                records.Add(record);
                HasTruncatedCells |= record.WasTruncated;
                record = reader.ReadRecord(cancellationToken);
            }
            return records.ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsurePageAsync(long dataRow, CancellationToken cancellationToken = default)
    {
        long page = dataRow / CsvPageCache.PageSize;
        if (!Cache.MarkLoading(page))
            return;
        try
        {
            Cache.Store(page, await LoadPageAsync(page, cancellationToken).ConfigureAwait(false));
        }
        catch
        {
            Cache.MarkFailed(page);
            throw;
        }
    }

    public bool HasChanged() => !Fingerprint.StillMatches();

    private static string[] BuildColumnNames(string[]? header, int count)
    {
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            string baseName = header is not null && i < header.Length && !string.IsNullOrWhiteSpace(header[i]) ? header[i].Trim() : $"Columna {i + 1}";
            if (!used.TryAdd(baseName, 1))
            {
                int suffix = ++used[baseName];
                names[i] = $"{baseName} ({suffix})";
            }
            else
            {
                names[i] = baseName;
            }
        }
        return names;
    }

    private static string CreatePreview(string value, int foundAt, int queryLength)
    {
        const int radius = 70;
        int start = Math.Max(0, foundAt - radius);
        int end = Math.Min(value.Length, foundAt + queryLength + radius);
        string preview = value[start..end].Replace('\r', ' ').Replace('\n', ' ');
        return (start > 0 ? "…" : string.Empty) + preview + (end < value.Length ? "…" : string.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _disposeCancellation.Cancel();
        await CancelBackgroundScanAsync().ConfigureAwait(false);
        await _scanGate.WaitAsync().ConfigureAwait(false);
        _scanGate.Release();
        Cache.Clear();
        _disposeCancellation.Dispose();
        _scanGate.Dispose();
    }
}
