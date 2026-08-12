using CsvPeek.Application;
using CsvPeek.Infrastructure;

namespace CsvPeek.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ThemeManager.Initialize();
        var files = args.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ICsvDocumentSessionFactory documentFactory = new CsvDocumentSessionFactory(
            new CsvRecordSourceFactory(),
            new CsvIndexStore());
        var context = new MultiWindowApplicationContext(documentFactory);
        if (files.Length == 0)
            context.OpenWindow(null);
        else
            foreach (string file in files)
                context.OpenWindow(file);
        System.Windows.Forms.Application.Run(context);
    }
}

internal sealed class MultiWindowApplicationContext(ICsvDocumentSessionFactory documentFactory) : ApplicationContext
{
    private int _windowCount;

    public void OpenWindow(string? path)
    {
        var form = new MainForm(path, documentFactory);
        _windowCount++;
        form.FormClosed += (_, _) =>
        {
            if (--_windowCount == 0)
                ExitThread();
        };
        form.Show();
    }
}
