using CsvPeek.Core;

namespace CsvPeek.Application;

internal sealed class CsvPageProvider(
    string path,
    CsvDialect dialect,
    CsvSparseIndex index,
    ICsvRecordSourceFactory sourceFactory,
    Action<CsvRecord> recordObserved)
{
    private readonly CsvPageCache _cache = new();

    public event EventHandler<long>? PageLoaded
    {
        add => _cache.PageLoaded += value;
        remove => _cache.PageLoaded -= value;
    }

    public CsvRecord? TryGet(long dataRow) => _cache.TryGet(dataRow);

    public Task<InitialPage> LoadInitialPageAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var reader = sourceFactory.Open(path, dialect);
            var rawRecords = new List<CsvRecord>(CsvPaging.PageSize + 1);
            while (rawRecords.Count < CsvPaging.PageSize + (dialect.FirstRowIsHeader ? 1 : 0))
            {
                var record = reader.ReadRecord(cancellationToken);
                if (record is null)
                    break;
                rawRecords.Add(record);
                index.Observe(record);
                recordObserved(record);
            }

            CsvRecord? header = dialect.FirstRowIsHeader && rawRecords.Count > 0 ? rawRecords[0] : null;
            var data = header is null ? rawRecords : rawRecords.Skip(1).ToList();
            int columnCount = Math.Max(header?.Fields.Length ?? 0, data.Count == 0 ? 0 : data.Max(record => record.Fields.Length));
            string[] columnNames = BuildColumnNames(header?.Fields, columnCount);
            _cache.Seed(data);

            bool isComplete = reader.ReadRecord(cancellationToken) is null;
            if (isComplete)
                index.Complete(rawRecords.Count);
            return new InitialPage(columnNames);
        }, cancellationToken);
    }

    public async Task EnsurePageAsync(long dataRow, CancellationToken cancellationToken)
    {
        long page = dataRow / CsvPaging.PageSize;
        if (!_cache.MarkLoading(page))
            return;
        try
        {
            _cache.Store(page, await LoadPageAsync(page, cancellationToken).ConfigureAwait(false));
        }
        catch
        {
            _cache.MarkFailed(page);
            throw;
        }
    }

    public void Clear() => _cache.Clear();

    private Task<CsvRecord[]> LoadPageAsync(long dataPage, CancellationToken cancellationToken)
    {
        long firstDataRow = dataPage * CsvPaging.PageSize;
        long firstRawRecord = firstDataRow + (dialect.FirstRowIsHeader ? 1 : 0);
        var (checkpointRecord, checkpointOffset) = index.FindCheckpoint(firstRawRecord);

        return Task.Run(() =>
        {
            using var reader = sourceFactory.Open(path, dialect, checkpointOffset, checkpointRecord);
            CsvRecord? record;
            do
            {
                record = reader.ReadRecord(cancellationToken);
            } while (record is not null && record.Number < firstRawRecord);

            var records = new List<CsvRecord>(CsvPaging.PageSize);
            while (record is not null && records.Count < CsvPaging.PageSize)
            {
                records.Add(record);
                recordObserved(record);
                record = reader.ReadRecord(cancellationToken);
            }
            return records.ToArray();
        }, cancellationToken);
    }

    private static string[] BuildColumnNames(string[]? header, int count)
    {
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            string baseName = header is not null && i < header.Length && !string.IsNullOrWhiteSpace(header[i])
                ? header[i].Trim()
                : $"Columna {i + 1}";
            if (!used.TryAdd(baseName, 1))
                names[i] = $"{baseName} ({++used[baseName]})";
            else
                names[i] = baseName;
        }
        return names;
    }

    internal sealed record InitialPage(string[] ColumnNames);
}
