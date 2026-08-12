using System.Security.Cryptography;
using System.Text;

namespace CsvPeek.Core;

public sealed class CsvIndexStore
{
    private const int FormatVersion = 1;
    private static readonly byte[] Magic = "CSVPEEKIDX"u8.ToArray();
    private readonly string _directory;

    public CsvIndexStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CSV Peek", "Indexes");
    }

    public string GetIndexPath(CsvFileFingerprint fingerprint)
    {
        string normalized = Path.GetFullPath(fingerprint.FullPath).ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(_directory, $"{hash}.cpi");
    }

    public async Task SaveAsync(CsvFileFingerprint fingerprint, CsvDialect dialect, CsvSparseIndex index, CancellationToken cancellationToken = default)
    {
        if (!index.IsComplete || !fingerprint.StillMatches())
            return;

        Directory.CreateDirectory(_directory);
        string target = GetIndexPath(fingerprint);
        string temporary = target + ".tmp";
        long[] offsets = index.SnapshotOffsets();

        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
        await using (var writer = new BinaryWriterStream(stream))
        {
            await writer.WriteAsync(Magic, cancellationToken);
            await writer.WriteAsync(FormatVersion, cancellationToken);
            await writer.WriteAsync(fingerprint.Length, cancellationToken);
            await writer.WriteAsync(fingerprint.LastWriteUtcTicks, cancellationToken);
            await writer.WriteAsync((int)dialect.Delimiter, cancellationToken);
            await writer.WriteAsync(dialect.BomLength, cancellationToken);
            await writer.WriteAsync(dialect.EncodingName, cancellationToken);
            await writer.WriteAsync(index.Interval, cancellationToken);
            await writer.WriteAsync(index.RecordCount, cancellationToken);
            await writer.WriteAsync(offsets.Length, cancellationToken);
            foreach (long offset in offsets)
                await writer.WriteAsync(offset, cancellationToken);
        }

        File.Move(temporary, target, true);
    }

    public async Task<bool> TryLoadAsync(CsvFileFingerprint fingerprint, CsvDialect dialect, CsvSparseIndex index, CancellationToken cancellationToken = default)
    {
        string path = GetIndexPath(fingerprint);
        if (!File.Exists(path))
            return false;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
            await using var reader = new BinaryReaderStream(stream);
            byte[] magic = await reader.ReadBytesAsync(Magic.Length, cancellationToken);
            if (!magic.AsSpan().SequenceEqual(Magic) || await reader.ReadInt32Async(cancellationToken) != FormatVersion)
                return false;
            if (await reader.ReadInt64Async(cancellationToken) != fingerprint.Length ||
                await reader.ReadInt64Async(cancellationToken) != fingerprint.LastWriteUtcTicks ||
                await reader.ReadInt32Async(cancellationToken) != dialect.Delimiter ||
                await reader.ReadInt32Async(cancellationToken) != dialect.BomLength ||
                !string.Equals(await reader.ReadStringAsync(cancellationToken), dialect.EncodingName, StringComparison.OrdinalIgnoreCase) ||
                await reader.ReadInt32Async(cancellationToken) != index.Interval)
                return false;

            long recordCount = await reader.ReadInt64Async(cancellationToken);
            int count = await reader.ReadInt32Async(cancellationToken);
            if (count <= 0 || count > 10_000_000)
                return false;
            long[] offsets = new long[count];
            for (int i = 0; i < count; i++)
                offsets[i] = await reader.ReadInt64Async(cancellationToken);
            index.Load(offsets, recordCount);
            return true;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or DecoderFallbackException)
        {
            return false;
        }
    }

    private sealed class BinaryWriterStream(Stream stream) : IAsyncDisposable
    {
        private readonly byte[] _buffer = new byte[8];
        public ValueTask WriteAsync(byte[] bytes, CancellationToken ct) => stream.WriteAsync(bytes, ct);
        public async ValueTask WriteAsync(int value, CancellationToken ct) { BitConverter.TryWriteBytes(_buffer.AsSpan(0, 4), value); await stream.WriteAsync(_buffer.AsMemory(0, 4), ct); }
        public async ValueTask WriteAsync(long value, CancellationToken ct) { BitConverter.TryWriteBytes(_buffer, value); await stream.WriteAsync(_buffer, ct); }
        public async ValueTask WriteAsync(string value, CancellationToken ct) { byte[] bytes = Encoding.UTF8.GetBytes(value); await WriteAsync(bytes.Length, ct); await stream.WriteAsync(bytes, ct); }
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }

    private sealed class BinaryReaderStream(Stream stream) : IAsyncDisposable
    {
        private readonly byte[] _buffer = new byte[8];
        public async Task<byte[]> ReadBytesAsync(int count, CancellationToken ct) { byte[] result = new byte[count]; await stream.ReadExactlyAsync(result, ct); return result; }
        public async Task<int> ReadInt32Async(CancellationToken ct) { await stream.ReadExactlyAsync(_buffer.AsMemory(0, 4), ct); return BitConverter.ToInt32(_buffer, 0); }
        public async Task<long> ReadInt64Async(CancellationToken ct) { await stream.ReadExactlyAsync(_buffer, ct); return BitConverter.ToInt64(_buffer, 0); }
        public async Task<string> ReadStringAsync(CancellationToken ct) { int count = await ReadInt32Async(ct); if (count < 0 || count > 4096) throw new IOException("Índice no válido."); return Encoding.UTF8.GetString(await ReadBytesAsync(count, ct)); }
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}

