using System.Windows;
using System.Windows.Media;
using System.IO;

namespace SpriteStudio;

public partial class App : Application
{
    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["AppBackground"] = "#101311", ["PanelBackground"] = "#171B18", ["PanelAlt"] = "#1D231F",
        ["BorderBrush"] = "#2A332D", ["TextBrush"] = "#EDF3EE", ["MutedBrush"] = "#91A095",
        ["AccentBrush"] = "#B4F178", ["AccentTextBrush"] = "#17321C", ["ButtonBackgroundBrush"] = "#1A201C",
        ["ButtonHoverBrush"] = "#242C26", ["ComboHoverBrush"] = "#29342C", ["ComboSelectedBrush"] = "#334234",
        ["SidebarBackgroundBrush"] = "#131714", ["PreviewTileBrush"] = "#1B211D", ["PreviewCanvasBrush"] = "#242A26",
        ["PreviewBorderBrush"] = "#303A32", ["SubtleTextBrush"] = "#737A75", ["DetailLabelBrush"] = "#859087",
        ["PrivacyTextBrush"] = "#69776D", ["PanelInsetBrush"] = "#151A16", ["PanelIconBrush"] = "#1C251D",
        ["CanvasOverlayBrush"] = "#B51B211D", ["CanvasOverlayLightBrush"] = "#551B211D", ["OverlayTextBrush"] = "#D7E8D8",
        ["CanvasTextBrush"] = "#E0E0E0", ["CheckerTileBrush"] = "#313832", ["CardBackgroundBrush"] = "#1A201C",
        ["CardBorderBrush"] = "#2A332D", ["SelectedBackgroundBrush"] = "#314D28", ["FieldBorderBrush"] = "#202622",
        ["SuccessBrush"] = "#B4F178", ["WarningBrush"] = "#E6B876", ["ErrorBrush"] = "#F2A59B", ["SoftErrorBrush"] = "#FF9D86"
    };

    private static readonly IReadOnlyDictionary<string, string> ObjectBuildPalette = new Dictionary<string, string>
    {
        ["AppBackground"] = "#383838", ["PanelBackground"] = "#454545", ["PanelAlt"] = "#4D4D4D",
        ["BorderBrush"] = "#303030", ["TextBrush"] = "#E6E6E6", ["MutedBrush"] = "#B7B7B7",
        ["AccentBrush"] = "#69B9EA", ["AccentTextBrush"] = "#16242E", ["ButtonBackgroundBrush"] = "#4B4B4B",
        ["ButtonHoverBrush"] = "#5A5A5A", ["ComboHoverBrush"] = "#555555", ["ComboSelectedBrush"] = "#43596A",
        ["SidebarBackgroundBrush"] = "#3D3D3D", ["PreviewTileBrush"] = "#494949", ["PreviewCanvasBrush"] = "#414141",
        ["PreviewBorderBrush"] = "#666666", ["SubtleTextBrush"] = "#B0B0B0", ["DetailLabelBrush"] = "#D0D0D0",
        ["PrivacyTextBrush"] = "#B0B0B0", ["PanelInsetBrush"] = "#4A4A4A", ["PanelIconBrush"] = "#555555",
        ["CanvasOverlayBrush"] = "#B52A2A2A", ["CanvasOverlayLightBrush"] = "#552A2A2A", ["OverlayTextBrush"] = "#EEEEEE",
        ["CanvasTextBrush"] = "#EEEEEE", ["CheckerTileBrush"] = "#565656", ["CardBackgroundBrush"] = "#414141",
        ["CardBorderBrush"] = "#303030", ["SelectedBackgroundBrush"] = "#53664A", ["FieldBorderBrush"] = "#333333",
        ["SuccessBrush"] = "#8BD66F", ["WarningBrush"] = "#F0C27A", ["ErrorBrush"] = "#F2A59B", ["SoftErrorBrush"] = "#FF9D86"
    };

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["AppBackground"] = "#F0F3F0", ["PanelBackground"] = "#FFFFFF", ["PanelAlt"] = "#E8EDE9",
        ["BorderBrush"] = "#C8D1CA", ["TextBrush"] = "#202820", ["MutedBrush"] = "#626E65",
        ["AccentBrush"] = "#8ACB50", ["AccentTextBrush"] = "#172211", ["ButtonBackgroundBrush"] = "#F2F5F2",
        ["ButtonHoverBrush"] = "#E3EAE4", ["ComboHoverBrush"] = "#E3EBE4", ["ComboSelectedBrush"] = "#D3E2D4",
        ["SidebarBackgroundBrush"] = "#E7ECE8", ["PreviewTileBrush"] = "#E5EAE6", ["PreviewCanvasBrush"] = "#D9DFDA",
        ["PreviewBorderBrush"] = "#AAB5AC", ["SubtleTextBrush"] = "#66736A", ["DetailLabelBrush"] = "#58645B",
        ["PrivacyTextBrush"] = "#66736A", ["PanelInsetBrush"] = "#F5F7F5", ["PanelIconBrush"] = "#E9EFEB",
        ["CanvasOverlayBrush"] = "#B5F4F7F4", ["CanvasOverlayLightBrush"] = "#55F4F7F4", ["OverlayTextBrush"] = "#243027",
        ["CanvasTextBrush"] = "#263129", ["CheckerTileBrush"] = "#CAD2CC", ["CardBackgroundBrush"] = "#F2F5F2",
        ["CardBorderBrush"] = "#C8D1CA", ["SelectedBackgroundBrush"] = "#D9EBCB", ["FieldBorderBrush"] = "#C0CBC2",
        ["SuccessBrush"] = "#5F9E38", ["WarningBrush"] = "#A96B14", ["ErrorBrush"] = "#B3362B", ["SoftErrorBrush"] = "#B3362B"
    };

    public static string CurrentTheme { get; private set; } = "dark";

    protected override void OnStartup(StartupEventArgs e)
    {
        ApplyTheme(EditorPreferences.LoadTheme());
        base.OnStartup(e);
    }

    public static void ApplyTheme(string themeId)
    {
        var palette = themeId switch
        {
            "object-build" => ObjectBuildPalette,
            "light" => LightPalette,
            _ => DarkPalette
        };
        foreach (var (key, value) in palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(value)!;
            // Replace resources instead of mutating BAML brushes, which WPF may freeze.
            Current.Resources[key] = new SolidColorBrush(color);
        }
        CurrentTheme = themeId is "object-build" or "light" ? themeId : "dark";
    }
}

internal static class EditorPreferences
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpriteStudio", "preferences.json");

    public static string LoadTheme()
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(SettingsPath));
            var theme = document.RootElement.GetProperty("theme").GetString();
            return theme is "dark" or "object-build" or "light" ? theme : "dark";
        }
        catch { return "dark"; }
    }

    public static void SaveTheme(string theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, System.Text.Json.JsonSerializer.Serialize(new { theme }));
        }
        catch { }
    }
}
