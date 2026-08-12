using System.Text;
using CsvPeek.Application;
using CsvPeek.Core;

namespace CsvPeek.App;

public sealed class MainForm : Form
{
    private readonly string? _initialPath;
    private readonly DataGridView _grid = new BufferedDataGridView();
    private readonly ToolStrip _toolbar = new();
    private readonly ToolStripComboBox _delimiterBox = new();
    private readonly ToolStripComboBox _encodingBox = new();
    private readonly ToolStripComboBox _themeBox = new();
    private readonly ToolStripButton _headerButton = new("Primera fila es encabezado") { CheckOnClick = true };
    private readonly Panel _searchPanel = new();
    private readonly TextBox _searchBox = new();
    private readonly Button _searchButton = new();
    private readonly Button _cancelSearchButton = new();
    private readonly ListView _results = new();
    private readonly Panel _changedPanel = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _formatLabel = new();
    private readonly ToolStripProgressBar _progress = new() { Width = 120, Visible = false };
    private readonly System.Windows.Forms.Timer _changeTimer = new() { Interval = 2000 };
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly DocumentWorkspaceController _workspace;
    private readonly VirtualCsvGridController _gridController;
    private readonly SearchController _searchController;
    private readonly MainFormThemeRenderer _themeRenderer;
    private CancellationTokenSource? _configurationCancellation;
    private bool _updatingFormatControls;

    public MainForm(string? initialPath, ICsvDocumentSessionFactory documentFactory)
    {
        _initialPath = initialPath;
        _workspace = new DocumentWorkspaceController(documentFactory);
        Text = "CSV Peek";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath) ?? Icon;
        Width = 1180;
        Height = 720;
        MinimumSize = new Size(720, 420);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AllowDrop = true;

        BuildToolbar();
        BuildSearchPanel();
        BuildChangedPanel();
        BuildResults();
        BuildStatusBar();

        Controls.Add(_grid);
        Controls.Add(_results);
        Controls.Add(_changedPanel);
        Controls.Add(_searchPanel);
        Controls.Add(_toolbar);
        Controls.Add(_statusStrip);

        _gridController = new VirtualCsvGridController(_grid, _lifetimeCancellation.Token);
        _gridController.IndexProgress += GridIndexProgress;
        _gridController.PageLoadFailed += (_, message) => _statusLabel.Text = $"No se pudo cargar una página: {message}";
        _searchController = new SearchController(
            _searchPanel,
            _searchBox,
            _searchButton,
            _cancelSearchButton,
            _results,
            _statusLabel,
            _progress,
            _gridController,
            _lifetimeCancellation.Token);
        _themeRenderer = new MainFormThemeRenderer(
            this,
            _toolbar,
            _statusStrip,
            _delimiterBox,
            _encodingBox,
            _themeBox,
            _searchPanel,
            _changedPanel,
            _grid,
            _results);

