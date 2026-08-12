using CsvPeek.Core;

namespace CsvPeek.Application;

internal sealed class CsvSearchService(
    CsvDialect dialect,
    IReadOnlyList<string> columnNames,
    string query,
    IProgress<IReadOnlyList<SearchMatch>> matches,
    IProgress<ScanProgress>? progress)
{
    private readonly List<SearchMatch> _batch = new(100);
    private long _matchCount;

    public long MatchCount => _matchCount;

    public bool Observe(CsvRecord record)
    {
        bool isHeader = dialect.FirstRowIsHeader && record.Number == 0;
        if (isHeader)
            return false;

        foreach (int column in record.MatchedColumns ?? [])
        {
            string value = column < record.Fields.Length ? record.Fields[column] : string.Empty;
            int foundAt = value.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            _matchCount++;
            if (_matchCount <= 10_000)
            {
                _batch.Add(new SearchMatch(
                    record.Number - (dialect.FirstRowIsHeader ? 1 : 0),
                    column,
                    column < columnNames.Count ? columnNames[column] : $"Columna {column + 1}",
                    foundAt >= 0 ? CreatePreview(value, foundAt, query.Length) : "Coincidencia en un valor grande (vista previa truncada)",
                    record.StartOffset));
            }
        }

        return _batch.Count >= 100;
    }

    public void Report(ScanProgress scanProgress)
    {
        if (_batch.Count > 0)
        {
            matches.Report(_batch.ToArray());
            _batch.Clear();
        }
        progress?.Report(scanProgress with { MatchesFound = _matchCount });
    }

    private static string CreatePreview(string value, int foundAt, int queryLength)
    {
        const int radius = 70;
        int start = Math.Max(0, foundAt - radius);
        int end = Math.Min(value.Length, foundAt + queryLength + radius);
        string preview = value[start..end].Replace('\r', ' ').Replace('\n', ' ');
        return (start > 0 ? "…" : string.Empty) + preview + (end < value.Length ? "…" : string.Empty);
    }
}
