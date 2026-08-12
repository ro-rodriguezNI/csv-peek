using System.Diagnostics;
using System.Text;
using CsvPeek.Core;

namespace CsvPeek.App;

public sealed class MainForm : Form
{
    private readonly string? _initialPath;
    private readonly DataGridView _grid = new();
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
    private CsvDocument? _document;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _configurationCancellation;
    private bool _updatingFormatControls;
    private bool _updatingThemeControl;
    private bool _isDarkTheme;
    private long _reportedMatches;

    public MainForm(string? initialPath)
    {
        _initialPath = initialPath;
        Text = "CSV Peek";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        Width = 1180;
        Height = 720;
        MinimumSize = new Size(720, 420);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AllowDrop = true;

        BuildToolbar();
        BuildSearchPanel();
        BuildChangedPanel();
        BuildGrid();
        BuildResults();
        BuildStatusBar();

        Controls.Add(_grid);
        Controls.Add(_results);
        Controls.Add(_changedPanel);
        Controls.Add(_searchPanel);
        Controls.Add(_toolbar);
        Controls.Add(_statusStrip);

        ThemeManager.ThemeChanged += OnThemeChanged;
        ApplyTheme();

        Shown += OnShown;
        FormClosed += OnFormClosed;
        KeyDown += OnFormKeyDown;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        _changeTimer.Tick += (_, _) =>
        {
            CheckThemeChange();
            CheckForExternalChange();
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
        _toolbar.Items.Add(NewButton("Buscar  Ctrl+Shift+F", (_, _) => ShowSearch()));
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
        _searchPanel.BackColor = Color.FromArgb(245, 247, 250);
        _searchPanel.Visible = false;

        var label = new Label { Text = "Buscar en todo el archivo:", AutoSize = true, Location = new Point(9, 10) };
        _searchBox.Location = new Point(168, 7);
        _searchBox.Width = 340;
        _searchBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _searchBox.KeyDown += SearchBoxKeyDown;
        _searchButton.Text = "Buscar";
        _searchButton.Location = new Point(518, 5);
        _searchButton.Size = new Size(80, 27);
        _searchButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _searchButton.Click += async (_, _) => await StartSearchAsync();
        _cancelSearchButton.Text = "Cancelar";
        _cancelSearchButton.Location = new Point(604, 5);
        _cancelSearchButton.Size = new Size(82, 27);
        _cancelSearchButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _cancelSearchButton.Enabled = false;
        _cancelSearchButton.Click += (_, _) => _searchCancellation?.Cancel();

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
        _changedPanel.BackColor = Color.FromArgb(255, 242, 204);
        _changedPanel.Visible = false;
        var text = new Label { Text = "El archivo cambió fuera de CSV Peek.", AutoSize = true, Location = new Point(9, 10) };
        var reload = new Button { Text = "Recargar", Size = new Size(90, 27), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        reload.Location = new Point(700, 5);
        reload.Click += async (_, _) => await ReloadDocumentAsync();
        _changedPanel.Resize += (_, _) => reload.Left = _changedPanel.ClientSize.Width - reload.Width - 8;
        _changedPanel.Controls.AddRange([text, reload]);
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.VirtualMode = true;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToOrderColumns = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.RowHeadersWidth = 64;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.MultiSelect = true;
        _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellValueNeeded += GridCellValueNeeded;
        _grid.RowPostPaint += GridRowPostPaint;
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
        _results.DoubleClick += async (_, _) => await JumpToSelectedResultAsync();
        _results.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                await JumpToSelectedResultAsync();
            }
        };
    }

    private void BuildStatusBar()
    {
        _statusStrip.Dock = DockStyle.Bottom;
        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(_progress);
        _statusStrip.Items.Add(_formatLabel);
        _statusLabel.Text = "Arrastra un CSV aquí o pulsa Abrir…";
    }

    private void ChangeThemeFromControl()
    {
        if (_updatingThemeControl || _themeBox.SelectedIndex < 0)
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
            BeginInvoke(ApplyTheme);
        else
            ApplyTheme();
    }