        ThemeManager.ThemeChanged += OnThemeChanged;
        _themeRenderer.Apply();
        Shown += OnShown;
        FormClosed += OnFormClosed;
        KeyDown += OnFormKeyDown;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        _changeTimer.Tick += async (_, _) =>
        {
            CheckThemeChange();
            await CheckForExternalChangeAsync();
        };
    }

    private void BuildToolbar()
    {
        _toolbar.Dock = DockStyle.Top;
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Padding = new Padding(6, 3, 6, 3);
        _toolbar.Items.Add(NewButton("Abrir…", (_, _) => OpenWithDialog()));
        _toolbar.Items.Add(NewButton("Recargar", async (_, _) => await ReloadDocumentAsync()));
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(new ToolStripLabel("Separador:"));
        _delimiterBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _delimiterBox.AutoSize = false;
        _delimiterBox.Width = 105;
        _delimiterBox.Items.AddRange(["Coma (,)", "Punto y coma (;)", "Tabulador"]);
        _delimiterBox.SelectedIndexChanged += async (_, _) => await ApplyFormatControlsAsync();
        _toolbar.Items.Add(_delimiterBox);
        _toolbar.Items.Add(new ToolStripLabel("Codificación:"));
        _encodingBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _encodingBox.AutoSize = false;
        _encodingBox.Width = 125;
        _encodingBox.Items.AddRange(["UTF-8", "UTF-16 LE", "UTF-16 BE", "Windows-1252"]);
        _encodingBox.SelectedIndexChanged += async (_, _) => await ApplyFormatControlsAsync();
        _toolbar.Items.Add(_encodingBox);
        _headerButton.CheckedChanged += async (_, _) => await ApplyFormatControlsAsync();
        _toolbar.Items.Add(_headerButton);
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(NewButton("Buscar  Ctrl+Shift+F", (_, _) => _searchController.Show()));
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(new ToolStripLabel("Tema:"));
        _themeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _themeBox.AutoSize = false;
        _themeBox.Width = 82;
        _themeBox.Items.AddRange(["Sistema", "Claro", "Oscuro"]);
        _themeBox.SelectedIndexChanged += (_, _) => ChangeThemeFromControl();
        _toolbar.Items.Add(_themeBox);
    }

    private static ToolStripButton NewButton(string text, EventHandler handler)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += handler;
        return button;
    }

    private void BuildSearchPanel()
    {
        _searchPanel.Dock = DockStyle.Top;
        _searchPanel.Height = 38;
        _searchPanel.Padding = new Padding(8, 6, 8, 5);
        _searchPanel.Visible = false;
        var label = new Label { Text = "Buscar en todo el archivo:", AutoSize = true, Location = new Point(9, 10) };
        _searchBox.Location = new Point(168, 7);
        _searchBox.Width = 340;
        _searchBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _searchButton.Text = "Buscar";
        _searchButton.Location = new Point(518, 5);
        _searchButton.Size = new Size(80, 27);
        _searchButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _cancelSearchButton.Text = "Cancelar";
        _cancelSearchButton.Location = new Point(604, 5);
        _cancelSearchButton.Size = new Size(82, 27);
        _cancelSearchButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _cancelSearchButton.Enabled = false;
        _searchPanel.Resize += (_, _) =>
        {
            _cancelSearchButton.Left = _searchPanel.ClientSize.Width - _cancelSearchButton.Width - 8;
            _searchButton.Left = _cancelSearchButton.Left - _searchButton.Width - 6;
            _searchBox.Width = Math.Max(120, _searchButton.Left - _searchBox.Left - 10);
        };
        _searchPanel.Controls.AddRange([label, _searchBox, _searchButton, _cancelSearchButton]);
    }

    private void BuildChangedPanel()
    {
        _changedPanel.Dock = DockStyle.Top;
        _changedPanel.Height = 37;
        _changedPanel.Padding = new Padding(8, 5, 8, 5);
        _changedPanel.Visible = false;
        var text = new Label { Text = "El archivo cambió fuera de CSV Peek.", AutoSize = true, Location = new Point(9, 10) };
        var reload = new Button { Text = "Recargar", Size = new Size(90, 27), Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(700, 5) };
        reload.Click += async (_, _) => await ReloadDocumentAsync();
        _changedPanel.Resize += (_, _) => reload.Left = _changedPanel.ClientSize.Width - reload.Width - 8;
        _changedPanel.Controls.AddRange([text, reload]);
    }

    private void BuildResults()
    {
        _results.Dock = DockStyle.Bottom;
        _results.Height = 190;
        _results.View = View.Details;
        _results.FullRowSelect = true;
        _results.HideSelection = false;
        _results.Visible = false;
        _results.Columns.Add("Fila", 85);
        _results.Columns.Add("Columna", 180);
        _results.Columns.Add("Coincidencia", 760);
    }

    private void BuildStatusBar()
    {
        _statusStrip.Dock = DockStyle.Bottom;
        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(_progress);
        _statusStrip.Items.Add(_formatLabel);
        _statusLabel.Text = "Arrastra un CSV aquí o pulsa Abrir…";
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        if (_initialPath is not null)
            await OpenDocumentAsync(_initialPath);
    }

    private async Task OpenDocumentAsync(string path)
    {
        try
        {
            UseWaitCursor = true;
            _statusLabel.Text = "Abriendo…";
            _gridController.StopPendingRowCountUpdate();
            _searchController.Cancel();
            _configurationCancellation?.Cancel();

            ICsvDocumentSession document = await _workspace.OpenAsync(path, _lifetimeCancellation.Token);
            _gridController.Bind(document);
            _searchController.Bind(document);
            UpdateFormatControls(document.Dialect);
            Text = $"{System.IO.Path.GetFileName(path)} — CSV Peek";
            _statusLabel.Text = $"{document.Fingerprint.Length:N0} bytes · {document.KnownDataRows:N0} filas disponibles";
            _formatLabel.Text = $"{FormatDelimiter(document.Dialect.Delimiter)} · {document.Dialect.EncodingName}";
            _changedPanel.Visible = false;
            _changeTimer.Start();
            BeginInvoke(document.StartBackgroundIndexing);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _statusLabel.Text = "No se pudo abrir el archivo.";
            MessageBox.Show(this, ex.Message, "CSV Peek", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void GridIndexProgress(object? sender, ScanProgress progress)
    {
        if (_searchController.IsRunning || sender is not ICsvDocumentSession document || !ReferenceEquals(document, _workspace.Current))
            return;
        _progress.Visible = !document.IsIndexComplete;
        _progress.Value = Math.Clamp(progress.Percentage, 0, 100);
        _statusLabel.Text = document.IsIndexComplete
            ? $"{document.KnownDataRows:N0} filas · índice listo" + (document.HasTruncatedCells ? " · hay valores truncados en pantalla" : string.Empty)
            : $"Indexando… {progress.RecordsScanned:N0} registros";
    }

    private async Task ApplyFormatControlsAsync()
    {
        ICsvDocumentSession? document = _workspace.Current;
        if (_updatingFormatControls || document is null || _delimiterBox.SelectedIndex < 0 || _encodingBox.SelectedIndex < 0)
            return;
        char delimiter = _delimiterBox.SelectedIndex switch { 1 => ';', 2 => '\t', _ => ',' };
        Encoding encoding = _encodingBox.SelectedIndex switch
        {
            1 => new UnicodeEncoding(false, false, true),
            2 => new UnicodeEncoding(true, false, true),
            3 => Encoding.GetEncoding(1252),
            _ => new UTF8Encoding(false, true)
        };
        if (delimiter == document.Dialect.Delimiter &&
            encoding.CodePage == document.Dialect.Encoding.CodePage &&
            _headerButton.Checked == document.Dialect.FirstRowIsHeader)
            return;

        _configurationCancellation?.Cancel();
        _configurationCancellation?.Dispose();
        _configurationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        try
        {
            _gridController.StopPendingRowCountUpdate();
            _searchController.Cancel();
            _statusLabel.Text = "Aplicando formato…";
            await document.ReconfigureAsync(delimiter, encoding, _headerButton.Checked, _configurationCancellation.Token);
            if (!ReferenceEquals(document, _workspace.Current))
                return;
            _gridController.Bind(document);
            _searchController.Bind(document);
            _formatLabel.Text = $"{FormatDelimiter(delimiter)} · {encoding.WebName}";
            document.StartBackgroundIndexing();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "CSV Peek", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateFormatControls(CsvDialect dialect)
    {
        _updatingFormatControls = true;
        _delimiterBox.SelectedIndex = dialect.Delimiter switch { ';' => 1, '\t' => 2, _ => 0 };
        _encodingBox.SelectedIndex = dialect.Encoding.CodePage switch { 1200 => 1, 1201 => 2, 1252 => 3, _ => 0 };
        _headerButton.Checked = dialect.FirstRowIsHeader;
        _updatingFormatControls = false;
    }

    private async Task ReloadDocumentAsync()
    {
        if (_workspace.Current is not null)
            await OpenDocumentAsync(_workspace.Current.Path);
    }

    private async Task CheckForExternalChangeAsync()
    {
        if (!_workspace.HasExternalChange())
            return;
        _changedPanel.Visible = true;
        _changeTimer.Stop();
        _searchController.Cancel();
        await _workspace.StopBackgroundWorkAsync();
    }

    private void ChangeThemeFromControl()
    {
        if (_themeRenderer.IsUpdatingControl || _themeBox.SelectedIndex < 0)
            return;
        ThemeManager.Set(_themeBox.SelectedIndex switch
        {
            1 => ThemePreference.Light,
            2 => ThemePreference.Dark,
            _ => ThemePreference.System
        });
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
            BeginInvoke(_themeRenderer.Apply);
        else
            _themeRenderer.Apply();
    }

    private void CheckThemeChange()
    {
        if (ThemeManager.Current == ThemePreference.System && _themeRenderer.IsDark != ThemeManager.IsDark())
            _themeRenderer.Apply();
    }

    private void OpenWithDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Abrir archivo CSV",
            Filter = "Archivos CSV (*.csv)|*.csv|Archivos de texto (*.txt;*.tsv)|*.txt;*.tsv|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _ = OpenDocumentAsync(dialog.FileName);
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.Shift && e.KeyCode == Keys.F)
        {
            e.SuppressKeyPress = true;
            _searchController.Show();
        }
        else if (e.KeyCode == Keys.F3)
        {
            e.SuppressKeyPress = true;
            _searchController.Navigate(e.Shift);
        }
        else if (e.KeyCode == Keys.Escape && _searchPanel.Visible)
        {
            e.SuppressKeyPress = true;
            _searchController.CancelOrHide();
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths && File.Exists(paths[0]))
            _ = OpenDocumentAsync(paths[0]);
    }

    private async void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _changeTimer.Stop();
        _lifetimeCancellation.Cancel();
        _searchController.Dispose();
        _configurationCancellation?.Cancel();
        _configurationCancellation?.Dispose();
        _gridController.Dispose();
        await _workspace.DisposeAsync();
        _changeTimer.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private static string FormatDelimiter(char delimiter) => delimiter switch
    {
        '\t' => "Tabulador",
        ';' => "Punto y coma",
        _ => "Coma"
    };
}
