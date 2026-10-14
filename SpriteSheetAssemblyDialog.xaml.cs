using System.Windows;
using System.Windows.Controls;

namespace SpriteStudio;

internal sealed record SpriteSheetAssemblyOptions(int Width, int Height, int FrameCount, string Category);

public partial class SpriteSheetAssemblyDialog : Window
{
    private readonly bool _isEnglish;
    private readonly int _imageWidth;
    private readonly int _imageHeight;
    private bool _initializing = true;

    internal SpriteSheetAssemblyOptions? Options { get; private set; }

    public SpriteSheetAssemblyDialog(string fileName, int imageWidth, int imageHeight, bool isEnglish,
        int initialWidth, int initialHeight, int initialFrameCount, string initialCategory)
    {
        InitializeComponent();
        _isEnglish = isEnglish;
        _imageWidth = imageWidth;
        _imageHeight = imageHeight;
        ApplyLanguage();
        SourceInfoText.Text = Text($"{System.IO.Path.GetFileName(fileName)} · {imageWidth:N0} × {imageHeight:N0} px",
            $"{System.IO.Path.GetFileName(fileName)} · {imageWidth:N0} × {imageHeight:N0} px");
        WidthInput.Text = initialWidth.ToString();
        HeightInput.Text = initialHeight.ToString();
        FrameCountInput.Text = initialFrameCount.ToString();
        CategoryCombo.SelectedItem = CategoryCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == initialCategory) ?? CategoryCombo.Items.OfType<ComboBoxItem>().First();
        _initializing = false;
        UpdateOptions();
    }

    private string Text(string pt, string en) => _isEnglish ? en : pt;

    private void ApplyLanguage()
    {
        Title = Text("Montar spritesheet", "Assemble sprite sheet");
        TitleText.Text = Text("Montar um novo objeto", "Assemble a new object");
        WidthLabel.Text = Text("Largura (sprites)", "Width (sprites)");
        HeightLabel.Text = Text("Altura (sprites)", "Height (sprites)");
        FrameCountLabel.Text = Text("Animações / quadros", "Animations / frames");
        CategoryLabel.Text = Text("Criar como", "Create as");
        CategoryCombo.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "item").Content = Text("Item", "Item");
        CategoryCombo.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "effect").Content = Text("Efeito", "Effect");
        CancelButton.Content = Text("Cancelar", "Cancel");
        AssembleButton.Content = Text("Montar", "Assemble");
    }

    private void Options_TextChanged(object sender, RoutedEventArgs e)
    {
        if (!_initializing) UpdateOptions();
    }

    private void UpdateOptions()
    {
        Options = null;
        AssembleButton.IsEnabled = false;
        if (!int.TryParse(WidthInput.Text, out var width) || width is < 1 or > byte.MaxValue
            || !int.TryParse(HeightInput.Text, out var height) || height is < 1 or > byte.MaxValue
            || !int.TryParse(FrameCountInput.Text, out var frameCount) || frameCount is < 1 or > byte.MaxValue)
        {
            ValidationText.Text = Text("Informe largura, altura e quadros entre 1 e 255.",
                "Enter width, height, and frame count from 1 to 255.");
            return;
        }

        var frameWidth = (long)width * 32;
        var frameHeight = (long)height * 32;
        var spriteCount = (long)width * height * frameCount;
        FrameSizeText.Text = Text($"Cada quadro: {frameWidth:N0} × {frameHeight:N0} px · {width * height:N0} sprites",
            $"Each frame: {frameWidth:N0} × {frameHeight:N0} px · {width * height:N0} sprites");
        if (spriteCount > 16_384)
        {
            ValidationText.Text = Text("Essa montagem ultrapassa o limite de 16.384 sprites por importação.",
                "This assembly exceeds the 16,384-sprite import limit.");
            SheetLayoutText.Text = Text("Reduza as dimensões ou a quantidade de quadros.", "Reduce the dimensions or frame count.");
            return;
        }

        if (_imageWidth % frameWidth != 0 || _imageHeight % frameHeight != 0)
        {
            ValidationText.Text = Text("As dimensões da imagem precisam ser divisíveis pelo tamanho de cada quadro.",
                "The image dimensions must be divisible by the size of each frame.");
            SheetLayoutText.Text = Text("Use uma spritesheet em grade regular, sem espaços entre os quadros.",
                "Use a regular sprite-sheet grid with no gaps between frames.");
            return;
        }

        var columns = (int)(_imageWidth / frameWidth);
        var rows = (int)(_imageHeight / frameHeight);
        var availableFrames = (long)columns * rows;
        SheetLayoutText.Text = Text(
            $"Grade detectada: {columns} × {rows} quadros. Leitura da esquerda para a direita e de cima para baixo; os quadros excedentes serão ignorados. Duração inicial: 100 ms.",
            $"Detected grid: {columns} × {rows} frames. Read left to right, then top to bottom; extra frames will be ignored. Initial duration: 100 ms.");
        if (frameCount > availableFrames)
        {
            ValidationText.Text = Text($"A imagem contém {availableFrames:N0} quadro(s), mas foram solicitados {frameCount:N0}.",
                $"The image contains {availableFrames:N0} frame(s), but {frameCount:N0} were requested.");
            return;
        }

        var category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (category is not ("item" or "effect"))
        {
            ValidationText.Text = Text("Escolha Item ou Efeito.", "Choose Item or Effect.");
            return;
        }

        ValidationText.Text = string.Empty;
        Options = new SpriteSheetAssemblyOptions(width, height, frameCount, category);
        AssembleButton.IsEnabled = true;
    }

    private void AssembleButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateOptions();
        if (Options is not null) DialogResult = true;
    }
}
