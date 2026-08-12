namespace CsvPeek.App;

internal sealed class MainFormThemeRenderer(
    Form form,
    ToolStrip toolbar,
    StatusStrip statusStrip,
    ToolStripComboBox delimiterBox,
    ToolStripComboBox encodingBox,
    ToolStripComboBox themeBox,
    Panel searchPanel,
    Panel changedPanel,
    DataGridView grid,
    ListView results)
{
    public bool IsDark { get; private set; }
    public bool IsUpdatingControl { get; private set; }

    public void Apply()
    {
        IsDark = ThemeManager.IsDark();
        ThemePalette palette = IsDark ? ThemePalette.Dark : ThemePalette.Light;

        form.SuspendLayout();
        form.BackColor = palette.Window;
        form.ForeColor = palette.Text;

        var renderer = new ToolStripProfessionalRenderer(new ThemeColorTable(palette));
        toolbar.Renderer = renderer;
        statusStrip.Renderer = renderer;
        toolbar.BackColor = palette.Surface;
        toolbar.ForeColor = palette.Text;
        statusStrip.BackColor = palette.Surface;
        statusStrip.ForeColor = palette.Text;
        foreach (ToolStripItem item in toolbar.Items)
            item.ForeColor = palette.Text;
        foreach (ToolStripItem item in statusStrip.Items)
            item.ForeColor = palette.Text;

        ConfigureComboColors(delimiterBox, palette);
        ConfigureComboColors(encodingBox, palette);
        ConfigureComboColors(themeBox, palette);
        ApplyPanelColors(searchPanel, palette.Surface, palette.Text, palette);
        ApplyPanelColors(changedPanel, palette.Warning, palette.WarningText, palette);

        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = palette.Window;
        grid.GridColor = palette.Border;
        grid.DefaultCellStyle.BackColor = palette.Window;
        grid.DefaultCellStyle.ForeColor = palette.Text;
        grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
        grid.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
        grid.AlternatingRowsDefaultCellStyle.BackColor = palette.Surface;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = palette.Text;
        grid.ColumnHeadersDefaultCellStyle.BackColor = palette.Elevated;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Elevated;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
        grid.RowHeadersDefaultCellStyle.BackColor = palette.Elevated;
        grid.RowHeadersDefaultCellStyle.ForeColor = palette.MutedText;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = palette.Selection;
        grid.RowHeadersDefaultCellStyle.SelectionForeColor = palette.SelectionText;
        results.BackColor = palette.Window;
        results.ForeColor = palette.Text;

        IsUpdatingControl = true;
        themeBox.SelectedIndex = ThemeManager.Current switch
        {
            ThemePreference.Light => 1,
            ThemePreference.Dark => 2,
            _ => 0
        };
        IsUpdatingControl = false;

        form.ResumeLayout(true);
        form.Invalidate(true);
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
}
