using CsvPeek.Application;
using CsvPeek.Core;

namespace CsvPeek.App;

internal sealed class SearchController : IDisposable
{
    private readonly Panel _panel;
    private readonly TextBox _queryBox;
    private readonly Button _searchButton;
    private readonly Button _cancelButton;
    private readonly ListView _results;
    private readonly ToolStripStatusLabel _status;
    private readonly ToolStripProgressBar _progress;
    private readonly VirtualCsvGridController _grid;
    private readonly CancellationToken _lifetimeToken;
    private ICsvDocumentSession? _document;
    private CancellationTokenSource? _searchCancellation;
    private long _reportedMatches;

    public SearchController(
        Panel panel,
        TextBox queryBox,
        Button searchButton,
        Button cancelButton,
        ListView results,
        ToolStripStatusLabel status,
        ToolStripProgressBar progress,
        VirtualCsvGridController grid,
        CancellationToken lifetimeToken)
    {
        _panel = panel;
        _queryBox = queryBox;
        _searchButton = searchButton;
        _cancelButton = cancelButton;
        _results = results;
        _status = status;
        _progress = progress;
        _grid = grid;
        _lifetimeToken = lifetimeToken;

        _queryBox.KeyDown += QueryBoxKeyDown;
        _searchButton.Click += async (_, _) => await StartAsync();
        _cancelButton.Click += (_, _) => Cancel();
        _results.DoubleClick += async (_, _) => await JumpToSelectedAsync();
        _results.KeyDown += ResultsKeyDown;
    }

    public bool IsRunning => _searchCancellation is { IsCancellationRequested: false };

    public void Bind(ICsvDocumentSession? document)
    {
        Cancel();
        _document = document;
        _results.Items.Clear();
        _results.Visible = false;
    }

    public void Show()
    {
        _panel.Visible = true;
        _queryBox.Focus();
        _queryBox.SelectAll();
    }

    public void Hide()
    {
        _panel.Visible = false;
        _results.Visible = false;
    }

    public void Cancel() => _searchCancellation?.Cancel();

    public void CancelOrHide()
    {
        if (IsRunning)
            Cancel();
        else
            Hide();
    }

    public void Navigate(bool backwards)
    {
        if (!_results.Visible || _results.Items.Count == 0)
            return;
        int current = _results.SelectedIndices.Count == 0 ? (backwards ? 0 : -1) : _results.SelectedIndices[0];
        int next = backwards ? Math.Max(0, current - 1) : Math.Min(_results.Items.Count - 1, current + 1);
        _results.SelectedItems.Clear();
        _results.Items[next].Selected = true;
        _results.Items[next].EnsureVisible();
        _ = JumpToSelectedAsync();
    }

    private async Task StartAsync()
    {
        ICsvDocumentSession? document = _document;
        string query = _queryBox.Text;
        if (document is null || string.IsNullOrWhiteSpace(query))
            return;

        Cancel();
        _searchCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        _searchCancellation = cancellation;
        _grid.StopPendingRowCountUpdate();
        _reportedMatches = 0;
        _results.BeginUpdate();
        _results.Items.Clear();
        _results.EndUpdate();
        _results.Visible = true;
        _cancelButton.Enabled = true;
        _searchButton.Enabled = false;
        _progress.Visible = true;
        _progress.Value = 0;
        _status.Text = "Buscando…";

        var matches = new Progress<IReadOnlyList<SearchMatch>>(batch => AppendMatches(document, batch));
        var progress = new Progress<ScanProgress>(value => ReportProgress(document, value));

        try
        {
            await document.SearchAsync(query, matches, progress, cancellation.Token);
            if (!ReferenceEquals(document, _document) || !ReferenceEquals(cancellation, _searchCancellation))
                return;
            _status.Text = _reportedMatches > 10_000
                ? $"Búsqueda terminada: {_reportedMatches:N0} coincidencias; se muestran las primeras 10,000."
                : $"Búsqueda terminada: {_reportedMatches:N0} coincidencias.";
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(document, _document) && ReferenceEquals(cancellation, _searchCancellation))
                _status.Text = "Búsqueda cancelada.";
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(document, _document) && ReferenceEquals(cancellation, _searchCancellation))
            {
                _status.Text = "La búsqueda falló.";
                MessageBox.Show(_panel.FindForm(), ex.Message, "CSV Peek", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (ReferenceEquals(cancellation, _searchCancellation))
            {
                _searchCancellation = null;
                _cancelButton.Enabled = false;
                _searchButton.Enabled = true;
                _progress.Visible = false;
            }
            cancellation.Dispose();
        }
    }

    private void AppendMatches(ICsvDocumentSession document, IReadOnlyList<SearchMatch> matches)
    {
        if (!ReferenceEquals(document, _document) || matches.Count == 0)
            return;
        _results.BeginUpdate();
        foreach (SearchMatch match in matches)
        {
            var item = new ListViewItem((match.DataRow + 1).ToString("N0")) { Tag = match };
            item.SubItems.Add(match.ColumnName);
            item.SubItems.Add(match.Preview);
            _results.Items.Add(item);
        }
        _results.EndUpdate();
    }

    private void ReportProgress(ICsvDocumentSession document, ScanProgress value)
    {
        if (!ReferenceEquals(document, _document))
            return;
        _reportedMatches = value.MatchesFound;
        _progress.Value = Math.Clamp(value.Percentage, 0, 100);
        string suffix = value.MatchesFound > 10_000 ? " · mostrando las primeras 10,000" : string.Empty;
        _status.Text = $"Buscando… {value.Percentage}% · {value.RecordsScanned:N0} registros · {value.MatchesFound:N0} coincidencias{suffix}";
        _grid.ScheduleKnownRowsUpdate(forceExact: document.IsIndexComplete);
    }

    private async Task JumpToSelectedAsync()
    {
        if (_results.SelectedItems.Count == 0 || _results.SelectedItems[0].Tag is not SearchMatch match)
            return;
        await _grid.JumpToAsync(match);
    }

    private async void QueryBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            await StartAsync();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            CancelOrHide();
        }
    }

    private async void ResultsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;
        e.Handled = true;
        await JumpToSelectedAsync();
    }

    public void Dispose()
    {
        Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
    }
}
