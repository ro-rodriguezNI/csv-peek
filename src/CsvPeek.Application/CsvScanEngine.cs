using CsvPeek.Core;

namespace CsvPeek.Application;

internal sealed class CsvScanEngine(ICsvRecordSourceFactory sourceFactory)
{
    public Task<ScanProgress> ScanAsync(
        string path,
        CsvDialect dialect,
        CsvFileFingerprint fingerprint,
        string? query,
        Func<CsvRecord, bool> recordObserver,
        Func<long> matchCount,
        Action<ScanProgress> progressObserver,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            long recordCount = 0;
            int fieldLimit = query is null ? 0 : CsvLimits.DefaultMaxFieldChars;
            using var reader = sourceFactory.Open(
                path,
                dialect,
                maxFieldChars: fieldLimit,
                captureFields: query is not null,
                matchQuery: query);

            CsvRecord? record;
            while ((record = reader.ReadRecord(cancellationToken)) is not null)
            {
                recordCount = record.Number + 1;
                bool flushRequested = recordObserver(record);
                if (recordCount % 4096 == 0 || flushRequested)
                {
                    progressObserver(new ScanProgress(
                        recordCount,
                        reader.BytePosition,
                        fingerprint.Length,
                        matchCount()));
                }
            }

            return new ScanProgress(recordCount, fingerprint.Length, fingerprint.Length, matchCount());
        }, cancellationToken);
    }
}
