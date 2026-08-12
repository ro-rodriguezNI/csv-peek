using System.Text;

namespace CsvPeek.Core;

public sealed record CsvDialect(char Delimiter, Encoding Encoding, int BomLength, bool FirstRowIsHeader = true)
{
    public string EncodingName => Encoding.WebName;

    public CsvDialect WithHeader(bool value) => this with { FirstRowIsHeader = value };
}

public sealed record CsvFileFingerprint(string FullPath, long Length, long LastWriteUtcTicks)
{
    public static CsvFileFingerprint Create(string path)
    {
        var info = new FileInfo(path);
        return new CsvFileFingerprint(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks);
    }

    public bool StillMatches()
    {
        var info = new FileInfo(FullPath);
        return info.Exists && info.Length == Length && info.LastWriteTimeUtc.Ticks == LastWriteUtcTicks;
    }
}

public sealed record CsvRecord(long Number, long StartOffset, string[] Fields, bool WasTruncated, int[]? MatchedColumns = null);

public sealed record SearchMatch(long DataRow, int ColumnIndex, string ColumnName, string Preview, long RecordOffset);

public sealed record ScanProgress(long RecordsScanned, long BytesScanned, long TotalBytes, long MatchesFound)
{
    public int Percentage => TotalBytes <= 0 ? 0 : (int)Math.Clamp(BytesScanned * 100L / TotalBytes, 0, 100);
}
