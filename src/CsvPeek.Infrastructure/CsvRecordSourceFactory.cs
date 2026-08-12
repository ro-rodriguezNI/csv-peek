using CsvPeek.Application;
using CsvPeek.Core;
using System.Text;

namespace CsvPeek.Infrastructure;

public sealed class CsvRecordSourceFactory : ICsvRecordSourceFactory
{
    public Task<CsvDialect> DetectDialectAsync(string path, CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("No se encontró el archivo CSV.", fullPath);
        return CsvDialectDetector.DetectAsync(fullPath, cancellationToken);
    }

    public CsvFileFingerprint GetFingerprint(string path)
    {
        var info = new FileInfo(Path.GetFullPath(path));
        if (!info.Exists)
            throw new FileNotFoundException("No se encontró el archivo CSV.", info.FullName);
        return new CsvFileFingerprint(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks);
    }

    public bool StillMatches(CsvFileFingerprint fingerprint)
    {
        var info = new FileInfo(fingerprint.FullPath);
        return info.Exists && info.Length == fingerprint.Length && info.LastWriteTimeUtc.Ticks == fingerprint.LastWriteUtcTicks;
    }

    public int GetBomLength(string path, Encoding encoding)
    {
        Span<byte> bytes = stackalloc byte[3];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int read = stream.Read(bytes);
        if (encoding.CodePage == Encoding.UTF8.CodePage && read >= 3 && bytes[..3].SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }))
            return 3;
        if (encoding.CodePage is 1200 or 1201 && read >= 2 &&
            (bytes[..2].SequenceEqual(new byte[] { 0xFF, 0xFE }) || bytes[..2].SequenceEqual(new byte[] { 0xFE, 0xFF })))
            return 2;
        return 0;
    }

    public ICsvRecordReader Open(
        string path,
        CsvDialect dialect,
        long startOffset = -1,
        long firstRecordNumber = 0,
        int maxFieldChars = CsvLimits.DefaultMaxFieldChars,
        bool captureFields = true,
        string? matchQuery = null) =>
        new CsvRecordReader(path, dialect, startOffset, firstRecordNumber, maxFieldChars, captureFields, matchQuery);
}
