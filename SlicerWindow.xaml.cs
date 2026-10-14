using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace SpriteStudio;

public partial class SlicerWindow : Window
{
    private sealed record SlicedSprite(string Label, string Dimensions, BitmapSource Bitmap);
    private sealed record CropSettings(int X, int Y, int Columns, int Rows, int Size, bool IncludeEmpty, int ImageVersion);
    private sealed record GridSuggestion(int Size, int X, int Y, int Columns, int Rows, int ComponentCount, bool HasVisiblePixels, bool AnalysisSkipped = false);
    private sealed class ForegroundRun(int start, int end, int row, int parent)
    {
        public int Start { get; } = start;
        public int End { get; } = end;
        public int Row { get; } = row;
        public int Parent { get; set; } = parent;
        public byte Rank { get; set; }
        public int PixelCount => End - Start + 1;
    }

    private readonly bool _isEnglish;
    private readonly Func<bool> _canImport;
    private readonly Func<IReadOnlyList<BitmapSource>, Task> _importSprites;
    private readonly DispatcherTimer _previewTimer = new();
    private readonly ObservableCollection<SlicedSprite> _sprites = [];
    private BitmapSource? _source;
    private string? _sourcePath;
    private CropSettings? _lastCrop;
    private Point _dragOffset;
    private double _zoom = 1;
    private int _imageVersion;
    private int _spriteSequence;
    private bool _ready;
    private bool _updatingInputs;
    private bool _dragging;
    private bool _applyingSuggestion;
    private bool _previewPlaying;
    private int _previewIndex;

    public SlicerWindow(bool isEnglish, Func<bool> canImport, Func<IReadOnlyList<BitmapSource>, Task> importSprites)
    {
        InitializeComponent();
        _isEnglish = isEnglish;
        _canImport = canImport;
        _importSprites = importSprites;
        _previewTimer.Interval = TimeSpan.FromMilliseconds(PreviewIntervalSlider.Value);
        _previewTimer.Tick += PreviewTimer_Tick;
        SpriteList.ItemsSource = _sprites;
        ApplyLanguage();
        _ready = true;
        RefreshView();
        UpdateButtons();
    }

    private string Text(string pt, string en) => _isEnglish ? en : pt;

    private int SpriteSize => (SpriteSizeCombo.SelectedItem as ComboBoxItem)?.Tag is string value
        && int.TryParse(value, out var size) ? size : 32;

    private double Zoom => ZoomSlider.Value;

    private void ApplyLanguage()
    {
        Title = "Slicer";
        OpenImageButton.Content = Text("Abrir imagem", "Open image");
        OpenImageButton.ToolTip = Text("Abrir uma imagem para recortar", "Open an image to slice");
        RotateRightButton.ToolTip = Text("Girar 90° à direita", "Rotate 90° clockwise");
        RotateLeftButton.ToolTip = Text("Girar 90° à esquerda", "Rotate 90° counterclockwise");
        FlipVerticalButton.ToolTip = Text("Inverter na vertical", "Flip vertically");
        FlipHorizontalButton.ToolTip = Text("Inverter na horizontal", "Flip horizontally");
        DimensionTitle.Text = Text("Dimensão do sprite", "Sprite Dimension");
        AutoDetectButton.Content = Text("Detectar grade automaticamente", "Detect grid automatically");
        AutoDetectButton.ToolTip = Text("Sugere dimensão e posição com base nos pixels visíveis", "Suggests the size and position based on visible pixels");
        CellsTitle.Text = Text("Células", "Cells");
        SubdivisionsCheckBox.Content = Text("Subdivisões", "Subdivisions");
        ShowTileGridCheckBox.Content = Text("Grade 32 × 32", "32 × 32 grid");
        EmptySpritesCheckBox.Content = Text("Sprites vazios", "Empty Sprites");
        ColumnsLabel.Text = Text("Colunas:", "Columns:");
        RowsLabel.Text = Text("Linhas:", "Rows:");
        ZoomTitle.Text = Text("Zoom", "Zoom");
        CropButton.Content = Text("Recortar", "Crop");
        CanvasTitle.Text = Text("Imagem", "Image");
        CanvasHint.Text = Text("Arraste a área verde para ajustar o recorte", "Drag the green area to adjust the crop");
        DropHint.Text = Text("Abra ou arraste uma imagem aqui", "Open or drop an image here");
        SpritesTitle.Text = Text("Sprites", "Sprites");
        PreviewTitle.Text = Text("Prévia da animação", "Animation preview");
        PreviewIntervalTitle.Text = Text("Intervalo", "Interval");
        UpdatePreviewIntervalLabel();
        UpdatePreviewFrame();
        ImportButton.Content = Text("Importar para SPR", "Import into SPR");
        ImportButton.ToolTip = Text("Adiciona os recortes ao SPR aberto. Somente sprites 32 × 32.", "Adds slices to the open SPR. 32 × 32 sprites only.");
        ExportButton.Content = Text("Exportar PNGs", "Export PNGs");
        ClearButton.Content = Text("Limpar", "Clear");
        if (_source is null)
            SourceInfoText.Text = Text("Arraste uma imagem para começar", "Drop an image to get started");
        UpdateSliceCount();
    }

