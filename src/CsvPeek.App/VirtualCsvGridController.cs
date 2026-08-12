using CsvPeek.Application;
using CsvPeek.Core;

namespace CsvPeek.App;

internal sealed class VirtualCsvGridController : IDisposable
{
    private readonly DataGridView _grid;
    private readonly CancellationToken _lifetimeToken;
    private readonly System.Windows.Forms.Timer _rowCountTimer = new() { Interval = 300 };
    private readonly RowCountUpdatePolicy _rowCountPolicy = new();
    private ICsvDocumentSession? _document;

    public VirtualCsvGridController(DataGridView grid, CancellationToken lifetimeToken)
    {
        _grid = grid;
        _lifetimeToken = lifetimeToken;
        ConfigureGrid();
        _rowCountTimer.Tick += (_, _) =>
        {
            _rowCountTimer.Stop();
            if (_rowCountPolicy.TimerElapsed() == RowCountUpdateAction.ApplyDeferred)
                UpdateKnownRows(forceExact: false);
        };
    }

    public event EventHandler<ScanProgress>? IndexProgress;
    public event EventHandler<string>? PageLoadFailed;

    public void Bind(ICsvDocumentSession? document)
    {
        if (_document is not null)
        {
            _document.IndexProgress -= DocumentIndexProgress;
            _document.PageLoaded -= DocumentPageLoaded;
        }

        _rowCountTimer.Stop();
        _rowCountPolicy.Reset();
        _document = document;
        _grid.RowCount = 0;
        _grid.Columns.Clear();
        if (document is null)
            return;

        for (int i = 0; i < document.ColumnNames.Count; i++)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = $"column{i}",
                HeaderText = document.ColumnNames[i],
                Width = 150,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        document.IndexProgress += DocumentIndexProgress;
        document.PageLoaded += DocumentPageLoaded;
        UpdateKnownRows(forceExact: true);
    }

    public void StopPendingRowCountUpdate()
    {
        _rowCountTimer.Stop();
        _rowCountPolicy.Reset();
    }

    public void ScheduleKnownRowsUpdate(bool forceExact)
    {
        switch (_rowCountPolicy.Request(forceExact, _document?.IsIndexComplete == true))
        {
            case RowCountUpdateAction.ApplyExact:
                _rowCountTimer.Stop();
                UpdateKnownRows(forceExact: true);
                break;
            case RowCountUpdateAction.StartTimer:
                _rowCountTimer.Start();
                break;
        }
    }

    public async Task JumpToAsync(SearchMatch match)
    {
        ICsvDocumentSession? document = _document;
        if (document is null)
            return;
        int row = checked((int)Math.Min(match.DataRow, int.MaxValue - 1L));
        if (_grid.RowCount <= row)
            _grid.RowCount = row + 1;
        await LoadPageSafelyAsync(document, row);
        if (!ReferenceEquals(document, _document) || row < 0 || row >= _grid.RowCount)
            return;
        _grid.FirstDisplayedScrollingRowIndex = row;
        int column = Math.Min(match.ColumnIndex, _grid.ColumnCount - 1);
        if (column >= 0)
            _grid.CurrentCell = _grid[column, row];
        _grid.Focus();
    }

    private void ConfigureGrid()
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
        _grid.Scroll += GridScroll;
    }

    private void GridCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        ICsvDocumentSession? document = _document;
        if (document is null)
            return;
        CsvRecord? record = document.TryGetRecord(e.RowIndex);
        if (record is null)
        {
            e.Value = "…";
            _ = LoadPageSafelyAsync(document, e.RowIndex);
            return;
        }
        if (e.ColumnIndex >= record.Fields.Length)
            return;
        string value = record.Fields[e.ColumnIndex];
        e.Value = record.WasTruncated && value.Length >= CsvLimits.DefaultMaxFieldChars
            ? value + " … [valor truncado]"
            : value;
    }

    private async Task LoadPageSafelyAsync(ICsvDocumentSession document, long dataRow)
    {
        try
        {
            await document.EnsurePageAsync(dataRow, _lifetimeToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(document, _document))
                PageLoadFailed?.Invoke(this, ex.Message);
        }
    }

    private void GridRowPostPaint(object? sender, DataGridViewRowPostPaintEventArgs e)
    {
        string number = (e.RowIndex + 1).ToString("N0");
        TextRenderer.DrawText(
            e.Graphics,
            number,
            _grid.RowHeadersDefaultCellStyle.Font ?? _grid.Font,
            new Rectangle(e.RowBounds.Left, e.RowBounds.Top, _grid.RowHeadersWidth - 5, e.RowBounds.Height),
            _grid.RowHeadersDefaultCellStyle.ForeColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    private void GridScroll(object? sender, ScrollEventArgs e)
    {
        if (e.ScrollOrientation != ScrollOrientation.VerticalScroll ||
            _rowCountPolicy.DeferForVerticalScroll() != RowCountUpdateAction.RestartTimer)
            return;
        _rowCountTimer.Stop();
        _rowCountTimer.Start();
    }

    private void DocumentIndexProgress(object? sender, ScanProgress progress)
    {
        if (_grid.IsDisposed || !_grid.IsHandleCreated)
            return;
        _grid.BeginInvoke(() =>
        {
            if (!ReferenceEquals(sender, _document))
                return;
            ScheduleKnownRowsUpdate(forceExact: _document?.IsIndexComplete == true);
            IndexProgress?.Invoke(sender, progress);
        });
    }

    private void DocumentPageLoaded(object? sender, long page)
    {
        if (_grid.IsDisposed || !_grid.IsHandleCreated)
            return;
        _grid.BeginInvoke(() => InvalidateLoadedPage(sender, page));
    }

    private void InvalidateLoadedPage(object? sender, long page)
    {
        if (!ReferenceEquals(sender, _document) || _grid.RowCount == 0)
            return;
        int pageFirst = checked((int)Math.Min(page * CsvPaging.PageSize, int.MaxValue));
        int pageLast = Math.Min(_grid.RowCount - 1, pageFirst + CsvPaging.PageSize - 1);
        int visibleFirst = Math.Max(0, _grid.FirstDisplayedScrollingRowIndex);
        int visibleCount = _grid.DisplayedRowCount(includePartialRow: true);
        int visibleLast = Math.Min(_grid.RowCount - 1, visibleFirst + Math.Max(0, visibleCount - 1));
        int first = Math.Max(pageFirst, visibleFirst);
        int last = Math.Min(pageLast, visibleLast);
        for (int row = first; row <= last; row++)
            _grid.InvalidateRow(row);
    }

    private void UpdateKnownRows(bool forceExact)
    {
        if (_document is null)
            return;
        int known = checked((int)Math.Min(_document.KnownDataRows, int.MaxValue));
        int desired = forceExact || _document.IsIndexComplete ? known : Math.Max(_grid.RowCount, known);
        if (_grid.RowCount != desired)
            _grid.RowCount = desired;
    }

    public void Dispose()
    {
        Bind(null);
        _rowCountTimer.Dispose();
    }
}
