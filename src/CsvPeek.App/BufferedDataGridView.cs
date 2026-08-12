namespace CsvPeek.App;

internal sealed class BufferedDataGridView : DataGridView
{
    public BufferedDataGridView()
    {
        EnableBufferedPainting();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        EnableBufferedPainting();
    }

    private void EnableBufferedPainting()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint,
            true);
        UpdateStyles();
    }
}
