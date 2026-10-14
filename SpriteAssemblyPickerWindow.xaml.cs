using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpriteStudio;

public partial class SpriteAssemblyPickerWindow : Window
{
    private const int CardWidthWithMargin = 151;
    private const int BitmapCacheLimit = 500;

    private sealed class SpritePickerRow(uint firstId, int count)
    {
        public IEnumerable<uint> SpriteIds => Enumerable.Range(0, count).Select(index => firstId + (uint)index);
    }

    private readonly uint _spriteCount;
    private readonly HashSet<uint> _selectedIds;
    private readonly Func<uint, Task<BitmapSource?>> _loadSprite;
    private readonly bool _isEnglish;
    private readonly Dictionary<uint, Border> _visibleCards = [];
    private readonly Dictionary<uint, BitmapSource> _bitmapCache = [];
    private readonly Queue<uint> _bitmapCacheOrder = new();
    private List<SpritePickerRow> _rows = [];
    private uint? _selectionAnchorId;
    private uint _currentSpriteId;
    private int _columns = 8;
    private int? _requiredSpriteCount;
    private string? _assemblyOptionsError;
    private bool _initializingOptions = true;
    private bool _isLoaded;
    private bool _updatingSpriteId;

    public IReadOnlyCollection<uint> SelectedSpriteIds => _selectedIds;
    public uint CurrentSpriteId => _currentSpriteId;
    public int AssemblyWidth { get; private set; }
    public int AssemblyHeight { get; private set; }
    public int AnimationCount { get; private set; }
    public string DestinationCategory { get; private set; } = "item";

