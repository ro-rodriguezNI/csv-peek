using System.Text;

namespace CsvPeek.Core;

public static class CsvLimits
{
    public const int DefaultMaxFieldChars = 256 * 1024;
}

public static class CsvPaging
{
    public const int PageSize = 256;
}

public sealed record CsvDialect(char Delimiter, Encoding Encoding, int BomLength, bool FirstRowIsHeader = true)
{
    public string EncodingName => Encoding.WebName;

    public CsvDialect WithHeader(bool value) => this with { FirstRowIsHeader = value };
}

public sealed record CsvFileFingerprint(string FullPath, long Length, long LastWriteUtcTicks);

public sealed record CsvRecord(long Number, long StartOffset, string[] Fields, bool WasTruncated, int[]? MatchedColumns = null);

public sealed record SearchMatch(long DataRow, int ColumnIndex, string ColumnName, string Preview, long RecordOffset);

public sealed record ScanProgress(long RecordsScanned, long BytesScanned, long TotalBytes, long MatchesFound)
{
    public int Percentage => TotalBytes <= 0 ? 0 : (int)Math.Clamp(BytesScanned * 100L / TotalBytes, 0, 100);
}
