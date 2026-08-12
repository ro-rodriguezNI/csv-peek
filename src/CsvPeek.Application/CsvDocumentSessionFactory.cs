namespace CsvPeek.Application;

public sealed class CsvDocumentSessionFactory(
    ICsvRecordSourceFactory sourceFactory,
    ICsvIndexStore indexStore) : ICsvDocumentSessionFactory
{
    public async Task<ICsvDocumentSession> OpenAsync(string path, CancellationToken cancellationToken = default) =>
        await CsvDocumentSession.OpenAsync(path, sourceFactory, indexStore, cancellationToken).ConfigureAwait(false);
}
