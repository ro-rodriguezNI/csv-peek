using CsvPeek.Core;

namespace CsvPeek.Application;

internal sealed class CsvPageCache
{
    private readonly object _gate = new();
    private readonly Dictionary<long, CacheEntry> _pages = [];
    private readonly HashSet<long> _loading = [];
    private readonly long _maxEstimatedBytes;
    private long _estimatedBytes;

    public CsvPageCache(long maxEstimatedBytes = 128L * 1024 * 1024) => _maxEstimatedBytes = maxEstimatedBytes;

    public event EventHandler<long>? PageLoaded;

    public CsvRecord? TryGet(long dataRow)
    {
        long page = dataRow / CsvPaging.PageSize;
        int index = (int)(dataRow % CsvPaging.PageSize);
        lock (_gate)
        {
            if (!_pages.TryGetValue(page, out var entry))
                return null;
            entry.LastAccess = Environment.TickCount64;
            return index < entry.Records.Length ? entry.Records[index] : null;
        }
    }

    public void Seed(IReadOnlyList<CsvRecord> records)
    {
        lock (_gate)
        {
            _pages.Clear();
            _loading.Clear();
            _estimatedBytes = 0;
            AddPage(0, records.Take(CsvPaging.PageSize).ToArray());
        }
    }

    public bool MarkLoading(long page)
    {
        lock (_gate)
            return !_pages.ContainsKey(page) && _loading.Add(page);
    }

    public void Store(long page, CsvRecord[] records)
    {
        lock (_gate)
        {
            _loading.Remove(page);
            AddPage(page, records);
            EvictIfNeeded(page);
        }
        PageLoaded?.Invoke(this, page);
    }

    public void MarkFailed(long page)
    {
        lock (_gate)
            _loading.Remove(page);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pages.Clear();
            _loading.Clear();
            _estimatedBytes = 0;
        }
    }

    private void AddPage(long page, CsvRecord[] records)
    {
        long size = records.Sum(EstimateSize);
        if (_pages.Remove(page, out var previous))
            _estimatedBytes -= previous.EstimatedBytes;
        _pages[page] = new CacheEntry(records, size, Environment.TickCount64);
        _estimatedBytes += size;
    }

    private void EvictIfNeeded(long protectedPage)
    {
        while (_estimatedBytes > _maxEstimatedBytes && _pages.Count > 1)
        {
            var candidate = _pages.Where(pair => pair.Key != protectedPage).MinBy(pair => pair.Value.LastAccess);
            if (!_pages.Remove(candidate.Key, out var removed))
                break;
            _estimatedBytes -= removed.EstimatedBytes;
        }
    }

    private static long EstimateSize(CsvRecord record) => 64L + record.Fields.Sum(value => 24L + value.Length * 2L);

    private sealed class CacheEntry(CsvRecord[] records, long estimatedBytes, long lastAccess)
    {
        public CsvRecord[] Records { get; } = records;
        public long EstimatedBytes { get; } = estimatedBytes;
        public long LastAccess { get; set; } = lastAccess;
    }
}
