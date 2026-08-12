using System.Text;

namespace CsvPeek.Core;

public static class CsvDialectDetector
{
    public const int MaxSampleBytes = 256 * 1024;

    static CsvDialectDetector() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static async Task<CsvDialect> DetectAsync(string path, CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[Math.Min(MaxSampleBytes, checked((int)Math.Min(new FileInfo(path).Length, MaxSampleBytes)))];
        int read;
        await using (var stream = OpenShared(path, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        var (encoding, bomLength) = DetectEncoding(buffer.AsSpan(0, read));
        string text = encoding.GetString(buffer, bomLength, Math.Max(0, read - bomLength));
        char delimiter = DetectDelimiter(text);
        return new CsvDialect(delimiter, encoding, bomLength, true);
    }

    public static (Encoding Encoding, int BomLength) DetectEncoding(ReadOnlySpan<byte> sample)
    {
        if (sample.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            return (new UTF8Encoding(false, true), 3);
        if (sample.StartsWith(new byte[] { 0xFF, 0xFE }))
            return (new UnicodeEncoding(false, false, true), 2);
        if (sample.StartsWith(new byte[] { 0xFE, 0xFF }))
            return (new UnicodeEncoding(true, false, true), 2);

        try
        {
            var utf8 = new UTF8Encoding(false, true);
            _ = utf8.GetString(sample);
            return (utf8, 0);
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.GetEncoding(1252), 0);
        }
    }

    public static char DetectDelimiter(string sample)
    {
        char[] candidates = [',', ';', '\t'];
        char best = ',';
        double bestScore = double.MinValue;

        foreach (char candidate in candidates)
        {
            var counts = CountColumns(sample, candidate, 64);
            if (counts.Count == 0)
                continue;

            var useful = counts.Where(value => value > 1).ToArray();
            if (useful.Length == 0)
                continue;

            int mode = useful.GroupBy(value => value).OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key).First().Key;
            int consistent = useful.Count(value => value == mode);
            double score = consistent * 1000d + mode * 10d - (counts.Count - consistent) * 25d;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static List<int> CountColumns(string sample, char delimiter, int maxRecords)
    {
        var result = new List<int>(maxRecords);
        bool quoted = false;
        int columns = 1;
        for (int i = 0; i < sample.Length && result.Count < maxRecords; i++)
        {
            char c = sample[i];
            if (c == '"')
            {
                if (quoted && i + 1 < sample.Length && sample[i + 1] == '"')
                    i++;
                else
                    quoted = !quoted;
            }
            else if (!quoted && c == delimiter)
            {
                columns++;
            }
            else if (!quoted && (c == '\r' || c == '\n'))
            {
                if (c == '\r' && i + 1 < sample.Length && sample[i + 1] == '\n')
                    i++;
                result.Add(columns);
                columns = 1;
            }
        }

        return result;
    }

    internal static FileStream OpenShared(string path, FileOptions options = FileOptions.SequentialScan) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, options);
}