    private void CheckThemeChange()
    {
        if (ThemeManager.Current == ThemePreference.System && _isDarkTheme != ThemeManager.IsDark())
            ApplyTheme();
    }

    private void ApplyTheme()
    {
        _isDarkTheme = ThemeManager.IsDark();
        ThemePalette palette = _isDarkTheme ? ThemePalette.Dark : ThemePalette.Light;

        SuspendLayout();
        BackColor = palette.Window;
        ForeColor = palette.Text;

        var renderer = new ToolStripProfessionalRenderer(new ThemeColorTable(palette));
        _toolbar.Renderer = renderer;
        _statusStrip.Renderer = renderer;
        _toolbar.BackColor = palette.Surface;
        _toolbar.ForeColor = palette.Text;
        _statusStrip.BackColor = palette.Surface;
        _statusStrip.ForeColor = palette.Text;
        foreach (ToolStripItem item in _toolbar.Items)
            item.ForeColor = palette.Text;
        foreach (ToolStripItem item in _statusStrip.Items)
            item.ForeColor = palette.Text;

        ConfigureComboColors(_delimiterBox, palette);
        ConfigureComboColors(_encodingBox, palette);
        ConfigureComboColors(_themeBox, palette);

        _searchPanel.BackColor = palette.Surface;
        ApplyPanelColors(_searchPanel, palette.Surface, palette.Text, palette);
        _changedPanel.BackColor = palette.Warning;
        ApplyPanelColors(_changedPanel, palette.Warning, palette.WarningText, palette);

        _grid.EnableHeadersVisualStyles = false;
        _grid.BackgroundColor = palette.Window;
        _grid.GridColor = palette.Border;
        _grid.DefaultCellStyle.BackColor = palette.Window;
        _grid.DefaultCellStyle.ForeColor = palette.Text;
        _grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
        _grid.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = palette.Surface;
        _grid.AlternatingRowsDefaultCellStyle.ForeColor = palette.Text;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = palette.Elevated;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Elevated;
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
        _grid.RowHeadersDefaultCellStyle.BackColor = palette.Elevated;
        _grid.RowHeadersDefaultCellStyle.ForeColor = palette.MutedText;
        _grid.RowHeadersDefaultCellStyle.SelectionBackColor = palette.Selection;
        _grid.RowHeadersDefaultCellStyle.SelectionForeColor = palette.SelectionText;

        _results.BackColor = palette.Window;
        _results.ForeColor = palette.Text;

        _updatingThemeControl = true;
        _themeBox.SelectedIndex = ThemeManager.Current switch
        {
            ThemePreference.Light => 1,
            ThemePreference.Dark => 2,
            _ => 0
        };
        _updatingThemeControl = false;

        ResumeLayout(true);
        Invalidate(true);
    }

    private static void ConfigureComboColors(ToolStripComboBox combo, ThemePalette palette)
    {
        combo.BackColor = palette.Elevated;
        combo.ForeColor = palette.Text;
        combo.ComboBox.BackColor = palette.Elevated;
        combo.ComboBox.ForeColor = palette.Text;
        combo.ComboBox.FlatStyle = FlatStyle.Flat;
    }

