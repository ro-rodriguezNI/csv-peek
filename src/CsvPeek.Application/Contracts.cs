using CsvPeek.Core;
using System.Text;

namespace CsvPeek.Application;

public interface ICsvRecordReader : IDisposable
{
    long BytePosition { get; }
    CsvRecord? ReadRecord(CancellationToken cancellationToken = default);
}

public interface ICsvRecordSourceFactory
{
    Task<CsvDialect> DetectDialectAsync(string path, CancellationToken cancellationToken = default);
    CsvFileFingerprint GetFingerprint(string path);
    bool StillMatches(CsvFileFingerprint fingerprint);
    int GetBomLength(string path, Encoding encoding);
    ICsvRecordReader Open(
        string path,
        CsvDialect dialect,
        long startOffset = -1,
        long firstRecordNumber = 0,
        int maxFieldChars = CsvLimits.DefaultMaxFieldChars,
        bool captureFields = true,
        string? matchQuery = null);
}

public interface ICsvIndexStore
{
    Task SaveAsync(
        CsvFileFingerprint fingerprint,
        CsvDialect dialect,
        CsvSparseIndex index,
        CancellationToken cancellationToken = default);

    Task<bool> TryLoadAsync(
        CsvFileFingerprint fingerprint,
        CsvDialect dialect,
        CsvSparseIndex index,
        CancellationToken cancellationToken = default);
}

public interface ICsvDocumentSession : IAsyncDisposable
{
    string Path { get; }
    CsvDialect Dialect { get; }
    CsvFileFingerprint Fingerprint { get; }
    IReadOnlyList<string> ColumnNames { get; }
    long KnownDataRows { get; }
    bool IsIndexComplete { get; }
    bool HasTruncatedCells { get; }

    event EventHandler<ScanProgress>? IndexProgress;
    event EventHandler<long>? PageLoaded;

    CsvRecord? TryGetRecord(long dataRow);
    Task EnsurePageAsync(long dataRow, CancellationToken cancellationToken = default);
    void StartBackgroundIndexing();
    Task CancelBackgroundIndexingAsync();
    Task SearchAsync(
        string query,
        IProgress<IReadOnlyList<SearchMatch>> matches,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task ReconfigureAsync(
        char delimiter,
        Encoding encoding,
        bool firstRowIsHeader,
        CancellationToken cancellationToken = default);
    bool HasChanged();
}

public interface ICsvDocumentSessionFactory
{
    Task<ICsvDocumentSession> OpenAsync(string path, CancellationToken cancellationToken = default);
}