    private async void OpenImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Text("Abrir imagem para recortar", "Open image to slice"),
            Filter = "Imagens|*.png;*.bmp;*.jpg;*.jpeg;*.gif;*.tif;*.tiff|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            await LoadImageAsync(dialog.FileName);
    }

    private async Task LoadImageAsync(string path)
    {
        try
        {
            var image = await Task.Run(() =>
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return (BitmapSource)bitmap;
            });
            SetSource(image, path, resetGrid: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Text($"Não foi possível abrir a imagem: {ex.Message}", $"Could not open the image: {ex.Message}"),
                "Slicer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetSource(BitmapSource? image, string? path, bool resetGrid)
    {
        StopPreview();
        var dimensionsChanged = _source is null || image is null
            || _source.PixelWidth != image.PixelWidth || _source.PixelHeight != image.PixelHeight;
        _source = image;
        _sourcePath = path;
        _imageVersion++;
        _lastCrop = null;
        _sprites.Clear();
        _spriteSequence = 0;
        _previewIndex = 0;
        PreviewImage.Source = null;
        if (resetGrid || dimensionsChanged)
        {
            _updatingInputs = true;
            try
            {
                SetInputText(XInput, "0");
                SetInputText(YInput, "0");
                SetInputText(ColumnsInput, "1");
                SetInputText(RowsInput, "1");
            }
            finally { _updatingInputs = false; }
            ZoomSlider.Value = 1;
        }
        if (image is null)
            SourceInfoText.Text = Text("Arraste uma imagem para começar", "Drop an image to get started");
        else
            SourceInfoText.Text = $"{Path.GetFileName(path)}  ·  {image.PixelWidth} × {image.PixelHeight} px";
        NormalizeGridOptions();
        RefreshView();
        UpdateButtons();
    }

    private void OpenImageFromDrop(string path)
    {
        var extension = Path.GetExtension(path);
        if (!new[] { ".png", ".bmp", ".jpg", ".jpeg", ".gif", ".tif", ".tiff" }
            .Contains(extension, StringComparer.OrdinalIgnoreCase)) return;
        _ = LoadImageAsync(path);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        var paths = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
        e.Effects = paths?.Any(path => File.Exists(path)) == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            OpenImageFromDrop(paths[0]);
    }

    private void SpriteSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _applyingSuggestion) return;
        NormalizeGridOptions();
        RefreshView();
    }

    private void GridOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        RefreshView();
    }

    private void GridOptions_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updatingInputs) return;
        NormalizeGridOptions();
        RefreshView();
    }

    private void NumericInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        NormalizeGridOptions();
        RefreshView();
    }

    private void DigitsOnly(object sender, TextCompositionEventArgs e) => e.Handled = e.Text.Any(character => !char.IsDigit(character));

    private async void AutoDetectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null) return;
        var source = _source;
        var imageVersion = _imageVersion;
        AutoDetectButton.IsEnabled = false;
        var previousText = SourceInfoText.Text;
        SourceInfoText.Text = Text("Analisando transparência e grade…", "Analyzing transparency and grid…");
        try
        {
            var suggestion = await Task.Run(() => DetectGrid(source, SpriteSize));
            if (_source is null || imageVersion != _imageVersion) return;
            _applyingSuggestion = true;
            try
            {
                SpriteSizeCombo.SelectedIndex = suggestion.Size switch { 64 => 1, 128 => 2, 256 => 3, _ => 0 };
                _updatingInputs = true;
                SetInputText(XInput, suggestion.X.ToString());
                SetInputText(YInput, suggestion.Y.ToString());
                SetInputText(ColumnsInput, suggestion.Columns.ToString());
                SetInputText(RowsInput, suggestion.Rows.ToString());
            }
            finally
            {
                _updatingInputs = false;
                _applyingSuggestion = false;
            }
            NormalizeGridOptions();
            _lastCrop = null;
            RefreshView();
            var detail = Text(
                $"Sugestão: {suggestion.Size} × {suggestion.Size}, {suggestion.Columns} × {suggestion.Rows} célula(s), posição {suggestion.X},{suggestion.Y}.",
                $"Suggestion: {suggestion.Size} × {suggestion.Size}, {suggestion.Columns} × {suggestion.Rows} cell(s), position {suggestion.X},{suggestion.Y}.");
            if (suggestion.AnalysisSkipped)
                detail += Text(" Imagem grande ou complexa; mantida a dimensão atual.", " Image is too large or complex; kept the current size.");
            else if (!suggestion.HasVisiblePixels)
                detail += Text(" Nenhum pixel visível; mantida a dimensão atual.", " No visible pixels; kept the current size.");
            else
                detail += Text($" ({suggestion.ComponentCount} área(s) detectada(s))", $" ({suggestion.ComponentCount} area(s) detected)");
            SourceInfoText.Text = detail;
        }
        catch (Exception ex)
        {
            SourceInfoText.Text = previousText;
            MessageBox.Show(this,
                Text($"Não foi possível detectar a grade: {ex.Message}", $"Could not detect the grid: {ex.Message}"),
                "Slicer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { UpdateButtons(); }
    }

    private static GridSuggestion DetectGrid(BitmapSource source, int currentSize)
    {
        const int maxAnalyzedPixels = 16_000_000;
        const int maxRuns = 500_000;
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        if ((long)width * height > maxAnalyzedPixels)
            return FallbackGrid(source, currentSize, analysisSkipped: true);

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var runs = new List<ForegroundRun>();
        var previousRow = new List<int>();
        var minContentX = width;
        var minContentY = height;
        var maxContentX = -1;
        var maxContentY = -1;

        for (var y = 0; y < height; y++)
        {
            var currentRow = new List<int>();
            var previousRunCursor = 0;
            var x = 0;
            while (x < width)
            {
                while (x < width && IsBackgroundPixel(pixels, y * stride + x * 4)) x++;
                if (x >= width) break;
                var start = x++;
                while (x < width && !IsBackgroundPixel(pixels, y * stride + x * 4)) x++;
                var end = x - 1;
                var runIndex = runs.Count;
                runs.Add(new ForegroundRun(start, end, y, runIndex));
                currentRow.Add(runIndex);
                if (runs.Count > maxRuns) return FallbackGrid(source, currentSize, analysisSkipped: true);

                while (previousRunCursor < previousRow.Count && runs[previousRow[previousRunCursor]].End < start - 1)
                    previousRunCursor++;
                for (var prior = previousRunCursor; prior < previousRow.Count && runs[previousRow[prior]].Start <= end + 1; prior++)
                    UnionRuns(runs, runIndex, previousRow[prior]);
            }
            previousRow = currentRow;
        }

        if (runs.Count == 0) return FallbackGrid(source, currentSize);

        var componentPixels = new Dictionary<int, int>();
        var componentBounds = new Dictionary<int, (int Left, int Top, int Right, int Bottom)>();
        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];
            var root = FindRunRoot(runs, index);
            componentPixels[root] = componentPixels.GetValueOrDefault(root) + run.PixelCount;
            if (componentBounds.TryGetValue(root, out var bounds))
                componentBounds[root] = (Math.Min(bounds.Left, run.Start), Math.Min(bounds.Top, run.Row), Math.Max(bounds.Right, run.End), Math.Max(bounds.Bottom, run.Row));
            else
                componentBounds[root] = (run.Start, run.Row, run.End, run.Row);
        }

        var dimensions = new List<int>();
        foreach (var (root, bounds) in componentBounds)
        {
            if (componentPixels[root] < 4) continue;
            minContentX = Math.Min(minContentX, bounds.Left);
            minContentY = Math.Min(minContentY, bounds.Top);
            maxContentX = Math.Max(maxContentX, bounds.Right);
            maxContentY = Math.Max(maxContentY, bounds.Bottom);
            dimensions.Add(Math.Max(bounds.Right - bounds.Left + 1, bounds.Bottom - bounds.Top + 1));
        }
        if (dimensions.Count == 0 || maxContentX < 0 || maxContentY < 0)
            return FallbackGrid(source, currentSize);

        dimensions.Sort();
        var typicalExtent = dimensions[(int)Math.Floor((dimensions.Count - 1) * 0.85)];
        var size = new[] { 32, 64, 128, 256 }.FirstOrDefault(candidate => candidate >= typicalExtent);
        if (size == 0) size = 256;
        var gridX = minContentX / size * size;
        var gridY = minContentY / size * size;
        var columns = Math.Max(1, (maxContentX - gridX) / size + 1);
        var rows = Math.Max(1, (maxContentY - gridY) / size + 1);
        columns = Math.Min(columns, Math.Max(1, (width - gridX) / size));
        rows = Math.Min(rows, Math.Max(1, (height - gridY) / size));
        gridX = Math.Clamp(gridX, 0, Math.Max(0, width - size));
        gridY = Math.Clamp(gridY, 0, Math.Max(0, height - size));
        return new GridSuggestion(size, gridX, gridY, columns, rows, dimensions.Count, true);
    }

    private static GridSuggestion FallbackGrid(BitmapSource source, int currentSize, bool analysisSkipped = false)
    {
        var size = currentSize is 32 or 64 or 128 or 256 ? currentSize : 32;
        var columns = Math.Max(1, (int)Math.Ceiling((double)source.PixelWidth / size));
        var rows = Math.Max(1, (int)Math.Ceiling((double)source.PixelHeight / size));
        return new GridSuggestion(size, 0, 0, columns, rows, 0, false, analysisSkipped);
    }

    private static bool IsBackgroundPixel(byte[] pixels, int index)
    {
        var alpha = pixels[index + 3];
        if (alpha < 8) return true;
        return pixels[index + 2] == 255 && pixels[index + 1] == 0 && pixels[index] == 255;
    }

    private static int FindRunRoot(List<ForegroundRun> runs, int index)
    {
        var root = index;
        while (runs[root].Parent != root) root = runs[root].Parent;
        while (runs[index].Parent != index)
        {
            var parent = runs[index].Parent;
            runs[index].Parent = root;
            index = parent;
        }
        return root;
    }

    private static void UnionRuns(List<ForegroundRun> runs, int first, int second)
    {
        var rootA = FindRunRoot(runs, first);
        var rootB = FindRunRoot(runs, second);
        if (rootA == rootB) return;
        if (runs[rootA].Rank < runs[rootB].Rank) (rootA, rootB) = (rootB, rootA);
        runs[rootB].Parent = rootA;
        if (runs[rootA].Rank == runs[rootB].Rank) runs[rootA].Rank++;
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _zoom = Zoom;
        ZoomValueText.Text = $"{_zoom * 100:0}%";
        RefreshView();
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sprites.Count < 2) return;
        if (_previewPlaying)
        {
            StopPreview();
            return;
        }
        _previewIndex = 0;
        _previewTimer.Interval = TimeSpan.FromMilliseconds(PreviewIntervalSlider.Value);
        _previewTimer.Start();
        _previewPlaying = true;
        UpdatePreviewFrame();
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (_sprites.Count < 2)
        {
            StopPreview();
            return;
        }
        _previewIndex = (_previewIndex + 1) % _sprites.Count;
        UpdatePreviewFrame();
    }

    private void PreviewIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PreviewIntervalText is null) return;
        UpdatePreviewIntervalLabel();
        _previewTimer.Interval = TimeSpan.FromMilliseconds(PreviewIntervalSlider.Value);
    }

    private void UpdatePreviewIntervalLabel() => PreviewIntervalText.Text = $"{PreviewIntervalSlider.Value:0} ms";

    private void StopPreview()
    {
        _previewTimer.Stop();
        _previewPlaying = false;
        if (PreviewButton is not null)
            PreviewButton.Content = Text("▶ Reproduzir", "▶ Play");
        UpdatePreviewFrame();
    }

    private void UpdatePreviewFrame()
    {
        if (PreviewImage is null || PreviewFrameText is null) return;
        if (_sprites.Count == 0)
        {
            PreviewImage.Source = null;
            PreviewFrameText.Text = "0 / 0";
        }
        else
        {
            _previewIndex = Math.Clamp(_previewIndex, 0, _sprites.Count - 1);
            PreviewImage.Source = _sprites[_previewIndex].Bitmap;
            PreviewFrameText.Text = $"{_previewIndex + 1} / {_sprites.Count}";
        }
        if (PreviewButton is not null)
        {
            PreviewButton.Content = _previewPlaying
                ? Text("Ⅱ Pausar", "Ⅱ Pause")
                : Text("▶ Reproduzir", "▶ Play");
            PreviewButton.IsEnabled = _sprites.Count > 1;
        }
    }

    private bool TryReadOptions(out int x, out int y, out int columns, out int rows)
    {
        x = y = columns = rows = 0;
        return int.TryParse(XInput.Text, out x)
            && int.TryParse(YInput.Text, out y)
            && int.TryParse(ColumnsInput.Text, out columns)
            && int.TryParse(RowsInput.Text, out rows);
    }

    private void NormalizeGridOptions()
    {
        if (_source is null || !TryReadOptions(out var x, out var y, out var columns, out var rows)) return;
        var size = SpriteSize;
        var maxColumns = Math.Max(1, (int)Math.Ceiling((double)_source.PixelWidth / size));
        var maxRows = Math.Max(1, (int)Math.Ceiling((double)_source.PixelHeight / size));
        columns = Math.Clamp(columns, 1, maxColumns);
        rows = Math.Clamp(rows, 1, maxRows);
        var maxX = Math.Max(0, _source.PixelWidth - columns * size);
        var maxY = Math.Max(0, _source.PixelHeight - rows * size);
        x = Math.Clamp(x, 0, maxX);
        y = Math.Clamp(y, 0, maxY);
        _updatingInputs = true;
        try
        {
            SetInputText(XInput, x.ToString());
            SetInputText(YInput, y.ToString());
            SetInputText(ColumnsInput, columns.ToString());
            SetInputText(RowsInput, rows.ToString());
        }
        finally { _updatingInputs = false; }
    }

    private void SetInputText(TextBox input, string text)
    {
        if (input.Text != text) input.Text = text;
    }

    private void RefreshView()
    {
        if (ImageCanvas is null) return;
        ImageCanvas.Children.Clear();
        if (_source is null)
        {
            ImageCanvas.Width = 640;
            ImageCanvas.Height = 480;
            DropHint.Visibility = Visibility.Visible;
            CropButton.IsEnabled = false;
            return;
        }

        DropHint.Visibility = Visibility.Collapsed;
        ImageCanvas.Width = Math.Max(1, _source.PixelWidth * Zoom);
        ImageCanvas.Height = Math.Max(1, _source.PixelHeight * Zoom);
        var image = new Image
        {
            Source = _source,
            Width = ImageCanvas.Width,
            Height = ImageCanvas.Height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        ImageCanvas.Children.Add(image);
        DrawReferenceGrid();
        DrawSelectionOverlay();
        CropButton.IsEnabled = TryReadOptions(out _, out _, out _, out _);
    }

    private void DrawReferenceGrid()
    {
        if (_source is null || ShowTileGridCheckBox.IsChecked != true) return;
        var brush = new SolidColorBrush(Color.FromArgb(190, 178, 112, 255));
        brush.Freeze();
        for (var x = 0; x <= _source.PixelWidth; x += 32)
            AddOverlayLine(x * Zoom, 0, x * Zoom, ImageCanvas.Height, brush, 1.1);
        if (_source.PixelWidth % 32 != 0)
            AddOverlayLine(ImageCanvas.Width, 0, ImageCanvas.Width, ImageCanvas.Height, brush, 1.1);
        for (var y = 0; y <= _source.PixelHeight; y += 32)
            AddOverlayLine(0, y * Zoom, ImageCanvas.Width, y * Zoom, brush, 1.1);
        if (_source.PixelHeight % 32 != 0)
            AddOverlayLine(0, ImageCanvas.Height, ImageCanvas.Width, ImageCanvas.Height, brush, 1.1);
    }

    private void DrawSelectionOverlay()
    {
        if (_source is null || !TryReadOptions(out var x, out var y, out var columns, out var rows)) return;
        var size = SpriteSize;
        var left = x * Zoom;
        var top = y * Zoom;
        var cell = size * Zoom;
        var overlayWidth = columns * cell;
        var overlayHeight = rows * cell;
        var shade = new Rectangle
        {
            Width = overlayWidth,
            Height = overlayHeight,
            Fill = new SolidColorBrush(Color.FromArgb(35, 180, 241, 120)),
            Stroke = new SolidColorBrush(Color.FromRgb(180, 241, 120)),
            StrokeThickness = 1.5,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(shade, left);
        Canvas.SetTop(shade, top);
        ImageCanvas.Children.Add(shade);

        var lineBrush = new SolidColorBrush(Color.FromArgb(180, 220, 244, 198));
        for (var column = 1; column < columns; column++)
            AddOverlayLine(left + column * cell, top, left + column * cell, top + overlayHeight, lineBrush);
        for (var row = 1; row < rows; row++)
            AddOverlayLine(left, top + row * cell, left + overlayWidth, top + row * cell, lineBrush);

        if (SubdivisionsCheckBox.IsChecked == true)
        {
            for (var column = 0; column < columns; column++)
                AddOverlayLine(left + (column + 0.5) * cell, top + 1, left + (column + 0.5) * cell, top + overlayHeight - 1, lineBrush, 0.8);
            for (var row = 0; row < rows; row++)
                AddOverlayLine(left + 1, top + (row + 0.5) * cell, left + overlayWidth - 1, top + (row + 0.5) * cell, lineBrush, 0.8);
        }
    }

    private void AddOverlayLine(double x1, double y1, double x2, double y2, Brush brush, double thickness = 1)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false };
        ImageCanvas.Children.Add(line);
    }

    private void ImageCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_source is null || !TryReadOptions(out var x, out var y, out var columns, out var rows)) return;
        var point = e.GetPosition(ImageCanvas);
        var imageX = point.X / Zoom;
        var imageY = point.Y / Zoom;
        var size = SpriteSize;
        var gridWidth = columns * size;
        var gridHeight = rows * size;
        _dragOffset = imageX >= x && imageX <= x + gridWidth && imageY >= y && imageY <= y + gridHeight
            ? new Point(imageX - x, imageY - y)
            : new Point(gridWidth / 2d, gridHeight / 2d);
        _dragging = true;
        ImageCanvas.CaptureMouse();
        MoveGridTo(imageX - _dragOffset.X, imageY - _dragOffset.Y);
        e.Handled = true;
    }

    private void ImageCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _source is null) return;
        var point = e.GetPosition(ImageCanvas);
        MoveGridTo(point.X / Zoom - _dragOffset.X, point.Y / Zoom - _dragOffset.Y);
    }

    private void ImageCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ImageCanvas.ReleaseMouseCapture();
    }

    private void MoveGridTo(double x, double y)
    {
        if (_source is null || !TryReadOptions(out _, out _, out var columns, out var rows)) return;
        var maxX = Math.Max(0, _source.PixelWidth - columns * SpriteSize);
        var maxY = Math.Max(0, _source.PixelHeight - rows * SpriteSize);
        var newX = Math.Clamp((int)Math.Round(x), 0, maxX);
        var newY = Math.Clamp((int)Math.Round(y), 0, maxY);
        _updatingInputs = true;
        try
        {
            SetInputText(XInput, newX.ToString());
            SetInputText(YInput, newY.ToString());
        }
        finally { _updatingInputs = false; }
        RefreshView();
    }

    private void CropButton_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null || !TryReadOptions(out var x, out var y, out var columns, out var rows)) return;
        NormalizeGridOptions();
        if (!TryReadOptions(out x, out y, out columns, out rows)) return;
        var settings = new CropSettings(x, y, columns, rows, SpriteSize, EmptySpritesCheckBox.IsChecked == true, _imageVersion);
        if (_lastCrop == settings) return;

        var added = 0;
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var bitmap = CreateSlice(x + column * SpriteSize, y + row * SpriteSize, SpriteSize);
            var empty = IsEmpty(bitmap);
            if (empty && EmptySpritesCheckBox.IsChecked != true) continue;
            _spriteSequence++;
            _sprites.Add(new SlicedSprite(
                Text($"Sprite {_spriteSequence}", $"Sprite {_spriteSequence}"),
                $"{SpriteSize} × {SpriteSize} px",
                bitmap));
            added++;
        }
        _lastCrop = settings;
        UpdateButtons();
        UpdateSliceCount();
        if (added == 0)
            SourceInfoText.Text = Text("A área selecionada não contém sprites visíveis.", "The selected area contains no visible sprites.");
        else
            SourceInfoText.Text = Text($"{added} sprite(s) recortado(s)", $"{added} sprite(s) cropped");
    }

    private BitmapSource CreateSlice(int x, int y, int size)
    {
        var pixels = new byte[size * size * 4];
        if (_source is null) return CreateBitmap(size, size, pixels);

        var sourceRect = new Int32Rect(x, y, size, size);
        var clippedLeft = Math.Max(0, sourceRect.X);
        var clippedTop = Math.Max(0, sourceRect.Y);
        var clippedRight = Math.Min(_source.PixelWidth, sourceRect.X + sourceRect.Width);
        var clippedBottom = Math.Min(_source.PixelHeight, sourceRect.Y + sourceRect.Height);
        if (clippedRight > clippedLeft && clippedBottom > clippedTop)
        {
            var cropped = new CroppedBitmap(_source,
                new Int32Rect(clippedLeft, clippedTop, clippedRight - clippedLeft, clippedBottom - clippedTop));
            var converted = new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0);
            var rowBytes = converted.PixelWidth * 4;
            var sourcePixels = new byte[rowBytes * converted.PixelHeight];
            converted.CopyPixels(sourcePixels, rowBytes, 0);
            var destinationX = clippedLeft - x;
            var destinationY = clippedTop - y;
            for (var row = 0; row < converted.PixelHeight; row++)
                Array.Copy(sourcePixels, row * rowBytes, pixels, ((destinationY + row) * size + destinationX) * 4, rowBytes);
        }

        for (var index = 0; index < pixels.Length; index += 4)
            if (pixels[index + 3] != 0 && pixels[index + 2] == 255 && pixels[index + 1] == 0 && pixels[index] == 255)
                pixels[index + 3] = 0;
        return CreateBitmap(size, size, pixels);
    }

    private static BitmapSource CreateBitmap(int width, int height, byte[] pixels)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static bool IsEmpty(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        for (var index = 3; index < pixels.Length; index += 4)
            if (pixels[index] != 0) return false;
        return true;
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sprites.Count == 0) return;
        var dialog = new OpenFolderDialog { Title = Text("Selecionar pasta para salvar os sprites", "Choose a folder to save the sprites") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var sprites = _sprites.Select(sprite => sprite.Bitmap).ToArray();
            var baseName = Path.GetFileNameWithoutExtension(_sourcePath);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "slices";
            var outputFolder = Path.Combine(dialog.FolderName, $"{baseName}-sprites-{DateTime.Now:yyyyMMdd-HHmmss}");
            await Task.Run(() =>
            {
                Directory.CreateDirectory(outputFolder);
                for (var index = 0; index < sprites.Length; index++)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(sprites[index]));
                    using var output = File.Create(Path.Combine(outputFolder, $"sprite-{index + 1:0000}.png"));
                    encoder.Save(output);
                }
            });
            MessageBox.Show(this,
                Text($"{sprites.Length} sprites foram salvos em:{Environment.NewLine}{outputFolder}", $"{sprites.Length} sprites were saved to:{Environment.NewLine}{outputFolder}"),
                Text("Exportação concluída", "Export complete"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Text($"Não foi possível salvar os sprites: {ex.Message}", $"Could not save the sprites: {ex.Message}"),
                Text("Falha ao exportar", "Export failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sprites.Count == 0) return;
        if (SpriteSize != 32)
        {
            MessageBox.Show(this,
                Text("A importação para SPR aceita sprites de 32 × 32. Escolha essa dimensão ou exporte os recortes como PNG.",
                    "SPR import accepts 32 × 32 sprites. Choose that dimension or export the slices as PNGs."),
                "Slicer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!_canImport())
        {
            MessageBox.Show(this,
                Text("Abra um arquivo SPR no Editor Universe antes de importar.", "Open an SPR file in Editor Universe before importing."),
                "Slicer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ImportButton.IsEnabled = false;
        try { await _importSprites(_sprites.Select(sprite => sprite.Bitmap).ToArray()); }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                Text($"Não foi possível importar os sprites: {ex.Message}", $"Could not import the sprites: {ex.Message}"),
                Text("Falha ao importar", "Import failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { UpdateButtons(); }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        StopPreview();
        _sprites.Clear();
        _spriteSequence = 0;
        _lastCrop = null;
        UpdateButtons();
        UpdateSliceCount();
        if (_source is not null)
            SourceInfoText.Text = $"{Path.GetFileName(_sourcePath)}  ·  {_source.PixelWidth} × {_source.PixelHeight} px";
    }

    private void UpdateButtons()
    {
        var hasImage = _source is not null;
        RotateRightButton.IsEnabled = RotateLeftButton.IsEnabled = FlipVerticalButton.IsEnabled = FlipHorizontalButton.IsEnabled = hasImage;
        AutoDetectButton.IsEnabled = hasImage;
        CropButton.IsEnabled = hasImage;
        ExportButton.IsEnabled = _sprites.Count > 0;
        ImportButton.IsEnabled = _sprites.Count > 0 && SpriteSize == 32 && _canImport();
        if (PreviewButton is not null) PreviewButton.IsEnabled = _sprites.Count > 1;
    }

    public void RefreshImportAvailability() => UpdateButtons();

    private void UpdateSliceCount()
    {
        SliceCountText.Text = Text($"{_sprites.Count} sprite(s)", $"{_sprites.Count} sprite(s)");
        UpdatePreviewFrame();
        if (_sprites.Count < 2 && _previewPlaying) StopPreview();
    }

    private void RotateRightButton_Click(object sender, RoutedEventArgs e) => TransformSource(new RotateTransform(90, _source!.PixelWidth / 2d, _source.PixelHeight / 2d));
    private void RotateLeftButton_Click(object sender, RoutedEventArgs e) => TransformSource(new RotateTransform(-90, _source!.PixelWidth / 2d, _source.PixelHeight / 2d));
    private void FlipVerticalButton_Click(object sender, RoutedEventArgs e) => TransformSource(new ScaleTransform(1, -1, _source!.PixelWidth / 2d, _source.PixelHeight / 2d));
    private void FlipHorizontalButton_Click(object sender, RoutedEventArgs e) => TransformSource(new ScaleTransform(-1, 1, _source!.PixelWidth / 2d, _source.PixelHeight / 2d));

    private void TransformSource(Transform transform)
    {
        if (_source is null) return;
        var bounds = transform.TransformBounds(new Rect(0, 0, _source.PixelWidth, _source.PixelHeight));
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            context.PushTransform(transform);
            context.DrawImage(_source, new Rect(0, 0, _source.PixelWidth, _source.PixelHeight));
            context.Pop();
            context.Pop();
        }
        var transformed = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        transformed.Render(visual);
        transformed.Freeze();
        SetSource(transformed, _sourcePath, resetGrid: false);
    }

}
