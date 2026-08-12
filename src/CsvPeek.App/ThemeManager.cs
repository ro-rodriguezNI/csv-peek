using Microsoft.Win32;

namespace CsvPeek.App;

internal enum ThemePreference
{
    System,
    Light,
    Dark
}

internal static class ThemeManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CSV Peek",
        "theme.txt");

    public static ThemePreference Current { get; private set; } = ThemePreference.System;

    public static event EventHandler? ThemeChanged;

    public static void Initialize()
    {
        Current = Load();
        ApplyNativeColorMode(Current);
    }

    public static void Set(ThemePreference preference)
    {
        if (Current == preference)
            return;

        Current = preference;
        ApplyNativeColorMode(preference);
        Save(preference);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static bool IsDark()
    {
        if (Current == ThemePreference.Dark)
            return true;
        if (Current == ThemePreference.Light)
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static ThemePreference Load()
    {
        try
        {
            if (File.Exists(SettingsPath) && Enum.TryParse(File.ReadAllText(SettingsPath).Trim(), true, out ThemePreference preference))
                return preference;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return ThemePreference.System;
    }

    private static void Save(ThemePreference preference)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, preference.ToString());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void ApplyNativeColorMode(ThemePreference preference)
    {
        Application.SetColorMode(preference switch
        {
            ThemePreference.Dark => SystemColorMode.Dark,
            ThemePreference.Light => SystemColorMode.Classic,
            _ => SystemColorMode.System
        });
    }
}

internal readonly record struct ThemePalette(
    Color Window,
    Color Surface,
    Color Elevated,
    Color Border,
    Color Text,
    Color MutedText,
    Color Selection,
    Color SelectionText,
    Color Warning,
    Color WarningText)
{
    public static ThemePalette Dark { get; } = new(
        Color.FromArgb(30, 30, 30),
        Color.FromArgb(37, 37, 38),
        Color.FromArgb(45, 45, 48),
        Color.FromArgb(63, 63, 70),
        Color.FromArgb(241, 241, 241),
        Color.FromArgb(200, 200, 200),
        Color.FromArgb(14, 99, 156),
        Color.White,
        Color.FromArgb(90, 66, 0),
        Color.FromArgb(255, 244, 206));

    public static ThemePalette Light { get; } = new(
        Color.FromArgb(255, 255, 255),
        Color.FromArgb(245, 247, 250),
        Color.FromArgb(238, 240, 243),
        Color.FromArgb(205, 208, 213),
        Color.FromArgb(32, 32, 32),
        Color.FromArgb(82, 82, 82),
        Color.FromArgb(0, 120, 215),
        Color.White,
        Color.FromArgb(255, 242, 204),
        Color.FromArgb(65, 48, 0));
}

internal sealed class ThemeColorTable(ThemePalette palette) : ProfessionalColorTable
{
    public override Color ToolStripGradientBegin => palette.Surface;
    public override Color ToolStripGradientMiddle => palette.Surface;
    public override Color ToolStripGradientEnd => palette.Surface;
    public override Color StatusStripGradientBegin => palette.Surface;
    public override Color StatusStripGradientEnd => palette.Surface;
    public override Color ToolStripBorder => palette.Border;
    public override Color ToolStripDropDownBackground => palette.Surface;
    public override Color ImageMarginGradientBegin => palette.Surface;
    public override Color ImageMarginGradientMiddle => palette.Surface;
    public override Color ImageMarginGradientEnd => palette.Surface;
    public override Color MenuItemSelected => palette.Elevated;
    public override Color MenuItemBorder => palette.Border;
    public override Color MenuItemPressedGradientBegin => palette.Elevated;
    public override Color MenuItemPressedGradientMiddle => palette.Elevated;
    public override Color MenuItemPressedGradientEnd => palette.Elevated;
    public override Color ButtonSelectedBorder => palette.Border;
    public override Color ButtonSelectedGradientBegin => palette.Elevated;
    public override Color ButtonSelectedGradientMiddle => palette.Elevated;
    public override Color ButtonSelectedGradientEnd => palette.Elevated;
    public override Color ButtonPressedGradientBegin => palette.Selection;
    public override Color ButtonPressedGradientMiddle => palette.Selection;
    public override Color ButtonPressedGradientEnd => palette.Selection;
    public override Color SeparatorDark => palette.Border;
    public override Color SeparatorLight => palette.Surface;
}
