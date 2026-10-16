using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SpriteStudio;

internal sealed record CompileAssetOptions(
    string Name,
    string OutputFolder,
    DatTargetVersion TargetVersion,
    bool Extended,
    bool Transparency,
    bool ImprovedAnimations,
    bool FrameGroups,
    bool ExportServerItems,
    string ServerItemsFolder,
    string ServerItemsFormat);

public partial class CompileAssetDialog : Window
{
    private readonly bool _hasServerItems;
    private readonly bool _hasXml;
    private bool _initializing = true;

    internal CompileAssetOptions? Options { get; private set; }

    public CompileAssetDialog(string name, string outputFolder, string serverItemsFolder,
        int currentVersion, bool extended, bool transparency, bool improvedAnimations, bool frameGroups,
        bool hasServerItems, bool hasXml)
    {
        InitializeComponent();
        NameInput.Text = name;
        OutputFolderInput.Text = outputFolder;
        ServerFolderInput.Text = serverItemsFolder;
        _hasServerItems = hasServerItems;
        _hasXml = hasXml;

        var versions = ClientVersionCatalog.GetDatCompileVersions();
        VersionCombo.ItemsSource = versions;
        VersionCombo.SelectedItem = versions.LastOrDefault(version => version.Value == currentVersion)
            ?? versions.LastOrDefault();

        ServerExportCheckBox.IsEnabled = hasServerItems;
        ServerExportUnavailableText.Visibility = hasServerItems ? Visibility.Collapsed : Visibility.Visible;
        ServerFileTypeCombo.Items.Add(new ComboBoxItem { Content = "items.otb", Tag = "otb" });
        if (hasXml) ServerFileTypeCombo.Items.Add(new ComboBoxItem { Content = "items.xml", Tag = "xml" });
        ServerFileTypeCombo.SelectedIndex = 0;
        ExtendedCheckBox.IsChecked = extended;
        TransparencyCheckBox.IsChecked = transparency;
        ImprovedAnimationsCheckBox.IsChecked = improvedAnimations;
        FrameGroupsCheckBox.IsChecked = frameGroups;
        _initializing = false;
        ApplyVersionDefaults();
    }

    private void VersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) ApplyVersionDefaults();
    }

    private void ApplyVersionDefaults()
    {
        if (VersionCombo.SelectedItem is not DatTargetVersion version) return;
        var requiresExtended = version.Value >= 960;
        ExtendedCheckBox.IsEnabled = !requiresExtended;
        ExtendedCheckBox.IsChecked = ExtendedCheckBox.IsChecked == true || requiresExtended;

        var requiresImprovedAnimations = version.Value >= 1050;
        ImprovedAnimationsCheckBox.IsEnabled = !requiresImprovedAnimations;
        ImprovedAnimationsCheckBox.IsChecked = ImprovedAnimationsCheckBox.IsChecked == true || requiresImprovedAnimations;

        // Frame Groups can be selected for older target versions as well; newer versions default to it.
        FrameGroupsCheckBox.IsEnabled = true;
        FrameGroupsCheckBox.IsChecked = FrameGroupsCheckBox.IsChecked == true || version.Value >= 1057;
    }

    private void ServerExportCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (ServerExportFields is not null)
            ServerExportFields.IsEnabled = _hasServerItems && ServerExportCheckBox.IsChecked == true;
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e) => BrowseFolder(OutputFolderInput, "Selecionar pasta de destino");
    private void BrowseServerButton_Click(object sender, RoutedEventArgs e) => BrowseFolder(ServerFolderInput, "Selecionar pasta de dados do servidor");

    private void BrowseFolder(TextBox target, string title)
    {
        var initial = Directory.Exists(target.Text)
            ? target.Text
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = initial, Multiselect = false };
        if (dialog.ShowDialog(this) == true) target.Text = dialog.FolderName;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameInput.Text.Trim();
        var outputFolder = OutputFolderInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name is "." or "..")
        {
            ValidationText.Text = "Digite um nome válido para os arquivos compilados.";
            return;
        }
        if (name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            ValidationText.Text = "Escolha uma pasta de destino.";
            return;
        }
        if (VersionCombo.SelectedItem is not DatTargetVersion version)
        {
            ValidationText.Text = "Selecione a versão do cliente.";
            return;
        }

        var exportServerItems = ServerExportCheckBox.IsEnabled && ServerExportCheckBox.IsChecked == true;
        var serverItemsFolder = ServerFolderInput.Text.Trim();
        var serverFormat = (ServerFileTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "otb";
        if (exportServerItems && string.IsNullOrWhiteSpace(serverItemsFolder))
        {
            ValidationText.Text = "Escolha a pasta de destino dos dados do servidor.";
            return;
        }

        try
        {
            outputFolder = Path.GetFullPath(outputFolder);
            if (exportServerItems) serverItemsFolder = Path.GetFullPath(serverItemsFolder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ValidationText.Text = $"O caminho de destino é inválido: {ex.Message}";
            return;
        }

        Options = new CompileAssetOptions(name, outputFolder, version,
            ExtendedCheckBox.IsChecked == true || version.Value >= 960,
            TransparencyCheckBox.IsChecked == true,
            ImprovedAnimationsCheckBox.IsChecked == true || version.Value >= 1050,
            FrameGroupsCheckBox.IsChecked == true || version.Value >= 1057,
            exportServerItems, serverItemsFolder, serverFormat);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
