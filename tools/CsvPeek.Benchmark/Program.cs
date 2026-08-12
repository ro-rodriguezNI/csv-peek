using System.Diagnostics;
using CsvPeek.Core;

if (args.Length == 0 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Uso: CsvPeek.Benchmark <archivo.csv> [texto-a-buscar]");
    return 2;
}

string path = Path.GetFullPath(args[0]);
string query = args.Length > 1 ? args[1] : $"__csv_peek_no_match_{Guid.NewGuid():N}__";
string temporaryIndexes = Path.Combine(Path.GetTempPath(), "CSV Peek Benchmark", Guid.NewGuid().ToString("N"));
var stopwatch = Stopwatch.StartNew();
await using var document = await CsvDocument.OpenAsync(path, new CsvIndexStore(temporaryIndexes));
long openMilliseconds = stopwatch.ElapsedMilliseconds;

long matches = 0;
var resultProgress = new InlineProgress<IReadOnlyList<SearchMatch>>(batch => matches += batch.Count);
ScanProgress? lastProgress = null;
var scanProgress = new InlineProgress<ScanProgress>(value => lastProgress = value);
stopwatch.Restart();
await document.SearchAsync(query, resultProgress, scanProgress);
double scanSeconds = stopwatch.Elapsed.TotalSeconds;
long bytes = new FileInfo(path).Length;

Console.WriteLine($"Archivo: {path}");
Console.WriteLine($"Tamaño: {bytes:N0} bytes");
Console.WriteLine($"Primera página: {openMilliseconds:N0} ms");
Console.WriteLine($"Registros: {document.Index.RecordCount:N0}");
Console.WriteLine($"Escaneo completo: {scanSeconds:N2} s");
Console.WriteLine($"Rendimiento: {bytes / 1024d / 1024d / Math.Max(scanSeconds, 0.001):N1} MiB/s");
Console.WriteLine($"Coincidencias: {lastProgress?.MatchesFound ?? matches:N0}");

try { Directory.Delete(temporaryIndexes, true); } catch (IOException) { }
return 0;

file sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}
