namespace CsvPeek.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ThemeManager.Initialize();
        var files = args.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var context = new MultiWindowApplicationContext();
        if (files.Length == 0)
            context.OpenWindow(null);
        else
            foreach (string file in files)
                context.OpenWindow(file);
        Application.Run(context);
    }
}

internal sealed class MultiWindowApplicationContext : ApplicationContext
{
    private int _windowCount;

    public void OpenWindow(string? path)
    {
        var form = new MainForm(path);
        _windowCount++;
        form.FormClosed += (_, _) =>
        {
            if (--_windowCount == 0)
                ExitThread();
        };
        form.Show();
    }
}
