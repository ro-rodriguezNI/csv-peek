using System.Text;

namespace CsvPeek.Core;

public sealed class CsvRecordReader : IDisposable
{
    public const int DefaultMaxFieldChars = 256 * 1024;

    private readonly FileStream _stream;
    private readonly StreamReader _reader;
    private readonly CsvDialect _dialect;
    private readonly int _maxFieldChars;
    private readonly bool _captureFields;
    private readonly string? _matchQuery;
    private readonly int[]? _matchPrefix;
    private long _nextRecordNumber;
    private long _bytePosition;
    private char? _pendingHighSurrogate;
    private int _peeked = -2;
    private bool _disposed;

    public CsvRecordReader(string path, CsvDialect dialect, long startOffset = -1, long firstRecordNumber = 0, int maxFieldChars = DefaultMaxFieldChars, bool captureFields = true, string? matchQuery = null)
    {
        _dialect = dialect;
        _maxFieldChars = maxFieldChars;
        _captureFields = captureFields;
        _matchQuery = string.IsNullOrEmpty(matchQuery) ? null : new string(matchQuery.Select(char.ToUpperInvariant).ToArray());
        _matchPrefix = _matchQuery is null ? null : BuildPrefixTable(_matchQuery);
        _stream = CsvDialectDetector.OpenShared(path);
        long initialOffset = startOffset < 0 ? dialect.BomLength : startOffset;
        _stream.Position = initialOffset;
        _bytePosition = initialOffset;
        _nextRecordNumber = firstRecordNumber;
        _reader = new StreamReader(_stream, dialect.Encoding, false, 1024 * 1024, true);
    }

    public long BytePosition => _bytePosition;

    public CsvRecord? ReadRecord(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        long startOffset = _bytePosition;
        var fields = new List<string>(16);
        var field = new StringBuilder(128);
        bool quoted = false;
        bool anyInput = false;
        bool fieldHasContent = false;
        bool truncated = false;
        int charsSinceCancellation = 0;
        int fieldIndex = 0;
        int matchedLength = 0;
        List<int>? matchedColumns = _matchQuery is null ? null : [];

        while (true)
        {
            int value = ReadChar();
            if (value < 0)
            {
                FlushPendingSurrogate();
                if (!anyInput && fields.Count == 0 && !fieldHasContent)
                    return null;
                if (_captureFields)
                    fields.Add(field.ToString());
                return new CsvRecord(_nextRecordNumber++, startOffset, fields.ToArray(), truncated, matchedColumns?.ToArray());
            }

            anyInput = true;
            char c = (char)value;
            ConsumeBytePosition(c);

            if (++charsSinceCancellation >= 16_384)
            {
                cancellationToken.ThrowIfCancellationRequested();
                charsSinceCancellation = 0;
            }

            if (c == '"')
            {
                if (quoted && PeekChar() == '"')
                {
                    char escaped = (char)ReadChar();
                    ConsumeBytePosition(escaped);
                    Append(field, '"', ref truncated);
                    MatchCharacter('"', fieldIndex, ref matchedLength, matchedColumns);
                    fieldHasContent = true;
                }
                else if (quoted || !fieldHasContent)
                {
                    quoted = !quoted;
                }
                else
                {
                    Append(field, c, ref truncated);
                    MatchCharacter(c, fieldIndex, ref matchedLength, matchedColumns);
                    fieldHasContent = true;
                }
            }
            else if (!quoted && c == _dialect.Delimiter)
            {
                if (_captureFields)
                    fields.Add(field.ToString());
                field.Clear();
                fieldHasContent = false;
                fieldIndex++;
                matchedLength = 0;
            }
            else if (!quoted && (c == '\r' || c == '\n'))
            {
                if (c == '\r' && PeekChar() == '\n')
                {
                    char lineFeed = (char)ReadChar();
                    ConsumeBytePosition(lineFeed);
                }
                FlushPendingSurrogate();
                if (_captureFields)
                    fields.Add(field.ToString());
                return new CsvRecord(_nextRecordNumber++, startOffset, fields.ToArray(), truncated, matchedColumns?.ToArray());
            }
            else
            {
                Append(field, c, ref truncated);
                MatchCharacter(c, fieldIndex, ref matchedLength, matchedColumns);
                fieldHasContent = true;
            }
        }
    }

    private void Append(StringBuilder builder, char value, ref bool truncated)
    {
        if (_captureFields && builder.Length < _maxFieldChars)
            builder.Append(value);
        else if (_captureFields)
            truncated = true;
    }

    private void MatchCharacter(char value, int fieldIndex, ref int matchedLength, List<int>? matchedColumns)
    {
        if (_matchQuery is null || _matchPrefix is null || matchedColumns is null || (matchedColumns.Count > 0 && matchedColumns[^1] == fieldIndex))
            return;

        char candidate = char.ToUpperInvariant(value);
        while (matchedLength > 0 && candidate != _matchQuery[matchedLength])
            matchedLength = _matchPrefix[matchedLength - 1];
        if (candidate == _matchQuery[matchedLength])
            matchedLength++;
        if (matchedLength == _matchQuery.Length)
        {
            matchedColumns.Add(fieldIndex);
            matchedLength = _matchPrefix[matchedLength - 1];
        }
    }

    private static int[] BuildPrefixTable(string query)
    {
        var prefix = new int[query.Length];
        for (int i = 1, matched = 0; i < query.Length; i++)
        {
            while (matched > 0 && query[i] != query[matched])
                matched = prefix[matched - 1];
            if (query[i] == query[matched])
                matched++;
            prefix[i] = matched;
        }
        return prefix;
    }

    private int ReadChar()
    {
        if (_peeked != -2)
        {
            int result = _peeked;
            _peeked = -2;
            return result;
        }
        return _reader.Read();
    }

    private int PeekChar()
    {
        if (_peeked == -2)
            _peeked = _reader.Read();
        return _peeked;
    }

    private void ConsumeBytePosition(char value)
    {
        Span<char> chars = stackalloc char[2];
        if (_pendingHighSurrogate is { } high)
        {
            if (char.IsLowSurrogate(value))
            {
                chars[0] = high;
                chars[1] = value;
                _bytePosition += _dialect.Encoding.GetByteCount(chars);
                _pendingHighSurrogate = null;
                return;
            }

            chars[0] = high;
            _bytePosition += _dialect.Encoding.GetByteCount(chars[..1]);
            _pendingHighSurrogate = null;
        }

        if (char.IsHighSurrogate(value))
        {
            _pendingHighSurrogate = value;
            return;
        }

        chars[0] = value;
        _bytePosition += _dialect.Encoding.GetByteCount(chars[..1]);
    }

    private void FlushPendingSurrogate()
    {
        if (_pendingHighSurrogate is not { } value)
            return;
        Span<char> chars = stackalloc char[1];
        chars[0] = value;
        _bytePosition += _dialect.Encoding.GetByteCount(chars);
        _pendingHighSurrogate = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _reader.Dispose();
        _stream.Dispose();
    }
}