    private static void ApplyPanelColors(Control parent, Color background, Color foreground, ThemePalette palette)
    {
        parent.BackColor = background;
        parent.ForeColor = foreground;
        foreach (Control control in parent.Controls)
        {
            control.ForeColor = foreground;
            switch (control)
            {
                case TextBox textBox:
                    textBox.BackColor = palette.Window;
                    textBox.ForeColor = palette.Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case Button button:
                    button.BackColor = palette.Elevated;
                    button.ForeColor = palette.Text;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = palette.Border;
                    button.FlatAppearance.MouseOverBackColor = palette.Selection;
                    button.FlatAppearance.MouseDownBackColor = palette.Selection;
                    break;
                default:
                    control.BackColor = background;
                    break;
            }
        }
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
            CancelSearch();
            _configurationCancellation?.Cancel();
            if (_document is not null)
            {
                UnsubscribeDocument(_document);
                await _document.DisposeAsync();
            }

            _document = await CsvDocument.OpenAsync(path, cancellationToken: _lifetimeCancellation.Token);
            SubscribeDocument(_document);
            ConfigureGrid(_document);
            UpdateFormatControls(_document.Dialect);
            UpdateKnownRows(forceExact: true);
            Text = $"{System.IO.Path.GetFileName(path)} — CSV Peek";
            _statusLabel.Text = $"{new FileInfo(path).Length:N0} bytes · {Math.Max(0, _document.KnownDataRows):N0} filas disponibles";
            _formatLabel.Text = $"{FormatDelimiter(_document.Dialect.Delimiter)} · {_document.Dialect.EncodingName}";
            _changedPanel.Visible = false;
            _changeTimer.Start();
            BeginInvoke(_document.StartBackgroundIndexing);
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

    private void SubscribeDocument(CsvDocument document)
    {
        document.IndexProgress += DocumentIndexProgress;
        document.Cache.PageLoaded += CachePageLoaded;
    }

    private void UnsubscribeDocument(CsvDocument document)
    {
        document.IndexProgress -= DocumentIndexProgress;
        document.Cache.PageLoaded -= CachePageLoaded;
    }

    private void ConfigureGrid(CsvDocument document)
    {
        _grid.RowCount = 0;
        _grid.Columns.Clear();
        for (int i = 0; i < document.ColumnNames.Length; i++)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = $"column{i}",
                HeaderText = document.ColumnNames[i],
                Width = 150,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }
    }

