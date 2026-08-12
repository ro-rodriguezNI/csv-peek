using System.Text;
using CsvPeek.Core;

namespace CsvPeek.Tests;

public sealed class CsvDocumentTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CsvPeek.Tests", Guid.NewGuid().ToString("N"));

    public CsvDocumentTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task OpensPagesIndexesSearchesAndReloadsIndex()
    {
        string path = Path.Combine(_directory, "many.csv");
        var text = new StringBuilder("id,name,note\n");
        for (int i = 0; i < 5_000; i++)
            text.Append(i).Append(",Name ").Append(i).Append(i == 4321 ? ",needle here\n" : ",ordinary\n");
        await File.WriteAllTextAsync(path, text.ToString(), new UTF8Encoding(false));

        string indexDirectory = Path.Combine(_directory, "indexes");
        var store = new CsvIndexStore(indexDirectory);
        await using (var document = await CsvDocument.OpenAsync(path, store))
        {
            var results = new List<SearchMatch>();
            var reporter = new ImmediateProgress<IReadOnlyList<SearchMatch>>(batch => results.AddRange(batch));
            await document.SearchAsync("NEEDLE", reporter);
            Assert.True(document.Index.IsComplete);
            Assert.Equal(5_000, document.KnownDataRows);
            Assert.Single(results);
            Assert.Equal(4321, results[0].DataRow);

            var page = await document.LoadPageAsync(4321 / CsvPageCache.PageSize);
            Assert.Contains(page, row => row.Fields[0] == "4321");
        }

        await using var reopened = await CsvDocument.OpenAsync(path, store);
        Assert.True(reopened.Index.IsComplete);
        Assert.Equal(5_000, reopened.KnownDataRows);
    }

    [Fact]
    public async Task InvalidatesPersistedIndexWhenFileChanges()
    {
        string path = Path.Combine(_directory, "changing.csv");
        await File.WriteAllTextAsync(path, "id,name\n1,one\n", new UTF8Encoding(false));
        var store = new CsvIndexStore(Path.Combine(_directory, "indexes"));
        await using (var first = await CsvDocument.OpenAsync(path, store))
        {
            var sink = new ImmediateProgress<IReadOnlyList<SearchMatch>>(_ => { });
            await first.SearchAsync("one", sink);
        }

        await File.AppendAllTextAsync(path, "2,two\n", new UTF8Encoding(false));
        await using var changed = await CsvDocument.OpenAsync(path, store);
        Assert.Equal(2, changed.KnownDataRows);
    }

    [Fact]
    public async Task SearchFindsTextBeyondDisplayTruncationLimit()
    {
        string path = Path.Combine(_directory, "datos á enormes.csv");
        string huge = new string('x', CsvRecordReader.DefaultMaxFieldChars + 10_000) + " hidden-needle";
        await File.WriteAllTextAsync(path, $"id,note\n1,\"{huge}\"\n", new UTF8Encoding(false));
        await using var document = await CsvDocument.OpenAsync(path, new CsvIndexStore(Path.Combine(_directory, "indexes")));
        var results = new List<SearchMatch>();
        await document.SearchAsync("HIDDEN-NEEDLE", new ImmediateProgress<IReadOnlyList<SearchMatch>>(batch => results.AddRange(batch)));
        Assert.Single(results);
        Assert.Contains("truncada", results[0].Preview);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    private sealed class ImmediateProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
