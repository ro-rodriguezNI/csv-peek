using System.Text;
using CsvPeek.Application;
using CsvPeek.Core;
using CsvPeek.Infrastructure;

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
        await using (var document = await OpenAsync(path, store))
        {
            var results = new List<SearchMatch>();
            var reporter = new ImmediateProgress<IReadOnlyList<SearchMatch>>(batch => results.AddRange(batch));
            await document.SearchAsync("NEEDLE", reporter);
            Assert.True(document.IsIndexComplete);
            Assert.Equal(5_000, document.KnownDataRows);
            Assert.Single(results);
            Assert.Equal(4321, results[0].DataRow);

            await document.EnsurePageAsync(4321);
            Assert.Equal("4321", document.TryGetRecord(4321)!.Fields[0]);
        }

        await using var reopened = await OpenAsync(path, store);
        Assert.True(reopened.IsIndexComplete);
        Assert.Equal(5_000, reopened.KnownDataRows);
    }

    [Fact]
    public async Task InvalidatesPersistedIndexWhenFileChanges()
    {
        string path = Path.Combine(_directory, "changing.csv");
        await File.WriteAllTextAsync(path, "id,name\n1,one\n", new UTF8Encoding(false));
        var store = new CsvIndexStore(Path.Combine(_directory, "indexes"));
        await using (var first = await OpenAsync(path, store))
        {
            var sink = new ImmediateProgress<IReadOnlyList<SearchMatch>>(_ => { });
            await first.SearchAsync("one", sink);
        }

        await File.AppendAllTextAsync(path, "2,two\n", new UTF8Encoding(false));
        await using var changed = await OpenAsync(path, store);
        Assert.Equal(2, changed.KnownDataRows);
    }

    [Fact]
    public async Task SearchFindsTextBeyondDisplayTruncationLimit()
    {
        string path = Path.Combine(_directory, "datos á enormes.csv");
        string huge = new string('x', CsvLimits.DefaultMaxFieldChars + 10_000) + " hidden-needle";
        await File.WriteAllTextAsync(path, $"id,note\n1,\"{huge}\"\n", new UTF8Encoding(false));
        await using var document = await OpenAsync(path, new CsvIndexStore(Path.Combine(_directory, "indexes")));
        var results = new List<SearchMatch>();
        await document.SearchAsync("HIDDEN-NEEDLE", new ImmediateProgress<IReadOnlyList<SearchMatch>>(batch => results.AddRange(batch)));
        Assert.Single(results);
        Assert.Contains("truncada", results[0].Preview);
    }

    [Fact]
    public async Task CreatesUniqueNamesForDuplicateOrEmptyHeaders()
    {
        string path = Path.Combine(_directory, "headers.csv");
        await File.WriteAllTextAsync(path, "name,name,,name\n1,2,3,4\n", new UTF8Encoding(false));

        await using var document = await OpenAsync(path, new CsvIndexStore(Path.Combine(_directory, "indexes")));

        Assert.Equal(["name", "name (2)", "Columna 3", "name (3)"], document.ColumnNames);
    }

    [Fact]
    public async Task DeduplicatesConcurrentRequestsForTheSamePage()
    {
        string path = await WriteRowsAsync("pages.csv", 1_000);
        await using var document = await OpenAsync(path, new CsvIndexStore(Path.Combine(_directory, "indexes")));
        int loaded = 0;
        document.PageLoaded += (_, page) =>
        {
            if (page == 1)
                Interlocked.Increment(ref loaded);
        };

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => document.EnsurePageAsync(300)));

        Assert.Equal(1, loaded);
        Assert.Equal("300", document.TryGetRecord(300)!.Fields[0]);
    }

    [Fact]
    public async Task CancelledSearchResumesBackgroundIndexing()
    {
        string path = await WriteRowsAsync("cancel.csv", 100_000);
        await using var document = await OpenAsync(path, new CsvIndexStore(Path.Combine(_directory, "indexes")));
        using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress<ScanProgress>(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            document.SearchAsync("not-present", new ImmediateProgress<IReadOnlyList<SearchMatch>>(_ => { }), progress, cancellation.Token));

        await WaitUntilAsync(() => document.IsIndexComplete, TimeSpan.FromSeconds(10));
        Assert.Equal(100_000, document.KnownDataRows);
    }

    [Fact]
    public async Task DisposesCleanlyWhileIndexingIsActive()
    {
        string path = await WriteRowsAsync("dispose.csv", 100_000);
        var document = await OpenAsync(path, new CsvIndexStore(Path.Combine(_directory, "indexes")));
        document.StartBackgroundIndexing();

        await document.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SearchReportsAllMatchesButReturnsAtMostTenThousand()
    {
        string path = Path.Combine(_directory, "many-matches.csv");
        var text = new StringBuilder("id,value\n");
        for (int i = 0; i < 10_050; i++)
            text.Append(i).Append(",needle\n");
        await File.WriteAllTextAsync(path, text.ToString(), new UTF8Encoding(false));
        await using var document = await OpenAsync(path, new CsvIndexStore(Path.Combine(_directory, "indexes")));
        var results = new List<SearchMatch>();
        ScanProgress? lastProgress = null;

        await document.SearchAsync(
            "needle",
            new ImmediateProgress<IReadOnlyList<SearchMatch>>(batch => results.AddRange(batch)),
            new ImmediateProgress<ScanProgress>(value => lastProgress = value));

        Assert.Equal(10_000, results.Count);
        Assert.Equal(10_050, lastProgress!.MatchesFound);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    private static Task<CsvDocumentSession> OpenAsync(string path, CsvIndexStore store) =>
        CsvDocumentSession.OpenAsync(path, new CsvRecordSourceFactory(), store);

    private async Task<string> WriteRowsAsync(string name, int rows)
    {
        string path = Path.Combine(_directory, name);
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync("id,value");
        for (int i = 0; i < rows; i++)
            await writer.WriteLineAsync($"{i},value {i}");
        return path;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!condition())
            await Task.Delay(25, cancellation.Token);
    }

    private sealed class ImmediateProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