    private void GridCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        var document = _document;
        if (document is null)
            return;
        var record = document.Cache.TryGet(e.RowIndex);
        if (record is null)
        {
            e.Value = "…";
            _ = LoadPageSafelyAsync(document, e.RowIndex);
            return;
        }
        if (e.ColumnIndex < record.Fields.Length)
        {
            string value = record.Fields[e.ColumnIndex];
            e.Value = record.WasTruncated && value.Length >= CsvRecordReader.DefaultMaxFieldChars
                ? value + " … [valor truncado]"
                : value;
        }
    }

    private async Task LoadPageSafelyAsync(CsvDocument document, long dataRow)
    {
        try
        {
            await document.EnsurePageAsync(dataRow, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed)
                BeginInvoke(() => _statusLabel.Text = $"No se pudo cargar una página: {ex.Message}");
        }
    }

    private void GridRowPostPaint(object? sender, DataGridViewRowPostPaintEventArgs e)
    {
        string number = (e.RowIndex + 1).ToString("N0");
        TextRenderer.DrawText(e.Graphics, number, _grid.RowHeadersDefaultCellStyle.Font ?? _grid.Font,
            new Rectangle(e.RowBounds.Left, e.RowBounds.Top, _grid.RowHeadersWidth - 5, e.RowBounds.Height),
            _grid.RowHeadersDefaultCellStyle.ForeColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    private void DocumentIndexProgress(object? sender, ScanProgress progress)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        BeginInvoke(() =>
        {
            if (_searchCancellation is not { IsCancellationRequested: false })
            {
                _progress.Visible = !(_document?.Index.IsComplete ?? true);
                _progress.Value = Math.Clamp(progress.Percentage, 0, 100);
                _statusLabel.Text = _document?.Index.IsComplete == true
                    ? $"{_document.KnownDataRows:N0} filas · índice listo" + (_document.HasTruncatedCells ? " · hay valores truncados en pantalla" : string.Empty)
                    : $"Indexando… {progress.RecordsScanned:N0} registros";
            }
            UpdateKnownRows(forceExact: _document?.Index.IsComplete == true);
        });
    }

    private void CachePageLoaded(object? sender, long page)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        BeginInvoke(() =>
        {
            int first = checked((int)Math.Min(page * CsvPageCache.PageSize, int.MaxValue));
            if (first >= 0 && first < _grid.RowCount)
                _grid.Invalidate();
        });
    }

    private void UpdateKnownRows(bool forceExact)
    {
        if (_document is null)
            return;
        int known = checked((int)Math.Min(_document.KnownDataRows, int.MaxValue));
        int desired = forceExact ? known : Math.Max(_grid.RowCount, known);
        if (_grid.RowCount != desired)
            _grid.RowCount = desired;
    }

    private void ShowSearch()
    {
        _searchPanel.Visible = true;
        _searchBox.Focus();
        _searchBox.SelectAll();
    }

    private async void SearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            await StartSearchAsync();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            if (_searchCancellation is { IsCancellationRequested: false })
                _searchCancellation.Cancel();
            else
                HideSearch();
        }
    }

    private async Task StartSearchAsync()
    {
        var document = _document;
        string query = _searchBox.Text;
        if (document is null || string.IsNullOrWhiteSpace(query))
            return;

        CancelSearch();
        _searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _reportedMatches = 0;
        _results.BeginUpdate();
        _results.Items.Clear();
        _results.EndUpdate();
        _results.Visible = true;
        _cancelSearchButton.Enabled = true;
        _searchButton.Enabled = false;
        _progress.Visible = true;
        _progress.Value = 0;
        _statusLabel.Text = "Buscando…";

        var matches = new Progress<IReadOnlyList<SearchMatch>>(AppendMatches);
        var progress = new Progress<ScanProgress>(value =>
        {
            _reportedMatches = value.MatchesFound;
            _progress.Value = Math.Clamp(value.Percentage, 0, 100);
            string suffix = value.MatchesFound > 10_000 ? " · mostrando las primeras 10,000" : string.Empty;
            _statusLabel.Text = $"Buscando… {value.Percentage}% · {value.RecordsScanned:N0} registros · {value.MatchesFound:N0} coincidencias{suffix}";
            UpdateKnownRows(forceExact: document.Index.IsComplete);
        });

        try
        {
            await document.SearchAsync(query, matches, progress, _searchCancellation.Token);
            _statusLabel.Text = _reportedMatches > 10_000
                ? $"Búsqueda terminada: {_reportedMatches:N0} coincidencias; se muestran las primeras 10,000."
                : $"Búsqueda terminada: {_reportedMatches:N0} coincidencias.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Búsqueda cancelada.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "La búsqueda falló.";
            MessageBox.Show(this, ex.Message, "CSV Peek", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cancelSearchButton.Enabled = false;
            _searchButton.Enabled = true;
            _progress.Visible = false;
        }
    }

    private void AppendMatches(IReadOnlyList<SearchMatch> matches)
    {
        if (matches.Count == 0)
            return;
        _results.BeginUpdate();
        foreach (var match in matches)
        {
            var item = new ListViewItem((match.DataRow + 1).ToString("N0")) { Tag = match };
            item.SubItems.Add(match.ColumnName);
            item.SubItems.Add(match.Preview);
            _results.Items.Add(item);
        }
        _results.EndUpdate();
    }

    private async Task JumpToSelectedResultAsync()
    {
        if (_document is null || _results.SelectedItems.Count == 0 || _results.SelectedItems[0].Tag is not SearchMatch match)
            return;
        int row = checked((int)Math.Min(match.DataRow, int.MaxValue - 1L));
        if (_grid.RowCount <= row)
            _grid.RowCount = row + 1;
        await LoadPageSafelyAsync(_document, row);
        if (row >= 0 && row < _grid.RowCount)
        {
            _grid.FirstDisplayedScrollingRowIndex = row;
            int column = Math.Min(match.ColumnIndex, _grid.ColumnCount - 1);
            if (column >= 0)
                _grid.CurrentCell = _grid[column, row];
            _grid.Focus();
        }
    }

    private void NavigateResult(bool backwards)
    {
        if (!_results.Visible || _results.Items.Count == 0)
            return;
        int current = _results.SelectedIndices.Count == 0 ? (backwards ? 0 : -1) : _results.SelectedIndices[0];
        int next = backwards ? Math.Max(0, current - 1) : Math.Min(_results.Items.Count - 1, current + 1);
        _results.SelectedItems.Clear();
        _results.Items[next].Selected = true;
        _results.Items[next].EnsureVisible();
        _ = JumpToSelectedResultAsync();
    }

    private void HideSearch()
    {
        _searchPanel.Visible = false;
        _results.Visible = false;
        _grid.Focus();
    }

    private void CancelSearch()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
    }

    private async Task ApplyFormatControlsAsync()
    {
        if (_updatingFormatControls || _document is null || _delimiterBox.SelectedIndex < 0 || _encodingBox.SelectedIndex < 0)
            return;
        char delimiter = _delimiterBox.SelectedIndex switch { 1 => ';', 2 => '\t', _ => ',' };
        Encoding encoding = _encodingBox.SelectedIndex switch
        {
            1 => new UnicodeEncoding(false, false, true),
            2 => new UnicodeEncoding(true, false, true),
            3 => Encoding.GetEncoding(1252),
            _ => new UTF8Encoding(false, true)
        };
        int bom = GetBomLength(_document.Path, encoding);
        var dialect = new CsvDialect(delimiter, encoding, bom, _headerButton.Checked);
        if (dialect.Delimiter == _document.Dialect.Delimiter && dialect.Encoding.CodePage == _document.Dialect.Encoding.CodePage && dialect.FirstRowIsHeader == _document.Dialect.FirstRowIsHeader)
            return;

        _configurationCancellation?.Cancel();
        _configurationCancellation?.Dispose();
        _configurationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        try
        {
            CancelSearch();
            _statusLabel.Text = "Aplicando formato…";
            await _document.ReconfigureAsync(dialect, _configurationCancellation.Token);
            ConfigureGrid(_document);
            UpdateKnownRows(forceExact: true);
            _formatLabel.Text = $"{FormatDelimiter(delimiter)} · {encoding.WebName}";
            _document.StartBackgroundIndexing();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "CSV Peek", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static int GetBomLength(string path, Encoding encoding)
    {
        Span<byte> bytes = stackalloc byte[3];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int read = stream.Read(bytes);
        if (encoding.CodePage == Encoding.UTF8.CodePage && read >= 3 && bytes[..3].SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })) return 3;
        if (encoding.CodePage is 1200 or 1201 && read >= 2 && (bytes[..2].SequenceEqual(new byte[] { 0xFF, 0xFE }) || bytes[..2].SequenceEqual(new byte[] { 0xFE, 0xFF }))) return 2;
        return 0;
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
        if (_document is not null)
            await OpenDocumentAsync(_document.Path);
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

    private async void CheckForExternalChange()
    {
        if (_document?.HasChanged() == true)
        {
            _changedPanel.Visible = true;
            _changeTimer.Stop();
            CancelSearch();
            await _document.CancelBackgroundScanAsync();
        }
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.Shift && e.KeyCode == Keys.F)
        {
            e.SuppressKeyPress = true;
            ShowSearch();
        }
        else if (e.KeyCode == Keys.F3)
        {
            e.SuppressKeyPress = true;
            NavigateResult(e.Shift);
        }
        else if (e.KeyCode == Keys.Escape && _searchPanel.Visible)
        {
            e.SuppressKeyPress = true;
            if (_searchCancellation is { IsCancellationRequested: false })
                _searchCancellation.Cancel();
            else
                HideSearch();
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
        CancelSearch();
        _configurationCancellation?.Cancel();
        _configurationCancellation?.Dispose();
        if (_document is not null)
        {
            UnsubscribeDocument(_document);
            await _document.DisposeAsync();
        }
        _lifetimeCancellation.Dispose();
    }

    private static string FormatDelimiter(char delimiter) => delimiter switch { '\t' => "Tabulador", ';' => "Punto y coma", _ => "Coma" };
}