    public SpriteAssemblyPickerWindow(uint spriteCount, IEnumerable<uint> selectedIds, uint currentSpriteId,
        Func<uint, Task<BitmapSource?>> loadSprite, bool isEnglish, int initialWidth, int initialHeight,
        int initialAnimationCount, string initialCategory)
    {
        InitializeComponent();
        _spriteCount = spriteCount;
        _selectedIds = selectedIds.Where(id => id > 0 && id <= spriteCount).ToHashSet();
        _loadSprite = loadSprite;
        _isEnglish = isEnglish;
        _currentSpriteId = spriteCount == 0 ? 0 : Math.Clamp(currentSpriteId, 1u, spriteCount);
        _selectionAnchorId = _selectedIds.Contains(_currentSpriteId) ? _currentSpriteId : null;
        SpriteIdInput.Text = _currentSpriteId.ToString();
        ApplyLanguage();
        WidthInput.Text = initialWidth.ToString();
        HeightInput.Text = initialHeight.ToString();
        AnimationCountInput.Text = initialAnimationCount.ToString();
        DestinationCombo.SelectedItem = DestinationCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == initialCategory)
            ?? DestinationCombo.Items.OfType<ComboBoxItem>().First();
        _initializingOptions = false;
        UpdateAssemblyOptions();
        RebuildRows();
        UpdateSelectionState();
    }

    private string Text(string pt, string en) => _isEnglish ? en : pt;

    private Brush ThemeBrush(string key) => (Brush)FindResource(key);

    private void ApplyLanguage()
    {
        Title = Text("Montar sprites da SPR", "Assemble SPR sprites");
        TitleText.Text = Text("Selecione sprites da SPR", "Select sprites from the SPR");
        InstructionsText.Text = Text(
            "Defina a grade, as animações e a categoria. Selecione exatamente a quantidade de sprites indicada; Shift + clique seleciona o intervalo e Ctrl + clique adiciona ou remove.",
            "Set the grid, animation count, and category. Select exactly the required number of sprites; Shift-click selects a range and Ctrl-click adds or removes.");
        WidthLabel.Text = Text("Largura (sprites)", "Width (sprites)");
        HeightLabel.Text = Text("Altura (sprites)", "Height (sprites)");
        AnimationCountLabel.Text = Text("Animações", "Animations");
        DestinationLabel.Text = Text("Criar em", "Create in");
        DimensionFormulaText.Text = Text("Sprites necessários = largura × altura × animações",
            "Required sprites = width × height × animations");
        DestinationCombo.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "item").Content = Text("Item", "Item");
        DestinationCombo.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == "effect").Content = Text("Efeito", "Effect");
        SpriteIdLabel.Text = Text("Ir para ID", "Go to ID");
        JumpToSpriteButton.Content = Text("Localizar", "Find");
        SelectVisibleButton.Content = Text("Selecionar visíveis", "Select visible");
        ClearSelectionButton.Content = Text("Limpar seleção", "Clear selection");
        SelectionHintText.Text = Text("A montagem cria um novo objeto DAT. Salve o DAT para gravar a alteração.",
            "Assembly creates a new DAT object. Save the DAT to keep the change.");
        CancelButton.Content = Text("Cancelar", "Cancel");
        ListStatusText.Text = Text("Lista completa", "Full list");
    }

    private void PickerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        RebuildRowsForWidth();
        ScrollToCurrentSprite();
    }

    private void PickerWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isLoaded) RebuildRowsForWidth();
    }

    private void RebuildRowsForWidth()
    {
        var width = SpriteRowsList.ActualWidth;
        if (width <= 0) return;
        var columns = Math.Clamp((int)((width - 24) / CardWidthWithMargin), 1, 24);
        if (columns == _columns) return;
        _columns = columns;
        RebuildRows();
        ScrollToCurrentSprite();
    }

    private void RebuildRows()
    {
        _visibleCards.Clear();
        var rowCount = ((ulong)_spriteCount + (uint)_columns - 1) / (uint)_columns;
        var rows = new List<SpritePickerRow>((int)rowCount);
        for (ulong rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var firstId = (uint)(rowIndex * (uint)_columns + 1);
            var count = (int)Math.Min((ulong)_columns, (ulong)_spriteCount - firstId + 1);
            rows.Add(new SpritePickerRow(firstId, count));
        }
        _rows = rows;
        SpriteRowsList.ItemsSource = _rows;
        SpriteRangeText.Text = Text($"{_spriteCount:N0} sprites na SPR", $"{_spriteCount:N0} sprites in the SPR");
        ListStatusText.Text = Text("Lista completa · sem páginas", "Full list · no pages");
    }

    private void ScrollToCurrentSprite()
    {
        if (_currentSpriteId == 0 || _rows.Count == 0) return;
        var rowIndex = (int)((_currentSpriteId - 1) / (uint)_columns);
        if (rowIndex >= 0 && rowIndex < _rows.Count) SpriteRowsList.ScrollIntoView(_rows[rowIndex]);
    }

    private void SpriteCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border card || card.DataContext is not uint id) return;
        _visibleCards[id] = card;
        UpdateCardAppearance(card, id);
        if (_bitmapCache.TryGetValue(id, out var cachedBitmap))
        {
            SetCardBitmap(card, cachedBitmap);
            return;
        }
        _ = LoadSpritePreviewAsync(id, card);
    }

    private void SpriteCard_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border card || card.DataContext is not uint id) return;
        if (_visibleCards.TryGetValue(id, out var visibleCard) && ReferenceEquals(visibleCard, card))
            _visibleCards.Remove(id);
    }

    private async Task LoadSpritePreviewAsync(uint id, Border card)
    {
        BitmapSource? bitmap;
        try { bitmap = await _loadSprite(id); }
        catch { return; }
        if (bitmap is null) return;
        if (!_bitmapCache.ContainsKey(id))
        {
            _bitmapCache[id] = bitmap;
            _bitmapCacheOrder.Enqueue(id);
            while (_bitmapCache.Count > BitmapCacheLimit && _bitmapCacheOrder.TryDequeue(out var oldest))
                _bitmapCache.Remove(oldest);
        }
        if (_visibleCards.TryGetValue(id, out var visibleCard) && ReferenceEquals(visibleCard, card))
            SetCardBitmap(card, bitmap);
    }

    private static void SetCardBitmap(Border card, BitmapSource bitmap)
    {
        if (card.Child is StackPanel stack && stack.Children[0] is Border thumbnail && thumbnail.Child is Image image)
            image.Source = bitmap;
    }

    private void SpriteCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border card || card.DataContext is not uint id || e.ChangedButton != MouseButton.Left) return;
        SelectSpriteWithModifiers(id, Keyboard.Modifiers);
        e.Handled = true;
    }

    private void SelectSpriteWithModifiers(uint id, ModifierKeys modifiers)
    {
        if (modifiers.HasFlag(ModifierKeys.Shift) && _selectionAnchorId is uint anchor)
        {
            if (!modifiers.HasFlag(ModifierKeys.Control)) _selectedIds.Clear();
            var first = Math.Min(anchor, id);
            var last = Math.Max(anchor, id);
            for (var current = first; ; current++)
            {
                _selectedIds.Add(current);
                if (current == last) break;
            }
        }
        else if (modifiers.HasFlag(ModifierKeys.Control))
        {
            if (!_selectedIds.Add(id)) _selectedIds.Remove(id);
            _selectionAnchorId = id;
        }
        else
        {
            _selectedIds.Clear();
            _selectedIds.Add(id);
            _selectionAnchorId = id;
        }
        _currentSpriteId = id;
        SetSpriteIdInput(id.ToString());
        UpdateVisibleCardAppearances();
        UpdateSelectionState();
    }

    private void UpdateVisibleCardAppearances()
    {
        foreach (var (id, card) in _visibleCards.ToArray()) UpdateCardAppearance(card, id);
    }

    private void UpdateCardAppearance(Border card, uint id)
    {
        var selected = _selectedIds.Contains(id);
        card.Background = selected ? ThemeBrush("SelectedBackgroundBrush") : ThemeBrush("CardBackgroundBrush");
        card.BorderBrush = selected ? ThemeBrush("AccentBrush") : ThemeBrush("CardBorderBrush");
        card.ToolTip = selected
            ? Text($"Sprite {id:N0} — selecionado", $"Sprite {id:N0} — selected")
            : Text($"Sprite {id:N0} — clique para selecionar", $"Sprite {id:N0} — click to select");
    }

    private void UpdateSelectionState()
    {
        var count = _selectedIds.Count;
        var categoryName = DestinationCategory switch { "item" => Text("Item", "Item"), "effect" => Text("Efeito", "Effect"), _ => DestinationCategory };
        if (_requiredSpriteCount is not int required)
        {
            SelectionCountText.Text = Text($"{count:N0} sprites selecionados", $"{count:N0} sprites selected");
            OptionsHintText.Text = _assemblyOptionsError ?? Text("Confira as dimensões informadas.", "Check the dimensions.");
            AssembleButton.Content = Text("Corrija a montagem", "Fix assembly settings");
            AssembleButton.IsEnabled = false;
        }
        else
        {
            SelectionCountText.Text = Text($"{count:N0} / {required:N0} selecionados", $"{count:N0} / {required:N0} selected");
            if (count < required)
                OptionsHintText.Text = Text($"Selecione mais {required - count:N0} sprite(s).", $"Select {required - count:N0} more sprite(s).");
            else if (count > required)
                OptionsHintText.Text = Text($"Remova {count - required:N0} sprite(s) da seleção.", $"Remove {count - required:N0} sprite(s) from the selection.");
            else
                OptionsHintText.Text = Text($"Pronto: {required:N0} sprites para {categoryName}.", $"Ready: {required:N0} sprites for {categoryName}.");
            AssembleButton.Content = Text($"Montar ({count:N0}/{required:N0})", $"Assemble ({count:N0}/{required:N0})");
            AssembleButton.IsEnabled = count == required;
        }
        ClearSelectionButton.IsEnabled = count > 0;
        SelectVisibleButton.IsEnabled = _visibleCards.Count > 0;
    }

    private void UpdateAssemblyOptions()
    {
        _assemblyOptionsError = null;
        _requiredSpriteCount = null;
        var category = (DestinationCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (!int.TryParse(WidthInput.Text, out var width) || width is < 1 or > byte.MaxValue
            || !int.TryParse(HeightInput.Text, out var height) || height is < 1 or > byte.MaxValue
            || !int.TryParse(AnimationCountInput.Text, out var animations) || animations is < 1 or > byte.MaxValue
            || category is not ("item" or "effect"))
        {
            _assemblyOptionsError = Text("Largura, altura e animações precisam ficar entre 1 e 255.",
                "Width, height, and animations must be between 1 and 255.");
            UpdateSelectionState();
            return;
        }

        var required = (long)width * height * animations;
        if (required > 1_000_000)
        {
            _assemblyOptionsError = Text("Essa montagem excede o limite de 1.000.000 de referências do DAT.",
                "This assembly exceeds the DAT limit of 1,000,000 references.");
            UpdateSelectionState();
            return;
        }

        AssemblyWidth = width;
        AssemblyHeight = height;
        AnimationCount = animations;
        DestinationCategory = category;
        _requiredSpriteCount = (int)required;
        UpdateSelectionState();
    }

    private void AssemblyDimension_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializingOptions) UpdateAssemblyOptions();
    }

    private void DestinationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializingOptions) UpdateAssemblyOptions();
    }

    private void SetSpriteIdInput(string value)
    {
        _updatingSpriteId = true;
        SpriteIdInput.Text = value;
        _updatingSpriteId = false;
    }

    private void GoToSpriteId()
    {
        if (_updatingSpriteId || _spriteCount == 0) return;
        if (!uint.TryParse(SpriteIdInput.Text, out var id) || id == 0 || id > _spriteCount)
        {
            SetSpriteIdInput(_currentSpriteId.ToString());
            return;
        }
        _currentSpriteId = id;
        ScrollToCurrentSprite();
    }

    private void SelectVisibleButton_Click(object sender, RoutedEventArgs e)
    {
        var visibleIds = _visibleCards.Keys.Order().ToArray();
        foreach (var id in visibleIds) _selectedIds.Add(id);
        if (visibleIds.Length > 0) _selectionAnchorId = visibleIds[0];
        UpdateVisibleCardAppearances();
        UpdateSelectionState();
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedIds.Clear();
        _selectionAnchorId = null;
        UpdateVisibleCardAppearances();
        UpdateSelectionState();
    }

    private void JumpToSpriteButton_Click(object sender, RoutedEventArgs e) => GoToSpriteId();
    private void SpriteIdInput_LostFocus(object sender, RoutedEventArgs e) => GoToSpriteId();

    private void SpriteIdInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        GoToSpriteId();
        e.Handled = true;
    }

    private void AssembleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_requiredSpriteCount is not int required || _selectedIds.Count != required) return;
        DialogResult = true;
    }
}
