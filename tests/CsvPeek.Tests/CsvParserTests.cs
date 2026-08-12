using System.Text;
using CsvPeek.Core;

namespace CsvPeek.Tests;

public sealed class CsvParserTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CsvPeek.Tests", Guid.NewGuid().ToString("N"));

    public CsvParserTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void ReadsQuotedDelimiterEscapedQuoteAndMultilineField()
    {
        string path = Write("quoted.csv", "id,note\r\n1,\"hello, \"\"world\"\"\r\nsecond line\"\r\n2,end\r\n", new UTF8Encoding(false));
        var dialect = new CsvDialect(',', new UTF8Encoding(false, true), 0);

        using var reader = new CsvRecordReader(path, dialect);
        var header = reader.ReadRecord();
        var first = reader.ReadRecord();
        var second = reader.ReadRecord();

        Assert.Equal(["id", "note"], header!.Fields);
        Assert.Equal(["1", "hello, \"world\"\r\nsecond line"], first!.Fields);
        Assert.Equal(["2", "end"], second!.Fields);
        Assert.Null(reader.ReadRecord());
    }

    [Theory]
    [InlineData("a,b,c\n1,2,3\n", ',')]
    [InlineData("a;b;c\n1;2;3\n", ';')]
    [InlineData("a\tb\tc\n1\t2\t3\n", '\t')]
    public async Task DetectsDelimiter(string content, char expected)
    {
        string path = Write("detect.csv", content, new UTF8Encoding(false));
        var dialect = await CsvDialectDetector.DetectAsync(path);
        Assert.Equal(expected, dialect.Delimiter);
    }

    [Fact]
    public async Task DetectsUtf16BomAndPreservesOffsetsWithEmoji()
    {
        string path = Write("unicode.csv", "name;value\r\n😀;dos\r\nfin;tres\r\n", new UnicodeEncoding(false, true));
        var dialect = await CsvDialectDetector.DetectAsync(path);
        Assert.Equal(Encoding.Unicode.CodePage, dialect.Encoding.CodePage);
        Assert.Equal(2, dialect.BomLength);

        using var reader = new CsvRecordReader(path, dialect);
        var records = new[] { reader.ReadRecord()!, reader.ReadRecord()!, reader.ReadRecord()! };
        Assert.Equal("😀", records[1].Fields[0]);
        Assert.Equal(records[2].StartOffset, dialect.BomLength + dialect.Encoding.GetByteCount("name;value\r\n😀;dos\r\n"));
    }

    [Fact]
    public async Task FallsBackToWindows1252WhenUtf8IsInvalid()
    {
        string path = Write("ansi.csv", "nombre;ciudad\r\nJosé;León\r\n", Encoding.GetEncoding(1252));
        var dialect = await CsvDialectDetector.DetectAsync(path);
        Assert.Equal(1252, dialect.Encoding.CodePage);
        using var reader = new CsvRecordReader(path, dialect);
        _ = reader.ReadRecord();
        Assert.Equal("José", reader.ReadRecord()!.Fields[0]);
    }

    [Fact]
    public void TruncatesHugeDisplayFieldWithoutBreakingFollowingRecords()
    {
        string path = Write("large.csv", $"value\n{new string('x', 200)}\nnext\n", new UTF8Encoding(false));
        var dialect = new CsvDialect(',', new UTF8Encoding(false, true), 0);
        using var reader = new CsvRecordReader(path, dialect, maxFieldChars: 32);
        _ = reader.ReadRecord();
        var large = reader.ReadRecord();
        var next = reader.ReadRecord();
        Assert.True(large!.WasTruncated);
        Assert.Equal(32, large.Fields[0].Length);
        Assert.Equal("next", next!.Fields[0]);
    }

    private string Write(string name, string content, Encoding encoding)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, content, encoding);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}

