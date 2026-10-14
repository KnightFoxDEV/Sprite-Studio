using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SpriteStudio;

internal sealed record ExportVersionOption(int Value, string Name);
internal sealed record ObdVersionOption(int Value, string Label);

internal sealed class DatExportOptions
{
    public required string Name { get; init; }
    public required string Folder { get; init; }
    public required string Category { get; init; }
    public required bool UseRange { get; init; }
    public required int RangeFrom { get; init; }
    public required int RangeTo { get; init; }
    public required string Format { get; init; }
    public required int ClientVersion { get; init; }
    public required string ClientVersionName { get; init; }
    public required int ObdVersion { get; init; }
    public required bool TransparentBackground { get; init; }
    public required int JpegQuality { get; init; }
}

public partial class DatExportDialog : Window
{
    internal DatExportOptions? Options { get; private set; }
    private readonly IReadOnlyDictionary<string, int> _categoryCounts;
    private readonly bool _isEnglish;
    private bool _updatingCategory = true;

    public DatExportDialog(string name, string folder, int currentVersion, string currentVersionName,
        bool showObdV3, bool preferObdV3, string category, IReadOnlyDictionary<string, int> categoryCounts,
        bool isEnglish, bool useRangeInitially)
    {
        InitializeComponent();
        _categoryCounts = categoryCounts;
        _isEnglish = isEnglish;
        NameInput.Text = name;
        FolderInput.Text = folder;

        var versions = ClientVersionCatalog.GetExportVersions()
            .Select(version => new ExportVersionOption(version.Value, version.Name)).ToArray();
        VersionCombo.ItemsSource = versions;
        VersionCombo.SelectedItem = versions.FirstOrDefault(version =>
            string.Equals(version.Name, currentVersionName, StringComparison.OrdinalIgnoreCase))
            ?? versions.LastOrDefault(version => version.Value == currentVersion)
            ?? versions.LastOrDefault();

        var obdVersions = new List<ObdVersionOption>();
        if (showObdV3) obdVersions.Add(new ObdVersionOption(3, "v3"));
        obdVersions.Add(new ObdVersionOption(2, "v2"));
        obdVersions.Add(new ObdVersionOption(1, "v1"));
        ObdVersionCombo.ItemsSource = obdVersions;
        ObdVersionCombo.SelectedItem = obdVersions.FirstOrDefault(version => version.Value == (preferObdV3 ? 3 : 2));
        CategoryCombo.SelectedItem = CategoryCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == category) ?? CategoryCombo.Items.OfType<ComboBoxItem>().First();
        RangeFromInput.Text = "0";
        RangeToInput.Text = GetCategoryMaximumIndex().ToString();
        UseRangeCheckBox.IsChecked = useRangeInitially;
        _updatingCategory = false;
        ApplyLanguage();
        UpdateFormatOptions();
        UpdateRangeControls();
    }

    private string Text(string portuguese, string english) => _isEnglish ? english : portuguese;

    private string SelectedCategory => (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "item";

    private int GetCategoryMaximumIndex() => Math.Max(0, _categoryCounts.GetValueOrDefault(SelectedCategory) - 1);

    private void ApplyLanguage()
    {
        Title = Text("Exportar", "Export");
        CategoryLabel.Text = Text("Categoria", "Category");
        ExportRangeGroup.Header = Text("Faixa de exportação", "Export Range");
        UseRangeCheckBox.Content = Text("Usar faixa", "Use Range");
        RangeFromLabel.Text = Text("De:", "From:");
        RangeToLabel.Text = Text("Até:", "To:");
        MaxRangeButton.Content = "MAX";
        CategoryCombo.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "item").Content = Text("Itens", "Items");
        CategoryCombo.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "outfit").Content = "Outfits";
        CategoryCombo.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "effect").Content = Text("Efeitos", "Effects");
        CategoryCombo.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "missile").Content = Text("Projéteis", "Missiles");
    }

    private void Dialog_Loaded(object sender, RoutedEventArgs e) => UpdateFormatOptions();

    private void FormatRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) UpdateFormatOptions();
    }

    private void CategoryCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_updatingCategory) return;
        RangeToInput.Text = GetCategoryMaximumIndex().ToString();
        UpdateRangeControls();
    }

    private void UseRangeCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateRangeControls();

    private void UpdateRangeControls()
    {
        if (RangeInputsPanel is not null)
            RangeInputsPanel.IsEnabled = UseRangeCheckBox.IsChecked == true;
    }

    private void MaxRangeButton_Click(object sender, RoutedEventArgs e) => RangeToInput.Text = GetCategoryMaximumIndex().ToString();

    private void UpdateFormatOptions()
    {
        if (PngOptions is null) return;
        PngOptions.Visibility = PngRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        JpgOptions.Visibility = JpgRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ObdOptions.Visibility = ObdRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TransparentBackgroundCheck.IsEnabled = PngRadio.IsChecked == true;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecionar pasta de destino",
            InitialDirectory = Directory.Exists(FolderInput.Text) ? FolderInput.Text : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) FolderInput.Text = dialog.FolderName;
    }

    private void JpgQualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (JpgQualityText is not null) JpgQualityText.Text = $"{(int)JpgQualitySlider.Value}%";
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameInput.Text.Trim();
        var folder = FolderInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ValidationText.Text = "Digite um nome válido para os arquivos exportados.";
            return;
        }
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            ValidationText.Text = "Escolha uma pasta de destino existente.";
            return;
        }
        if (ObdRadio.IsChecked == true && VersionCombo.SelectedItem is not ExportVersionOption)
        {
            ValidationText.Text = "Selecione a versão do cliente para o arquivo OBD.";
            return;
        }

        var useRange = UseRangeCheckBox.IsChecked == true;
        var rangeFrom = 0;
        var rangeTo = GetCategoryMaximumIndex();
        if (useRange && (!int.TryParse(RangeFromInput.Text, out rangeFrom)
            || !int.TryParse(RangeToInput.Text, out rangeTo)
            || rangeFrom < 0 || rangeTo < rangeFrom || rangeTo > GetCategoryMaximumIndex()))
        {
            ValidationText.Text = Text("Informe uma faixa válida para a categoria selecionada.", "Enter a valid range for the selected category.");
            return;
        }

        var selectedVersion = (ExportVersionOption?)VersionCombo.SelectedItem;
        var selectedObdVersion = (ObdVersionOption?)ObdVersionCombo.SelectedItem;
        Options = new DatExportOptions
        {
            Name = name,
            Folder = folder,
            Category = SelectedCategory,
            UseRange = useRange,
            RangeFrom = rangeFrom,
            RangeTo = rangeTo,
            Format = PngRadio.IsChecked == true ? "png"
                : BmpRadio.IsChecked == true ? "bmp"
                : JpgRadio.IsChecked == true ? "jpg" : "obd",
            ClientVersion = selectedVersion?.Value ?? 0,
            ClientVersionName = selectedVersion?.Name ?? string.Empty,
            ObdVersion = selectedObdVersion?.Value ?? 2,
            TransparentBackground = PngRadio.IsChecked == true && TransparentBackgroundCheck.IsChecked == true,
            JpegQuality = (int)JpgQualitySlider.Value
        };
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
