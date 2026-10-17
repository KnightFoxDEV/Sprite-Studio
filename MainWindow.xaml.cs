using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using Line = System.Windows.Shapes.Line;
using Microsoft.Win32;

namespace SpriteStudio;

public partial class MainWindow : Window
{
    public sealed class DatThingExchangeDocument
    {
        public DatThingExchangeDocument() { }
        public int FormatVersion { get; set; }
        public int VersionValue { get; set; }
        public DatThingExchangeObject? Thing { get; set; }
    }

    public sealed class DatThingExchangeObject
    {
        public DatThingExchangeObject() { }
        public uint Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public List<DatThingExchangeFrameGroup> FrameGroups { get; set; } = [];
        public int SpriteCount { get; set; }
        public byte[] RawProperties { get; set; } = [];
    }

    public sealed class DatThingExchangeFrameGroup
    {
        public DatThingExchangeFrameGroup() { }
        public byte Type { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int CropSize { get; set; }
        public int Layers { get; set; }
        public int PatternX { get; set; }
        public int PatternY { get; set; }
        public int PatternZ { get; set; }
        public byte AnimationMode { get; set; }
        public int LoopCount { get; set; }
        public sbyte StartFrame { get; set; }
        public List<DatThingExchangeFrame> Frames { get; set; } = [];
    }

    public sealed class DatThingExchangeFrame
    {
        public DatThingExchangeFrame() { }
        public uint[] SpriteIds { get; set; } = [];
        public uint MinimumDuration { get; set; }
        public uint MaximumDuration { get; set; }
    }

    private const int PageSize = 100;
    private const int SpriteSide = 32;
    private const int SpritePixelCount = SpriteSide * SpriteSide;
    private const string SpriteDragDataFormat = "SpriteStudio.SpriteId";
    private static readonly IReadOnlyDictionary<string, string> SpriteVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["3DFF4AEB"] = "7.10", ["411A6279"] = "7.30", ["41B9EA86"] = "7.40", ["42F81949"] = "7.50",
        ["434F9CDE"] = "7.55", ["439852BE"] = "7.70", ["44CE4206"] = "7.80", ["457957C8"] = "7.90",
        ["45880FE8"] = "7.92", ["467F9E74"] = "8.00", ["475D0B01"] = "8.10", ["47EBB9B2"] = "8.11",
        ["4868ECC9"] = "8.20", ["48C8E712"] = "8.30", ["493D4E7C"] = "8.40", ["49B140EA"] = "8.42",
        ["4A44FD4E"] = "8.52", ["4ACB5230"] = "8.53", ["4B1E2C87"] = "8.54", ["4B0D3AFF"] = "8.54",
        ["4B913871"] = "8.55", ["4C220594"] = "8.60", ["4C63F145"] = "8.62", ["4CFD078A"] = "8.70",
        ["4D3D65D0"] = "8.71", ["4DAD1A32"] = "9.00", ["4E12DB27"] = "9.10", ["4E807C23"] = "9.20",
        ["4EE71E06"] = "9.40", ["4F0EEFEF"] = "9.44", ["4F1051D7"] = "9.44", ["4F3131F6"] = "9.44",
        ["4F5DCEF7"] = "9.46", ["4F75B7CD"] = "9.50", ["4F857F8E"] = "9.52", ["4FA11282"] = "9.53",
        ["4FD595B7"] = "9.54", ["4FFA74F9"] = "9.60", ["50226FBD"] = "9.61", ["503CB954"] = "9.63",
        ["5072A567"] = "9.70", ["50C70753"] = "9.80", ["50D1C685"] = "9.81", ["512CAD68"] = "9.82",
        ["51407BC7"] = "9.83", ["51641A84"] = "9.85", ["5170E96F"] = "9.86", ["51E3F8E9"] = "10.10",
        ["5236F14F"] = "10.20", ["526A5090"] = "10.21", ["52A5905F"] = "10.30", ["52AED5A7"] = "10.31",
        ["52D8D0CE"] = "10.32", ["52E74ADA"] = "10.34", ["52FDFC54"] = "10.35", ["53159CA9"] = "10.36",
        ["531EA856"] = "10.37", ["5333C1C3"] = "10.38", ["535A50D5"] = "10.39", ["53799876"] = "10.40",
        ["53835077"] = "10.41", ["53B64639"] = "10.50", ["53C8CC3F"] = "10.51", ["53FAD799"] = "10.53",
        ["53E898E5"] = "10.54", ["54128755"] = "10.55", ["542143DE"] = "10.56", ["54253627"] = "10.57",
        ["542D1315"] = "10.58", ["54340879"] = "10.59", ["5448DA10"] = "10.61", ["54622667"] = "10.62",
        ["546B505E"] = "10.63", ["547F0632"] = "10.64", ["5481BC06"] = "10.70", ["548E9EFE"] = "10.71",
        ["54B37B99"] = "10.72", ["54BC95AE"] = "10.73", ["54C5FAB2"] = "10.74", ["54D85085"] = "10.75",
        ["54F03CE9"] = "10.76", ["5525213D"] = "10.77", ["565EE171"] = "10.90", ["56BC8198"] = "10.91",
        ["570742B8"] = "10.92", ["57161DEA"] = "10.93", ["5726E657"] = "10.93", ["57459D43"] = "10.94",
        ["575A84BD"] = "10.95", ["57BBD603"] = "10.98", ["57FF106B"] = "10.99", ["59E48E02"] = "13.10"
    };

    private static readonly List<MainWindow> OpenEditorWindows = [];
    private readonly Dictionary<uint, BitmapSource> _pageBitmaps = new();
    private readonly Dictionary<uint, BitmapSource> _objectSpriteBitmaps = new();
    private readonly Dictionary<uint, BitmapSource> _selectedThingSpriteBitmaps = new();
    private readonly SortedDictionary<uint, BitmapSource> _pendingPixelSprites = new();
    private readonly HashSet<uint> _selectedThingIds = [];
    private readonly HashSet<uint> _selectedObjectSpriteIds = [];
    private readonly HashSet<int> _selectedFilmRollFrames = [];
    private int _filmRollSelectionAnchor = -1;
    private readonly List<(int Dx, int Dy)> _queuedPixelNudges = [];
    private readonly Dictionary<string, Border> _propertySections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _propertyToggles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<TextBox>> _propertyTextInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ComboBox>> _propertyCombos = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Key, int ValueIndex), Button> _propertyColorButtons = new();
    private readonly Dictionary<string, CheckBox> _attributeFlagControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RadioButton> _renderOrderControls = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _infoPreviewTimer;
    private SlicerWindow? _slicerWindow;
    private FileStream? _sprStream;
    private uint[] _offsets = [];
    private string? _filePath;
    private string? _datPath;
    private DatFileModel? _datModel;
    private ServerItemFiles? _serverItemFiles;
    private uint _signature;
    private uint _spriteCount;
    private int _sprHeaderSize;
    private int _page;
    private uint _objectSpriteId = 1;
    private uint? _objectSpriteSelectionAnchorId;
    private uint? _selectedId;
    private DatThing? _selectedThing;
    private DatThing? _copiedDatThing;
    private IReadOnlyList<DatFrameGroup>? _copiedDatPatterns;
    private string? _copiedDatPatternCategory;
    private int? _copiedDatVersion;
    private Dictionary<string, DatPropertyField>? _copiedDatProperties;
    private Dictionary<string, DatPropertyField>? _copiedDatAttributes;
    private string? _copiedDatRenderOrder;
    private uint? _thingSelectionAnchorId;
    private DatFrameGroup? _selectedFrameGroup;
    private int _selectedAnimationFrame;
    private int _selectedLayer;
    private int _selectedPatternX;
    private int _selectedPatternY;
    private int _selectedPatternZ;
    private int _previewRenderVersion;
    private int _filmRollRenderVersion;
    private int _previewGridWidth;
    private int _previewGridHeight;
    private int _previewPixelGridWidth;
    private int _previewPixelGridHeight;
    private int _previewTileWidth;
    private int _previewTileHeight;
    private double _appliedPreviewScale = double.NaN;
    private int _infoPreviewRenderVersion;
    private int _infoPreviewFrame;
    private ComboBox? _bonesDirectionCombo;
    private StackPanel? _bonesDirectionRow;
    private int _animationLoop;
    private int _animationDirection = 1;
    private bool _initialized;
    private bool _languageChanging;
    private bool _gridMode;
    private bool _objectSpriteGridMode;
    private bool _updatingObjectSpriteIdInput;
    private bool _updatingThingControls;
    private bool _updatingAttributeControls;
    private bool _updatingServerItemControls;
    private bool _isPlaying;
    private bool _editPixelsMode;
    private bool _wrapPixelShift;
    private bool _filmRollMode;
    private bool _isPixelEditInProgress;
    private bool _isRenderingThingPreview;
    private bool _isRenderingInfoPreview;
    private bool _metadataHasUnsavedChanges;
    private bool _isNavigatingSearch;
    private bool _sprEditInProgress;
    private PixelNudgeContext? _queuedPixelNudgeContext;

    private sealed record PixelNudgeContext(string Category, uint ThingId, byte GroupType,
        int Frame, int Layer, int PatternX, int PatternY, int PatternZ, int[] FilmRollFrames, bool Wrap);
    private sealed record PropertyInputBinding(string Key, int ValueIndex, bool Signed = false, bool TextValue = false,
        bool BoneX = false, bool BoneY = false, int Minimum = 0, int Maximum = ushort.MaxValue);
    private sealed record PropertyComboBinding(string Key, int ValueIndex);
    private sealed record PropertyColorBinding(string Key, int ValueIndex, Popup Popup);
    private sealed record PropertyColorSwatchBinding(string Key, int ValueIndex, int Color, Popup Popup);
    private sealed record XmlAttributeInputBinding(TextBox KeyInput, TextBox ValueInput);

    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    public MainWindow()
    {
        InitializeComponent();
        ThemeCombo.SelectedValue = App.CurrentTheme;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, CompileAsCommand_Executed));
        InputBindings.Add(new KeyBinding(ApplicationCommands.SaveAs,
            new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift)));
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _animationTimer.Tick += AnimationTimer_Tick;
        _infoPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _infoPreviewTimer.Tick += InfoPreviewTimer_Tick;
        Closing += (_, e) => { if (!ConfirmDiscardUnsavedMetadata()) e.Cancel = true; };
        Closed += (_, _) =>
        {
            OpenEditorWindows.Remove(this);
            foreach (var openWindow in OpenEditorWindows)
                openWindow.UpdateArrangeWindowsButtonState();
            _animationTimer.Stop();
            _infoPreviewTimer.Stop();
        };
        _initialized = true;
        UpdateWindowCaptionButtons();
        ApplyLanguage();
        ApplyPreviewZoom();
        UpdateViewButtons();
        RenderPage();
        UpdateEmptyState();
        OpenEditorWindows.Add(this);
        foreach (var openWindow in OpenEditorWindows)
            openWindow.UpdateArrangeWindowsButtonState();
    }

    private bool IsEnglish => (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "en";

    private string Text(string pt, string en) => IsEnglish ? en : pt;

    private bool ConfirmWithTheme(string title, string message, bool defaultNo = false) =>
        ThemedConfirmationDialog.Confirm(this, title, message,
            Text("Sim", "Yes"), Text("Não", "No"), defaultNo);

    private Brush ThemeBrush(string key) => (Brush)FindResource(key);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessageHook);

        // Attach the native hook before maximizing so the first maximize uses
        // the selected monitor's work area (the space above/beside the taskbar).
        WindowState = WindowState.Maximized;
    }

    private static IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmGetMinMaxInfo || lParam == IntPtr.Zero)
            return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return IntPtr.Zero;

        var monitorInfo = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
            return IntPtr.Zero;

        var minMaxInfo = Marshal.PtrToStructure<NativeMinMaxInfo>(lParam);
        minMaxInfo.MaxPosition.X = monitorInfo.Work.Left - monitorInfo.Monitor.Left;
        minMaxInfo.MaxPosition.Y = monitorInfo.Work.Top - monitorInfo.Monitor.Top;
        minMaxInfo.MaxSize.X = monitorInfo.Work.Right - monitorInfo.Work.Left;
        minMaxInfo.MaxSize.Y = monitorInfo.Work.Bottom - monitorInfo.Work.Top;
        Marshal.StructureToPtr(minMaxInfo, lParam, false);

        handled = true;
        return IntPtr.Zero;
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeWindowButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_StateChanged(object? sender, EventArgs e) => UpdateWindowCaptionButtons();

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_initialized) return;
        ApplyCompactToolbarLayout();
    }

    private void ApplyCompactToolbarLayout()
    {
        if (TopContextPanel is null) return;
        var compact = Width < 1280;
        var utilityVisibility = compact ? Visibility.Collapsed : Visibility.Visible;
        TopContextPanel.Visibility = utilityVisibility;
        CompileAsButton.Visibility = utilityVisibility;
        StatusBadge.Visibility = utilityVisibility;
        OtbStatusBadge.Visibility = utilityVisibility;
        SlicerButton.Visibility = utilityVisibility;
        ImportFolderButton.Visibility = utilityVisibility;
        OpenServerItemsButton.Visibility = utilityVisibility;
        PrimaryOpenButton.Visibility = utilityVisibility;
        ObjectSpriteColumn.Width = !compact && IsObjectMode && _datModel is not null
            ? new GridLength(270) : new GridLength(0);
    }

    private void UpdateArrangeWindowsButtonState()
    {
        if (ArrangeWindowsButton is null) return;
        ArrangeWindowsButton.IsEnabled = OpenEditorWindows.Any(window => window != this && window.IsLoaded);
    }
    private void UpdateWindowCaptionButtons()
    {
        if (MaximizeWindowButton is null) return;
        var maximized = WindowState == WindowState.Maximized;
        if (MaximizeWindowButton.Content is TextBlock glyph) glyph.Text = maximized ? "❐" : "□";
        MinimizeWindowButton.ToolTip = Text("Minimizar", "Minimize");
        MaximizeWindowButton.ToolTip = maximized ? Text("Restaurar", "Restore") : Text("Maximizar", "Maximize");
        CloseWindowButton.ToolTip = Text("Fechar", "Close");
    }

    private void ApplyLanguage()
    {
        if (!_initialized) return;
        UpdateWindowCaptionButtons();
        BrandSubtitle.Text = Text("Editor de sprites para Tibia", "Tibia sprite editor");
        TopWorkspace.Text = Text("EDITOR DE SPRITES", "SPRITE EDITOR");
        TopLibrary.Text = Text("Biblioteca do cliente", "Client library");
        LanguageLabel.Text = Text("Idioma", "Language");
        ThemeLabel.Text = Text("Tema", "Theme");
        foreach (var item in ThemeCombo.Items.OfType<ComboBoxItem>())
            item.Content = item.Tag?.ToString() switch
            {
                "dark" => Text("Escuro", "Dark"),
                "object-build" => "Object Build",
                "light" => Text("Claro", "Light"),
                _ => item.Content
            };
        PrivacyLabel.Text = Text("Seus arquivos ficam no seu computador.", "Your files stay on your computer.");
        OpenServerItemsButton.Content = Text("Abrir OTB/XML", "Open OTB/XML");
        CompileAsButton.Content = Text("Compilar como…", "Compile As…");
        CompileAsButton.ToolTip = Text("Compilar DAT e SPR em outra versão (Ctrl+Shift+S)", "Compile DAT and SPR for another version (Ctrl+Shift+S)");
        SlicerButton.Content = "Slicer";
        ImportFolderButton.Content = Text("Importar pasta…", "Import Folder…");
        ImportFolderButton.ToolTip = Text("Importar todos os objetos OBD de uma pasta para o DAT/SPR e items.otb abertos", "Import every OBD object in a folder into the open DAT/SPR and items.otb");
        OpenSecondWindowButton.Content = Text("Nova janela", "New window");
        OpenSecondWindowButton.ToolTip = Text("Abrir outra janela para trabalhar com um segundo cliente", "Open another window to work with a second client");
        ArrangeWindowsButton.Content = Text("Lado a lado", "Side by side");
        ArrangeWindowsButton.ToolTip = Text("Organizar esta janela e a outra lado a lado", "Arrange this window and the other one side by side");
        PrimaryOpenButton.Content = Text("＋  Abrir pasta do cliente", "＋  Open client folder");
        InfoTitle.Text = Text("Informações", "Info");
        ClientVersionLabel.Text = Text("Versão:", "Version:");
        SpriteDimensionLabel.Text = Text("Dimensão:", "Sprite size:");
        DatSignatureLabel.Text = Text("Assinatura DAT:", "DAT signature:");
        SprSignatureLabel.Text = Text("Assinatura SPR:", "SPR signature:");
        ItemsCountLabel.Text = Text("Itens:", "Items:");
        OutfitsCountLabel.Text = Text("Outfits:", "Outfits:");
        EffectsCountLabel.Text = Text("Efeitos:", "Effects:");
        MissilesCountLabel.Text = Text("Projéteis:", "Missiles:");
        SpritesCountLabel.Text = Text("Sprites:", "Sprites:");
        CategorySectionLabel.Text = Text("Categoria", "Category");
        InfoPreviewEmptyText.Text = Text("Selecione um objeto", "Select an object");
        Eyebrow.Text = Text("EDITOR UNIVERSE", "EDITOR UNIVERSE");
        TitleText.Text = Text("Editor de sprites", "Sprite editor");
        DropTitle.Text = Text("Abra a pasta do cliente Tibia", "Open your Tibia client folder");
        DropText.Text = Text("O programa encontra DAT e SPR e também pode carregar items.otb/items.xml.", "The app finds DAT and SPR and can also load items.otb/items.xml.");
        ChooseFileButton.Content = Text("Selecionar pasta do cliente", "Select client folder");
        SupportedText.Text = Text("Ou importe uma pasta com os arquivos do cliente.", "Or import a folder with the client files.");
        SpritesTitle.Text = Text("Objetos", "Objects");
        ListViewButton.Content = Text("Lista", "List");
        GridViewButton.Content = Text("Grade", "Grid");
        ObjectSpritesTitle.Text = Text("Sprites", "Sprites");
        ObjectSpriteListButton.Content = Text("Lista", "List");
        ObjectSpriteGridButton.Content = Text("Grade", "Grid");
        SelectSpritePageButton.Content = Text("Selecionar página", "Select page");
        SelectSpritePageButton.ToolTip = Text("Selecionar todos os sprites visíveis nesta página", "Select all sprites visible on this page");
        AutoAssembleSpritesButton.Content = Text("Montar novo", "Assemble new");
        AutoAssembleSpritesButton.ToolTip = Text("Definir largura, altura, animações e criar um novo Item ou Efeito com sprites da SPR",
            "Set width, height, and animations, then create a new Item or Effect from SPR sprites");
        ObjectSpriteFirstButton.ToolTip = Text("Primeiro sprite", "First sprite");
        ObjectSpritePreviousButton.ToolTip = Text("Página anterior", "Previous page");
        ObjectSpriteNextButton.ToolTip = Text("Próxima página", "Next page");
        ObjectSpriteLastButton.ToolTip = Text("Último sprite", "Last sprite");
        ObjectSpriteIdInput.ToolTip = Text("ID do sprite atual", "Current sprite ID");
        NewSpriteButton.ToolTip = Text("Novo: criar um quadrado vazio de 32 × 32", "New: create a blank 32 × 32 sprite");
        ReplaceSpriteButton.ToolTip = Text("Substituir o sprite selecionado", "Replace the selected sprite");
        ReplaceSpritesAcrossWindowsButton.ToolTip = Text("Substituir sprites selecionados em outra janela (mesma quantidade, em ordem de ID)", "Replace selected sprites in another window (same count, in ID order)");
        DuplicateSpriteButton.ToolTip = Text("Duplicar o sprite selecionado", "Duplicate the selected sprite");
        ImportSpriteButton.ToolTip = Text("Importar uma ou mais imagens PNG ou BMP de 32 × 32", "Import one or more 32 × 32 PNG or BMP images");
        ExportSpriteButton.ToolTip = Text("Exportar o sprite selecionado como PNG", "Export the selected sprite as PNG");
        DeleteSpriteButton.ToolTip = Text("Excluir os sprites selecionados", "Delete the selected sprites");
        DatNewButton.ToolTip = Text("Novo: criar um objeto vazio nesta categoria", "New: create a blank object in this category");
        DatReplaceButton.ToolTip = Text("Substituir o objeto selecionado por um arquivo exportado pelo Studio", "Replace the selected object with a Studio export");
        DatDuplicateButton.ToolTip = Text("Duplicar o objeto DAT selecionado", "Duplicate the selected DAT object");
        DatImportButton.ToolTip = Text("Importar um objeto exportado pelo Studio como novo objeto", "Import a Studio object export as a new object");
        DatExportButton.ToolTip = Text("Exportar a seleção ou escolher uma categoria e faixa completa", "Export the selection or choose a full category and range");
        DatTransferButton.ToolTip = Text("Enviar os objetos selecionados para outra janela do editor", "Send selected objects to another editor window");
        DatReplaceAcrossWindowsButton.ToolTip = Text("Substituir os objetos DAT selecionados em outra janela, mantendo os IDs do destino", "Replace selected DAT objects in another window, preserving destination IDs");
        DatDeleteButton.ToolTip = Text("Excluir o objeto selecionado; objetos no meio da lista serão limpos para preservar os IDs seguintes", "Delete the selected object; objects between IDs are cleared to preserve later IDs");
        FindButton.Content = Text("Buscar", "Find");
        SearchBox.ToolTip = Text("Digite o ID e pressione Enter para ir até ele", "Enter an ID and press Enter to jump to it");
        ((ComboBoxItem)ViewModeCombo.Items[0]).Content = Text("Sprites SPR", "SPR sprites");
        ((ComboBoxItem)ViewModeCombo.Items[1]).Content = Text("Objetos DAT", "DAT objects");
        ((ComboBoxItem)CategoryCombo.Items[0]).Content = Text("Itens", "Items");
        ((ComboBoxItem)CategoryCombo.Items[1]).Content = Text("Outfits", "Outfits");
        ((ComboBoxItem)CategoryCombo.Items[2]).Content = Text("Efeitos", "Effects");
        ((ComboBoxItem)CategoryCombo.Items[3]).Content = Text("Projéteis", "Missiles");
        BulkAnimationSpeedButton.Content = Text("Velocidade de animação…", "Animation speed…");
        PreviewTitle.Text = Text("Aparência", "Appearance");
        ZoomLabel.Text = Text("Zoom:", "Zoom:");
        ActualSizeCheckBox.Content = Text("Tamanho real", "Actual size");
        ActualSizeCheckBox.ToolTip = Text("Mostrar a mini prévia no tamanho nativo do sprite", "Show the sidebar preview at the sprite's native size");
        ShowGridCheckBox.Content = Text("Exibir grades", "Show grid");
        ShowGridCheckBox.ToolTip = Text("Mostrar as divisões de 32 × 32 do item", "Show the item's 32 × 32 tile divisions");
        EditPixelsCheckBox.Content = Text("Editar Pixels", "Edit Pixels");
        EditPixelsCheckBox.ToolTip = Text("Ative para mover um pixel pelas setas na tela ou do teclado", "Enable to move one pixel with the on-screen or keyboard arrows");
        PixelMoveLeftButton.ToolTip = Text("Mover 1 pixel para a esquerda", "Move 1 pixel left");
        PixelMoveUpButton.ToolTip = Text("Mover 1 pixel para cima", "Move 1 pixel up");
        PixelMoveDownButton.ToolTip = Text("Mover 1 pixel para baixo", "Move 1 pixel down");
        PixelMoveRightButton.ToolTip = Text("Mover 1 pixel para a direita", "Move 1 pixel right");
        WrapPixelsCheckBox.Content = "Wrap";
        WrapPixelsCheckBox.ToolTip = Text("Envolver os pixels pela borda ao mover", "Wrap pixels across the edge when shifting");
        FilmRollCheckBox.Content = "Film Roll";
        FilmRollCheckBox.ToolTip = Text("Exibir todos os quadros da animação lado a lado", "Show every animation frame side by side");
        DeleteAnimationFramesButton.Content = Text("Excluir", "Delete");
        DeleteAnimationFramesButton.ToolTip = Text("Excluir o quadro selecionado da animação", "Delete the selected animation frame");
        if (PixelAlignmentCombo.Items.Count >= 6)
        {
            ((ComboBoxItem)PixelAlignmentCombo.Items[0]).Content = Text("Padrão", "Default");
            ((ComboBoxItem)PixelAlignmentCombo.Items[1]).Content = Text("Centralizar", "Center");
            ((ComboBoxItem)PixelAlignmentCombo.Items[2]).Content = Text("Superior esquerdo", "Top left");
            ((ComboBoxItem)PixelAlignmentCombo.Items[3]).Content = Text("Superior direito", "Top right");
            ((ComboBoxItem)PixelAlignmentCombo.Items[4]).Content = Text("Inferior direito", "Bottom right");
            ((ComboBoxItem)PixelAlignmentCombo.Items[5]).Content = Text("Inferior esquerdo", "Bottom left");
        }
        FrameGroupLabel.Text = Text("Grupo", "Group");
        PatternXLabel.Text = Text("Direção / X", "Direction / X");
        PatternYLabel.Text = Text("Y", "Y");
        PatternZLabel.Text = Text("Z", "Z");
        AnimationLabel.Text = Text("Animação", "Animation");
        MinimumDurationLabel.Text = Text("Mín.", "Min.");
        MaximumDurationLabel.Text = Text("Máx.", "Max.");
        ApplyMinimumDurationButton.ToolTip = Text("Aplicar mínimo a todos os quadros", "Apply minimum to all frames");
        ApplyMaximumDurationButton.ToolTip = Text("Aplicar máximo a todos os quadros", "Apply maximum to all frames");
        FirstPageButton.ToolTip = Text("Primeira página", "First page");
        LastPageButton.ToolTip = Text("Última página", "Last page");
        LayerLabel.Text = Text("Camada", "Layer");
        PatternWidthLabel.Text = Text("LARGURA", "WIDTH");
        FrameGroupsLabel.Text = Text("GRUPOS", "GROUPS");
        PatternHeightLabel.Text = Text("ALTURA", "HEIGHT");
        PatternCropLabel.Text = Text("TAMANHO EXATO", "EXACT SIZE");
        PatternLayersLabel.Text = Text("CAMADAS", "LAYERS");
        PatternXInfoLabel.Text = Text("PADRÃO X", "PATTERN X");
        PatternYInfoLabel.Text = Text("PADRÃO Y", "PATTERN Y");
        PatternZInfoLabel.Text = Text("PADRÃO Z", "PATTERN Z");
        PatternFramesLabel.Text = Text("ANIMAÇÕES", "ANIMATIONS");
        AnimationDimensionsHeading.Text = Text("DIMENSÕES", "DIMENSIONS");
        PatternSectionLabel.Text = Text("PADRÃO & CAMADAS", "PATTERN & LAYERS");
        SaveAllButton.Content = Text("Salvar tudo", "Save all");
        SaveAllButton.ToolTip = Text(
            "Salva as alterações pendentes do DAT e dos arquivos items.otb/items.xml carregados. Alterações no SPR são gravadas automaticamente.",
            "Saves pending changes to the DAT and loaded items.otb/items.xml files. SPR edits are saved automatically.");
        SaveDatButton.Content = Text("Salvar DAT", "Save DAT");
        PropertiesPlaceholder.Text = Text("Selecione um objeto DAT para ver suas propriedades.", "Select a DAT object to view its properties.");
        AttributesPlaceholder.Text = Text("Selecione um item DAT para editar seus atributos.", "Select a DAT item to edit its attributes.");
        AttributesEditorHeading.Text = Text("Atributos do item", "Item attributes");
        AttributeFlagsTitle.Text = Text("Atributos e flags", "Attributes and flags");
        AttributesSaveHint.Text = Text("Salve para gravar as alterações no DAT e nos arquivos OTB/XML carregados.", "Save to write changes to the DAT and the loaded OTB/XML files.");
        ServerItemPanelTitle.Text = Text("Dados do servidor (items.otb / items.xml)", "Server data (items.otb / items.xml)");
        ServerItemIdLabel.Text = Text("ID servidor", "Server ID");
        ServerItemClientIdLabel.Text = Text("ID cliente", "Client ID");
        ServerItemNameLabel.Text = Text("Nome OTB", "OTB name");
        ServerGroundSpeedLabel.Text = Text("Velocidade do piso", "Ground speed");
        ServerMinimapLabel.Text = Text("Cor do minimapa", "Minimap color");
        ServerTradeAsLabel.Text = "Trade as";
        ServerLightLevelLabel.Text = Text("Intensidade da luz", "Light intensity");
        ServerLightColorLabel.Text = Text("Cor da luz", "Light color");
        ServerXmlNameLabel.Text = Text("Nome XML", "XML name");
        ServerArticleLabel.Text = Text("Artigo", "Article");
        ServerPluralLabel.Text = Text("Plural", "Plural");
        ServerXmlAttributesTitle.Text = Text("Atributos personalizados do XML", "Custom XML attributes");
        AddXmlAttributeButton.Content = Text("＋ Atributo", "＋ Attribute");
        BuildItemPropertyControls();
        BuildItemFlagControls();
        MetadataSaveStatus.Text = _metadataHasUnsavedChanges
            ? Text("Alterações não salvas", "Unsaved changes") : string.Empty;
        CanvasEmptyState.Text = Text("Selecione um objeto para visualizar", "Select an object to preview");
        CanvasAssemblyHint.Text = Text("Arraste sprites da lista à direita para montar o objeto", "Drag sprites from the list on the right to assemble the object");
        CanvasDimensionsLabel.Text = Text("DIMENSÕES", "SIZE");
        ExportButton.Content = Text("↓ PNG", "↓ PNG");
        CanvasExportButton.Content = Text("↓ PNG", "↓ PNG");
        DetailIdLabel.Text = IsObjectMode ? Text("ID DO OBJETO", "OBJECT ID") : Text("ID DO SPRITE", "SPRITE ID");
        DetailFileLabel.Text = Text("ARQUIVO", "FILE");
        VersionLabel.Text = Text("VERSÃO", "VERSION");
        TextureTab.Header = Text("Textura", "Texture");
        PropertiesTab.Header = Text("Propriedades", "Properties");
        AttributesTab.Header = Text("Atributos", "Attributes");
        EmptyTitle.Text = Text("Nenhum objeto encontrado", "No objects found");
        EmptyBody.Text = Text("Abra a pasta do cliente para carregar os arquivos.", "Open the client folder to load its files.");
        ObjectSpriteEmptyTitle.Text = Text("Nenhum SPR aberto", "No SPR open");
        ObjectSpriteEmptyBody.Text = Text("Abra um SPR para ver os sprites aqui.", "Open an SPR file to see its sprites here.");
        StatusText.Text = _filePath is null && _datModel is null ? Text("Aguardando arquivo", "Waiting for a file") : (_datModel?.VersionName ?? GetVersionName());
        UpdateClientInfo();
        UpdateThingControlLabels();
        if (_selectedFrameGroup is not null) UpdateAnimationAndLayerControls();
        UpdateMetadataEditingState();
        UpdateItemAttributeEditor();
        UpdateObjectSpritePanelVisibility();
        UpdateDatActionToolbar();
        UpdateViewButtons();
        UpdatePagerText();
        UpdateSpriteActionToolbar();
        UpdatePixelEditorControls();
        UpdateOtbStatusIndicator();
    }

    private void UpdateOtbStatusIndicator()
    {
        var isLoaded = _serverItemFiles is not null;
        OtbStatusDot.Fill = isLoaded ? ThemeBrush("SuccessBrush") : ThemeBrush("ErrorBrush");
        OtbStatusText.Foreground = isLoaded
            ? (Brush)FindResource("TextBrush")
            : (Brush)FindResource("MutedBrush");
        OtbStatusBadge.ToolTip = isLoaded
            ? Text($"items.otb carregado: {Path.GetFileName(_serverItemFiles!.OtbPath)}", $"items.otb loaded: {Path.GetFileName(_serverItemFiles!.OtbPath)}")
            : Text("items.otb não aberto", "items.otb not open");
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _languageChanging) return;
        _languageChanging = true;
        try { ApplyLanguage(); RenderPage(); }
        finally { _languageChanging = false; }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || ThemeCombo.SelectedValue is not string themeId) return;
        App.ApplyTheme(themeId);
        EditorPreferences.SaveTheme(themeId);
        RenderPage();
        UpdateViewButtons();
        UpdateObjectSpritePanelVisibility();
        UpdateItemAttributeEditor();
    }

    private void ListViewButton_Click(object sender, RoutedEventArgs e)
    {
        _gridMode = false;
        UpdateViewButtons();
        RenderPage();
    }

    private void GridViewButton_Click(object sender, RoutedEventArgs e)
    {
        _gridMode = true;
        UpdateViewButtons();
        RenderPage();
    }

    private void FindButton_Click(object sender, RoutedEventArgs e)
    {
        if (uint.TryParse(SearchBox.Text.Trim(), out var id) && NavigateToSearchResult(id)) return;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void ZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        ApplyPreviewZoom();
        if (_filmRollMode) _ = RenderFilmRollFramesAsync();
    }

    private void ActualSizeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        InfoPreviewImage.Stretch = ActualSizeCheckBox.IsChecked == true
            ? System.Windows.Media.Stretch.None
            : System.Windows.Media.Stretch.Uniform;
    }

    private void ApplyPreviewZoom()
    {
        if (PreviewImage is null) return;
        var scale = GetEffectivePreviewScale();
        if (double.IsNaN(_appliedPreviewScale) || Math.Abs(_appliedPreviewScale - scale) > 0.001)
        {
            PreviewImage.LayoutTransform = new ScaleTransform(scale, scale);
            PreviewTileBackground.LayoutTransform = new ScaleTransform(scale, scale);
            PreviewGridOverlay.LayoutTransform = new ScaleTransform(scale, scale);
            PreviewPixelGridOverlay.LayoutTransform = new ScaleTransform(scale, scale);
            _appliedPreviewScale = scale;
        }
        UpdatePreviewGridOverlay();
    }

    private double GetEffectivePreviewScale()
    {
        var requestedScale = double.TryParse((ZoomCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var value) ? value : 1;
        if (requestedScale > 1 || PreviewImage.Source is not BitmapSource bitmap) return requestedScale;

        var availableWidth = PreviewCanvasRoot.ActualWidth - 20;
        var availableHeight = PreviewCanvasRoot.ActualHeight - 20;
        if (availableWidth <= 0 || availableHeight <= 0 || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            return requestedScale;

        var fitScale = Math.Min(availableWidth / bitmap.PixelWidth, availableHeight / bitmap.PixelHeight);
        return Math.Min(requestedScale, fitScale);
    }

    private void PreviewCanvasRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_initialized) return;
        ApplyPreviewZoom();
    }

    private void ShowGridCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdatePreviewGridOverlay();
    }

    private void UpdatePreviewGridOverlay()
    {
        if (PreviewTileBackground is null || PreviewGridOverlay is null || PreviewPixelGridOverlay is null) return;

        var showFilmRoll = _filmRollMode && _selectedThing is not null
            && _selectedFrameGroup is { Frames.Count: > 1 } && IsObjectMode;
        PreviewImage.Visibility = showFilmRoll ? Visibility.Collapsed : Visibility.Visible;
        FilmRollScrollViewer.Visibility = showFilmRoll ? Visibility.Visible : Visibility.Collapsed;
        if (showFilmRoll)
        {
            PreviewTileBackground.Visibility = Visibility.Collapsed;
            PreviewGridOverlay.Visibility = Visibility.Collapsed;
            PreviewPixelGridOverlay.Visibility = Visibility.Collapsed;
            CanvasAssemblyHint.Visibility = Visibility.Collapsed;
            return;
        }

        if (!IsObjectMode || _selectedThing is null || PreviewImage.Source is not BitmapSource bitmap)
        {
            PreviewTileBackground.Visibility = Visibility.Collapsed;
            PreviewGridOverlay.Visibility = Visibility.Collapsed;
            PreviewPixelGridOverlay.Visibility = Visibility.Collapsed;
            CanvasAssemblyHint.Visibility = Visibility.Collapsed;
            return;
        }

        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            PreviewTileBackground.Visibility = Visibility.Collapsed;
            PreviewGridOverlay.Visibility = Visibility.Collapsed;
            PreviewPixelGridOverlay.Visibility = Visibility.Collapsed;
            CanvasAssemblyHint.Visibility = Visibility.Collapsed;
            return;
        }

        CanvasAssemblyHint.Visibility = !_editPixelsMode
            && _selectedFrameGroup?.Frames.ElementAtOrDefault(_selectedAnimationFrame)?.SpriteIds.All(id => id == 0) == true
            ? Visibility.Visible : Visibility.Collapsed;

        PreviewTileBackground.Width = width;
        PreviewTileBackground.Height = height;
        if (_previewTileWidth != width || _previewTileHeight != height)
        {
            PreviewTileBackground.Children.Clear();
            var tileBrush = ThemeBrush("CheckerTileBrush");
            for (var y = 0; y < height; y += SpriteSide)
            for (var x = 0; x < width; x += SpriteSide)
            {
                var tile = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Min(SpriteSide, width - x),
                    Height = Math.Min(SpriteSide, height - y),
                    Fill = tileBrush,
                    SnapsToDevicePixels = true
                };
                Canvas.SetLeft(tile, x);
                Canvas.SetTop(tile, y);
                PreviewTileBackground.Children.Add(tile);
            }
            _previewTileWidth = width;
            _previewTileHeight = height;
        }
        PreviewTileBackground.Visibility = Visibility.Visible;
        UpdatePixelGridOverlay(width, height);

        if (ShowGridCheckBox.IsChecked != true)
        {
            PreviewGridOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        PreviewGridOverlay.Width = width;
        PreviewGridOverlay.Height = height;
        if (_previewGridWidth != width || _previewGridHeight != height)
        {
            PreviewGridOverlay.Children.Clear();
            var gridBrush = new SolidColorBrush(Color.FromArgb(215, 239, 69, 209));
            var borderBrush = new SolidColorBrush(Color.FromArgb(245, 239, 69, 209));
            for (var x = 0; x <= width; x += SpriteSide)
            {
                PreviewGridOverlay.Children.Add(new Line
                {
                    X1 = x, Y1 = 0, X2 = x, Y2 = height,
                    Stroke = x == 0 || x == width ? borderBrush : gridBrush,
                    StrokeThickness = x == 0 || x == width ? 1.25 : 1,
                    SnapsToDevicePixels = true
                });
            }
            for (var y = 0; y <= height; y += SpriteSide)
            {
                PreviewGridOverlay.Children.Add(new Line
                {
                    X1 = 0, Y1 = y, X2 = width, Y2 = y,
                    Stroke = y == 0 || y == height ? borderBrush : gridBrush,
                    StrokeThickness = y == 0 || y == height ? 1.25 : 1,
                    SnapsToDevicePixels = true
                });
            }
            _previewGridWidth = width;
            _previewGridHeight = height;
        }

        PreviewGridOverlay.Visibility = Visibility.Visible;
    }

    private void UpdatePixelGridOverlay(int width, int height)
    {
        PreviewPixelGridOverlay.Width = width;
        PreviewPixelGridOverlay.Height = height;
        if (_previewPixelGridWidth != width || _previewPixelGridHeight != height)
        {
            PreviewPixelGridOverlay.Children.Clear();
            var pixelGridBrush = new SolidColorBrush(Color.FromArgb(92, 255, 255, 255));
            for (var x = 4; x < width; x += 4)
                PreviewPixelGridOverlay.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = height, Stroke = pixelGridBrush, StrokeThickness = 1, SnapsToDevicePixels = true });
            for (var y = 4; y < height; y += 4)
                PreviewPixelGridOverlay.Children.Add(new Line { X1 = 0, Y1 = y, X2 = width, Y2 = y, Stroke = pixelGridBrush, StrokeThickness = 1, SnapsToDevicePixels = true });
            _previewPixelGridWidth = width;
            _previewPixelGridHeight = height;
        }
        PreviewPixelGridOverlay.Visibility = _editPixelsMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePixelEditorControls()
    {
        if (PixelEditingToolbar is null) return;
        var canShow = IsObjectMode && _datModel is not null && _datPath is not null;
        var hasThing = canShow && _selectedThing is not null && _selectedFrameGroup is not null;
        var hasSprites = hasThing && _sprStream is not null && _filePath is not null;
        if (_selectedFrameGroup?.Frames.Count <= 1 && _filmRollMode)
        {
            _filmRollMode = false;
            _selectedFilmRollFrames.Clear();
            _filmRollSelectionAnchor = -1;
            _filmRollRenderVersion++;
            FilmRollPanel.Children.Clear();
        }
        PixelEditingToolbar.Visibility = canShow ? Visibility.Visible : Visibility.Collapsed;
        var hasPendingPixelMove = _queuedPixelNudges.Count > 0;
        EditPixelsCheckBox.IsChecked = _editPixelsMode;
        EditPixelsCheckBox.IsEnabled = hasSprites && !_sprEditInProgress && !_isPixelEditInProgress && !hasPendingPixelMove;
        PixelNudgeButtons.Visibility = _editPixelsMode ? Visibility.Visible : Visibility.Collapsed;
        PixelNudgeButtons.IsEnabled = hasSprites && _editPixelsMode && (!_sprEditInProgress || _isPixelEditInProgress || hasPendingPixelMove);
        WrapPixelsCheckBox.IsChecked = _wrapPixelShift;
        WrapPixelsCheckBox.IsEnabled = hasSprites && _editPixelsMode && !_sprEditInProgress && !_isPixelEditInProgress && !hasPendingPixelMove;
        PixelAlignmentCombo.IsEnabled = hasSprites && _editPixelsMode && !_sprEditInProgress && !_isPixelEditInProgress && !hasPendingPixelMove;
        FilmRollCheckBox.IsChecked = _filmRollMode;
        FilmRollCheckBox.IsEnabled = hasSprites && _selectedFrameGroup?.Frames.Count > 1 && !_sprEditInProgress && !_isPixelEditInProgress && !hasPendingPixelMove;
        UpdateFilmRollDeleteButton();
        PreviewCanvasBorder.BorderBrush = _editPixelsMode
            ? new SolidColorBrush(Color.FromRgb(108, 172, 236))
            : ThemeBrush("PreviewBorderBrush");
        PreviewCanvasBorder.BorderThickness = new Thickness(_editPixelsMode ? 2 : 1);
        UpdatePreviewGridOverlay();
        UpdateFilmRollSelectionVisuals();
    }

    private void EditPixelsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _editPixelsMode = EditPixelsCheckBox.IsChecked == true;
        _selectedFilmRollFrames.Clear();
        _filmRollSelectionAnchor = -1;
        UpdatePixelEditorControls();
        if (_editPixelsMode) PreviewCanvasRoot.Focus();
    }

    private void PixelNudgeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string direction }) return;
        var (dx, dy) = direction switch
        {
            "left" => (-1, 0),
            "right" => (1, 0),
            "up" => (0, -1),
            "down" => (0, 1),
            _ => (0, 0)
        };
        if (dx == 0 && dy == 0) return;
        StartPixelTransform(dx, dy);
        PreviewCanvasRoot.Focus();
    }

    private void WrapPixelsCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _wrapPixelShift = WrapPixelsCheckBox.IsChecked == true;
    }

    private void FilmRollCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _filmRollMode = FilmRollCheckBox.IsChecked == true;
        _selectedFilmRollFrames.Clear();
        _filmRollSelectionAnchor = -1;
        UpdatePixelEditorControls();
        if (_filmRollMode) _ = RenderFilmRollFramesAsync();
        else
        {
            _filmRollRenderVersion++;
            FilmRollPanel.Children.Clear();
            _ = LoadSelectedThingPreviewAsync();
        }
        PreviewCanvasRoot.Focus();
    }

    private void PixelAlignmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || !_editPixelsMode || PixelAlignmentCombo.SelectedItem is not ComboBoxItem { Tag: string alignment }
            || alignment == "default") return;
        PixelAlignmentCombo.SelectedIndex = 0;
        StartPixelTransform(0, 0, alignment);
    }

    private void PreviewCanvasRoot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editPixelsMode) PreviewCanvasRoot.Focus();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_editPixelsMode || _selectedThing is null || _selectedFrameGroup is null
            || _sprStream is null) return;
        if (Keyboard.FocusedElement is TextBoxBase or ComboBox) return;

        if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _filmRollMode
            && _selectedFrameGroup.Frames.Count > 1)
        {
            _selectedFilmRollFrames.Clear();
            for (var frame = 0; frame < _selectedFrameGroup.Frames.Count; frame++) _selectedFilmRollFrames.Add(frame);
            _filmRollSelectionAnchor = _selectedAnimationFrame;
            UpdateFilmRollSelectionVisuals();
            e.Handled = true;
            return;
        }

        var (dx, dy) = e.Key switch
        {
            Key.Left => (-1, 0), Key.Right => (1, 0), Key.Up => (0, -1), Key.Down => (0, 1), _ => (0, 0)
        };
        if (dx == 0 && dy == 0) return;
        e.Handled = true;
        StartPixelTransform(dx, dy);
    }

    private async void StartPixelTransform(int dx, int dy, string? alignment = null)
    {
        if (!_editPixelsMode) return;
        if (_isPixelEditInProgress)
        {
            if (alignment is null && (dx != 0 || dy != 0))
            {
                QueuePixelNudge(dx, dy);
                MetadataSaveStatus.Text = Text("Movimento na fila; as setas continuam disponíveis.",
                    "Movement queued; the arrows remain available.");
            }
            return;
        }

        BeginPixelTransform(dx, dy, alignment);
    }

    private void QueuePixelNudge(int dx, int dy)
    {
        if (_queuedPixelNudges.Count == 0 && _selectedThing is { } thing && _selectedFrameGroup is { } group)
        {
            _queuedPixelNudgeContext = new PixelNudgeContext(
                thing.Category, thing.Id, group.Type, _selectedAnimationFrame, _selectedLayer,
                _selectedPatternX, _selectedPatternY, _selectedPatternZ,
                _selectedFilmRollFrames.Order().ToArray(), _wrapPixelShift);
        }
        _queuedPixelNudges.Add((dx, dy));
    }

    private async void BeginPixelTransform(int dx, int dy, string? alignment)
    {
        _isPixelEditInProgress = true;
        UpdatePixelEditorControls();
        try
        {
            await RunSpriteOperationAsync(() => ApplyPixelTransformAsync(dx, dy, alignment));
        }
        finally
        {
            _isPixelEditInProgress = false;
            UpdatePixelEditorControls();
            StartQueuedPixelNudges();
        }
    }

    private void StartQueuedPixelNudges()
    {
        if (_queuedPixelNudges.Count == 0 || !_editPixelsMode || _isPixelEditInProgress) return;
        var queued = _queuedPixelNudges.ToArray();
        var context = _queuedPixelNudgeContext;
        _queuedPixelNudges.Clear();
        _queuedPixelNudgeContext = null;

        if (context is null || _selectedThing is not { } selectedThing || _selectedFrameGroup is not { } selectedGroup
            || selectedThing.Category != context.Category || selectedThing.Id != context.ThingId
            || selectedGroup.Type != context.GroupType || _selectedAnimationFrame != context.Frame
            || _selectedLayer != context.Layer || _selectedPatternX != context.PatternX
            || _selectedPatternY != context.PatternY || _selectedPatternZ != context.PatternZ
            || _wrapPixelShift != context.Wrap || !_selectedFilmRollFrames.Order().SequenceEqual(context.FilmRollFrames))
        {
            MetadataSaveStatus.Text = Text("Movimento cancelado porque a seleção mudou.",
                "Movement canceled because the selection changed.");
            UpdatePixelEditorControls();
            return;
        }

        var preview = PreviewImage.Source as BitmapSource;
        var group = selectedGroup;
        byte[]? pixels = preview is not null && group is not null
            && preview.PixelWidth == group.Width * SpriteSide && preview.PixelHeight == group.Height * SpriteSide
            ? ReadBitmapPixels(preview) : null;
        var width = group is null ? 0 : group.Width * SpriteSide;
        var height = group is null ? 0 : group.Height * SpriteSide;
        var totalX = 0;
        var totalY = 0;

        foreach (var (dx, dy) in queued)
        {
            if (pixels is not null)
            {
                if (!_wrapPixelShift && PixelShiftWouldClip(pixels, width, height, dx, dy)) continue;
                pixels = ShiftPixelCanvas(pixels, width, height, dx, dy, _wrapPixelShift);
            }
            totalX = checked(totalX + dx);
            totalY = checked(totalY + dy);
        }

        if (totalX != 0 || totalY != 0)
            BeginPixelTransform(totalX, totalY, alignment: null);
        else
            UpdatePixelEditorControls();
    }

    private async Task RenderFilmRollFramesAsync()
    {
        var renderVersion = ++_filmRollRenderVersion;
        FilmRollPanel.Children.Clear();
        if (!_filmRollMode || _selectedThing is not { } thing || _selectedFrameGroup is not { Frames.Count: > 1 } group
            || _sprStream is not { } stream || !IsObjectMode) return;

        var sourcePath = _filePath;
        try
        {
            var zoom = double.TryParse((ZoomCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var scale) ? scale : 1;
            for (var frameIndex = 0; frameIndex < group.Frames.Count; frameIndex++)
            {
                var bitmap = await ReadThingBitmapAsync(thing, group, frameIndex,
                    _selectedPatternX, _selectedPatternY, _selectedPatternZ, _selectedLayer,
                    stream.SafeFileHandle, cacheSelectedSprites: false);
                if (renderVersion != _filmRollRenderVersion || !ReferenceEquals(stream, _sprStream)
                    || !ReferenceEquals(thing, _selectedThing) || !ReferenceEquals(group, _selectedFrameGroup)
                    || sourcePath != _filePath || !_filmRollMode) return;
                if (bitmap is null) continue;

                var image = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.None,
                    LayoutTransform = new ScaleTransform(zoom, zoom),
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                var cardContent = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
                cardContent.Children.Add(new TextBlock
                {
                    Text = Text($"Quadro {frameIndex + 1}", $"Frame {frameIndex + 1}"),
                    Foreground = (Brush)FindResource("MutedBrush"), FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4)
                });
                cardContent.Children.Add(image);
                var card = new Border
                {
                    Tag = frameIndex,
                    Child = cardContent,
                    Padding = new Thickness(5),
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(5),
                    BorderThickness = new Thickness(1),
                    Background = ThemeBrush("CardBackgroundBrush"),
                    Cursor = Cursors.Hand,
                    ToolTip = Text("Clique para selecionar; Ctrl+clique adiciona/remove; Shift+clique seleciona um intervalo.",
                        "Click to select; Ctrl+click toggles a frame; Shift+click selects a range.")
                };
                card.PreviewMouseLeftButtonUp += FilmRollFrame_PreviewMouseLeftButtonUp;
                FilmRollPanel.Children.Add(card);
            }
            UpdateFilmRollSelectionVisuals();
        }
        catch (Exception ex)
        {
            if (renderVersion == _filmRollRenderVersion)
                SetError(Text($"Não foi possível montar a Film Roll: {ex.Message}", $"Could not render the Film Roll: {ex.Message}"));
        }
    }

    private void FilmRollFrame_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: int frameIndex } || _selectedFrameGroup is not { } group
            || frameIndex < 0 || frameIndex >= group.Frames.Count) return;
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            var anchor = _filmRollSelectionAnchor >= 0 && _filmRollSelectionAnchor < group.Frames.Count
                ? _filmRollSelectionAnchor : _selectedAnimationFrame;
            _selectedFilmRollFrames.Clear();
            for (var index = Math.Min(anchor, frameIndex); index <= Math.Max(anchor, frameIndex); index++)
                _selectedFilmRollFrames.Add(index);
        }
        else if (modifiers.HasFlag(ModifierKeys.Control))
        {
            if (_selectedFilmRollFrames.Count == 0)
            {
                _selectedFilmRollFrames.Add(_selectedAnimationFrame);
                _filmRollSelectionAnchor = _selectedAnimationFrame;
            }
            if (!_selectedFilmRollFrames.Add(frameIndex)) _selectedFilmRollFrames.Remove(frameIndex);
            if (_selectedFilmRollFrames.Count == 0) _selectedFilmRollFrames.Add(frameIndex);
        }
        else
        {
            _selectedFilmRollFrames.Clear();
            _selectedFilmRollFrames.Add(frameIndex);
            _filmRollSelectionAnchor = frameIndex;
        }
        if (_selectedAnimationFrame != frameIndex)
            AnimationFrameSlider.Value = frameIndex + 1;
        else
            UpdateFilmRollSelectionVisuals();
        PreviewCanvasRoot.Focus();
        e.Handled = true;
    }

    private void UpdateFilmRollSelectionVisuals()
    {
        if (FilmRollPanel is null) return;
        foreach (var card in FilmRollPanel.Children.OfType<Border>())
        {
            if (card.Tag is not int frameIndex) continue;
            var active = frameIndex == _selectedAnimationFrame;
            var multiSelected = _selectedFilmRollFrames.Contains(frameIndex);
            card.BorderBrush = active
                ? (Brush)FindResource("AccentBrush")
                : multiSelected ? new SolidColorBrush(Color.FromRgb(255, 170, 0))
                : ThemeBrush("PreviewBorderBrush");
            card.BorderThickness = active || multiSelected ? new Thickness(2) : new Thickness(1);
            card.Background = active
                ? ThemeBrush("SelectedBackgroundBrush")
                : ThemeBrush("CardBackgroundBrush");
        }
        UpdateFilmRollDeleteButton();
    }

    private void UpdateFilmRollDeleteButton()
    {
        if (DeleteAnimationFramesButton is null) return;
        var group = _selectedFrameGroup;
        var visible = _filmRollMode && group is { Frames.Count: > 1 };
        DeleteAnimationFramesButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible || group is null) return;

        var selectedCount = _selectedFilmRollFrames.Count == 0
            ? 1
            : _selectedFilmRollFrames.Count(index => index >= 0 && index < group.Frames.Count);
        selectedCount = Math.Clamp(selectedCount, 1, group.Frames.Count);
        DeleteAnimationFramesButton.Content = selectedCount > 1
            ? Text($"Excluir ({selectedCount})", $"Delete ({selectedCount})")
            : Text("Excluir", "Delete");
        DeleteAnimationFramesButton.ToolTip = selectedCount > 1
            ? Text($"Excluir {selectedCount} quadros selecionados da animação", $"Delete {selectedCount} selected animation frames")
            : Text("Excluir o quadro selecionado da animação", "Delete the selected animation frame");
        DeleteAnimationFramesButton.IsEnabled = selectedCount < group.Frames.Count
            && !_sprEditInProgress && !_isPixelEditInProgress && _queuedPixelNudges.Count == 0;
    }

    private void DeleteAnimationFramesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedThing is not { } thing || _selectedFrameGroup is not { Frames.Count: > 1 } group
            || _datModel is null || _datPath is null || !_filmRollMode
            || _sprEditInProgress || _isPixelEditInProgress || _queuedPixelNudges.Count > 0)
            return;

        var framesToDelete = _selectedFilmRollFrames
            .Where(index => index >= 0 && index < group.Frames.Count)
            .Order()
            .ToArray();
        if (framesToDelete.Length == 0) framesToDelete = [_selectedAnimationFrame];
        if (framesToDelete.Length >= group.Frames.Count)
        {
            MetadataSaveStatus.Text = Text("A animação precisa manter pelo menos um quadro.",
                "The animation must keep at least one frame.");
            return;
        }

        var frameDescription = framesToDelete.Length == 1
            ? Text($"o quadro {framesToDelete[0] + 1}", $"frame {framesToDelete[0] + 1}")
            : Text($"os {framesToDelete.Length} quadros selecionados", $"the {framesToDelete.Length} selected frames");
        if (!ConfirmWithTheme(
                Text("Excluir quadros da animação", "Delete animation frames"),
                Text($"Excluir {frameDescription} da animação do objeto #{thing.Id:N0}? Os quadros restantes manterão suas imagens e durações. Os dados de sprite não utilizados continuarão no SPR.",
                    $"Delete {frameDescription} from object #{thing.Id:N0}'s animation? The remaining frames keep their images and durations. Unused sprite data will remain in the SPR."))) return;

        var selectedOldFrame = Math.Clamp(_selectedAnimationFrame, 0, group.Frames.Count - 1);
        var deleteSet = framesToDelete.ToHashSet();
        var remainingIndices = Enumerable.Range(0, group.Frames.Count).Where(index => !deleteSet.Contains(index)).ToArray();
        var remainingFrames = remainingIndices.Select(index => group.Frames[index]).ToArray();
        var mappedSelectedIndex = Array.IndexOf(remainingIndices, selectedOldFrame);
        if (mappedSelectedIndex < 0)
        {
            var nextRetainedFrame = Array.FindIndex(remainingIndices, index => index > selectedOldFrame);
            mappedSelectedIndex = nextRetainedFrame >= 0 ? nextRetainedFrame : remainingIndices.Length - 1;
        }

        var updatedGroup = group with
        {
            Frames = remainingFrames,
            StartFrame = RemapAnimationStartFrame(group.StartFrame, remainingIndices)
        };
        var updatedGroups = thing.FrameGroups.ToArray();
        var groupIndex = Array.FindIndex(updatedGroups, item => item.Type == group.Type);
        if (groupIndex < 0) return;
        updatedGroups[groupIndex] = updatedGroup;
        var updatedThing = thing with
        {
            FrameGroups = updatedGroups,
            SpriteCount = updatedGroups.Sum(item => item.Frames.Sum(frame => frame.SpriteIds.Length))
        };

        RunDatThingOperation(() =>
        {
            StopAnimation();
            ApplyDatThingChange(updatedThing, addNew: false, preserveTextureSelection: true, refreshUi: false);
            _selectedAnimationFrame = mappedSelectedIndex;
            _selectedFilmRollFrames.Clear();
            if (_filmRollMode) _selectedFilmRollFrames.Add(mappedSelectedIndex);
            _filmRollSelectionAnchor = mappedSelectedIndex;
            _animationLoop = 0;
            _animationDirection = 1;
            UpdateAnimationAndLayerControls();
            UpdatePatternInfo(updatedGroup);
            UpdateItemAttributeEditor();
            SetSelectionSummary($"{CategoryName(thing.Category)} #{thing.Id}",
                $"{updatedGroup.Width * SpriteSide} × {updatedGroup.Height * SpriteSide} px");
            MetadataSaveStatus.Text = Text($"{framesToDelete.Length} quadro(s) removido(s) da animação. Salve o DAT para gravar a alteração.",
                $"{framesToDelete.Length} frame(s) removed from the animation. Save the DAT to keep the change.");
            _ = LoadSelectedThingPreviewAsync();
            _ = LoadSelectedThingInfoPreviewAsync();
            if (_filmRollMode) _ = RenderFilmRollFramesAsync();
        });
    }

    private static sbyte RemapAnimationStartFrame(sbyte startFrame, int[] remainingFrameIndices)
    {
        if (startFrame < 0 || remainingFrameIndices.Length == 0) return startFrame;
        var exactFrame = Array.IndexOf(remainingFrameIndices, (int)startFrame);
        if (exactFrame >= 0) return checked((sbyte)exactFrame);

        var nearestFrameIndex = 0;
        var nearestDistance = int.MaxValue;
        for (var index = 0; index < remainingFrameIndices.Length; index++)
        {
            var distance = Math.Abs(remainingFrameIndices[index] - startFrame);
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearestFrameIndex = index;
        }
        return checked((sbyte)nearestFrameIndex);
    }

    private async Task ApplyPixelTransformAsync(int dx, int dy, string? alignment)
    {
        if (_datModel is not { } model || _selectedThing is not { } thing || _selectedFrameGroup is not { } group
            || _sprStream is not { } stream || _filePath is null)
            throw new InvalidOperationException(Text("Abra um DAT e um SPR e selecione um objeto.", "Open a DAT and SPR and select an object."));

        int[] targetFrames = _filmRollMode && _selectedFilmRollFrames.Count > 0
            ? _selectedFilmRollFrames.Order().Where(frame => frame >= 0 && frame < group.Frames.Count).ToArray()
            : [_selectedAnimationFrame];
        var changedSprites = new List<(int Frame, int Slot, BitmapSource Bitmap)>();
        var canvasWidth = group.Width * SpriteSide;
        var canvasHeight = group.Height * SpriteSide;
        var previousPreview = PreviewImage.Source as BitmapSource;
        BitmapSource? shiftedPreview = null;

        foreach (var frameIndex in targetFrames)
        {
            var composite = await ReadThingBitmapAsync(thing, group, frameIndex,
                _selectedPatternX, _selectedPatternY, _selectedPatternZ, _selectedLayer,
                stream.SafeFileHandle, cacheSelectedSprites: true, colorizeOutfitPreview: false);
            if (composite is null) continue;
            var pixels = ReadBitmapPixels(composite);
            var shiftX = dx;
            var shiftY = dy;
            if (alignment is not null)
            {
                (shiftX, shiftY) = GetPixelAlignmentShift(pixels, canvasWidth, canvasHeight, alignment);
                if (shiftX == 0 && shiftY == 0) continue;
            }
            else if (!_wrapPixelShift && PixelShiftWouldClip(pixels, canvasWidth, canvasHeight, shiftX, shiftY))
            {
                MetadataSaveStatus.Text = Text("O deslocamento foi bloqueado para não cortar pixels. Ative Wrap para contornar a borda.",
                    "The shift was blocked to prevent clipping. Enable Wrap to move pixels across the edge.");
                return;
            }

            var shifted = ShiftPixelCanvas(pixels, canvasWidth, canvasHeight, shiftX, shiftY, _wrapPixelShift);
            if (pixels.AsSpan().SequenceEqual(shifted)) continue;
            if (frameIndex == _selectedAnimationFrame && previousPreview is not null
                && previousPreview.PixelWidth == canvasWidth && previousPreview.PixelHeight == canvasHeight)
            {
                var previewPixels = ReadBitmapPixels(previousPreview);
                shiftedPreview = ToBitmap(ShiftPixelCanvas(previewPixels, canvasWidth, canvasHeight,
                    shiftX, shiftY, _wrapPixelShift), canvasWidth, canvasHeight);
            }
            var frameIds = group.Frames[frameIndex].SpriteIds;
            for (var visualY = 0; visualY < group.Height; visualY++)
            for (var visualX = 0; visualX < group.Width; visualX++)
            {
                var tilePixels = ExtractPixelTile(shifted, canvasWidth, visualX, visualY);
                var tileIndex = (group.Height - visualY - 1) * group.Width + group.Width - visualX - 1;
                var spriteSlot = GetSpriteReferenceIndex(group, _selectedPatternX, _selectedPatternY, _selectedPatternZ, _selectedLayer, tileIndex);
                var originalId = spriteSlot < frameIds.Length ? frameIds[spriteSlot] : 0;
                var originalBitmap = originalId == 0 ? null : await ReadSelectedThingSpriteBitmapAsync(originalId, stream.SafeFileHandle, useSelectionCache: true);
                var originalPixels = originalBitmap is null ? new byte[SpritePixelCount * 4] : ReadBitmapPixels(originalBitmap);
                if (tilePixels.AsSpan().SequenceEqual(originalPixels)) continue;
                changedSprites.Add((frameIndex, spriteSlot, ToBitmap(tilePixels)));
            }
        }

        if (changedSprites.Count == 0)
        {
            MetadataSaveStatus.Text = alignment is null
                ? Text("Nenhum pixel precisou ser alterado.", "No pixels needed to change.")
                : Text("O conteúdo já está nessa posição.", "The content is already in that position.");
            return;
        }

        var frameGroups = thing.FrameGroups.ToArray();
        var groupIndex = Array.FindIndex(frameGroups, item => item.Type == group.Type);
        if (groupIndex < 0) throw new InvalidOperationException(Text("O grupo de quadros selecionado não existe mais.", "The selected frame group no longer exists."));
        var frames = group.Frames.Select(frame => frame with { SpriteIds = (uint[])frame.SpriteIds.Clone() }).ToArray();
        var stagedSprites = new Dictionary<uint, BitmapSource>();
        var nextVirtualId = checked(_spriteCount + (uint)_pendingPixelSprites.Count + 1);
        foreach (var (frame, slot, bitmap) in changedSprites)
        {
            var spriteId = frames[frame].SpriteIds[slot];
            if (spriteId <= _spriteCount || (!_pendingPixelSprites.ContainsKey(spriteId) && !stagedSprites.ContainsKey(spriteId)))
                spriteId = nextVirtualId++;
            stagedSprites[spriteId] = bitmap;
            frames[frame].SpriteIds[slot] = spriteId;
        }
        var updatedGroup = group with { Frames = frames };
        frameGroups[groupIndex] = updatedGroup;
        var updatedThing = thing with
        {
            FrameGroups = frameGroups,
            SpriteCount = frameGroups.Sum(item => item.Frames.Sum(frame => frame.SpriteIds.Length))
        };
        ApplyDatThingChange(updatedThing, addNew: false, preserveTextureSelection: true, refreshUi: false,
            serializeModelForValidation: false, fastPixelEdit: true);
        foreach (var (id, bitmap) in stagedSprites) _pendingPixelSprites[id] = bitmap;
        _selectedThingSpriteBitmaps.Clear();
        if (shiftedPreview is not null) SetCanvasPreviewBitmap(shiftedPreview);
        UpdateSaveButtonsState();
        MetadataSaveStatus.Text = alignment is null
            ? Text($"Pixels deslocados {Math.Abs(dx) + Math.Abs(dy)} px; salve tudo para gravar no SPR e no DAT.",
                $"Pixels shifted by {Math.Abs(dx) + Math.Abs(dy)} px; save all to write the SPR and DAT.")
            : Text("Pixels alinhados; salve tudo para gravar no SPR e no DAT.", "Pixels aligned; save all to write the SPR and DAT.");
        if (_filmRollMode) _ = RenderFilmRollFramesAsync();
    }

    private static byte[] ReadBitmapPixels(BitmapSource bitmap)
    {
        var converted = bitmap.Format == PixelFormats.Bgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }

    private static bool PixelShiftWouldClip(byte[] pixels, int width, int height, int dx, int dy)
    {
        if (dx > 0)
            for (var y = 0; y < height; y++) if (pixels[(y * width + width - 1) * 4 + 3] > 0) return true;
        if (dx < 0)
            for (var y = 0; y < height; y++) if (pixels[(y * width) * 4 + 3] > 0) return true;
        if (dy > 0)
            for (var x = 0; x < width; x++) if (pixels[((height - 1) * width + x) * 4 + 3] > 0) return true;
        if (dy < 0)
            for (var x = 0; x < width; x++) if (pixels[x * 4 + 3] > 0) return true;
        return false;
    }

    private static (int X, int Y) GetPixelAlignmentShift(byte[] pixels, int width, int height, string alignment)
    {
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (pixels[(y * width + x) * 4 + 3] == 0) continue;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        if (maxX < 0) return (0, 0);
        var contentWidth = maxX - minX + 1;
        var contentHeight = maxY - minY + 1;
        var targetX = alignment switch { "topRight" or "bottomRight" => width - contentWidth, "center" => (width - contentWidth) / 2, _ => 0 };
        var targetY = alignment switch { "bottomRight" or "bottomLeft" => height - contentHeight, "center" => (height - contentHeight) / 2, _ => 0 };
        return (targetX - minX, targetY - minY);
    }

    private static byte[] ShiftPixelCanvas(byte[] pixels, int width, int height, int dx, int dy, bool wrap)
    {
        var shifted = new byte[pixels.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var source = (y * width + x) * 4;
            if (pixels[source + 3] == 0) continue;
            var targetX = x + dx;
            var targetY = y + dy;
            if (wrap)
            {
                targetX = (targetX % width + width) % width;
                targetY = (targetY % height + height) % height;
            }
            else if (targetX < 0 || targetX >= width || targetY < 0 || targetY >= height) continue;
            Buffer.BlockCopy(pixels, source, shifted, (targetY * width + targetX) * 4, 4);
        }
        return shifted;
    }

    private static byte[] ExtractPixelTile(byte[] canvas, int canvasWidth, int visualX, int visualY)
    {
        var tile = new byte[SpritePixelCount * 4];
        for (var y = 0; y < SpriteSide; y++)
        for (var x = 0; x < SpriteSide; x++)
        {
            var source = ((visualY * SpriteSide + y) * canvasWidth + visualX * SpriteSide + x) * 4;
            var destination = (y * SpriteSide + x) * 4;
            Buffer.BlockCopy(canvas, source, tile, destination, 4);
        }
        return tile;
    }

    private void PreviewCanvas_DragOver(object sender, DragEventArgs e)
    {
        var imageSheetDrop = _datModel is not null && _sprStream is not null && _filePath is not null
            && !_sprEditInProgress && TryGetDroppedSpriteSheetPath(e, out _);
        var validSpriteDrop = e.Data.GetDataPresent(SpriteDragDataFormat) && _datModel is not null
            && _selectedThing is not null && _selectedFrameGroup is not null
            && _sprStream is not null && _filePath is not null;
        var valid = imageSheetDrop || validSpriteDrop;
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        CanvasDropHint.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        if (valid)
            CanvasDropHintText.Text = imageSheetDrop
                ? Text("Solte para fatiar a spritesheet e montar um novo objeto", "Drop to slice the sprite sheet and assemble a new object")
                : Text("Solte para colocar o sprite nesta célula", "Drop to place the sprite in this cell");
        e.Handled = true;
    }

    private void PreviewCanvas_DragLeave(object sender, DragEventArgs e) => CanvasDropHint.Visibility = Visibility.Collapsed;

    private async void PreviewCanvas_Drop(object sender, DragEventArgs e)
    {
        CanvasDropHint.Visibility = Visibility.Collapsed;
        if (TryGetDroppedSpriteSheetPath(e, out var imagePath))
        {
            e.Handled = true;
            if (_datModel is not null && _sprStream is not null && _filePath is not null && !_sprEditInProgress)
                await ImportSpriteSheetAsync(imagePath);
            return;
        }
        if (!e.Data.GetDataPresent(SpriteDragDataFormat)
            || e.Data.GetData(SpriteDragDataFormat) is not uint spriteId
            || _datModel is null || _selectedThing is null || _selectedFrameGroup is null
            || spriteId == 0 || spriteId > _spriteCount) return;

        var group = _selectedFrameGroup;
        var zoom = GetEffectivePreviewScale();
        var displayedWidth = group.Width * SpriteSide * zoom;
        var displayedHeight = group.Height * SpriteSide * zoom;
        var point = e.GetPosition(PreviewCanvasRoot);
        var x = point.X - (PreviewCanvasRoot.ActualWidth - displayedWidth) / 2;
        var y = point.Y - (PreviewCanvasRoot.ActualHeight - displayedHeight) / 2;
        if (x < 0 || y < 0 || x >= displayedWidth || y >= displayedHeight) return;

        var renderedX = Math.Clamp((int)(x / (SpriteSide * zoom)), 0, group.Width - 1);
        var renderedY = Math.Clamp((int)(y / (SpriteSide * zoom)), 0, group.Height - 1);
        var datX = group.Width - renderedX - 1;
        var datY = group.Height - renderedY - 1;
        var tileIndex = datY * group.Width + datX;
        RunDatThingOperation(() => AssignSpriteToCurrentCell(spriteId, tileIndex));
        e.Handled = true;
    }

    private static bool TryGetDroppedSpriteSheetPath(DragEventArgs e, out string path)
    {
        path = string.Empty;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] files
            || files.Length != 1) return false;
        var extension = Path.GetExtension(files[0]);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)) return false;
        path = files[0];
        return true;
    }

    private async Task ImportSpriteSheetAsync(string path)
    {
        try
        {
            var source = await Task.Run(() => LoadSpriteImage(path));
            var category = SelectedDatCategory is "item" or "effect" ? SelectedDatCategory : "item";
            var currentGroup = _selectedThing?.Category == category ? _selectedFrameGroup : null;
            var initialWidth = currentGroup?.Width ?? 1;
            var initialHeight = currentGroup?.Height ?? 1;
            var estimatedColumns = source.PixelWidth / (initialWidth * SpriteSide);
            var estimatedRows = source.PixelHeight / (initialHeight * SpriteSide);
            var estimatedFrames = currentGroup?.Frames.Count
                ?? (int)Math.Clamp((long)estimatedColumns * estimatedRows, 1, byte.MaxValue);
            var dialog = new SpriteSheetAssemblyDialog(path, source.PixelWidth, source.PixelHeight, IsEnglish,
                initialWidth, initialHeight, estimatedFrames, category) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Options is not { } options) return;

            var model = _datModel ?? throw new InvalidOperationException(Text("Abra um DAT antes de montar a imagem.",
                "Open a DAT file before assembling the image."));
            if (_sprStream is null || _filePath is null)
                throw new InvalidOperationException(Text("Abra um SPR antes de montar a imagem.", "Open an SPR file before assembling the image."));
            _ = GetNextDatThingId(model, options.Category);

            var sprites = SliceSpriteSheet(source, options);
            if ((ulong)_spriteCount + (uint)sprites.Count > uint.MaxValue)
                throw new InvalidDataException(Text("A quantidade ultrapassaria o limite de IDs do SPR.",
                    "The sprite count would exceed the SPR ID limit."));

            var firstSpriteId = await SaveSprChangesAsync(sprites);
            if (firstSpriteId is not uint firstId)
                throw new InvalidOperationException(Text("Não foi possível gravar os sprites no SPR.",
                    "Could not write the sprites to the SPR."));
            var spriteIds = Enumerable.Range(0, sprites.Count)
                .Select(index => checked(firstId + (uint)index)).ToArray();
            RunDatThingOperation(() => CreateAssembledDatThing(options.Category,
                options.Width, options.Height, options.FrameCount, spriteIds));
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            MessageBox.Show(this, ex.Message, Text("Falha ao montar spritesheet", "Sprite sheet assembly failed"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private List<BitmapSource> SliceSpriteSheet(BitmapSource source, SpriteSheetAssemblyOptions options)
    {
        var frameWidth = checked(options.Width * SpriteSide);
        var frameHeight = checked(options.Height * SpriteSide);
        if (source.PixelWidth % frameWidth != 0 || source.PixelHeight % frameHeight != 0)
            throw new InvalidDataException(Text("As dimensões da imagem não correspondem ao tamanho selecionado para cada quadro.",
                "The image dimensions do not match the selected frame size."));
        var columns = source.PixelWidth / frameWidth;
        var availableFrames = (long)columns * (source.PixelHeight / frameHeight);
        var spriteCount = checked(options.Width * options.Height * options.FrameCount);
        if (options.FrameCount > availableFrames || spriteCount > 16_384)
            throw new InvalidDataException(Text("A spritesheet não contém a quantidade de quadros solicitada.",
                "The sprite sheet does not contain the requested number of frames."));

        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        if (bgra.CanFreeze) bgra.Freeze();

        var sprites = new List<BitmapSource>(spriteCount);
        for (var frame = 0; frame < options.FrameCount; frame++)
        {
            var frameX = (frame % columns) * frameWidth;
            var frameY = (frame / columns) * frameHeight;
            for (var tileY = 0; tileY < options.Height; tileY++)
            for (var tileX = 0; tileX < options.Width; tileX++)
            {
                var crop = new CroppedBitmap(bgra, new Int32Rect(
                    frameX + tileX * SpriteSide, frameY + tileY * SpriteSide, SpriteSide, SpriteSide));
                BitmapSource tile = crop.Format == PixelFormats.Bgra32
                    ? crop
                    : new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
                if (tile.CanFreeze) tile.Freeze();
                sprites.Add(tile);
            }
        }
        return sprites;
    }

    private void AssignSpriteToCurrentCell(uint spriteId, int tileIndex)
    {
        if (_datModel is null || _selectedThing is null || _selectedFrameGroup is null)
            throw new InvalidOperationException(Text("Selecione um objeto DAT antes de soltar o sprite.", "Select a DAT object before dropping a sprite."));
        if (spriteId == 0 || spriteId > _spriteCount)
            throw new InvalidDataException(Text("O sprite selecionado não existe no SPR aberto.", "The selected sprite does not exist in the open SPR."));

        var thing = _selectedThing;
        var group = _selectedFrameGroup;
        var groupIndex = Array.FindIndex(thing.FrameGroups.ToArray(), candidate => candidate.Type == group.Type);
        if (groupIndex < 0 || tileIndex < 0 || tileIndex >= group.Width * group.Height)
            throw new InvalidOperationException(Text("Não foi possível identificar a célula do objeto.", "Could not identify the object cell."));
        var frameIndex = Math.Clamp(_selectedAnimationFrame, 0, group.Frames.Count - 1);
        var patternIndex = (_selectedPatternZ * group.PatternY + _selectedPatternY) * group.PatternX + _selectedPatternX;
        var spriteIndex = patternIndex * group.Width * group.Height * group.Layers
            + _selectedLayer * group.Width * group.Height + tileIndex;
        var frames = group.Frames.ToArray();
        var spriteIds = (uint[])frames[frameIndex].SpriteIds.Clone();
        if (spriteIndex >= spriteIds.Length)
            throw new InvalidDataException(Text("A célula selecionada não corresponde às referências do objeto.", "The selected cell does not match the object's sprite references."));
        if (spriteIds[spriteIndex] == spriteId) return;
        spriteIds[spriteIndex] = spriteId;
        frames[frameIndex] = frames[frameIndex] with { SpriteIds = spriteIds };
        var updatedGroup = group with { Frames = frames };
        var groups = thing.FrameGroups.ToArray();
        groups[groupIndex] = updatedGroup;
        var updatedThing = thing with { FrameGroups = groups, SpriteCount = groups.Sum(item => item.Frames.Sum(frame => frame.SpriteIds.Length)) };
        ApplyDatThingChange(updatedThing, addNew: false, preserveTextureSelection: true);
    }

    private void AutoAssembleSpritesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_datModel is null || _sprStream is null || _spriteCount == 0) return;
        var category = SelectedDatCategory is "item" or "effect" ? SelectedDatCategory : "item";
        var currentGroup = _selectedThing?.Category == category ? _selectedFrameGroup : null;
        var stream = _sprStream;
        var picker = new SpriteAssemblyPickerWindow(_spriteCount, _selectedObjectSpriteIds, _objectSpriteId,
            id => ReadSpriteBitmapAsync(id, stream.SafeFileHandle), IsEnglish,
            currentGroup?.Width ?? 1, currentGroup?.Height ?? 1, currentGroup?.Frames.Count ?? 1, category)
        {
            Owner = this
        };
        if (picker.ShowDialog() != true) return;

        var selectedIds = picker.SelectedSpriteIds.Order().ToArray();
        _selectedObjectSpriteIds.Clear();
        _selectedObjectSpriteIds.UnionWith(selectedIds);
        _objectSpriteSelectionAnchorId = picker.CurrentSpriteId;
        SetObjectSpriteSelection(picker.CurrentSpriteId, resetMultipleSelection: false);
        UpdateObjectSpriteSelectionVisuals();
        RunDatThingOperation(() => CreateAssembledDatThing(picker.DestinationCategory,
            picker.AssemblyWidth, picker.AssemblyHeight, picker.AnimationCount, selectedIds));
    }

    private void CreateAssembledDatThing(string category, int width, int height, int animationCount,
        IReadOnlyList<uint> spriteIds)
    {
        var model = _datModel ?? throw new InvalidOperationException(Text("Abra um DAT primeiro.", "Open a DAT file first."));
        if (category is not ("item" or "effect"))
            throw new InvalidDataException(Text("Escolha Item ou Efeito para criar o objeto.", "Choose Item or Effect for the new object."));
        var spritesPerFrame = checked(width * height);
        var requiredSpriteCount = checked(spritesPerFrame * animationCount);
        if (spriteIds.Count != requiredSpriteCount)
            throw new InvalidDataException(Text($"A montagem precisa de exatamente {requiredSpriteCount:N0} sprites.",
                $"Assembly requires exactly {requiredSpriteCount:N0} sprites."));

        // Slicer/picker order is top-left to bottom-right. DAT sprite references
        // are rendered from the bottom-right anchor, so map each visual cell to
        // its corresponding DAT slot instead of copying the list directly.
        var visualTileSlots = GetVisualTileIndices(width, height).ToArray();
        var frames = Enumerable.Range(0, animationCount)
            .Select(frameIndex =>
            {
                var frameSpriteIds = new uint[spritesPerFrame];
                var frameStart = frameIndex * spritesPerFrame;
                for (var visualTile = 0; visualTile < spritesPerFrame; visualTile++)
                    frameSpriteIds[visualTileSlots[visualTile]] = spriteIds[frameStart + visualTile];
                return new DatAnimationFrame(frameSpriteIds, 100, 100);
            })
            .ToArray();
        var frameGroup = new DatFrameGroup(0, width, height, 32, 1, 1, 1, 1, 0, 0, 0, frames);
        var id = GetNextDatThingId(model, category);
        var thing = CreateBlankDatThing(id, category, null, model.VersionValue) with
        {
            FrameGroups = [frameGroup],
            SpriteCount = requiredSpriteCount
        };

        SelectComboTag(CategoryCombo, category);
        ApplyDatThingChange(thing, addNew: true);
        var categoryName = CategoryName(category);
        MetadataSaveStatus.Text = Text(
            $"{categoryName} #{id:N0} criado: grade {width} × {height}, {animationCount} animação(ões), {requiredSpriteCount:N0} sprites. Salve o DAT.",
            $"{categoryName} #{id:N0} created: {width} × {height} grid, {animationCount} animation(s), {requiredSpriteCount:N0} sprites. Save the DAT.");
    }

    private void AutoAssembleSelectedSprites()
    {
        if (_datModel is null || _selectedThing is null || _selectedFrameGroup is null) return;
        var thing = _selectedThing;
        var group = _selectedFrameGroup;
        var selectedIds = _selectedObjectSpriteIds.Where(id => id > 0 && id <= _spriteCount).Order().ToArray();
        if (selectedIds.Length == 0) throw new InvalidOperationException(Text("Selecione um ou mais sprites na lista à direita.", "Select one or more sprites in the list on the right."));

        var groupIndex = Array.FindIndex(thing.FrameGroups.ToArray(), candidate => candidate.Type == group.Type);
        if (groupIndex < 0) throw new InvalidOperationException(Text("O grupo de quadros selecionado não existe mais.", "The selected frame group no longer exists."));

        var tileCount = checked(group.Width * group.Height);
        var frames = group.Frames.Select(frame => frame with { SpriteIds = (uint[])frame.SpriteIds.Clone() }).ToList();
        var newWidth = group.Width;
        var newHeight = group.Height;
        string placement;

        if (group.Frames.Count == 1 && thing.Category == "item")
        {
            if (selectedIds.Length > tileCount)
            {
                (newWidth, newHeight) = GetAutoGridDimensions(selectedIds.Length);
                var resizedReferences = (long)newWidth * newHeight * group.Layers * group.PatternX * group.PatternY * group.PatternZ * group.Frames.Count;
                var otherReferences = thing.FrameGroups.Where((_, index) => index != groupIndex)
                    .Sum(item => (long)item.Frames.Sum(frame => frame.SpriteIds.Length));
                if (resizedReferences + otherReferences > 1_000_000)
                    throw new InvalidDataException(Text("A grade ultrapassaria o limite de referências do DAT.", "The grid would exceed the DAT reference limit."));
                group = ResizeDatFrameGroup(group, newWidth, newHeight);
                frames = group.Frames.Select(frame => frame with { SpriteIds = (uint[])frame.SpriteIds.Clone() }).ToList();
                tileCount = checked(newWidth * newHeight);
            }

            var targetSlots = GetVisualTileIndices(newWidth, newHeight)
                .Select(tile => GetSpriteReferenceIndex(group, _selectedPatternX, _selectedPatternY, _selectedPatternZ, _selectedLayer, tile))
                .ToArray();
            ClearSpriteSlots(frames, targetSlots);
            for (var index = 0; index < selectedIds.Length && index < targetSlots.Length; index++)
                frames[0].SpriteIds[targetSlots[index]] = selectedIds[index];
            placement = Text($"grade de {newWidth} × {newHeight}", $"{newWidth} × {newHeight} grid");
        }
        else if (group.Frames.Count == 1 && selectedIds.Length == group.Frames[0].SpriteIds.Length)
        {
            var allSlots = GetAllVisualSpriteReferenceIndices(group).ToArray();
            if (allSlots.Length != selectedIds.Length)
                throw new InvalidDataException(Text("A estrutura do grupo de quadros está inconsistente.", "The frame group structure is inconsistent."));
            Array.Clear(frames[0].SpriteIds);
            for (var index = 0; index < selectedIds.Length; index++) frames[0].SpriteIds[allSlots[index]] = selectedIds[index];
            placement = Text("todos os padrões e camadas", "all patterns and layers");
        }
        else if (group.Frames.Count == 1 && thing.Category == "outfit"
            && selectedIds.Length == group.PatternX * tileCount)
        {
            var directionSlots = Enumerable.Range(0, group.PatternX)
                .SelectMany(patternX => GetVisualTileIndices(group.Width, group.Height)
                    .Select(tile => GetSpriteReferenceIndex(group, patternX, _selectedPatternY, _selectedPatternZ, _selectedLayer, tile)))
                .ToArray();
            ClearSpriteSlots(frames, directionSlots);
            for (var index = 0; index < selectedIds.Length; index++) frames[0].SpriteIds[directionSlots[index]] = selectedIds[index];
            placement = Text("direções do outfit", "outfit directions");
        }
        else
        {
            var requiredFrames = Math.Max(group.Frames.Count, (selectedIds.Length + tileCount - 1) / tileCount);
            if (requiredFrames > byte.MaxValue)
                throw new InvalidDataException(Text("A seleção exigiria mais de 255 quadros de animação.", "The selection would require more than 255 animation frames."));
            var totalGroupReferences = (long)group.Width * group.Height * group.Layers * group.PatternX * group.PatternY * group.PatternZ * requiredFrames;
            var otherReferences = thing.FrameGroups.Where((_, index) => index != groupIndex)
                .Sum(item => (long)item.Frames.Sum(frame => frame.SpriteIds.Length));
            if (totalGroupReferences + otherReferences > 1_000_000)
                throw new InvalidDataException(Text("A montagem ultrapassaria o limite de referências do DAT.", "The assembly would exceed the DAT reference limit."));

            while (frames.Count < requiredFrames)
            {
                var last = frames[^1];
                frames.Add(last with { SpriteIds = (uint[])last.SpriteIds.Clone() });
            }
            var targetSlots = GetVisualTileIndices(group.Width, group.Height)
                .Select(tile => GetSpriteReferenceIndex(group, _selectedPatternX, _selectedPatternY, _selectedPatternZ, _selectedLayer, tile))
                .ToArray();
            ClearSpriteSlots(frames, targetSlots);
            for (var index = 0; index < selectedIds.Length; index++)
            {
                var frameIndex = index / tileCount;
                var tileOffset = index % tileCount;
                frames[frameIndex].SpriteIds[targetSlots[tileOffset]] = selectedIds[index];
            }
            placement = Text($"{frames.Count} quadro(s) de animação", $"{frames.Count} animation frame(s)");
        }

        var updatedGroup = group with { Width = newWidth, Height = newHeight, Frames = frames.ToArray() };
        var groups = thing.FrameGroups.ToArray();
        groups[groupIndex] = updatedGroup;
        var updatedThing = thing with
        {
            FrameGroups = groups,
            SpriteCount = groups.Sum(item => item.Frames.Sum(frame => frame.SpriteIds.Length))
        };
        ApplyDatThingChange(updatedThing, addNew: false, preserveTextureSelection: true);
        MetadataSaveStatus.Text = Text($"{selectedIds.Length:N0} sprites montados em {placement}. Salve o DAT.",
            $"{selectedIds.Length:N0} sprites assembled into {placement}. Save the DAT.");
    }

    private (int Width, int Height) GetAutoGridDimensions(int spriteCount)
    {
        if (spriteCount is < 1 or > 65_025)
            throw new InvalidDataException(Text("A montagem em grade aceita de 1 a 65.025 sprites.", "Grid assembly accepts 1 to 65,025 sprites."));
        var width = Math.Min(byte.MaxValue, (int)Math.Ceiling(Math.Sqrt(spriteCount)));
        var height = (spriteCount + width - 1) / width;
        if (height > byte.MaxValue)
            throw new InvalidDataException(Text("A grade automática ultrapassa as dimensões máximas do DAT.", "The automatic grid exceeds DAT dimension limits."));
        return (width, height);
    }

    private static DatFrameGroup ResizeDatFrameGroup(DatFrameGroup group, int width, int height)
    {
        var oldTileCount = group.Width * group.Height;
        var newTileCount = width * height;
        var patternCount = group.PatternX * group.PatternY * group.PatternZ;
        var frames = group.Frames.Select(frame =>
        {
            var resized = new uint[checked(newTileCount * group.Layers * patternCount)];
            for (var pattern = 0; pattern < patternCount; pattern++)
            for (var layer = 0; layer < group.Layers; layer++)
            for (var y = 0; y < Math.Min(group.Height, height); y++)
            for (var x = 0; x < Math.Min(group.Width, width); x++)
            {
                var oldIndex = pattern * oldTileCount * group.Layers + layer * oldTileCount + y * group.Width + x;
                var newIndex = pattern * newTileCount * group.Layers + layer * newTileCount + y * width + x;
                if (oldIndex < frame.SpriteIds.Length) resized[newIndex] = frame.SpriteIds[oldIndex];
            }
            return frame with { SpriteIds = resized };
        }).ToArray();
        return group with { Width = width, Height = height, Frames = frames };
    }

    private static IEnumerable<int> GetVisualTileIndices(int width, int height)
    {
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            yield return (height - y - 1) * width + width - x - 1;
    }

    private static int GetSpriteReferenceIndex(DatFrameGroup group, int patternX, int patternY, int patternZ, int layer, int tile)
    {
        var pattern = (patternZ * group.PatternY + patternY) * group.PatternX + patternX;
        return pattern * group.Width * group.Height * group.Layers + layer * group.Width * group.Height + tile;
    }

    private static IEnumerable<int> GetAllVisualSpriteReferenceIndices(DatFrameGroup group)
    {
        for (var patternZ = 0; patternZ < group.PatternZ; patternZ++)
        for (var patternY = 0; patternY < group.PatternY; patternY++)
        for (var patternX = 0; patternX < group.PatternX; patternX++)
        for (var layer = 0; layer < group.Layers; layer++)
        foreach (var tile in GetVisualTileIndices(group.Width, group.Height))
            yield return GetSpriteReferenceIndex(group, patternX, patternY, patternZ, layer, tile);
    }

    private static void ClearSpriteSlots(IReadOnlyList<DatAnimationFrame> frames, IReadOnlyList<int> slots)
    {
        foreach (var frame in frames)
        foreach (var slot in slots)
            if (slot >= 0 && slot < frame.SpriteIds.Length) frame.SpriteIds[slot] = 0;
    }

    private void UpdateThingControlLabels()
    {
        if (_selectedThing is null || _selectedFrameGroup is null) return;
        _updatingThingControls = true;
        try
        {
            PopulatePatternCombo(PatternXCombo, _selectedFrameGroup.PatternX, _selectedPatternX, 'x');
            PopulatePatternCombo(PatternYCombo, _selectedFrameGroup.PatternY, _selectedPatternY, 'y');
            PopulatePatternCombo(PatternZCombo, _selectedFrameGroup.PatternZ, _selectedPatternZ, 'z');
            PatternXLabel.Text = _selectedThing.Category == "outfit"
                ? Text("Direção", "Direction") : Text("Padrão X", "Pattern X");
            PatternYLabel.Text = _selectedThing.Category == "outfit"
                ? Text("Addon", "Addon") : Text("Padrão Y", "Pattern Y");
            PatternZLabel.Text = _selectedThing.Category == "outfit"
                ? Text("Montaria", "Mount") : Text("Padrão Z", "Pattern Z");
            for (var index = 0; index < FrameGroupCombo.Items.Count; index++)
            {
                if (FrameGroupCombo.Items[index] is not ComboBoxItem item || item.Tag is not byte groupType) continue;
                item.Content = FrameGroupName(groupType);
            }
        }
        finally { _updatingThingControls = false; }
    }

    private void ConfigureThingControls(DatThing thing)
    {
        StopAnimation();
        _selectedThing = thing;
        _selectedFrameGroup = thing.Category == "outfit"
            ? thing.FrameGroups.FirstOrDefault(group => group.Type == 1) ?? thing.FrameGroups.FirstOrDefault()
            : thing.FrameGroups.FirstOrDefault();
        if (_selectedFrameGroup is null)
        {
            ResetThingControls();
            return;
        }
        AnimationLayerPanel.Visibility = PatternMetadataPanel.Visibility = Visibility.Visible;

        _selectedAnimationFrame = InitialAnimationFrame(_selectedFrameGroup);
        _selectedLayer = 0;
        _selectedPatternX = thing.Category == "outfit" ? Math.Min(2, _selectedFrameGroup.PatternX - 1) : 0;
        _selectedPatternY = 0;
        _selectedPatternZ = 0;
        _animationLoop = 0;
        _animationDirection = 1;

        _updatingThingControls = true;
        try
        {
            FrameGroupCombo.Items.Clear();
            foreach (var group in thing.FrameGroups)
                FrameGroupCombo.Items.Add(new ComboBoxItem { Content = FrameGroupName(group.Type), Tag = group.Type });
            FrameGroupCombo.SelectedItem = FrameGroupCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is byte type && type == _selectedFrameGroup.Type);
            FrameGroupLabel.Visibility = FrameGroupCombo.Visibility = thing.FrameGroups.Count > 1
                ? Visibility.Visible : Visibility.Collapsed;
            PatternSelectorsPanel.Visibility = thing.FrameGroups.Count > 1
                || _selectedFrameGroup.PatternX > 1 || _selectedFrameGroup.PatternY > 1 || _selectedFrameGroup.PatternZ > 1
                ? Visibility.Visible : Visibility.Collapsed;
            PopulatePatternCombo(PatternXCombo, _selectedFrameGroup.PatternX, _selectedPatternX, 'x');
            PopulatePatternCombo(PatternYCombo, _selectedFrameGroup.PatternY, _selectedPatternY, 'y');
            PopulatePatternCombo(PatternZCombo, _selectedFrameGroup.PatternZ, _selectedPatternZ, 'z');
            PatternXLabel.Text = thing.Category == "outfit" ? Text("Direção", "Direction") : Text("Padrão X", "Pattern X");
            PatternYLabel.Text = thing.Category == "outfit" ? Text("Addon", "Addon") : Text("Padrão Y", "Pattern Y");
            PatternZLabel.Text = thing.Category == "outfit" ? Text("Montaria", "Mount") : Text("Padrão Z", "Pattern Z");
            UpdateAnimationAndLayerControls();
            UpdatePatternInfo(_selectedFrameGroup);
        }
        finally { _updatingThingControls = false; }
    }

    private void PopulatePatternCombo(ComboBox combo, int count, int selectedIndex, char axis)
    {
        combo.Items.Clear();
        var directions = new[]
        {
            Text("Norte", "North"), Text("Leste", "East"),
            Text("Sul", "South"), Text("Oeste", "West")
        };
        var addonNames = new[] { Text("Sem addon", "No addon"), Text("Addon 1", "Addon 1"), Text("Addon 2", "Addon 2"), Text("Addon 3", "Addon 3") };
        var mountNames = new[] { Text("Sem montaria", "No mount"), Text("Montaria 1", "Mount 1"), Text("Montaria 2", "Mount 2"), Text("Montaria 3", "Mount 3") };
        for (var index = 0; index < count; index++)
        {
            var content = axis switch
            {
                'x' when count == 4 => directions[index],
                'y' when _selectedThing?.Category == "outfit" && index < addonNames.Length => addonNames[index],
                'z' when _selectedThing?.Category == "outfit" && index < mountNames.Length => mountNames[index],
                _ => Text($"Padrão {index + 1}", $"Pattern {index + 1}")
            };
            combo.Items.Add(new ComboBoxItem { Content = content, Tag = index });
        }
        combo.SelectedIndex = count == 0 ? -1 : Math.Clamp(selectedIndex, 0, count - 1);
        var label = axis switch { 'x' => PatternXLabel, 'y' => PatternYLabel, _ => PatternZLabel };
        label.Visibility = combo.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string FrameGroupName(byte type) => type switch
    {
        0 => Text("Padrão / parado", "Default / idle"),
        1 => Text("Andando", "Walking"),
        _ => Text($"Grupo {type + 1}", $"Group {type + 1}")
    };

    private static int InitialAnimationFrame(DatFrameGroup group)
    {
        if (group.Frames.Count == 0) return 0;
        if (group.AnimationMode == 1)
        {
            var durations = group.Frames.Select(frame => Math.Max(1d,
                (frame.MinimumDuration + (double)frame.MaximumDuration) / 2)).ToArray();
            var elapsed = Environment.TickCount64 % Math.Max(1L, (long)durations.Sum());
            for (var index = 0; index < durations.Length; index++)
            {
                if (elapsed < durations[index]) return index;
                elapsed -= (long)durations[index];
            }
            return 0;
        }
        if (group.StartFrame >= 0 && group.StartFrame < group.Frames.Count) return group.StartFrame;
        return group.StartFrame < 0 ? Random.Shared.Next(group.Frames.Count) : 0;
    }

    private void UpdateAnimationAndLayerControls()
    {
        var group = _selectedFrameGroup;
        var frames = group?.Frames.Count ?? 0;
        var layers = group?.Layers ?? 0;
        _updatingThingControls = true;
        try
        {
            AnimationFrameSlider.Minimum = 1;
            AnimationFrameSlider.Maximum = Math.Max(1, frames);
            AnimationFrameSlider.Value = frames == 0 ? 1 : Math.Clamp(_selectedAnimationFrame + 1, 1, frames);
            AnimationFrameSlider.IsEnabled = frames > 1;
            AnimationPlayButton.IsEnabled = frames > 1 && _sprStream is not null;
            AnimationPlayButton.Content = _isPlaying ? "❚❚" : "▶";
            AnimationFrameText.Text = frames == 0 ? "0/0" : $"{_selectedAnimationFrame + 1}/{frames}";

            LayerSlider.Minimum = 1;
            LayerSlider.Maximum = Math.Max(1, layers);
            LayerSlider.Value = layers == 0 ? 1 : Math.Clamp(_selectedLayer + 1, 1, layers);
            LayerSlider.IsEnabled = layers > 1;
            LayerCounterText.Text = layers == 0 ? "—/—" : $"{_selectedLayer + 1}/{layers}";

            if (group is not null && frames > 0)
            {
                var frame = group.Frames[Math.Clamp(_selectedAnimationFrame, 0, frames - 1)];
                MinimumDurationInput.Text = frame.MinimumDuration.ToString();
                MaximumDurationInput.Text = frame.MaximumDuration.ToString();
                PatternFramesText.Text = frames.ToString();
            }
            else
            {
                MinimumDurationInput.Text = "—";
                MaximumDurationInput.Text = "—";
                PatternFramesText.Text = "—";
            }
        }
        finally { _updatingThingControls = false; }
        UpdateAnimationTimerInterval();
        UpdatePixelEditorControls();
    }

    private void UpdatePatternInfo(DatFrameGroup? group)
    {
        var wasUpdating = _updatingThingControls;
        _updatingThingControls = true;
        try
        {
            FrameGroupsValue.Text = _selectedThing?.FrameGroups.Count.ToString() ?? "—";
            PatternWidthText.Text = group?.Width.ToString() ?? "—";
            PatternHeightText.Text = group?.Height.ToString() ?? "—";
            PatternCropText.Text = group?.CropSize.ToString() ?? "—";
            PatternLayersText.Text = group?.Layers.ToString() ?? "—";
            PatternXText.Text = group?.PatternX.ToString() ?? "—";
            PatternYText.Text = group?.PatternY.ToString() ?? "—";
            PatternZText.Text = group?.PatternZ.ToString() ?? "—";
            PatternFramesText.Text = group?.Frames.Count.ToString() ?? "—";
        }
        finally { _updatingThingControls = wasUpdating; }
        UpdateMetadataEditingState();
    }

    private void UpdateMetadataEditingState()
    {
        var canEdit = _datModel is not null && _datPath is not null
            && _selectedThing is not null && _selectedFrameGroup is not null;
        FrameGroupsValue.IsEnabled = canEdit && _selectedThing?.Category == "outfit" && _datModel?.VersionValue >= 1057;
        PatternWidthText.IsEnabled = PatternHeightText.IsEnabled = PatternLayersText.IsEnabled =
            PatternXText.IsEnabled = PatternYText.IsEnabled = PatternFramesText.IsEnabled = canEdit;
        PatternCropText.IsEnabled = canEdit && (_selectedFrameGroup?.Width > 1 || _selectedFrameGroup?.Height > 1);
        PatternZText.IsEnabled = canEdit && _datModel?.VersionValue > 750;
        MinimumDurationInput.IsEnabled = MaximumDurationInput.IsEnabled = canEdit && _selectedFrameGroup?.Frames.Count > 0;
        ApplyMinimumDurationButton.IsEnabled = ApplyMaximumDurationButton.IsEnabled = canEdit && _selectedFrameGroup?.Frames.Count > 1;
        UpdateSaveButtonsState();
        CompileAsButton.IsEnabled = _datModel is not null && _datPath is not null
            && _sprStream is not null && _filePath is not null;
        UpdateBulkAnimationSpeedState();
    }

    private void UpdateSaveButtonsState()
    {
        var hasPendingChanges = _metadataHasUnsavedChanges || _pendingPixelSprites.Count > 0 || _serverItemFiles?.IsDirty == true;
        SaveDatButton.IsEnabled = hasPendingChanges && ((_datModel is not null && _datPath is not null) || _serverItemFiles is not null);
        SaveAllButton.IsEnabled = SaveDatButton.IsEnabled;
        SaveDatButton.Content = _pendingPixelSprites.Count > 0
            ? Text("Salvar tudo", "Save all")
            : _serverItemFiles is null ? Text("Salvar DAT", "Save DAT") : Text("Salvar DAT + OTB/XML", "Save DAT + OTB/XML");
    }

    private string SelectedDatCategory => (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "item";

    private void UpdateBulkAnimationSpeedState()
    {
        if (BulkAnimationSpeedButton is null) return;
        var category = SelectedDatCategory;
        var animatedThings = _datModel?.Things
            .Where(thing => thing.Category == category && thing.FrameGroups.Any(group => group.Frames.Count > 1))
            .ToArray() ?? [];
        var supportsDurations = _datModel?.ImprovedAnimations == true;
        BulkAnimationSpeedButton.IsEnabled = _datPath is not null && supportsDurations && animatedThings.Length > 0;
        BulkAnimationSpeedButton.ToolTip = !supportsDurations
            ? Text("Ative Animações melhoradas para editar as durações por quadro.",
                "Enable Improved Animations to edit per-frame durations.")
            : animatedThings.Length > 0
                ? Text($"Aplicar a {animatedThings.Length:N0} objetos animados desta categoria",
                    $"Apply to {animatedThings.Length:N0} animated objects in this category")
                : Text("A categoria atual não tem objetos com vários quadros.",
                    "The current category has no multi-frame objects.");
    }

    private void BuildItemPropertyControls()
    {
        PropertiesContent.Children.Clear();
        _propertySections.Clear();
        _propertyToggles.Clear();
        _propertyTextInputs.Clear();
        _propertyCombos.Clear();
        _propertyColorButtons.Clear();
        PropertiesContent.Children.Add(new TextBlock
        {
            Text = Text("Propriedades do DAT", "DAT properties"), FontSize = 11, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 0, 7)
        });

        AddNumericPropertySection("ground", "Piso", "Ground", [("Velocidade do piso", "Ground speed", 0, false, 0, 1000)]);
        var lightSection = CreatePropertySection("light", "Tem Luz", "Has light");
        AddPropertyNumberRow(lightSection, "light", "Cor da luz", "Light color", 1, signed: false, 0, 215, colorPicker: true);
        AddPropertyNumberRow(lightSection, "light", "Intensidade da luz", "Light intensity", 0, signed: false, 0, ushort.MaxValue);
        var automapSection = CreatePropertySection("automap", "Aparece no Minimapa", "Show on minimap");
        AddPropertyNumberRow(automapSection, "automap", "Cor no minimapa", "Minimap color", 0, signed: false, 0, 215, colorPicker: true);
        AddOffsetPropertySection();
        AddBonesPropertySection();
        AddStandaloneFlagPropertySection("animateAlways", "Sempre Animado", "Always animate");
        AddStandaloneFlagPropertySection("topEffect", "Efeito no Topo", "Top effect");
        AddNumericPropertySection("elevation", "Tem Elevação", "Has elevation", [("Elevação", "Elevation", 0, false, 0, 32)]);
        AddComboPropertySection("cloth", "Equipamento", "Equipment", "Slot", "Slot", [
            ("Arma de Duas Mãos", "Two Hand Weapon"), ("Capacete", "Helmet"), ("Amuleto", "Amulet"),
            ("Mochila", "Backpack"), ("Armadura", "Armor"), ("Escudo", "Shield"),
            ("Arma de Uma Mão", "One Hand Weapon"), ("Calças", "Legs"), ("Botas", "Boots"),
            ("Anel", "Ring"), ("Flecha", "Arrow")
        ]);
        AddMarketPropertySection();
        AddNumericPropertySection("writable", "Gravável", "Writable", [("Máx. de caracteres", "Max. characters", 0, false, 0, ushort.MaxValue)]);
        AddNumericPropertySection("writableOnce", "Gravável Só Uma Vez", "Writable once", [("Máx. de caracteres", "Max. characters", 0, false, 0, ushort.MaxValue)]);
        AddComboPropertySection("defaultAction", "Tem Ação", "Has action", "Tipo de ação", "Action type", [
            ("Nenhuma", "None"), ("Olhar", "Look"), ("Usar", "Use"), ("Abrir", "Open"), ("Destaque ao caminhar", "Autowalk highlight")
        ]);
        AddComboPropertySection("lensHelp", "Lens Help", "Lens help", "Tipo", "Type", [
            ("Escadas", "Ladders"), ("Grades de esgoto", "Sewer grates"), ("Piso de masmorra", "Dungeon floor"),
            ("Alavancas", "Levers"), ("Portas", "Doors"), ("Portas especiais", "Special doors"),
            ("Escadarias", "Stairs"), ("Caixas de correio", "Mailboxes"), ("Caixas de depósito", "Depot boxes"),
            ("Lixeiras", "Dustbins"), ("Pilhas de pedras", "Stone piles"), ("Placas", "Signs"), ("Livros e pergaminhos", "Books and scrolls")
        ], firstValue: 1100);
    }

    private void AddNumericPropertySection(string key, string titlePt, string titleEn,
        params (string Pt, string En, int Index, bool Signed, int Minimum, int Maximum)[] fields)
    {
        var body = CreatePropertySection(key, titlePt, titleEn);
        foreach (var field in fields)
            AddPropertyNumberRow(body, key, field.Pt, field.En, field.Index, field.Signed, field.Minimum, field.Maximum);
    }

    private void AddOffsetPropertySection()
    {
        var body = CreatePropertySection("offset", "Deslocamento", "Offset");
        AddPropertyNumberRow(body, "offset", "Deslocamento X", "Offset X", 0, signed: true, -256, 256);
        AddPropertyNumberRow(body, "offset", "Deslocamento Y", "Offset Y", 1, signed: true, -256, 256);
    }

    private void AddBonesPropertySection()
    {
        var body = CreatePropertySection("bones", "Ossos", "Bones");
        AddPropertyComboRow(body, "bonesDirection", "Direção", "Direction", [
            ("Norte", "North", 0), ("Sul", "South", 1), ("Leste", "East", 2), ("Oeste", "West", 3)
        ]);
        _bonesDirectionCombo = _propertyCombos["bonesDirection"][0];
        _bonesDirectionRow = _bonesDirectionCombo.Parent as StackPanel;
        _bonesDirectionCombo.SelectionChanged += BonesDirectionCombo_SelectionChanged;
        AddPropertyNumberRow(body, "bones", "Deslocamento X", "Offset X", -1, signed: true, -1000, 1000, boneX: true);
        AddPropertyNumberRow(body, "bones", "Deslocamento Y", "Offset Y", -1, signed: true, -1000, 1000, boneY: true);
    }

    private void AddStandaloneFlagPropertySection(string key, string labelPt, string labelEn)
    {
        var card = new Border
        {
            BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1),
            Background = ThemeBrush("PanelInsetBrush"), CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 0, 0, 7)
        };
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = Text("Flags", "Flags"), Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 9, Margin = new Thickness(2, 0, 0, 5)
        });
        var toggle = new CheckBox
        {
            Style = (Style)FindResource("PropertySwitchCheckBox"),
            Content = Text(labelPt, labelEn), Tag = key,
            Foreground = (Brush)FindResource("TextBrush"), FontSize = 9, Margin = new Thickness(1, 2, 0, 2)
        };
        toggle.Checked += PropertyToggle_Changed;
        toggle.Unchecked += PropertyToggle_Changed;
        content.Children.Add(toggle);
        card.Child = content;
        PropertiesContent.Children.Add(card);
        _propertySections[key] = card;
        _propertyToggles[key] = toggle;
    }

    private void AddComboPropertySection(string key, string titlePt, string titleEn, string labelPt, string labelEn,
        (string Pt, string En)[] options, int firstValue = 0)
    {
        var body = CreatePropertySection(key, titlePt, titleEn);
        var items = options.Select((option, index) => (option.Pt, option.En, index + firstValue)).ToArray();
        AddPropertyComboRow(body, key, labelPt, labelEn, items, key, 0);
    }

    private void AddMarketPropertySection()
    {
        var body = CreatePropertySection("market", "Mercado", "Market");
        AddPropertyTextRow(body, "market", "Nome", "Name");
        AddPropertyComboRow(body, "marketCategory", "Categoria", "Category", [
            ("Armaduras", "Armors", 1), ("Amuletos", "Amulets", 2), ("Botas", "Boots", 3), ("Recipientes", "Containers", 4),
            ("Decoração", "Decoration", 5), ("Comidas", "Foods", 6), ("Capacetes e chapéus", "Helmets and hats", 7),
            ("Calças", "Legs", 8), ("Outros", "Others", 9), ("Poções", "Potions", 10), ("Anéis", "Rings", 11),
            ("Runas", "Runes", 12), ("Escudos", "Shields", 13), ("Ferramentas", "Tools", 14), ("Valiosos", "Valuables", 15),
            ("Munição", "Ammunition", 16), ("Machados", "Axes", 17), ("Clavas", "Clubs", 18), ("Distância", "Distance", 19),
            ("Espadas", "Swords", 20), ("Wands e rods", "Wands and rods", 21), ("Pergaminhos premium", "Premium scrolls", 22),
            ("Todas as armas", "All weapons", 255)
        ], propertyKey: "market", valueIndex: 0);
        AddPropertyNumberRow(body, "market", "Comercializar como", "Trade as", 1, false, 0, ushort.MaxValue);
        AddPropertyNumberRow(body, "market", "Exibir como", "Show as", 2, false, 0, ushort.MaxValue);
        AddPropertyNumberRow(body, "market", "Vocação", "Vocation", 3, false, 0, ushort.MaxValue);
        AddPropertyNumberRow(body, "market", "Level", "Level", 4, false, 0, ushort.MaxValue);
    }

    private StackPanel CreatePropertySection(string key, string titlePt, string titleEn)
    {
        var card = new Border
        {
            BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1),
            Background = ThemeBrush("PanelInsetBrush"), CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 0, 0, 7)
        };
        var content = new StackPanel();
        var toggle = new CheckBox
        {
            Style = (Style)FindResource("PropertySwitchCheckBox"),
            Content = Text(titlePt, titleEn), Tag = key, Foreground = (Brush)FindResource("TextBrush"),
            FontSize = 9, Margin = new Thickness(0, 0, 0, 4)
        };
        toggle.Checked += PropertyToggle_Changed;
        toggle.Unchecked += PropertyToggle_Changed;
        content.Children.Add(toggle);
        card.Child = content;
        PropertiesContent.Children.Add(card);
        _propertySections[key] = card;
        _propertyToggles[key] = toggle;
        return content;
    }

    private void AddPropertyNumberRow(Panel parent, string key, string labelPt, string labelEn, int valueIndex,
        bool signed, int minimum, int maximum, bool boneX = false, bool boneY = false, bool colorPicker = false)
    {
        var row = CreatePropertyRow(labelPt, labelEn);
        var input = new TextBox
        {
            Width = 86, Height = 23, Padding = new Thickness(4, 1, 4, 1), HorizontalContentAlignment = HorizontalAlignment.Right,
            Background = ThemeBrush("CardBackgroundBrush"), Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"), FontSize = 9,
            Tag = new PropertyInputBinding(key, valueIndex, signed, BoneX: boneX, BoneY: boneY)
        };
        input.TextChanged += PropertyTextBox_TextChanged;
        row.Children.Add(input);
        parent.Children.Add(row);
        AddPropertyInput(key, input);
        input.Tag = new PropertyInputBinding(key, valueIndex, signed, BoneX: boneX, BoneY: boneY, Minimum: minimum, Maximum: maximum);
        if (colorPicker) AddPropertyColorPicker(row, key, valueIndex);
    }

    private void AddPropertyColorPicker(Panel row, string key, int valueIndex)
    {
        var button = new Button
        {
            Width = 27, Height = 23, Padding = new Thickness(2), Margin = new Thickness(5, 0, 0, 0),
            BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1),
            ToolTip = Text("Escolher cor", "Choose color")
        };
        var popup = new Popup
        {
            PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true
        };
        var palette = new UniformGrid { Columns = 16, Rows = 14 };
        for (var color = 0; color < 216; color++)
        {
            var swatch = new Button
            {
                Width = 18, Height = 18, Padding = new Thickness(0), Margin = new Thickness(1),
                Background = new SolidColorBrush(EightBitColor(color)),
                BorderBrush = ThemeBrush("FieldBorderBrush"), BorderThickness = new Thickness(1),
                ToolTip = $"{color}", Tag = new PropertyColorSwatchBinding(key, valueIndex, color, popup)
            };
            swatch.Click += PropertyColorSwatch_Click;
            palette.Children.Add(swatch);
        }
        popup.Child = new Border
        {
            Background = ThemeBrush("PanelBackground"),
            BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1),
            Padding = new Thickness(5), Child = palette
        };
        button.Tag = new PropertyColorBinding(key, valueIndex, popup);
        button.Click += PropertyColorButton_Click;
        row.Children.Add(button);
        _propertyColorButtons[(key, valueIndex)] = button;
    }

    private static Color EightBitColor(int index) => Color.FromRgb(
        (byte)(index / 36 % 6 * 51), (byte)(index / 6 % 6 * 51), (byte)(index % 6 * 51));

    private void PropertyColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PropertyColorBinding binding })
            binding.Popup.IsOpen = !binding.Popup.IsOpen;
    }

    private void PropertyColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedThing is null || _datModel is null
            || sender is not Button { Tag: PropertyColorSwatchBinding binding }) return;
        var fields = DatPropertiesCodec.Read(_selectedThing.RawProperties, _datModel.VersionValue, _datModel.MetadataReaderVersion);
        var field = DatPropertiesCodec.Get(fields, binding.Key);
        if (binding.ValueIndex < 0 || binding.ValueIndex >= field.Values.Length) return;
        var values = field.Values.ToArray();
        values[binding.ValueIndex] = binding.Color;
        binding.Popup.IsOpen = false;
        StoreSelectedProperty(binding.Key, field with { Enabled = true, Values = values });
        UpdateItemAttributeEditor();
    }

    private void AddPropertyTextRow(Panel parent, string key, string labelPt, string labelEn)
    {
        var row = CreatePropertyRow(labelPt, labelEn);
        var input = new TextBox
        {
            MinWidth = 180, Height = 23, Padding = new Thickness(4, 1, 4, 1),
            Background = ThemeBrush("CardBackgroundBrush"), Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"), FontSize = 9,
            Tag = new PropertyInputBinding(key, -1, TextValue: true)
        };
        input.TextChanged += PropertyTextBox_TextChanged;
        row.Children.Add(input);
        parent.Children.Add(row);
        AddPropertyInput(key, input);
    }

    private void AddPropertyInput(string key, TextBox input)
    {
        if (!_propertyTextInputs.TryGetValue(key, out var controls))
            _propertyTextInputs[key] = controls = [];
        controls.Add(input);
    }

    private StackPanel CreatePropertyRow(string labelPt, string labelEn)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 0, 0) };
        row.Children.Add(new TextBlock
        {
            Text = $"{Text(labelPt, labelEn)}:", Foreground = (Brush)FindResource("MutedBrush"), FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 7, 0)
        });
        return row;
    }

    private void AddPropertyComboRow(Panel parent, string collectionKey, string labelPt, string labelEn,
        (string Pt, string En, int Value)[] options, string? propertyKey = null, int valueIndex = 0)
    {
        var row = CreatePropertyRow(labelPt, labelEn);
        var combo = new ComboBox { Width = 195, Height = 23, FontSize = 9, Tag = new PropertyComboBinding(propertyKey ?? collectionKey, valueIndex) };
        foreach (var option in options)
            combo.Items.Add(new ComboBoxItem { Content = Text(option.Pt, option.En), Tag = option.Value });
        combo.SelectionChanged += PropertyCombo_SelectionChanged;
        row.Children.Add(combo);
        parent.Children.Add(row);
        if (!_propertyCombos.TryGetValue(collectionKey, out var controls))
            _propertyCombos[collectionKey] = controls = [];
        controls.Add(combo);
    }

    private void BuildItemFlagControls()
    {
        AttributeFlagsLeft.Children.Clear();
        AttributeFlagsRight.Children.Clear();
        _attributeFlagControls.Clear();
        _renderOrderControls.Clear();
        AttributeFlagsLeft.Children.Add(new TextBlock
        {
            Text = Text("Ordem de desenho", "Draw order"), Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 8, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 3)
        });
        AddRenderOrderRadio("common", "Comum", "Common", AttributeFlagsLeft);
        AddRenderOrderRadio("groundBorder", "Borda", "Ground border", AttributeFlagsLeft);
        AddRenderOrderRadio("onBottom", "Abaixo", "On bottom", AttributeFlagsLeft);
        AddRenderOrderRadio("onTop", "No Topo", "On top", AttributeFlagsLeft);
        AddItemFlag("container", "Recipiente", "Container", AttributeFlagsLeft);
        AddItemFlag("stackable", "Agrupável", "Stackable", AttributeFlagsLeft);
        AddItemFlag("forceUse", "Forçar Uso", "Force use", AttributeFlagsLeft);
        AddItemFlag("multiUse", "Multiúso", "Multi-use", AttributeFlagsLeft);
        AddItemFlag("fluidContainer", "Recipiente para Líquidos", "Fluid container", AttributeFlagsLeft);
        AddItemFlag("fluid", "Líquido", "Fluid", AttributeFlagsLeft);
        AddItemFlag("unpassable", "Não Pode Ser Atravessado", "Unpassable", AttributeFlagsLeft);
        AddItemFlag("unmovable", "Não Pode Ser Movido", "Unmovable", AttributeFlagsLeft);
        AddItemFlag("blockMissile", "Bloqueia Mísseis", "Blocks missiles", AttributeFlagsLeft);
        AddItemFlag("blockPathfinder", "Bloqueia o Caminho", "Blocks pathfinding", AttributeFlagsLeft);

        AddItemFlag("noMoveAnimation", "Não Muda Animação", "No move animation", AttributeFlagsRight);
        AddItemFlag("pickupable", "Pode Ser Apanhado", "Pickupable", AttributeFlagsRight);
        AddItemFlag("hangable", "Pode Ser Pendurado", "Hangable", AttributeFlagsRight);
        AddItemFlag("horizontal", "Gancho Horizontal", "Horizontal hook", AttributeFlagsRight);
        AddItemFlag("vertical", "Gancho Vertical", "Vertical hook", AttributeFlagsRight);
        AddItemFlag("rotatable", "Girável", "Rotatable", AttributeFlagsRight);
        AddItemFlag("dontHide", "Não Deve Ser Ocultado", "Do not hide", AttributeFlagsRight);
        AddItemFlag("translucent", "Translúcido", "Translucent", AttributeFlagsRight);
        AddItemFlag("lyingObject", "Objeto Deitado", "Lying object", AttributeFlagsRight);
        AddItemFlag("animateAlways", "Sempre Animado", "Always animate", AttributeFlagsRight);
        AddItemFlag("fullGround", "Piso Completo", "Full ground", AttributeFlagsRight);
        AddItemFlag("ignoreLook", "Ignora Look", "Ignore look", AttributeFlagsRight);
        AddItemFlag("usable", "Usável", "Usable", AttributeFlagsRight);
        AddItemFlag("hasCharges", "Tem Cargas", "Has charges", AttributeFlagsRight);
        AddItemFlag("floorChange", "Altera Piso", "Changes floor", AttributeFlagsRight);
        AddItemFlag("wrappable", "Embrulhável", "Wrappable", AttributeFlagsRight);
        AddItemFlag("unwrappable", "Desembrulhável", "Unwrappable", AttributeFlagsRight);
        AddItemFlag("topEffect", "Efeito no Topo", "Top effect", AttributeFlagsRight);
    }

    private void AddRenderOrderRadio(string key, string labelPt, string labelEn, Panel parent)
    {
        var radio = new RadioButton
        {
            Content = Text(labelPt, labelEn), Tag = key, GroupName = "ItemRenderOrder",
            Foreground = (Brush)FindResource("TextBrush"), FontSize = 9, Margin = new Thickness(1, 2, 0, 2)
        };
        radio.Checked += RenderOrderRadio_Checked;
        parent.Children.Add(radio);
        _renderOrderControls[key] = radio;
    }

    private void AddItemFlag(string key, string labelPt, string labelEn, Panel parent)
    {
        var checkBox = new CheckBox
        {
            Style = (Style)FindResource("PropertySwitchCheckBox"),
            Content = Text(labelPt, labelEn), Tag = key, Foreground = (Brush)FindResource("TextBrush"),
            FontSize = 9, Margin = new Thickness(1, 3, 0, 3)
        };
        checkBox.Checked += AttributeFlag_Changed;
        checkBox.Unchecked += AttributeFlag_Changed;
        parent.Children.Add(checkBox);
        _attributeFlagControls[key] = checkBox;
    }

    private void UpdateItemAttributeEditor()
    {
        var version = _datModel?.VersionValue ?? 0;
        var isItem = _selectedThing?.Category == "item" && _datModel is not null;
        var isOutfit = _selectedThing?.Category == "outfit" && _datModel is not null;
        var isEffect = _selectedThing?.Category == "effect" && _datModel is not null;
        var isMissile = _selectedThing?.Category == "missile" && _datModel is not null;
        var hasProperties = isItem || isOutfit || isEffect || isMissile;
        var canEdit = hasProperties && _datPath is not null;
        PropertiesPlaceholder.Visibility = hasProperties ? Visibility.Collapsed : Visibility.Visible;
        PropertiesScroll.Visibility = hasProperties ? Visibility.Visible : Visibility.Collapsed;
        AttributesPlaceholder.Visibility = isItem ? Visibility.Collapsed : Visibility.Visible;
        AttributesScroll.Visibility = isItem ? Visibility.Visible : Visibility.Collapsed;
        UpdateServerItemEditor();
        if (!hasProperties || _selectedThing is null || _datModel is null) return;

        var fields = DatPropertiesCodec.Read(_selectedThing.RawProperties, version, _datModel.MetadataReaderVersion);
        _updatingAttributeControls = true;
        try
        {
            foreach (var (key, section) in _propertySections)
            {
                var appliesToCategory = isItem
                    ? key is not ("animateAlways" or "topEffect")
                    : isOutfit
                        ? key is "light" or "bones" or "animateAlways"
                        : isEffect
                            ? key is "light" or "bones" or "topEffect"
                            : key is "light" or "bones";
                var supported = appliesToCategory && DatPropertiesCodec.Supports(version, key, _datModel.MetadataReaderVersion);
                section.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
                var field = DatPropertiesCodec.Get(fields, key);
                var toggle = _propertyToggles[key];
                toggle.IsEnabled = canEdit && supported;
                toggle.IsChecked = field.Enabled;

                if (_propertyTextInputs.TryGetValue(key, out var inputs))
                {
                    foreach (var input in inputs)
                    {
                        if (input.Tag is not PropertyInputBinding binding) continue;
                        var valueIndex = PropertyInputValueIndex(binding);
                        input.Text = binding.TextValue ? field.Text
                            : valueIndex >= 0 && valueIndex < field.Values.Length ? field.Values[valueIndex].ToString() : "0";
                        input.IsEnabled = canEdit && supported && field.Enabled
                            && (binding.TextValue || DatPropertiesCodec.HasEditableValues(version, key, _datModel.MetadataReaderVersion));
                    }
                }

                if (_propertyCombos.TryGetValue(key, out var combos))
                {
                    foreach (var combo in combos)
                    {
                        if (combo.Tag is not PropertyComboBinding binding) continue;
                        var value = binding.ValueIndex >= 0 && binding.ValueIndex < field.Values.Length
                            ? field.Values[binding.ValueIndex] : -1;
                        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is int option && option == value);
                        combo.IsEnabled = canEdit && supported && field.Enabled;
                    }
                }
            }

            if (_propertyCombos.TryGetValue("marketCategory", out var marketCombos))
            {
                var market = DatPropertiesCodec.Get(fields, "market");
                foreach (var combo in marketCombos)
                {
                    if (combo.Tag is not PropertyComboBinding binding) continue;
                    var value = binding.ValueIndex >= 0 && binding.ValueIndex < market.Values.Length
                        ? market.Values[binding.ValueIndex] : -1;
                    var selectedValue = value == 0 ? 9 : value; // ObjectBuilder treats legacy category 0 as Others (9).
                    combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is int option && option == selectedValue);
                    combo.IsEnabled = canEdit && market.Enabled;
                }
            }

            if (_bonesDirectionCombo is not null)
            {
                var bones = DatPropertiesCodec.Get(fields, "bones");
                if (_bonesDirectionRow is not null)
                    _bonesDirectionRow.Visibility = isItem ? Visibility.Visible : Visibility.Collapsed;
                _bonesDirectionCombo.IsEnabled = canEdit && bones.Enabled && isItem;
                if (_bonesDirectionCombo.SelectedIndex < 0) _bonesDirectionCombo.SelectedIndex = 0;
            }

            var order = DatPropertiesCodec.Get(fields, "groundBorder").Enabled ? "groundBorder"
                : DatPropertiesCodec.Get(fields, "onBottom").Enabled ? "onBottom"
                : DatPropertiesCodec.Get(fields, "onTop").Enabled ? "onTop" : "common";
            foreach (var (key, radio) in _renderOrderControls)
            {
                var supported = key == "common" || DatPropertiesCodec.Supports(version, key, _datModel.MetadataReaderVersion);
                radio.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
                radio.IsEnabled = canEdit && supported;
                radio.IsChecked = key == order;
            }

            foreach (var (key, control) in _attributeFlagControls)
            {
                var supported = DatPropertiesCodec.Supports(version, key, _datModel.MetadataReaderVersion);
                control.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
                control.IsEnabled = canEdit && supported;
                control.IsChecked = DatPropertiesCodec.Get(fields, key).Enabled;
            }

            foreach (var ((key, valueIndex), button) in _propertyColorButtons)
            {
                var supported = DatPropertiesCodec.Supports(version, key, _datModel.MetadataReaderVersion);
                var field = DatPropertiesCodec.Get(fields, key);
                var value = valueIndex >= 0 && valueIndex < field.Values.Length ? field.Values[valueIndex] : 0;
                button.Background = new SolidColorBrush(EightBitColor(Math.Clamp(value, 0, 215)));
                button.IsEnabled = canEdit && supported && field.Enabled;
            }
        }
        finally { _updatingAttributeControls = false; }
    }

    private int PropertyInputValueIndex(PropertyInputBinding binding)
    {
        if (!binding.BoneX && !binding.BoneY) return binding.ValueIndex;
        var direction = _selectedThing?.Category switch
        {
            "outfit" => _selectedPatternX switch { 0 => 0, 1 => 2, 2 => 1, 3 => 3, _ => 0 },
            "effect" or "missile" => 0,
            _ => _bonesDirectionCombo?.SelectedItem is ComboBoxItem { Tag: int index } ? index : 0
        };
        return direction * 2 + (binding.BoneY ? 1 : 0);
    }

    private void UpdateServerItemEditor()
    {
        var isItem = _selectedThing?.Category == "item" && _serverItemFiles is not null;
        ServerItemPanel.Visibility = isItem ? Visibility.Visible : Visibility.Collapsed;
        if (!isItem || _selectedThing is null || _serverItemFiles is null)
        {
            ServerItemNameInput.IsEnabled = ServerGroundSpeedInput.IsEnabled = ServerMinimapInput.IsEnabled = false;
            ServerTradeAsInput.IsEnabled = ServerLightLevelInput.IsEnabled = ServerLightColorInput.IsEnabled = false;
            ServerXmlNameInput.IsEnabled = ServerArticleInput.IsEnabled = ServerPluralInput.IsEnabled = false;
            ServerXmlAttributesTitle.Visibility = AddXmlAttributeButton.Visibility = Visibility.Collapsed;
            ServerXmlAttributesEditor.Children.Clear();
            return;
        }

        var info = _serverItemFiles.FindByClientId(_selectedThing.Id);
        _updatingServerItemControls = true;
        try
        {
            var hasMatch = info is not null;
            ServerItemsStatusText.Text = !hasMatch
                ? Text("ID não encontrado no OTB", "Client ID not found in OTB")
                : _serverItemFiles.HasXml
                    ? Text($"OTB + XML · {_serverItemFiles.ItemCount:N0} itens", $"OTB + XML · {_serverItemFiles.ItemCount:N0} items")
                    : Text($"OTB · {_serverItemFiles.ItemCount:N0} itens · XML ausente", $"OTB · {_serverItemFiles.ItemCount:N0} items · XML missing");
            ServerItemServerIdInput.Text = info?.ServerId.ToString() ?? "—";
            ServerItemClientIdInput.Text = info?.ClientId.ToString() ?? _selectedThing.Id.ToString();
            ServerItemNameInput.Text = info?.Name ?? "";
            ServerGroundSpeedInput.Text = info?.GroundSpeed.ToString() ?? "0";
            ServerMinimapInput.Text = info?.MinimapColor.ToString() ?? "0";
            ServerTradeAsInput.Text = info?.TradeAs.ToString() ?? "0";
            ServerLightLevelInput.Text = info?.LightLevel.ToString() ?? "0";
            ServerLightColorInput.Text = info?.LightColor.ToString() ?? "0";
            var xml = info is null ? new XmlItemInfo("", "", "") : _serverItemFiles.GetXmlInfo(_selectedThing.Id);
            ServerXmlNameInput.Text = xml.Name;
            ServerArticleInput.Text = xml.Article;
            ServerPluralInput.Text = xml.Plural;
            ServerItemNameInput.IsEnabled = hasMatch;
            ServerGroundSpeedInput.IsEnabled = hasMatch;
            ServerMinimapInput.IsEnabled = hasMatch;
            ServerTradeAsInput.IsEnabled = hasMatch;
            ServerLightLevelInput.IsEnabled = hasMatch;
            ServerLightColorInput.IsEnabled = hasMatch;
            var canEditXml = hasMatch && _serverItemFiles.HasXml;
            ServerXmlNameInput.IsEnabled = ServerArticleInput.IsEnabled = ServerPluralInput.IsEnabled = canEditXml;
            ServerXmlAttributesTitle.Visibility = AddXmlAttributeButton.Visibility = canEditXml ? Visibility.Visible : Visibility.Collapsed;
            ServerXmlAttributesEditor.Children.Clear();
            if (canEditXml)
            {
                foreach (var attribute in _serverItemFiles.GetXmlAttributes(_selectedThing.Id))
                    ServerXmlAttributesEditor.Children.Add(CreateXmlAttributeRow(attribute.Key, attribute.Value, canEditKey: false));
            }
        }
        finally { _updatingServerItemControls = false; }
    }

    private FrameworkElement CreateXmlAttributeRow(string key, string value, bool canEditKey)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = canEditKey ? new GridLength(120) : new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TextBox keyInput;
        if (canEditKey)
        {
            keyInput = new TextBox
            {
                Text = key, Height = 21, Padding = new Thickness(4, 1, 4, 1), FontSize = 9,
                Background = (Brush)FindResource("PanelBackground"), Foreground = (Brush)FindResource("TextBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"), Margin = new Thickness(0, 0, 6, 0)
            };
        }
        else
        {
            keyInput = new TextBox
            {
                Text = key, Height = 21, Padding = new Thickness(4, 1, 4, 1), FontSize = 9, IsReadOnly = true,
                Background = (Brush)FindResource("PanelBackground"), Foreground = (Brush)FindResource("MutedBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"), Margin = new Thickness(0, 0, 6, 0)
            };
        }
        var valueInput = new TextBox
        {
            Text = value, Height = 21, Padding = new Thickness(4, 1, 4, 1), FontSize = 9,
            Background = (Brush)FindResource("PanelBackground"), Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush")
        };
        Grid.SetColumn(valueInput, 1);
        var binding = new XmlAttributeInputBinding(keyInput, valueInput);
        keyInput.Tag = valueInput.Tag = binding;
        keyInput.TextChanged += ServerItemField_TextChanged;
        valueInput.TextChanged += ServerItemField_TextChanged;
        row.Children.Add(keyInput);
        row.Children.Add(valueInput);
        return row;
    }

    private void AddXmlAttributeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_serverItemFiles is null || _selectedThing?.Category != "item"
            || _serverItemFiles.FindByClientId(_selectedThing.Id) is null || !_serverItemFiles.HasXml) return;
        ServerXmlAttributesEditor.Children.Add(CreateXmlAttributeRow("novoAtributo", "", canEditKey: true));
    }

    private void ServerItemField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingServerItemControls || sender is not TextBox input
            || _selectedThing?.Category != "item" || _serverItemFiles is null) return;
        try
        {
            bool changed;
            if (input.Tag is XmlAttributeInputBinding binding)
            {
                var key = binding.KeyInput.Text.Trim();
                if (key.Length == 0) return;
                changed = _serverItemFiles.UpdateXmlAttribute(_selectedThing.Id, key, binding.ValueInput.Text);
            }
            else if (input.Tag is string field)
                changed = field.StartsWith("xml", StringComparison.Ordinal) || field is "article" or "plural"
                    ? _serverItemFiles.UpdateXmlField(_selectedThing.Id, field, input.Text)
                    : _serverItemFiles.UpdateOtbField(_selectedThing.Id, field, input.Text);
            else return;
            if (!changed) return;
            _metadataHasUnsavedChanges = true;
            ClearMetadataStatusError();
            MetadataSaveStatus.Foreground = (Brush)FindResource("MutedBrush");
            MetadataSaveStatus.Text = Text("Alterações não salvas", "Unsaved changes");
            UpdateMetadataEditingState();
        }
        catch (InvalidDataException ex)
        {
            SetMetadataStatusError(ex.Message);
        }
    }

    private void PropertyToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingAttributeControls || sender is not CheckBox { Tag: string key, IsChecked: bool isChecked }
            || _selectedThing is null || _datModel is null) return;
        var fields = DatPropertiesCodec.Read(_selectedThing.RawProperties, _datModel.VersionValue, _datModel.MetadataReaderVersion);
        var field = DatPropertiesCodec.Get(fields, key);
        StoreSelectedProperty(key, field with { Enabled = isChecked });
        UpdateItemAttributeEditor();
    }

    private void AttributeFlag_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingAttributeControls || sender is not CheckBox { Tag: string key, IsChecked: bool isChecked }
            || _selectedThing is null || _datModel is null) return;
        var fields = DatPropertiesCodec.Read(_selectedThing.RawProperties, _datModel.VersionValue, _datModel.MetadataReaderVersion);
        var field = DatPropertiesCodec.Get(fields, key);
        StoreSelectedProperty(key, field with { Enabled = isChecked });
    }

    private void RenderOrderRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_updatingAttributeControls || sender is not RadioButton { Tag: string key, IsChecked: true }
            || _selectedThing is null || _datModel is null) return;
        var raw = DatPropertiesCodec.SetRenderOrder(_selectedThing.RawProperties, _datModel.VersionValue, key, _datModel.MetadataReaderVersion);
        StoreSelectedRawProperties(raw);
        UpdateItemAttributeEditor();
    }

    private void PropertyTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingAttributeControls || sender is not TextBox { Tag: PropertyInputBinding binding }
            || _selectedThing is null || _datModel is null) return;
        var fields = DatPropertiesCodec.Read(_selectedThing.RawProperties, _datModel.VersionValue, _datModel.MetadataReaderVersion);
        var field = DatPropertiesCodec.Get(fields, binding.Key);
        if (binding.TextValue)
        {
            if (Encoding.Latin1.GetByteCount(((TextBox)sender).Text) > ushort.MaxValue)
            {
                SetMetadataStatusError(Text("O nome de mercado excede o limite do DAT.", "The market name exceeds the DAT limit."));
                return;
            }
            StoreSelectedProperty(binding.Key, field with { Enabled = true, Text = ((TextBox)sender).Text });
            return;
        }

        if (!int.TryParse(((TextBox)sender).Text, out var value)
            || value < binding.Minimum || value > binding.Maximum)
        {
            SetMetadataStatusError(Text($"Use um valor entre {binding.Minimum} e {binding.Maximum}.",
                $"Enter a value from {binding.Minimum} to {binding.Maximum}."));
            return;
        }

        var valueIndex = PropertyInputValueIndex(binding);
        if (valueIndex < 0 || valueIndex >= field.Values.Length) return;
        var values = field.Values.ToArray();
        values[valueIndex] = value;
        StoreSelectedProperty(binding.Key, field with { Enabled = true, Values = values });
    }

    private void PropertyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingAttributeControls || sender is not ComboBox { Tag: PropertyComboBinding binding }
            || binding.Key == "bonesDirection" || _selectedThing is null || _datModel is null
            || ((ComboBox)sender).SelectedItem is not ComboBoxItem { Tag: int value }) return;
        var fields = DatPropertiesCodec.Read(_selectedThing.RawProperties, _datModel.VersionValue, _datModel.MetadataReaderVersion);
        var field = DatPropertiesCodec.Get(fields, binding.Key);
        if (binding.ValueIndex < 0 || binding.ValueIndex >= field.Values.Length) return;
        var values = field.Values.ToArray();
        values[binding.ValueIndex] = value;
        StoreSelectedProperty(binding.Key, field with { Enabled = true, Values = values });
    }

    private void BonesDirectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingAttributeControls) return;
        var fields = _selectedThing is not null && _datModel is not null
            ? DatPropertiesCodec.Read(_selectedThing.RawProperties, _datModel.VersionValue, _datModel.MetadataReaderVersion) : null;
        if (fields is null) return;
        var bones = DatPropertiesCodec.Get(fields, "bones");
        _updatingAttributeControls = true;
        try
        {
            foreach (var input in _propertyTextInputs.GetValueOrDefault("bones", []))
            {
                if (input.Tag is not PropertyInputBinding binding) continue;
                var index = PropertyInputValueIndex(binding);
                input.Text = index >= 0 && index < bones.Values.Length ? bones.Values[index].ToString() : "0";
            }
        }
        finally { _updatingAttributeControls = false; }
    }

    private void StoreSelectedProperty(string key, DatPropertyField field)
    {
        if (_selectedThing is null || _datModel is null) return;
        try
        {
            var raw = DatPropertiesCodec.Set(_selectedThing.RawProperties, _datModel.VersionValue, key, field, _datModel.MetadataReaderVersion);
            StoreSelectedRawProperties(raw);
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException or ArgumentException)
        {
            SetMetadataStatusError(Text($"Propriedade inválida: {ex.Message}", $"Invalid property: {ex.Message}"));
        }
    }

    private void StoreSelectedRawProperties(byte[] raw)
    {
        if (_selectedThing is null || _datModel is null || _selectedThing.RawProperties.AsSpan().SequenceEqual(raw)) return;
        var updatedThing = _selectedThing with { RawProperties = raw };
        _datModel = _datModel with
        {
            Things = _datModel.Things.Select(thing => thing.Id == updatedThing.Id && thing.Category == updatedThing.Category
                ? updatedThing : thing).ToArray()
        };
        _selectedThing = updatedThing;
        if (_serverItemFiles is not null && _datModel is not null)
        {
            _serverItemFiles.SynchronizeFromDat(updatedThing, _datModel.VersionValue);
            UpdateServerItemEditor();
        }
        _metadataHasUnsavedChanges = true;
        ClearMetadataStatusError();
        UpdateMetadataEditingState();
    }

    private bool IsItemPropertyFormValid()
    {
        foreach (var input in _propertyTextInputs.Values.SelectMany(items => items).Where(input => input.IsEnabled))
        {
            if (input.Tag is not PropertyInputBinding binding) continue;
            if (binding.TextValue)
            {
                if (Encoding.Latin1.GetByteCount(input.Text) > ushort.MaxValue)
                {
                    SetMetadataStatusError(Text("O nome de mercado excede o limite do DAT.", "The market name exceeds the DAT limit."));
                    return false;
                }
                continue;
            }
            if (!int.TryParse(input.Text, out var value) || value < binding.Minimum || value > binding.Maximum)
            {
                SetMetadataStatusError(Text("Corrija os valores das propriedades antes de salvar.", "Correct the property values before saving."));
                return false;
            }
        }
        return true;
    }

    private void AnimationDuration_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _updatingThingControls || _datModel is null
            || _selectedThing is null || _selectedFrameGroup is not { Frames.Count: > 0 } group) return;

        if (!uint.TryParse(MinimumDurationInput.Text, out var minimumDuration)
            || !uint.TryParse(MaximumDurationInput.Text, out var maximumDuration))
        {
            SetMetadataStatusError(Text("Informe a duração em milissegundos usando números inteiros.",
                "Enter the duration in milliseconds as whole numbers."));
            return;
        }

        var frameIndex = Math.Clamp(_selectedAnimationFrame, 0, group.Frames.Count - 1);
        var frame = group.Frames[frameIndex];
        if (frame.MinimumDuration == minimumDuration && frame.MaximumDuration == maximumDuration)
        {
            ClearMetadataStatusError();
            return;
        }

        var frames = group.Frames.ToArray();
        frames[frameIndex] = frame with
        {
            MinimumDuration = minimumDuration,
            MaximumDuration = maximumDuration
        };
        var updatedGroup = group with { Frames = frames };
        var groups = _selectedThing.FrameGroups.ToArray();
        var groupIndex = Math.Clamp(FrameGroupCombo.SelectedIndex, 0, groups.Length - 1);
        groups[groupIndex] = updatedGroup;
        var updatedThing = _selectedThing with { FrameGroups = groups };
        _datModel = _datModel with
        {
            Things = _datModel.Things.Select(thing => thing.Id == updatedThing.Id && thing.Category == updatedThing.Category
                ? updatedThing : thing).ToArray()
        };
        _selectedThing = updatedThing;
        _selectedFrameGroup = updatedGroup;
        _metadataHasUnsavedChanges = true;
        ClearMetadataStatusError();
        UpdateMetadataEditingState();
        UpdateAnimationTimerInterval();
        UpdateInfoPreviewTimerInterval();
    }

    private void ApplyMinimumDurationToAll_Click(object sender, RoutedEventArgs e) => ApplyDurationToAllFrames(minimum: true);

    private void ApplyMaximumDurationToAll_Click(object sender, RoutedEventArgs e) => ApplyDurationToAllFrames(minimum: false);

    private void BulkAnimationSpeedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_datModel is null || _datPath is null || !_datModel.ImprovedAnimations) return;
        var category = SelectedDatCategory;
        var animatedThings = _datModel.Things
            .Where(thing => thing.Category == category && thing.FrameGroups.Any(group => group.Frames.Count > 1))
            .ToArray();
        if (animatedThings.Length == 0) return;

        var categoryName = CategoryName(category);
        var dialog = new Window
        {
            Title = Text("Velocidade de animação", "Animation speed"),
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = (Brush)FindResource("AppBackground"),
            Foreground = (Brush)FindResource("TextBrush")
        };
        var content = new StackPanel { Width = 370, Margin = new Thickness(20) };
        content.Children.Add(new TextBlock
        {
            Text = Text($"Aplicar velocidade a todos os {categoryName}", $"Apply speed to all {categoryName}"),
            FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = Text($"Serão alterados {animatedThings.Length:N0} objetos animados da categoria atual. Defina a duração de cada quadro em milissegundos.",
                $"This will change {animatedThings.Length:N0} animated objects in the current category. Set each frame's duration in milliseconds."),
            Foreground = (Brush)FindResource("MutedBrush"), FontSize = 10, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 14)
        });

        var suggestedFrame = _selectedThing?.Category == category && _selectedFrameGroup?.Frames.Count > 1
            ? _selectedFrameGroup.Frames[Math.Clamp(_selectedAnimationFrame, 0, _selectedFrameGroup.Frames.Count - 1)]
            : null;
        var minimumInput = CreateBulkDurationInput(suggestedFrame?.MinimumDuration ?? 100);
        var maximumInput = CreateBulkDurationInput(suggestedFrame?.MaximumDuration ?? 100);
        var values = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        values.ColumnDefinitions.Add(new ColumnDefinition());
        values.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        values.ColumnDefinitions.Add(new ColumnDefinition());
        values.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        values.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddBulkDurationField(values, Text("Mínimo (ms)", "Minimum (ms)"), minimumInput, 0);
        AddBulkDurationField(values, Text("Máximo (ms)", "Maximum (ms)"), maximumInput, 2);
        content.Children.Add(values);

        var validation = new TextBlock
        {
            Foreground = ThemeBrush("ErrorBrush"), FontSize = 9,
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8)
        };
        content.Children.Add(validation);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelButton = new Button
        {
            Content = Text("Cancelar", "Cancel"), Style = (Style)FindResource("SecondaryButton"),
            Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), IsCancel = true
        };
        var applyButton = new Button
        {
            Content = Text($"Aplicar a {animatedThings.Length:N0} objetos", $"Apply to {animatedThings.Length:N0} objects"),
            Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(12, 6, 12, 6), IsDefault = true
        };
        cancelButton.Click += (_, _) => dialog.DialogResult = false;
        applyButton.Click += (_, _) =>
        {
            if (!uint.TryParse(minimumInput.Text, out var minimum) || !uint.TryParse(maximumInput.Text, out var maximum)
                || maximum < minimum)
            {
                validation.Text = Text("Informe durações inteiras e um máximo igual ou maior que o mínimo.",
                    "Enter whole-number durations and a maximum equal to or greater than the minimum.");
                validation.Visibility = Visibility.Visible;
                return;
            }

            ApplyAnimationSpeedToCategory(category, minimum, maximum);
            dialog.DialogResult = true;
        };
        actions.Children.Add(cancelButton);
        actions.Children.Add(applyButton);
        content.Children.Add(actions);
        dialog.Content = content;
        dialog.ShowDialog();
    }

    private TextBox CreateBulkDurationInput(uint value) => new()
    {
        Text = value.ToString(), Width = 110, Height = 29, Padding = new Thickness(7, 4, 7, 4),
        HorizontalContentAlignment = HorizontalAlignment.Right, Background = ThemeBrush("CardBackgroundBrush"),
        Foreground = ThemeBrush("TextBrush"), BorderBrush = ThemeBrush("FieldBorderBrush"), FontSize = 10
    };

    private void AddBulkDurationField(Grid grid, string label, TextBox input, int column)
    {
        var field = new StackPanel();
        field.Children.Add(new TextBlock { Text = label, Foreground = ThemeBrush("MutedBrush"), FontSize = 9, Margin = new Thickness(0, 0, 0, 5) });
        field.Children.Add(input);
        Grid.SetColumn(field, column);
        grid.Children.Add(field);
    }

    private void ApplyAnimationSpeedToCategory(string category, uint minimum, uint maximum)
    {
        if (_datModel is null) return;
        var affectedThings = 0;
        var changedFrames = 0;
        var updatedThings = _datModel.Things.Select(thing =>
        {
            if (thing.Category != category) return thing;
            var thingChanged = false;
            var groups = thing.FrameGroups.Select(group =>
            {
                if (group.Frames.Count <= 1) return group;
                var frames = group.Frames.Select(frame =>
                {
                    if (frame.MinimumDuration == minimum && frame.MaximumDuration == maximum) return frame;
                    changedFrames++;
                    thingChanged = true;
                    return frame with { MinimumDuration = minimum, MaximumDuration = maximum };
                }).ToArray();
                return thingChanged ? group with { Frames = frames } : group;
            }).ToArray();
            if (!thingChanged) return thing;
            affectedThings++;
            return thing with { FrameGroups = groups };
        }).ToArray();

        if (changedFrames == 0)
        {
            MetadataSaveStatus.Foreground = (Brush)FindResource("MutedBrush");
            MetadataSaveStatus.Text = Text("Essa categoria já usa essas durações.", "This category already uses these durations.");
            return;
        }

        _datModel = _datModel with { Things = updatedThings };
        _metadataHasUnsavedChanges = true;
        MetadataSaveStatus.Foreground = (Brush)FindResource("MutedBrush");
        MetadataSaveStatus.Text = Text($"Velocidade aplicada a {affectedThings:N0} objetos. Salve o DAT.",
            $"Speed applied to {affectedThings:N0} objects. Save the DAT.");

        if (_selectedThing?.Category == category)
        {
            _selectedThing = updatedThings.FirstOrDefault(thing => thing.Category == category && thing.Id == _selectedThing.Id);
            if (_selectedThing is not null && _selectedThing.FrameGroups.Count > 0)
            {
                var groupIndex = Math.Clamp(FrameGroupCombo.SelectedIndex, 0, _selectedThing.FrameGroups.Count - 1);
                _selectedFrameGroup = _selectedThing.FrameGroups[groupIndex];
                _selectedAnimationFrame = Math.Clamp(_selectedAnimationFrame, 0, _selectedFrameGroup.Frames.Count - 1);
                UpdateAnimationAndLayerControls();
                UpdateAnimationTimerInterval();
                UpdateInfoPreviewTimerInterval();
            }
        }
        UpdateMetadataEditingState();
    }

    private void ApplyDurationToAllFrames(bool minimum)
    {
        if (_datModel is null || _selectedThing is null || _selectedFrameGroup is not { Frames.Count: > 1 } group) return;

        var input = minimum ? MinimumDurationInput : MaximumDurationInput;
        if (!uint.TryParse(input.Text, out var duration))
        {
            SetMetadataStatusError(Text("Informe uma duração inteira em milissegundos.", "Enter a whole-number duration in milliseconds."));
            return;
        }

        if (group.Frames.All(frame => minimum ? frame.MinimumDuration == duration : frame.MaximumDuration == duration))
        {
            ClearMetadataStatusError();
            return;
        }

        var frames = group.Frames.Select(frame => minimum
            ? frame with { MinimumDuration = duration }
            : frame with { MaximumDuration = duration }).ToArray();
        var updatedGroup = group with { Frames = frames };
        var groups = _selectedThing.FrameGroups.ToArray();
        var groupIndex = Math.Clamp(FrameGroupCombo.SelectedIndex, 0, groups.Length - 1);
        groups[groupIndex] = updatedGroup;
        var updatedThing = _selectedThing with { FrameGroups = groups };
        _datModel = _datModel with
        {
            Things = _datModel.Things.Select(thing => thing.Id == updatedThing.Id && thing.Category == updatedThing.Category
                ? updatedThing : thing).ToArray()
        };
        _selectedThing = updatedThing;
        _selectedFrameGroup = updatedGroup;
        _metadataHasUnsavedChanges = true;
        ClearMetadataStatusError();
        UpdateMetadataEditingState();
        UpdateAnimationTimerInterval();
        UpdateInfoPreviewTimerInterval();
    }

    private void MetadataField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _updatingThingControls || _datModel is null
            || _selectedThing is null || _selectedFrameGroup is null) return;

        if (ReferenceEquals(sender, FrameGroupsValue))
        {
            if (_selectedThing.Category != "outfit" || _datModel.VersionValue < 1057) return;
            if (!TryReadMetadataValue(FrameGroupsValue, out var groupCount))
            {
                SetMetadataStatusError(Text("Use um valor entre 1 e 255.", "Enter a value from 1 to 255."));
                return;
            }
            ClearMetadataStatusError();
            ApplyFrameGroupCount(groupCount);
            return;
        }

        if (!TryReadMetadataValue(PatternWidthText, out var width)
            || !TryReadMetadataValue(PatternHeightText, out var height)
            || !TryReadMetadataValue(PatternCropText, out var cropSize, allowZero: true)
            || !TryReadMetadataValue(PatternLayersText, out var layers)
            || !TryReadMetadataValue(PatternXText, out var patternX)
            || !TryReadMetadataValue(PatternYText, out var patternY)
            || !TryReadMetadataValue(PatternZText, out var patternZ)
            || !TryReadMetadataValue(PatternFramesText, out var animationFrames))
        {
            SetMetadataStatusError(Text("Use valores inteiros entre 1 e 255.", "Enter whole numbers from 1 to 255."));
            return;
        }
        ClearMetadataStatusError();

        var group = _selectedFrameGroup;
        if (width == group.Width && height == group.Height && cropSize == group.CropSize
            && layers == group.Layers && patternX == group.PatternX && patternY == group.PatternY
            && patternZ == group.PatternZ && animationFrames == group.Frames.Count) return;

        var referencesPerFrame = (long)width * height * layers * patternX * patternY * patternZ;
        var previewBytes = (long)width * height * SpritePixelCount * 4;
        if (referencesPerFrame <= 0 || referencesPerFrame * animationFrames > 1_000_000
            || previewBytes > 32L * 1024 * 1024)
        {
            SetMetadataStatusError(Text("Essa combinação excede o limite de sprites ou de tamanho do preview.", "These values exceed the sprite reference or preview size limit."));
            return;
        }

        try
        {
            var updatedGroup = ResizeFrameGroup(group, width, height, cropSize, layers,
                patternX, patternY, patternZ, animationFrames);
            var groups = _selectedThing.FrameGroups.ToArray();
            var groupIndex = Math.Clamp(FrameGroupCombo.SelectedIndex, 0, groups.Length - 1);
            groups[groupIndex] = updatedGroup;
            ApplyUpdatedThing(groups, groupIndex, frameGroupsChanged: false);
        }
        catch (InvalidDataException ex) { SetMetadataStatusError(ex.Message); }
        catch (OverflowException) { SetMetadataStatusError(Text("Os valores excedem o limite do formato DAT.", "The values exceed the DAT format limit.")); }
    }

    private void ApplyFrameGroupCount(int count)
    {
        if (_selectedThing is null || count == _selectedThing.FrameGroups.Count) return;
        var groups = _selectedThing.FrameGroups.ToList();
        while (groups.Count > count) groups.RemoveAt(groups.Count - 1);
        while (groups.Count < count)
        {
            var source = groups[^1];
            groups.Add(source with { Type = checked((byte)groups.Count) });
        }
        var selectedIndex = Math.Clamp(FrameGroupCombo.SelectedIndex, 0, groups.Count - 1);
        ApplyUpdatedThing(groups, selectedIndex, frameGroupsChanged: true);
    }

    private void ApplyUpdatedThing(IReadOnlyList<DatFrameGroup> groups, int selectedGroupIndex, bool frameGroupsChanged)
    {
        if (_datModel is null || _selectedThing is null) return;
        var previousThing = _selectedThing;
        var spriteCount = groups.Sum(group => group.Frames.Sum(frame => frame.SpriteIds.Length));
        var updatedThing = previousThing with { FrameGroups = groups, SpriteCount = spriteCount };
        _datModel = _datModel with
        {
            Things = _datModel.Things.Select(thing => thing.Id == previousThing.Id && thing.Category == previousThing.Category
                ? updatedThing : thing).ToArray()
        };
        _selectedThing = updatedThing;
        _serverItemFiles?.SynchronizeFromDat(updatedThing, _datModel.VersionValue);
        _selectedFrameGroup = groups[selectedGroupIndex];
        _selectedAnimationFrame = Math.Clamp(_selectedAnimationFrame, 0, _selectedFrameGroup.Frames.Count - 1);
        _selectedLayer = Math.Clamp(_selectedLayer, 0, _selectedFrameGroup.Layers - 1);
        _selectedPatternX = Math.Clamp(_selectedPatternX, 0, _selectedFrameGroup.PatternX - 1);
        _selectedPatternY = Math.Clamp(_selectedPatternY, 0, _selectedFrameGroup.PatternY - 1);
        _selectedPatternZ = Math.Clamp(_selectedPatternZ, 0, _selectedFrameGroup.PatternZ - 1);

        var wasUpdating = _updatingThingControls;
        _updatingThingControls = true;
        try
        {
            if (frameGroupsChanged)
            {
                FrameGroupCombo.Items.Clear();
                foreach (var group in groups)
                    FrameGroupCombo.Items.Add(new ComboBoxItem { Content = FrameGroupName(group.Type), Tag = group.Type });
            }
            FrameGroupCombo.SelectedIndex = selectedGroupIndex;
            FrameGroupLabel.Visibility = FrameGroupCombo.Visibility = groups.Count > 1
                ? Visibility.Visible : Visibility.Collapsed;
            PatternSelectorsPanel.Visibility = groups.Count > 1 || _selectedFrameGroup.PatternX > 1
                || _selectedFrameGroup.PatternY > 1 || _selectedFrameGroup.PatternZ > 1
                ? Visibility.Visible : Visibility.Collapsed;
            PopulatePatternCombo(PatternXCombo, _selectedFrameGroup.PatternX, _selectedPatternX, 'x');
            PopulatePatternCombo(PatternYCombo, _selectedFrameGroup.PatternY, _selectedPatternY, 'y');
            PopulatePatternCombo(PatternZCombo, _selectedFrameGroup.PatternZ, _selectedPatternZ, 'z');
        }
        finally { _updatingThingControls = wasUpdating; }

        UpdateAnimationAndLayerControls();
        UpdatePatternInfo(_selectedFrameGroup);
        SetSelectionSummary($"{CategoryName(updatedThing.Category)} #{updatedThing.Id}",
            $"{_selectedFrameGroup.Width * SpriteSide} × {_selectedFrameGroup.Height * SpriteSide} px");
        SelectedFileText.Text = $"{Path.GetFileName(_datPath ?? string.Empty)}{Environment.NewLine}{updatedThing.SpriteCount} {Text("referências de sprite", "sprite references")}";
        _metadataHasUnsavedChanges = true;
        MetadataSaveStatus.Foreground = (Brush)FindResource("MutedBrush");
        MetadataSaveStatus.Text = Text("Alterações não salvas", "Unsaved changes");
        UpdateMetadataEditingState();
        UpdateItemAttributeEditor();
        _selectedThingSpriteBitmaps.Clear();
        _ = LoadSelectedThingPreviewAsync();
        StartInfoPreviewAnimation();
    }

    private static bool TryReadMetadataValue(TextBox input, out int value, bool allowZero = false) =>
        int.TryParse(input.Text, out value) && value >= (allowZero ? 0 : 1) && value <= byte.MaxValue;

    private static DatFrameGroup ResizeFrameGroup(DatFrameGroup group, int width, int height, int cropSize,
        int layers, int patternX, int patternY, int patternZ, int frameCount)
    {
        var spritesPerFrame = checked(width * height * layers * patternX * patternY * patternZ);
        var frames = new DatAnimationFrame[frameCount];
        for (var index = 0; index < frameCount; index++)
        {
            var source = group.Frames.Count == 0
                ? new DatAnimationFrame([], 100, 100)
                : group.Frames[Math.Min(index, group.Frames.Count - 1)];
            var spriteIds = new uint[spritesPerFrame];
            var sourceTileCount = group.Width * group.Height;
            var destinationTileCount = width * height;
            var sharedWidth = Math.Min(group.Width, width);
            var sharedHeight = Math.Min(group.Height, height);
            var sharedLayers = Math.Min(group.Layers, layers);
            var sharedPatternX = Math.Min(group.PatternX, patternX);
            var sharedPatternY = Math.Min(group.PatternY, patternY);
            var sharedPatternZ = Math.Min(group.PatternZ, patternZ);
            for (var z = 0; z < sharedPatternZ; z++)
            for (var y = 0; y < sharedPatternY; y++)
            for (var x = 0; x < sharedPatternX; x++)
            for (var layer = 0; layer < sharedLayers; layer++)
            for (var tileY = 0; tileY < sharedHeight; tileY++)
            for (var tileX = 0; tileX < sharedWidth; tileX++)
            {
                var oldPattern = (z * group.PatternY + y) * group.PatternX + x;
                var newPattern = (z * patternY + y) * patternX + x;
                var oldIndex = oldPattern * sourceTileCount * group.Layers
                    + layer * sourceTileCount + tileY * group.Width + tileX;
                var newIndex = newPattern * destinationTileCount * layers
                    + layer * destinationTileCount + tileY * width + tileX;
                if (oldIndex < source.SpriteIds.Length)
                    spriteIds[newIndex] = source.SpriteIds[oldIndex];
            }
            frames[index] = new DatAnimationFrame(spriteIds, source.MinimumDuration, source.MaximumDuration);
        }
        return group with
        {
            Width = width, Height = height, CropSize = cropSize, Layers = layers,
            PatternX = patternX, PatternY = patternY, PatternZ = patternZ, Frames = frames
        };
    }

    private void SetMetadataStatusError(string message)
    {
        MetadataSaveStatus.Foreground = ThemeBrush("ErrorBrush");
        MetadataSaveStatus.Text = message;
    }

    private void ClearMetadataStatusError()
    {
        MetadataSaveStatus.Foreground = (Brush)FindResource("MutedBrush");
        MetadataSaveStatus.Text = _metadataHasUnsavedChanges
            ? Text("Alterações não salvas", "Unsaved changes") : string.Empty;
    }

    private bool IsMetadataFormValid()
    {
        if (_selectedThing is null || _selectedFrameGroup is null) return true;
        if (!IsItemPropertyFormValid()) return false;
        if (_selectedFrameGroup.Frames.Count > 0
            && (!uint.TryParse(MinimumDurationInput.Text, out _) || !uint.TryParse(MaximumDurationInput.Text, out _)))
        {
            SetMetadataStatusError(Text("Corrija as durações da animação antes de salvar.",
                "Correct the animation durations before saving."));
            return false;
        }
        if (FrameGroupsValue.IsEnabled && !TryReadMetadataValue(FrameGroupsValue, out _))
        {
            SetMetadataStatusError(Text("Use um valor entre 1 e 255 para grupos.", "Enter a group count from 1 to 255."));
            return false;
        }
        if (!TryReadMetadataValue(PatternWidthText, out var width)
            || !TryReadMetadataValue(PatternHeightText, out var height)
            || !TryReadMetadataValue(PatternCropText, out _, allowZero: true)
            || !TryReadMetadataValue(PatternLayersText, out var layers)
            || !TryReadMetadataValue(PatternXText, out var patternX)
            || !TryReadMetadataValue(PatternYText, out var patternY)
            || !TryReadMetadataValue(PatternZText, out var patternZ)
            || !TryReadMetadataValue(PatternFramesText, out var animations)
            || (long)width * height * layers * patternX * patternY * patternZ * animations > 1_000_000
            || (long)width * height * SpritePixelCount * 4 > 32L * 1024 * 1024)
        {
            SetMetadataStatusError(Text("Corrija os valores antes de salvar.", "Correct the values before saving."));
            return false;
        }
        return true;
    }

    private void DatNewButton_Click(object sender, RoutedEventArgs e)
    {
        RunDatThingOperation(() =>
        {
            if (_datModel is null) return;
            var category = SelectedDatCategory;
            var nextId = GetNextDatThingId(_datModel, category);
            var template = _selectedThing?.Category == category ? _selectedThing : null;
            var thing = CreateBlankDatThing(nextId, category, template, _datModel.VersionValue);
            ApplyDatThingChange(thing, addNew: true);
        });
    }

    private async void DatReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        var targets = GetSelectedDatThings();
        if (targets.Count == 0 || _datModel is null) return;
        var dialog = new OpenFileDialog
        {
            Title = Text("Escolher objeto exportado para substituir", "Choose an exported object to replace the selection"),
            Filter = "Objetos OBD ou DAT do Editor Universe (*.obd;*.json)|*.obd;*.json|Objeto OBD (*.obd)|*.obd|Objeto DAT do Editor Universe (*.json)|*.json|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (string.Equals(Path.GetExtension(dialog.FileName), ".obd", StringComparison.OrdinalIgnoreCase))
        {
            await RunDatThingImportAsync(async () =>
            {
                var imported = await ImportObdThingAsync(dialog.FileName, targets[0].Category);
                var replacements = targets.Select(target => CloneDatThing(imported, target.Id, target.Category)).ToArray();
                ApplyDatThingBatchChanges(replacements, replacements[^1]);
            });
            return;
        }
        RunDatThingOperation(() =>
        {
            var imported = ReadDatThingExchange(dialog.FileName, targets[0].Category);
            var replacements = targets.Select(target => CloneDatThing(imported, target.Id, target.Category)).ToArray();
            ApplyDatThingBatchChanges(replacements, replacements[^1]);
        });
    }

    private void DatDuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_datModel is null) return;
        RunDatThingOperation(() =>
        {
            var sources = GetSelectedDatThings();
            if (sources.Count == 0) throw new InvalidOperationException(Text("Selecione um objeto DAT.", "Select a DAT object."));
            ApplyDatThingAdditions(CreateDatThingCopies(sources));
        });
    }

    private async void DatImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_datModel is null) return;
        var dialog = new OpenFileDialog
        {
            Title = Text("Importar objeto OBD ou DAT", "Import an OBD or DAT object"),
            Filter = "Objetos OBD ou DAT do Editor Universe (*.obd;*.json)|*.obd;*.json|Objeto OBD (*.obd)|*.obd|Objeto DAT do Editor Universe (*.json)|*.json|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (string.Equals(Path.GetExtension(dialog.FileName), ".obd", StringComparison.OrdinalIgnoreCase))
        {
            await RunDatThingImportAsync(async () =>
            {
                var category = SelectedDatCategory;
                var imported = await ImportObdThingAsync(dialog.FileName, category);
                var id = GetNextDatThingId(_datModel!, category);
                var newThing = CloneDatThing(imported, id, category);
                ApplyDatThingChange(newThing, addNew: true);
            });
            return;
        }
        RunDatThingOperation(() =>
        {
            var category = SelectedDatCategory;
            var imported = ReadDatThingExchange(dialog.FileName, category);
            var id = GetNextDatThingId(_datModel!, category);
            var newThing = CloneDatThing(imported, id, category);
            ApplyDatThingChange(newThing, addNew: true);
        });
    }

    private void OpenSecondWindowButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new MainWindow();
        window.Show();
        window.Activate();
    }

    private void ArrangeWindowsSideBySideButton_Click(object sender, RoutedEventArgs e)
    {
        var otherWindow = OpenEditorWindows.LastOrDefault(window => window != this && window.IsLoaded);
        if (otherWindow is null) return;

        if (!IsVisible) Show();
        if (!otherWindow.IsVisible) otherWindow.Show();
        WindowState = WindowState.Normal;
        otherWindow.WindowState = WindowState.Normal;

        var handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;

        var monitorInfo = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = Math.Max(dpi.DpiScaleX, 0.1);
        var scaleY = Math.Max(dpi.DpiScaleY, 0.1);
        var workLeft = monitorInfo.Work.Left / scaleX;
        var workTop = monitorInfo.Work.Top / scaleY;
        var workWidth = (monitorInfo.Work.Right - monitorInfo.Work.Left) / scaleX;
        var workHeight = (monitorInfo.Work.Bottom - monitorInfo.Work.Top) / scaleY;
        var leftWidth = Math.Floor(workWidth / 2);
        var rightWidth = workWidth - leftWidth;

        SetSideBySideWindowBounds(this, workLeft, workTop, leftWidth, workHeight);
        SetSideBySideWindowBounds(otherWindow, workLeft + leftWidth, workTop, rightWidth, workHeight);
        otherWindow.Activate();
        Activate();
    }

    private static void SetSideBySideWindowBounds(MainWindow window, double left, double top, double width, double height)
    {
        window.MinWidth = Math.Min(680, width);
        window.MinHeight = Math.Min(500, height);
        window.WindowState = WindowState.Normal;
        window.Left = left;
        window.Top = top;
        window.Width = width;
        window.Height = height;
    }
    private async void DatReplaceAcrossWindowsButton_Click(object sender, RoutedEventArgs e)
        => await TransferDatSelectionAsync(replace: true);

    private async void DatTransferButton_Click(object sender, RoutedEventArgs e)
        => await TransferDatSelectionAsync(replace: false);

    private async Task TransferDatSelectionAsync(bool replace)
    {
        var sourceModel = _datModel;
        var sourceSpr = _sprStream;
        var sourceOffsets = (uint[])_offsets.Clone();
        if (sourceModel is null || sourceSpr is null || _datPath is null || _filePath is null || _sprEditInProgress)
            return;

        var things = GetSelectedDatThings();
        if (things.Count == 0)
        {
            MessageBox.Show(this, Text("Selecione um ou mais objetos na lista antes de transferir.",
                "Select one or more objects in the list before transferring."),
                Text("Nenhum objeto selecionado", "No objects selected"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targets = OpenEditorWindows
            .Where(window => window != this && window._datModel is not null && window._sprStream is not null
                && window._datPath is not null && window._filePath is not null && !window._sprEditInProgress && window.IsEnabled)
            .Select(window => new DatTransferTargetOption(window,
                $"{Path.GetFileName(window._datPath)}  —  {window._datModel!.VersionName}"))
            .ToArray();
        if (targets.Length == 0)
        {
            MessageBox.Show(this, Text("Abra uma nova janela pelo botão no topo e carregue nela o cliente de destino antes de transferir.",
                "Open a new window with the button at the top and load the destination client before transferring."),
                Text("Janela de destino não encontrada", "No destination window found"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MainWindow? destination;
        if (targets.Length == 1)
        {
            destination = targets[0].Window;
        }
        else
        {
            var targetPicker = new DatTransferTargetDialog(targets, IsEnglish) { Owner = this };
            if (targetPicker.ShowDialog() != true) return;
            destination = targetPicker.SelectedTarget;
        }
        if (destination is null || !OpenEditorWindows.Contains(destination)) return;
        if (destination._datModel is null || destination._sprStream is null || destination._sprEditInProgress || !destination.IsEnabled)
        {
            MessageBox.Show(this, Text("A janela de destino precisa estar com DAT e SPR abertos e sem edição em andamento.",
                "The destination window must have DAT and SPR open with no edit in progress."),
                Text("Destino indisponível", "Destination unavailable"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var replacementTargets = replace ? destination.GetSelectedDatThings().ToArray() : null;
        if (replace && (replacementTargets!.Length != things.Count
            || replacementTargets.Any(target => target.Category != things[0].Category)))
        {
            MessageBox.Show(this, Text(
                $"Selecione a mesma categoria e a mesma quantidade de objetos DAT nas duas janelas. Origem: {things.Count:N0}; destino: {replacementTargets.Length:N0}. Use Ctrl ou Shift para selecionar vários objetos.",
                $"Select the same category and number of DAT objects in both windows. Source: {things.Count:N0}; destination: {replacementTargets.Length:N0}. Use Ctrl or Shift to select multiple objects."),
                Text("Seleções incompatíveis", "Incompatible selections"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var spriteCount = things.Sum(thing =>
        {
            var groups = thing.Category == "outfit" ? thing.FrameGroups : thing.FrameGroups.Take(1).ToArray();
            return (long)groups.SelectMany(group => group.Frames).SelectMany(frame => frame.SpriteIds)
                .Where(id => id != 0).Distinct().Count();
        });
        var prompt = Text(
            $"Transferir {things.Count:N0} objeto(s) e até {spriteCount:N0} sprites vinculados para {Path.GetFileName(destination._datPath)}? Os objetos receberão novos IDs no final da categoria. Salve tudo na janela de destino para gravar as alterações.",
            $"Transfer {things.Count:N0} object(s) and up to {spriteCount:N0} linked sprites to {Path.GetFileName(destination._datPath)}? Objects will receive new IDs at the end of the category. Save all in the destination window to write the changes.");
        if (replace)
        {
            var pairs = string.Join("\n", things.Zip(replacementTargets!).Take(8)
                .Select(pair => $"#{pair.First.Id} → #{pair.Second.Id}"));
            if (things.Count > 8) pairs += "\n…";
            prompt = Text(
                $"Substituir {things.Count:N0} objeto(s) em {destination._datPath}?\n\n{pairs}\n\nOs pares seguem a ordem crescente dos IDs. Serão substituídos os atributos, sprites e animações, mantendo os IDs DAT do destino. Os sprites serão adicionados ao SPR de destino. Salve tudo na janela de destino para gravar o DAT.",
                $"Replace {things.Count:N0} object(s) in {destination._datPath}?\n\n{pairs}\n\nPairs follow ascending ID order. Attributes, sprites and animations will be replaced, preserving destination DAT IDs. Sprites will be appended to the destination SPR. Save all in the destination window to write the DAT.");
        }
        if (!ConfirmWithTheme(replace ? Text("Confirmar substituição", "Confirm replacement") : Text("Confirmar transferência", "Confirm transfer"), prompt, defaultNo: true))
            return;

        if (!OpenEditorWindows.Contains(destination) || destination._datModel is null || destination._sprStream is null
            || destination._sprEditInProgress)
        {
            MessageBox.Show(this, Text("A janela de destino foi fechada ou descarregada.", "The destination window was closed or its client was unloaded."),
                Text("Transferência cancelada", "Transfer cancelled"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var destinationModel = destination._datModel;
        var tempFolder = Path.Combine(Path.GetTempPath(), $"EditorUniverseTransfer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);
        destination.IsEnabled = false;
        IsEnabled = false;
        try
        {
            var obdPaths = new List<string>(things.Count);
            for (var index = 0; index < things.Count; index++)
            {
                var thing = things[index];
                StatusText.Text = Text($"Preparando objeto {index + 1:N0}/{things.Count:N0}…",
                    $"Preparing object {index + 1:N0}/{things.Count:N0}…");
                var groups = thing.Category == "outfit" ? thing.FrameGroups : thing.FrameGroups.Take(1).ToArray();
                var spriteIds = groups.SelectMany(group => group.Frames).SelectMany(frame => frame.SpriteIds)
                    .Distinct().ToArray();
                var pixels = await ReadSourceObdSpritePixelsAsync(spriteIds, sourceOffsets, sourceSpr.SafeFileHandle);
                var obdBytes = await Task.Run(() => ObdFileWriter.Serialize(
                    thing, sourceModel.VersionValue, destinationModel.VersionValue, 3, pixels));
                var path = Path.Combine(tempFolder, $"{index:D8}-{thing.Category}-{thing.Id}.obd");
                await File.WriteAllBytesAsync(path, obdBytes);
                obdPaths.Add(path);
            }

            await destination.ImportObdFolderAsync(obdPaths, replacementTargets);
            destination.IsEnabled = true;
            if (destination.WindowState == WindowState.Minimized) destination.WindowState = WindowState.Normal;
            destination.Activate();
            StatusText.Text = replace
                ? Text($"Substituídos {things.Count:N0} objeto(s) na outra janela. Salve tudo no destino.", $"Replaced {things.Count:N0} object(s) in the other window. Save all in the destination.")
                : Text($"Transferidos {things.Count:N0} objeto(s).", $"Transferred {things.Count:N0} object(s).");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            MessageBox.Show(this, ex.Message, Text("Falha na transferência", "Transfer failed"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            destination.IsEnabled = true;
            IsEnabled = true;
        }
    }
    private static async Task<IReadOnlyDictionary<uint, byte[]>> ReadSourceObdSpritePixelsAsync(
        IEnumerable<uint> spriteIds, uint[] offsets, Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        var results = new Dictionary<uint, byte[]>();
        var fileLength = RandomAccess.GetLength(handle);
        foreach (var id in spriteIds)
        {
            if (id == 0)
            {
                results[id] = new byte[SpritePixelCount * 4];
                continue;
            }
            if (id > offsets.Length)
                throw new InvalidDataException($"O DAT de origem referencia o sprite {id}, que não existe no SPR escolhido.");

            var offset = offsets[id - 1];
            if (offset == 0)
            {
                results[id] = new byte[SpritePixelCount * 4];
                continue;
            }
            if (offset + 5L > fileLength)
                throw new InvalidDataException($"O sprite {id} aponta para fora do arquivo SPR de origem.");

            var header = new byte[5];
            await ReadExactlyAsync(handle, header, offset);
            var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(3));
            if (compressedLength == 0)
            {
                results[id] = new byte[SpritePixelCount * 4];
                continue;
            }
            if (offset + 5L + compressedLength > fileLength)
                throw new InvalidDataException($"Os pixels do sprite {id} estão incompletos no SPR de origem.");

            var compressed = new byte[compressedLength];
            await ReadExactlyAsync(handle, compressed, offset + 5L);
            var bgra = DecodePixels(compressed)
                ?? throw new InvalidDataException($"Não foi possível decodificar o sprite {id} do SPR de origem.");
            var argb = new byte[bgra.Length];
            for (var pixel = 0; pixel < bgra.Length; pixel += 4)
            {
                argb[pixel] = bgra[pixel + 3];
                argb[pixel + 1] = bgra[pixel + 2];
                argb[pixel + 2] = bgra[pixel + 1];
                argb[pixel + 3] = bgra[pixel];
            }
            results[id] = argb;
        }
        return results;
    }

    private async Task<DatThing> ImportObdThingAsync(string path, string expectedCategory)
    {
        var model = _datModel ?? throw new InvalidOperationException(Text("Abra um arquivo DAT primeiro.", "Open a DAT file first."));
        if (_sprStream is null)
            throw new InvalidOperationException(Text("Abra também o arquivo SPR para importar os sprites do OBD.", "Open the SPR file too to import sprites from OBD."));

        var decoded = await Task.Run(() => ObdFileReader.Read(path, model.VersionValue));
        if (!string.Equals(decoded.Thing.Category, expectedCategory, StringComparison.Ordinal))
            throw new InvalidDataException(Text("A categoria do objeto OBD não corresponde à categoria selecionada.",
                "The OBD object's category does not match the selected category."));

        var originalSpriteIds = decoded.SpritePixels.Keys.Where(id => id != 0).OrderBy(id => id).ToArray();
        var importedSpriteIds = new Dictionary<uint, uint>();
        if (originalSpriteIds.Length > 0)
        {
            var bitmaps = originalSpriteIds.Select(id => ToBitmap(ConvertObdArgbToBgra(decoded.SpritePixels[id]))).ToArray();
            var firstNewId = await SaveSprChangesAsync(bitmaps)
                ?? throw new InvalidOperationException(Text("Não foi possível adicionar os sprites ao SPR aberto.", "Could not add the sprites to the open SPR."));
            for (var index = 0; index < originalSpriteIds.Length; index++)
                importedSpriteIds.Add(originalSpriteIds[index], checked(firstNewId + (uint)index));
        }

        var remappedGroups = decoded.Thing.FrameGroups.Select(group => group with
        {
            Frames = group.Frames.Select(frame => frame with
            {
                SpriteIds = frame.SpriteIds.Select(id => id == 0 ? 0u
                    : importedSpriteIds.TryGetValue(id, out var newId) ? newId
                    : throw new InvalidDataException($"O OBD não contém os pixels do sprite {id}.")).ToArray()
            }).ToArray()
        }).ToArray();
        return decoded.Thing with { FrameGroups = remappedGroups };
    }

    private static byte[] ConvertObdArgbToBgra(byte[] argb)
    {
        if (argb.Length != SpritePixelCount * 4) throw new InvalidDataException("Os pixels de um sprite OBD têm tamanho inválido.");
        var bgra = new byte[argb.Length];
        for (var index = 0; index < argb.Length; index += 4)
        {
            bgra[index] = argb[index + 3];
            bgra[index + 1] = argb[index + 2];
            bgra[index + 2] = argb[index + 1];
            bgra[index + 3] = argb[index];
        }
        return bgra;
    }

    private async Task RunDatThingImportAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception ex)
        {
            SetError(ex.Message);
            MessageBox.Show(this, ex.Message, Text("Falha ao importar objeto", "Object import failed"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DatExportButton_Click(object sender, RoutedEventArgs e)
    {
        ExportSelectedDatThings();
    }

    private async void ExportSelectedDatThings()
    {
        var selectedThings = GetSelectedDatThings();
        var model = _datModel;
        var spriteStream = _sprStream;
        if (model is null) return;
        if (spriteStream is null)
        {
            MessageBox.Show(this, Text("Abra também o arquivo SPR para exportar os gráficos dos objetos.", "Open the SPR file too to export object graphics."),
                Text("Arquivo SPR necessário", "SPR file required"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var category = selectedThings.FirstOrDefault()?.Category ?? SelectedDatCategory;
        var folder = Path.GetDirectoryName(_datPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var supportsObdV3 = model.Things.Any(thing => thing.Category == "outfit" && thing.FrameGroups.Count > 1);
        var categoryCounts = model.Things.GroupBy(thing => thing.Category)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var dialog = new DatExportDialog(category, folder, model.VersionValue, model.VersionName,
            supportsObdV3, supportsObdV3, category, categoryCounts, IsEnglish, selectedThings.Count == 0) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Options is not { } options) return;

        IReadOnlyList<DatThing> things = options.UseRange
            ? model.Things.Where(thing => thing.Category == options.Category).OrderBy(thing => thing.Id)
                .Skip(options.RangeFrom).Take(options.RangeTo - options.RangeFrom + 1).ToArray()
            : selectedThings;
        if (things.Count == 0)
        {
            MessageBox.Show(this, Text("A faixa selecionada não contém objetos para exportar.", "The selected range has no objects to export."),
                Text("Nada para exportar", "Nothing to export"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var exportCount = 0;
            foreach (var thing in things)
            {
                byte[] contents;
                if (options.Format == "obd")
                {
                    var groups = options.ObdVersion == 3 && thing.Category == "outfit"
                        ? thing.FrameGroups : thing.FrameGroups.Take(1).ToArray();
                    var referencedSpriteIds = groups.SelectMany(group => group.Frames)
                        .SelectMany(frame => frame.SpriteIds).Distinct().ToArray();
                    if (referencedSpriteIds.Length > 4096)
                        throw new InvalidDataException($"O objeto #{thing.Id} tem {referencedSpriteIds.Length:N0} sprites diferentes; o formato OBD aceita até 4.096.");
                    var spritePixels = await ReadObdSpritePixelsAsync(referencedSpriteIds, spriteStream.SafeFileHandle);
                    contents = ObdFileWriter.Serialize(thing, model.VersionValue, options.ClientVersion,
                        options.ObdVersion, spritePixels);
                }
                else
                {
                    var bitmap = await RenderThingSpriteSheetAsync(thing, spriteStream.SafeFileHandle,
                        options.Format == "png" && options.TransparentBackground);
                    contents = EncodeThingBitmap(bitmap, options.Format, options.JpegQuality);
                }

                var versionSuffix = options.Format == "obd"
                    ? $"_{options.ClientVersionName.Replace(" ", string.Empty).Replace(".", string.Empty)}-v{options.ObdVersion}"
                    : string.Empty;
                var fileName = $"{options.Name}_{thing.Id}{versionSuffix}.{options.Format}";
                var outputPath = Path.Combine(options.Folder, fileName);
                await File.WriteAllBytesAsync(outputPath, contents);
                exportCount++;
            }

            StatusText.Text = Text($"{exportCount} arquivo(s) exportado(s)", $"{exportCount} file(s) exported");
            StatusDot.Fill = (Brush)FindResource("AccentBrush");
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            MessageBox.Show(this, ex.Message, Text("Falha ao exportar", "Export failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportSelectedDatThingsJson()
    {
        var things = GetSelectedDatThings();
        if (things.Count == 0 || _datModel is null) return;
        var multiple = things.Count > 1;
        var dialog = new SaveFileDialog
        {
            Title = multiple ? Text("Exportar objetos DAT", "Export DAT objects") : Text("Exportar objeto DAT", "Export DAT object"),
            FileName = multiple ? $"{things[0].Category}-{things[0].Id}-{things[^1].Id}.zip" : $"{things[0].Category}-{things[0].Id}.json",
            Filter = multiple
                ? Text("Pacote de objetos DAT (*.zip)|*.zip", "DAT object package (*.zip)|*.zip")
                : Text("Objeto DAT do Editor Universe (*.json)|*.json", "Editor Universe DAT object (*.json)|*.json"),
            AddExtension = true, DefaultExt = multiple ? ".zip" : ".json"
        };
        if (dialog.ShowDialog(this) != true) return;
        RunDatThingOperation(() =>
        {
            if (multiple)
            {
                using var archive = ZipFile.Open(dialog.FileName, ZipArchiveMode.Create);
                foreach (var thing in things)
                {
                    var entry = archive.CreateEntry($"{thing.Category}-{thing.Id}.json", CompressionLevel.Optimal);
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    writer.Write(SerializeDatThingExchange(thing, _datModel!.VersionValue));
                }
            }
            else
                File.WriteAllText(dialog.FileName, SerializeDatThingExchange(things[0], _datModel!.VersionValue), new UTF8Encoding(false));
        });
    }

    private async Task<BitmapSource> RenderThingSpriteSheetAsync(DatThing thing,
        Microsoft.Win32.SafeHandles.SafeFileHandle handle, bool transparentBackground)
    {
        var groups = thing.FrameGroups;
        if (groups.Count == 0) throw new InvalidDataException($"O objeto #{thing.Id} não tem grupo de quadros.");
        var totalColumns = groups.Max(group => checked(group.PatternX * group.PatternZ * group.Layers));
        var totalRows = groups.Sum(group => checked(group.Frames.Count * group.PatternY));
        var maxWidth = groups.Max(group => group.Width);
        var maxHeight = groups.Max(group => group.Height);
        var cellWidth = checked(maxWidth * SpriteSide);
        var cellHeight = checked(maxHeight * SpriteSide);
        var outputWidth = checked(totalColumns * cellWidth);
        var outputHeight = checked(totalRows * cellHeight);
        if ((long)outputWidth * outputHeight > 100_000_000)
            throw new InvalidDataException($"A folha de sprites do objeto #{thing.Id} ficaria grande demais para exportar.");

        var pixels = new byte[checked(outputWidth * outputHeight * 4)];
        if (!transparentBackground)
        {
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 255; pixels[index + 1] = 0; pixels[index + 2] = 255; pixels[index + 3] = 255;
            }
        }

        var groupRowOffset = 0;
        foreach (var group in groups)
        {
            for (var frame = 0; frame < group.Frames.Count; frame++)
            for (var patternZ = 0; patternZ < group.PatternZ; patternZ++)
            for (var patternY = 0; patternY < group.PatternY; patternY++)
            for (var patternX = 0; patternX < group.PatternX; patternX++)
            for (var layer = 0; layer < group.Layers; layer++)
            {
                var textureIndex = ((((frame * group.PatternZ + patternZ) * group.PatternY + patternY)
                    * group.PatternX + patternX) * group.Layers + layer);
                var cellX = textureIndex % totalColumns;
                var cellY = groupRowOffset + textureIndex / totalColumns;
                var bitmap = await ReadThingBitmapAsync(thing, group, frame, patternX, patternY, patternZ, layer,
                    handle, cacheSelectedSprites: false, colorizeOutfitPreview: false);
                if (bitmap is null) continue;

                var sourcePixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
                bitmap.CopyPixels(sourcePixels, bitmap.PixelWidth * 4, 0);
                for (var row = 0; row < bitmap.PixelHeight; row++)
                {
                    var destinationOffset = ((cellY * cellHeight + row) * outputWidth + cellX * cellWidth) * 4;
                    CompositeExportPixels(sourcePixels, row * bitmap.PixelWidth * 4, pixels, destinationOffset, bitmap.PixelWidth);
                }
            }
            groupRowOffset += group.Frames.Count * group.PatternY;
        }

        var output = BitmapSource.Create(outputWidth, outputHeight, 96, 96, PixelFormats.Bgra32, null, pixels, outputWidth * 4);
        output.Freeze();
        return output;
    }

    private static void CompositeExportPixels(byte[] source, int sourceOffset, byte[] destination,
        int destinationOffset, int pixelCount)
    {
        for (var pixel = 0; pixel < pixelCount; pixel++)
        {
            var sourceIndex = sourceOffset + pixel * 4;
            var targetIndex = destinationOffset + pixel * 4;
            var sourceAlpha = source[sourceIndex + 3];
            if (sourceAlpha == 0) continue;
            if (sourceAlpha == 255 || destination[targetIndex + 3] == 0)
            {
                Buffer.BlockCopy(source, sourceIndex, destination, targetIndex, 4);
                continue;
            }

            var destinationAlpha = destination[targetIndex + 3];
            var outputAlpha = sourceAlpha + destinationAlpha * (255 - sourceAlpha) / 255;
            for (var channel = 0; channel < 3; channel++)
            {
                var numerator = source[sourceIndex + channel] * sourceAlpha
                    + destination[targetIndex + channel] * destinationAlpha * (255 - sourceAlpha) / 255;
                destination[targetIndex + channel] = (byte)(outputAlpha == 0 ? 0 : numerator / outputAlpha);
            }
            destination[targetIndex + 3] = (byte)outputAlpha;
        }
    }

    private async Task<IReadOnlyDictionary<uint, byte[]>> ReadObdSpritePixelsAsync(IEnumerable<uint> spriteIds,
        Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        var results = new Dictionary<uint, byte[]>();
        foreach (var id in spriteIds)
        {
            if (id == 0)
            {
                results[id] = new byte[SpritePixelCount * 4];
                continue;
            }
            var bitmap = await ReadSpriteBitmapAsync(id, handle)
                ?? throw new InvalidDataException($"Não foi possível ler o sprite {id} do arquivo SPR.");
            var bgra = new byte[SpritePixelCount * 4];
            bitmap.CopyPixels(bgra, SpriteSide * 4, 0);
            var argb = new byte[bgra.Length];
            for (var index = 0; index < bgra.Length; index += 4)
            {
                argb[index] = bgra[index + 3];
                argb[index + 1] = bgra[index + 2];
                argb[index + 2] = bgra[index + 1];
                argb[index + 3] = bgra[index];
            }
            results[id] = argb;
        }
        return results;
    }

    private static byte[] EncodeThingBitmap(BitmapSource bitmap, string format, int jpegQuality)
    {
        BitmapEncoder encoder = format switch
        {
            "bmp" => new BmpBitmapEncoder(),
            "jpg" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 10, 100) },
            _ => new PngBitmapEncoder()
        };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private void DatDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedDatThings();
    }

    private void DeleteSelectedDatThings()
    {
        var selected = GetSelectedDatThings();
        if (selected.Count == 0 || _datModel is not { } model) return;
        var category = selected[0].Category;
        var idSummary = selected.Count == 1 ? $"#{selected[0].Id:N0}" : $"{selected.Count} {Text("objetos selecionados", "selected objects")}";
        var prompt = Text($"Excluir {idSummary}? IDs no meio da categoria serão mantidos vazios para preservar os demais objetos; IDs finais selecionados serão removidos. Salve o DAT para gravar a alteração.",
            $"Delete {idSummary}? IDs inside the category will be kept empty to preserve later objects; selected trailing IDs will be removed. Save the DAT to write the change.");
        if (!ConfirmWithTheme(Text("Excluir objeto DAT", "Delete DAT object"), prompt, defaultNo: true)) return;

        RunDatThingOperation(() =>
        {
            var selectedIds = selected.Select(thing => thing.Id).ToHashSet();
            var count = GetDatCategoryCount(model, category);
            var firstId = category == "item" ? 100u : 1u;
            var newCount = count;
            while (newCount >= firstId && selectedIds.Contains(newCount)) newCount--;
            var updatedThings = model.Things.Where(thing => thing.Category != category || !selectedIds.Contains(thing.Id) || thing.Id <= newCount)
                .Select(thing => thing.Category == category && selectedIds.Contains(thing.Id) && thing.Id <= newCount
                    ? CreateBlankDatThing(thing.Id, category, thing, model.VersionValue) : thing)
                .ToArray();
            var updatedModel = WithDatCategoryCount(model with { Things = updatedThings }, category, newCount);
            _ = DatFileWriter.Serialize(updatedModel);
            if (_serverItemFiles is not null && category == "item")
            {
                foreach (var deleted in selected)
                {
                    if (deleted.Id > newCount) _serverItemFiles.RemoveByClientId(deleted.Id);
                    else if (updatedThings.FirstOrDefault(thing => thing.Category == "item" && thing.Id == deleted.Id) is { } blank)
                        _serverItemFiles.SynchronizeFromDat(blank, model.VersionValue);
                }
            }
            _datModel = updatedModel;
            _metadataHasUnsavedChanges = true;
            MetadataSaveStatus.Foreground = (Brush)FindResource("MutedBrush");
            MetadataSaveStatus.Text = Text("Alterações não salvas", "Unsaved changes");
            UpdateClientInfo();
            _selectedThingIds.Clear();
            _thingSelectionAnchorId = null;
            _pageBitmaps.Clear();
            ClearSearchForNavigation();
            var remainingThings = updatedModel.Things.Where(thing => thing.Category == category).OrderBy(thing => thing.Id).ToArray();
            if (remainingThings.Length > 0)
            {
                var focus = remainingThings.LastOrDefault(thing => thing.Id < selected[0].Id) ?? remainingThings[^1];
                var first = remainingThings[0].Id;
                _page = (int)(((long)focus.Id - first) / PageSize);
                RenderPage();
                SelectThing(focus);
                ScrollSearchResultIntoView(focus.Id);
            }
            else
            {
                _selectedThing = null;
                _selectedThingSpriteBitmaps.Clear();
                _selectedId = null;
                ResetThingControls();
                SetPreviewBitmap(null);
                SetSelectionSummary("—", "—");
                SelectedFileText.Text = Path.GetFileName(_datPath ?? string.Empty);
                SetExportEnabled(false);
                _page = 0;
                RenderPage();
            }
            UpdateDatActionToolbar();
        });
    }

    private string SerializeDatThingExchange(DatThing thing, int version) => JsonSerializer.Serialize(
        new DatThingExchangeDocument { FormatVersion = 1, VersionValue = version, Thing = ToDatThingExchange(thing) },
        new JsonSerializerOptions { WriteIndented = true });

    private DatThing ReadDatThingExchange(string path, string expectedCategory)
    {
        var model = _datModel ?? throw new InvalidOperationException(Text("Abra um arquivo DAT primeiro.", "Open a DAT file first."));
        if (new FileInfo(path).Length > 16L * 1024 * 1024)
            throw new InvalidDataException(Text("O arquivo de objeto é maior que 16 MB.", "The object file is larger than 16 MB."));
        var document = JsonSerializer.Deserialize<DatThingExchangeDocument>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            MaxDepth = 64
        }) ?? throw new InvalidDataException(Text("O arquivo não contém um objeto DAT válido.", "The file does not contain a valid DAT object."));
        if (document.FormatVersion != 1 || document.Thing is null)
            throw new InvalidDataException(Text("O formato do arquivo de objeto não é compatível.", "The object file format is not supported."));
        if (document.VersionValue != model.VersionValue)
            throw new InvalidDataException(Text("O objeto exportado pertence a outra versão do Tibia.", "The exported object belongs to a different Tibia version."));
        var thing = FromDatThingExchange(document.Thing);
        if (!string.Equals(thing.Category, expectedCategory, StringComparison.Ordinal))
            throw new InvalidDataException(Text("O objeto precisa ser da mesma categoria do DAT selecionado.", "The object must belong to the same category as the selected DAT object."));
        return ValidateDatThingExchange(thing, model, expectedCategory);
    }

    private static DatThingExchangeObject ToDatThingExchange(DatThing thing) => new()
    {
        Id = thing.Id,
        Category = thing.Category,
        SpriteCount = thing.SpriteCount,
        RawProperties = (byte[])thing.RawProperties.Clone(),
        FrameGroups = thing.FrameGroups.Select(group => new DatThingExchangeFrameGroup
        {
            Type = group.Type,
            Width = group.Width,
            Height = group.Height,
            CropSize = group.CropSize,
            Layers = group.Layers,
            PatternX = group.PatternX,
            PatternY = group.PatternY,
            PatternZ = group.PatternZ,
            AnimationMode = group.AnimationMode,
            LoopCount = group.LoopCount,
            StartFrame = group.StartFrame,
            Frames = group.Frames.Select(frame => new DatThingExchangeFrame
            {
                SpriteIds = (uint[])frame.SpriteIds.Clone(),
                MinimumDuration = frame.MinimumDuration,
                MaximumDuration = frame.MaximumDuration
            }).ToList()
        }).ToList()
    };

    private static DatThing FromDatThingExchange(DatThingExchangeObject thing) => new(
        thing.Id,
        thing.Category,
        thing.FrameGroups.Select(group => new DatFrameGroup(group.Type, group.Width, group.Height, group.CropSize,
            group.Layers, group.PatternX, group.PatternY, group.PatternZ, group.AnimationMode, group.LoopCount,
            group.StartFrame, group.Frames.Select(frame => new DatAnimationFrame((uint[])frame.SpriteIds.Clone(),
                frame.MinimumDuration, frame.MaximumDuration)).ToArray())).ToArray(),
        thing.SpriteCount,
        (byte[])thing.RawProperties.Clone());

    private DatThing ValidateDatThingExchange(DatThing thing, DatFileModel model, string category, uint? spriteIdLimit = null)
    {
        if (thing.RawProperties is not { Length: > 0 } || thing.RawProperties[^1] != 0xFF)
            throw new InvalidDataException(Text("O objeto não tem propriedades DAT válidas.", "The object has invalid DAT properties."));
        var supportsGroups = category == "outfit" && model.VersionValue >= 1057;
        if (thing.FrameGroups is not { Count: > 0 } || (!supportsGroups && thing.FrameGroups.Count != 1) || thing.FrameGroups.Count > byte.MaxValue)
            throw new InvalidDataException(Text("O objeto tem uma quantidade de grupos incompatível com esta versão.", "The object has an incompatible number of frame groups for this version."));

        long totalSprites = 0;
        foreach (var group in thing.FrameGroups)
        {
            if (group.Width is < 1 or > byte.MaxValue || group.Height is < 1 or > byte.MaxValue
                || group.CropSize is < 0 or > byte.MaxValue || group.Layers is < 1 or > byte.MaxValue
                || group.PatternX is < 1 or > byte.MaxValue || group.PatternY is < 1 or > byte.MaxValue
                || group.PatternZ is < 1 or > byte.MaxValue || group.Frames is not { Count: > 0 and <= byte.MaxValue })
                throw new InvalidDataException(Text("O objeto contém dimensões ou padrões inválidos.", "The object contains invalid dimensions or patterns."));
            var spritesPerFrame = (long)group.Width * group.Height * group.Layers * group.PatternX * group.PatternY * group.PatternZ;
            if (spritesPerFrame > 1_000_000) throw new InvalidDataException(Text("O objeto tem referências demais de sprites.", "The object contains too many sprite references."));
            foreach (var frame in group.Frames)
            {
                if (frame.SpriteIds is null || frame.SpriteIds.LongLength != spritesPerFrame)
                    throw new InvalidDataException(Text("A quantidade de sprites não corresponde ao padrão do objeto.", "The sprite count does not match the object's pattern."));
                var maximumSpriteId = spriteIdLimit ?? checked(_spriteCount + (uint)_pendingPixelSprites.Count);
                if (maximumSpriteId > 0 && frame.SpriteIds.Any(id => id > maximumSpriteId))
                    throw new InvalidDataException(Text("O objeto aponta para um sprite que não existe no SPR aberto.", "The object references a sprite that does not exist in the open SPR."));
                totalSprites += frame.SpriteIds.Length;
                if (totalSprites > 1_000_000) throw new InvalidDataException(Text("O objeto tem referências demais de sprites.", "The object contains too many sprite references."));
            }
        }
        if (thing.SpriteCount != totalSprites)
            throw new InvalidDataException(Text("A contagem de sprites do objeto está inválida.", "The object's sprite count is invalid."));
        return thing;
    }

    private uint GetNextDatThingId(DatFileModel model, string category)
    {
        return category switch
        {
            "item" when model.ItemCount < DatFileParser.MaximumThingCountPerCategory => Math.Max(100u, model.ItemCount + 1),
            "outfit" when model.OutfitCount < DatFileParser.MaximumThingCountPerCategory => model.OutfitCount + 1,
            "effect" when model.EffectCount < DatFileParser.MaximumThingCountPerCategory => model.EffectCount + 1,
            "missile" when model.MissileCount < DatFileParser.MaximumThingCountPerCategory => model.MissileCount + 1,
            _ => throw new InvalidOperationException(Text("Esta categoria já atingiu o limite de IDs do DAT.", "This category has reached the DAT ID limit."))
        };
    }

    private static DatThing CreateBlankDatThing(uint id, string category, DatThing? template, int version)
    {
        DatFrameGroup[] groups;
        if (template is not null)
        {
            groups = template.FrameGroups.Select(group => group with
            {
                Frames = group.Frames.Select(frame => frame with { SpriteIds = new uint[frame.SpriteIds.Length] }).ToArray()
            }).ToArray();
        }
        else
        {
            var patternX = category == "outfit" ? 4 : 1;
            var groupTypes = category == "outfit" && version >= 1057 ? new byte[] { 0, 1 } : new byte[] { 0 };
            groups = groupTypes.Select(type => new DatFrameGroup(type, 1, 1, 32, 1, patternX, 1, 1,
                0, 0, 0, [new DatAnimationFrame(new uint[patternX], 100, 100)])).ToArray();
        }
        var spriteCount = groups.Sum(group => group.Frames.Sum(frame => frame.SpriteIds.Length));
        return new DatThing(id, category, groups, spriteCount, [0xFF]);
    }

    private static DatThing CloneDatThing(DatThing source, uint id, string category) => source with
    {
        Id = id,
        Category = category,
        RawProperties = (byte[])source.RawProperties.Clone(),
        FrameGroups = source.FrameGroups.Select(group => group with
        {
            Frames = group.Frames.Select(frame => frame with { SpriteIds = (uint[])frame.SpriteIds.Clone() }).ToArray()
        }).ToArray()
    };

    private void ApplyDatThingChange(DatThing thing, bool addNew, bool preserveTextureSelection = false, bool refreshUi = true,
        uint? spriteIdLimit = null, bool serializeModelForValidation = true, bool fastPixelEdit = false)
    {
        var model = _datModel ?? throw new InvalidOperationException(Text("Abra um arquivo DAT primeiro.", "Open a DAT file first."));
        var textureGroupType = preserveTextureSelection ? _selectedFrameGroup?.Type : null;
        var textureFrame = _selectedAnimationFrame;
        var textureLayer = _selectedLayer;
        var texturePatternX = _selectedPatternX;
        var texturePatternY = _selectedPatternY;
        var texturePatternZ = _selectedPatternZ;
        var validated = fastPixelEdit ? thing : ValidateDatThingExchange(thing, model, thing.Category, spriteIdLimit);
        DatFileModel updatedModel;
        if (fastPixelEdit)
        {
            if (addNew) throw new InvalidOperationException("Pixel edits cannot add DAT objects.");
            var updatedThings = model.Things.ToArray();
            var existingIndex = -1;
            for (var index = 0; index < updatedThings.Length; index++)
                if (updatedThings[index].Category == thing.Category && updatedThings[index].Id == thing.Id)
                {
                    existingIndex = index;
                    break;
                }
            if (existingIndex < 0)
                throw new InvalidOperationException(Text("O objeto escolhido não existe mais no DAT.", "The selected object no longer exists in the DAT."));
            updatedThings[existingIndex] = validated;
            updatedModel = model with { Things = updatedThings };
        }
        else if (addNew)
        {
            if (model.Things.Any(existing => existing.Category == thing.Category && existing.Id == thing.Id))
                throw new InvalidOperationException(Text("O próximo ID já está em uso no DAT.", "The next ID is already used in the DAT."));
            updatedModel = thing.Category switch
            {
                "item" => model with { ItemCount = thing.Id, Uses32BitIds = model.Uses32BitIds || thing.Id > ushort.MaxValue, Things = OrderDatThings(model.Things.Append(validated)) },
                "outfit" => model with { OutfitCount = thing.Id, Uses32BitIds = model.Uses32BitIds || thing.Id > ushort.MaxValue, Things = OrderDatThings(model.Things.Append(validated)) },
                "effect" => model with { EffectCount = thing.Id, Uses32BitIds = model.Uses32BitIds || thing.Id > ushort.MaxValue, Things = OrderDatThings(model.Things.Append(validated)) },
                "missile" => model with { MissileCount = thing.Id, Uses32BitIds = model.Uses32BitIds || thing.Id > ushort.MaxValue, Things = OrderDatThings(model.Things.Append(validated)) },
                _ => throw new InvalidDataException(Text("Categoria DAT desconhecida.", "Unknown DAT category."))
            };
        }
        else
        {
            if (!model.Things.Any(existing => existing.Category == thing.Category && existing.Id == thing.Id))
                throw new InvalidOperationException(Text("O objeto escolhido não existe mais no DAT.", "The selected object no longer exists in the DAT."));
            updatedModel = model with
            {
                Things = model.Things.Select(existing => existing.Category == thing.Category && existing.Id == thing.Id
                    ? validated : existing).ToArray()
            };
        }
        if (serializeModelForValidation) _ = DatFileWriter.Serialize(updatedModel);
        _datModel = updatedModel;
        if (!fastPixelEdit && _serverItemFiles is not null && thing.Category == "item")
        {
            if (addNew) _serverItemFiles.AddFromDat(validated, updatedModel.VersionValue);
            else _serverItemFiles.SynchronizeFromDat(validated, updatedModel.VersionValue);
        }
        _metadataHasUnsavedChanges = true;
        MetadataSaveStatus.Foreground = (Brush)FindResource("MutedBrush");
        MetadataSaveStatus.Text = Text("Alterações não salvas", "Unsaved changes");
        UpdateClientInfo();
        if (!refreshUi)
        {
            _selectedThing = validated;
            _selectedFrameGroup = validated.FrameGroups.FirstOrDefault(group => group.Type == textureGroupType)
                ?? validated.FrameGroups.FirstOrDefault();
            if (fastPixelEdit)
                return;
            UpdateAnimationAndLayerControls();
            UpdatePatternInfo(_selectedFrameGroup);
            UpdateDatActionToolbar();
            return;
        }
        var categoryThings = updatedModel.Things.Where(existing => existing.Category == thing.Category).OrderBy(existing => existing.Id).ToArray();
        var firstId = categoryThings.Length == 0 ? thing.Id : categoryThings[0].Id;
        _page = (int)(((long)thing.Id - firstId) / PageSize);
        _pageBitmaps.Clear();
        ClearSearchForNavigation();
        RenderPage();
        SelectThing(validated);
        if (preserveTextureSelection && textureGroupType is byte groupType)
            RestoreTextureEditorPosition(groupType, textureFrame, textureLayer, texturePatternX, texturePatternY, texturePatternZ);
        ScrollSearchResultIntoView(thing.Id);
        UpdateDatActionToolbar();
    }

    private void RestoreTextureEditorPosition(byte groupType, int frame, int layer, int patternX, int patternY, int patternZ)
    {
        if (_selectedThing is null) return;
        var group = _selectedThing.FrameGroups.FirstOrDefault(candidate => candidate.Type == groupType);
        if (group is null) return;
        StopAnimation();
        _selectedFrameGroup = group;
        _selectedAnimationFrame = Math.Clamp(frame, 0, group.Frames.Count - 1);
        _selectedLayer = Math.Clamp(layer, 0, group.Layers - 1);
        _selectedPatternX = Math.Clamp(patternX, 0, group.PatternX - 1);
        _selectedPatternY = Math.Clamp(patternY, 0, group.PatternY - 1);
        _selectedPatternZ = Math.Clamp(patternZ, 0, group.PatternZ - 1);
        _updatingThingControls = true;
        try
        {
            FrameGroupCombo.SelectedItem = FrameGroupCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is byte type && type == group.Type);
            PopulatePatternCombo(PatternXCombo, group.PatternX, _selectedPatternX, 'x');
            PopulatePatternCombo(PatternYCombo, group.PatternY, _selectedPatternY, 'y');
            PopulatePatternCombo(PatternZCombo, group.PatternZ, _selectedPatternZ, 'z');
        }
        finally { _updatingThingControls = false; }
        UpdateAnimationAndLayerControls();
        UpdatePatternInfo(group);
        SetSelectionSummary($"{CategoryName(_selectedThing.Category)} #{_selectedThing.Id}",
            $"{group.Width * SpriteSide} × {group.Height * SpriteSide} px");
        _ = LoadSelectedThingPreviewAsync();
        StartInfoPreviewAnimation();
    }

    private static IReadOnlyList<DatThing> OrderDatThings(IEnumerable<DatThing> things) => things
        .OrderBy(thing => thing.Category switch { "item" => 0, "outfit" => 1, "effect" => 2, "missile" => 3, _ => 4 })
        .ThenBy(thing => thing.Id)
        .ToArray();

    private void RunDatThingOperation(Action operation)
    {
        try { operation(); }
        catch (Exception ex)
        {
            SetError(ex.Message);
            MessageBox.Show(this, ex.Message, Text("Falha ao editar DAT", "DAT edit failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowDatThingActionComplete(string message)
    {
        SetError(null);
        MessageBox.Show(this, message, Text("Objeto DAT atualizado", "DAT object updated"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void SaveAllButton_Click(object sender, RoutedEventArgs e) => await SavePendingChangesAsync();

    private async void SaveDatButton_Click(object sender, RoutedEventArgs e) => await SavePendingChangesAsync();

    private async Task SavePendingChangesAsync()
    {
        if (_isPixelEditInProgress || _queuedPixelNudges.Count > 0)
        {
            MetadataSaveStatus.Text = Text("Aguarde o movimento terminar antes de salvar.", "Wait for the movement to finish before saving.");
            return;
        }
        if (!_metadataHasUnsavedChanges && _pendingPixelSprites.Count == 0 && _serverItemFiles?.IsDirty != true) return;
        if (_datModel is not null && _datPath is not null && !IsMetadataFormValid()) return;
        if (!IsServerItemFormValid()) return;
        var temporaryFiles = new List<(string Temporary, string Target)>();
        DatFileModel? verifiedModel = null;
        try
        {
            await FlushPendingPixelSpritesAsync();
            var modelToSave = _datModel;
            var targetPath = _datPath;
            if (modelToSave is not null && targetPath is not null)
            {
                var temporaryPath = CreateTemporaryPath(targetPath);
                temporaryFiles.Add((temporaryPath, targetPath));
                await File.WriteAllBytesAsync(temporaryPath, DatFileWriter.Serialize(modelToSave));
                verifiedModel = await Task.Run(() => DatFileParser.Parse(temporaryPath, modelToSave.VersionValue, modelToSave.Extended, modelToSave.ImprovedAnimations, modelToSave.FrameGroups));
            }

            if (_serverItemFiles is not null)
            {
                var temporaryOtbPath = CreateTemporaryPath(_serverItemFiles.OtbPath);
                temporaryFiles.Add((temporaryOtbPath, _serverItemFiles.OtbPath));
                var otbBytes = _serverItemFiles.SerializeOtb();
                await File.WriteAllBytesAsync(temporaryOtbPath, otbBytes);
                _ = await Task.Run(() => ServerItemFiles.Load(temporaryOtbPath));

                if (_serverItemFiles.XmlPath is { } xmlPath && _serverItemFiles.SerializeXml() is { } xmlBytes)
                {
                    var temporaryXmlPath = CreateTemporaryPath(xmlPath);
                    temporaryFiles.Add((temporaryXmlPath, xmlPath));
                    await File.WriteAllBytesAsync(temporaryXmlPath, xmlBytes);
                    await Task.Run(() => System.Xml.Linq.XDocument.Load(temporaryXmlPath, System.Xml.Linq.LoadOptions.PreserveWhitespace));
                }
            }

            foreach (var (temporary, target) in temporaryFiles.OrderBy(pair => pair.Target == targetPath ? 1 : 0))
                File.Move(temporary, target, overwrite: true);

            var selectedId = _selectedThing?.Id ?? _selectedId;
            if (targetPath is not null) _datPath = targetPath;
            if (verifiedModel is not null) _datModel = verifiedModel;
            _serverItemFiles?.ClearDirty();
            _metadataHasUnsavedChanges = false;
            MetadataSaveStatus.Foreground = (Brush)FindResource("MutedBrush");
            MetadataSaveStatus.Text = _serverItemFiles is null
                ? Text("DAT salvo", "DAT saved")
                : modelToSave is null
                    ? _serverItemFiles.HasXml ? Text("OTB e XML salvos", "OTB and XML saved") : Text("OTB salvo", "OTB saved")
                    : _serverItemFiles.HasXml ? Text("DAT, OTB e XML salvos", "DAT, OTB, and XML saved") : Text("DAT e OTB salvos", "DAT and OTB saved");
            if (_datModel is not null)
            {
                UpdateClientInfo();
                _pageBitmaps.Clear();
                RenderPage();
            }
            var savedThing = selectedId is uint id
                ? _datModel?.Things.FirstOrDefault(thing => thing.Id == id && thing.Category == _selectedThing?.Category)
                : null;
            if (savedThing is not null) SelectThing(savedThing);
            else UpdateMetadataEditingState();
            StatusDot.Fill = ThemeBrush("SuccessBrush");
            StatusText.Text = _datModel?.VersionName ?? GetVersionName();
            SetError(null);
        }
        catch (Exception ex)
        {
            SetMetadataStatusError(Text($"Falha ao salvar: {ex.Message}", $"Save failed: {ex.Message}"));
            SetError(Text($"Não foi possível salvar os arquivos: {ex.Message}", $"Could not save the files: {ex.Message}"));
        }
        finally
        {
            foreach (var (temporary, _) in temporaryFiles)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
            }
        }
    }

    private static string CreateTemporaryPath(string targetPath) => Path.Combine(
        Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory,
        $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

    private bool IsServerItemFormValid()
    {
        foreach (var input in new[] { ServerGroundSpeedInput, ServerMinimapInput, ServerTradeAsInput, ServerLightLevelInput, ServerLightColorInput }
            .Where(input => input.IsEnabled))
        {
            if (ushort.TryParse(input.Text, out _)) continue;
            SetMetadataStatusError(Text("Os campos numéricos do items.otb devem ficar entre 0 e 65535.",
                "Numeric items.otb fields must be between 0 and 65535."));
            return false;
        }
        return true;
    }

    private bool ConfirmDiscardUnsavedMetadata()
    {
        if (!_metadataHasUnsavedChanges && _pendingPixelSprites.Count == 0 && _serverItemFiles?.IsDirty != true) return true;
        return ConfirmWithTheme(Text("Alterações não salvas", "Unsaved changes"),
            Text("Existem alterações do DAT/SPR que ainda não foram salvas. Deseja continuar e descartá-las?",
                "There are unsaved DAT/SPR changes. Continue and discard them?"));
    }

    private void ResetThingControls()
    {
        StopAnimation();
        StopInfoPreviewAnimation();
        _previewRenderVersion++;
        _filmRollRenderVersion++;
        _selectedFilmRollFrames.Clear();
        _filmRollSelectionAnchor = -1;
        _editPixelsMode = false;
        _wrapPixelShift = false;
        _filmRollMode = false;
        FilmRollPanel.Children.Clear();
        _selectedThing = null;
        _selectedFrameGroup = null;
        FrameGroupCombo.Items.Clear();
        PatternSelectorsPanel.Visibility = Visibility.Collapsed;
        FrameGroupLabel.Visibility = FrameGroupCombo.Visibility = Visibility.Collapsed;
        PatternXLabel.Visibility = PatternXCombo.Visibility = Visibility.Collapsed;
        PatternYLabel.Visibility = PatternYCombo.Visibility = Visibility.Collapsed;
        PatternZLabel.Visibility = PatternZCombo.Visibility = Visibility.Collapsed;
        AnimationLayerPanel.Visibility = PatternMetadataPanel.Visibility = Visibility.Collapsed;
        UpdateAnimationAndLayerControls();
        UpdatePatternInfo(null);
        UpdateMetadataEditingState();
        UpdateItemAttributeEditor();
    }

    private void FrameGroupCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updatingThingControls || _selectedThing is null
            || FrameGroupCombo.SelectedItem is not ComboBoxItem { Tag: byte type }) return;
        var group = _selectedThing.FrameGroups.FirstOrDefault(candidate => candidate.Type == type);
        if (group is null || ReferenceEquals(group, _selectedFrameGroup)) return;
        StopAnimation();
        _selectedFrameGroup = group;
        _selectedFilmRollFrames.Clear();
        _filmRollSelectionAnchor = -1;
        _selectedAnimationFrame = InitialAnimationFrame(group);
        _selectedLayer = 0;
        _selectedPatternX = _selectedThing.Category == "outfit" ? Math.Min(2, group.PatternX - 1) : 0;
        _selectedPatternY = _selectedPatternZ = 0;
        _animationLoop = 0;
        _animationDirection = 1;
        _updatingThingControls = true;
        try
        {
            PatternSelectorsPanel.Visibility = _selectedThing.FrameGroups.Count > 1
                || group.PatternX > 1 || group.PatternY > 1 || group.PatternZ > 1
                ? Visibility.Visible : Visibility.Collapsed;
            PopulatePatternCombo(PatternXCombo, group.PatternX, _selectedPatternX, 'x');
            PopulatePatternCombo(PatternYCombo, group.PatternY, _selectedPatternY, 'y');
            PopulatePatternCombo(PatternZCombo, group.PatternZ, _selectedPatternZ, 'z');
            UpdateAnimationAndLayerControls();
            UpdatePatternInfo(group);
        }
        finally { _updatingThingControls = false; }
        UpdateItemAttributeEditor();
        SetSelectionSummary($"{CategoryName(_selectedThing.Category)} #{_selectedThing.Id}",
            $"{group.Width * SpriteSide} × {group.Height * SpriteSide} px");
        _ = LoadSelectedThingPreviewAsync();
        StartInfoPreviewAnimation();
        if (_filmRollMode) _ = RenderFilmRollFramesAsync();
    }

    private void PatternCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updatingThingControls || _selectedThing is null) return;
        _selectedPatternX = ComboIndex(PatternXCombo);
        _selectedPatternY = ComboIndex(PatternYCombo);
        _selectedPatternZ = ComboIndex(PatternZCombo);
        _selectedFilmRollFrames.Clear();
        _filmRollSelectionAnchor = -1;
        UpdateItemAttributeEditor();
        _ = LoadSelectedThingPreviewAsync();
        _ = LoadSelectedThingInfoPreviewAsync();
        if (_filmRollMode) _ = RenderFilmRollFramesAsync();
    }

    private static int ComboIndex(ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem { Tag: int index } ? index : 0;

    private void AnimationFrameSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _updatingThingControls || _selectedFrameGroup is null) return;
        _selectedAnimationFrame = Math.Clamp((int)Math.Round(AnimationFrameSlider.Value) - 1, 0, Math.Max(0, _selectedFrameGroup.Frames.Count - 1));
        _animationLoop = 0;
        UpdateAnimationAndLayerControls();
        _ = LoadSelectedThingPreviewAsync();
        UpdateFilmRollSelectionVisuals();
    }

    private void LayerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _updatingThingControls || _selectedFrameGroup is null) return;
        _selectedLayer = Math.Clamp((int)Math.Round(LayerSlider.Value) - 1, 0, Math.Max(0, _selectedFrameGroup.Layers - 1));
        _selectedFilmRollFrames.Clear();
        _filmRollSelectionAnchor = -1;
        UpdateAnimationAndLayerControls();
        _ = LoadSelectedThingPreviewAsync();
        if (_filmRollMode) _ = RenderFilmRollFramesAsync();
    }

    private void AnimationPlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFrameGroup?.Frames.Count < 2) return;
        _isPlaying = !_isPlaying;
        if (_isPlaying)
        {
            UpdateAnimationTimerInterval();
            _animationTimer.Start();
        }
        else _animationTimer.Stop();
        AnimationPlayButton.Content = _isPlaying ? "❚❚" : "▶";
    }

    private void StopAnimation()
    {
        _animationTimer.Stop();
        _isPlaying = false;
        if (AnimationPlayButton is not null) AnimationPlayButton.Content = "▶";
    }

    private async void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isPlaying || _selectedFrameGroup is not { Frames.Count: > 1 } group || _isRenderingThingPreview) return;
        var next = _selectedAnimationFrame + _animationDirection;
        var synchronous = group.AnimationMode == 1;
        if (!synchronous && group.LoopCount < 0)
        {
            if (next >= group.Frames.Count || next < 0)
            {
                _animationDirection *= -1;
                next = _selectedAnimationFrame + _animationDirection;
            }
        }
        else if (next >= group.Frames.Count)
        {
            if (!synchronous && group.LoopCount > 0 && _animationLoop + 1 >= group.LoopCount)
            {
                _isPlaying = false;
                _animationTimer.Stop();
                AnimationPlayButton.Content = "▶";
                return;
            }
            _animationLoop++;
            next = 0;
        }
        _selectedAnimationFrame = next;
        UpdateAnimationAndLayerControls();
        await LoadSelectedThingPreviewAsync();
    }

    private void UpdateAnimationTimerInterval()
    {
        if (_selectedFrameGroup is not { Frames.Count: > 0 } group) return;
        var frame = group.Frames[Math.Clamp(_selectedAnimationFrame, 0, group.Frames.Count - 1)];
        var duration = (frame.MinimumDuration + (double)frame.MaximumDuration) / 2;
        _animationTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(duration, 10, 360000));
    }

    private void StartInfoPreviewAnimation()
    {
        StopInfoPreviewAnimation();
        if (_selectedThing is null || _selectedFrameGroup is not { Frames.Count: > 0 } group || _sprStream is null)
        {
            SetInfoPreviewBitmap(null);
            return;
        }

        _infoPreviewFrame = InitialAnimationFrame(group);
        _ = LoadSelectedThingInfoPreviewAsync();
        UpdateInfoPreviewTimerInterval();
        if (group.Frames.Count > 1) _infoPreviewTimer.Start();
    }

    private void StopInfoPreviewAnimation()
    {
        _infoPreviewTimer.Stop();
        _infoPreviewRenderVersion++;
    }

    private void UpdateInfoPreviewTimerInterval()
    {
        if (_selectedFrameGroup is not { Frames.Count: > 0 } group) return;
        var frame = group.Frames[Math.Clamp(_infoPreviewFrame, 0, group.Frames.Count - 1)];
        var duration = (frame.MinimumDuration + (double)frame.MaximumDuration) / 2;
        _infoPreviewTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(duration, 10, 360000));
    }

    private async void InfoPreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (_selectedFrameGroup is not { Frames.Count: > 1 } group) return;
        _infoPreviewFrame = (_infoPreviewFrame + 1) % group.Frames.Count;
        UpdateInfoPreviewTimerInterval();
        await LoadSelectedThingInfoPreviewAsync();
    }

    private void UpdateViewButtons()
    {
        if (ListViewButton is null) return;
        var active = (Style)FindResource("PrimaryButton");
        var inactive = (Style)FindResource("SecondaryButton");
        ListViewButton.Style = _gridMode ? inactive : active;
        GridViewButton.Style = _gridMode ? active : inactive;
        ObjectSpriteListButton.Style = _objectSpriteGridMode ? inactive : active;
        ObjectSpriteGridButton.Style = _objectSpriteGridMode ? active : inactive;
    }

    private void UpdateObjectSpritePanelVisibility()
    {
        var visible = IsObjectMode && _datModel is not null;
        ShowGridCheckBox.Visibility = IsObjectMode ? Visibility.Visible : Visibility.Collapsed;
        BulkAnimationSpeedButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ObjectSpritePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ObjectSpriteColumn.Width = visible && Width >= 1280 ? new GridLength(270) : new GridLength(0);
        UpdateViewButtons();
        if (visible) RenderObjectSpritePage();
        UpdatePixelEditorControls();
        UpdateBulkAnimationSpeedState();
    }

    private void RenderObjectSpritePage()
    {
        if (ObjectSpriteGrid is null || ObjectSpritePanel.Visibility != Visibility.Visible) return;
        ObjectSpriteGrid.Children.Clear();
        _objectSpriteBitmaps.Clear();
        var spriteReady = _sprStream is not null && _spriteCount > 0;
        UpdateObjectSpriteCountText();
        ObjectSpriteEmptyState.Visibility = spriteReady ? Visibility.Collapsed : Visibility.Visible;
        ObjectSpriteEmptyTitle.Text = spriteReady
            ? Text("Nenhum sprite", "No sprites") : Text("Nenhum SPR aberto", "No SPR open");
        ObjectSpriteEmptyBody.Text = spriteReady
            ? Text("Não há sprites neste trecho.", "There are no sprites in this range.")
            : Text("Abra um arquivo SPR para navegar pelos sprites.", "Open an SPR file to browse its sprites.");
        ObjectSpriteFirstButton.IsEnabled = ObjectSpritePreviousButton.IsEnabled =
            ObjectSpriteNextButton.IsEnabled = ObjectSpriteLastButton.IsEnabled = spriteReady;
        ObjectSpriteIdInput.IsEnabled = spriteReady;
        UpdateSpriteActionToolbar();

        if (!spriteReady)
        {
            ObjectSpriteRangeText.Text = Text("Aguardando o arquivo SPR", "Waiting for the SPR file");
            SetObjectSpriteIdInput("—");
            return;
        }

        if (_objectSpriteId == 0 || _objectSpriteId > _spriteCount) _objectSpriteId = 1;
        UpdateObjectSpriteRangeText();
        var page = (long)(_objectSpriteId - 1) / PageSize;
        var start = (uint)(page * PageSize + 1);
        var end = (uint)Math.Min(_spriteCount, (ulong)start + PageSize - 1);
        SetObjectSpriteIdInput(_objectSpriteId.ToString());
        var ids = new List<uint>(PageSize);
        for (var id = start; id <= end; id++) ids.Add(id);
        foreach (var id in ids) ObjectSpriteGrid.Children.Add(CreateObjectSpriteCard(id));
        _ = LoadObjectSpritePreviewsAsync(ids, start);
        ObjectSpriteScrollViewer.ScrollToTop();
    }

    private void UpdateObjectSpriteRangeText()
    {
        if (ObjectSpriteRangeText is null) return;
        if (_spriteCount == 0)
        {
            ObjectSpriteRangeText.Text = Text("Aguardando o arquivo SPR", "Waiting for the SPR file");
            return;
        }
        var page = (long)(_objectSpriteId - 1) / PageSize;
        var start = (uint)(page * PageSize + 1);
        var end = (uint)Math.Min(_spriteCount, (ulong)start + PageSize - 1);
        ObjectSpriteRangeText.Text = Text($"{start:N0}–{end:N0} de {_spriteCount:N0}", $"{start:N0}–{end:N0} of {_spriteCount:N0}");
    }

    private Border CreateObjectSpriteCard(uint id)
    {
        var imageSize = _objectSpriteGridMode ? 46 : 38;
        var tileSize = _objectSpriteGridMode ? 54 : 42;
        var image = new Image { Width = imageSize, Height = imageSize, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var thumbnail = new Border
        {
            Width = tileSize, Height = tileSize, CornerRadius = new CornerRadius(4),
            Background = ThemeBrush("CardBackgroundBrush"), Child = image,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = id.ToString(), Foreground = ThemeBrush("TextBrush"), FontSize = 10,
            HorizontalAlignment = _objectSpriteGridMode ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = _objectSpriteGridMode ? new Thickness(0, 5, 0, 0) : new Thickness(9, 0, 0, 0)
        };
        var content = new StackPanel
        {
            Orientation = _objectSpriteGridMode ? Orientation.Vertical : Orientation.Horizontal,
            HorizontalAlignment = _objectSpriteGridMode ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { thumbnail, label }
        };
        var selected = _selectedObjectSpriteIds.Contains(id);
        var card = new Border
        {
            Width = _objectSpriteGridMode ? 82 : 234, Height = _objectSpriteGridMode ? 94 : 47,
            Margin = new Thickness(1), Padding = new Thickness(_objectSpriteGridMode ? 6 : 3),
            CornerRadius = new CornerRadius(4),
            Background = (selected ? ThemeBrush("SelectedBackgroundBrush") : ThemeBrush("CardBackgroundBrush")),
            BorderBrush = selected ? (Brush)FindResource("AccentBrush") : ThemeBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1), Child = content, Tag = id, Cursor = System.Windows.Input.Cursors.Hand
        };
        var dragStart = new Point();
        var dragStarted = false;
        card.PreviewMouseLeftButtonDown += (_, args) =>
        {
            dragStart = args.GetPosition(card);
            dragStarted = false;
        };
        card.PreviewMouseMove += (_, args) =>
        {
            if (args.LeftButton != MouseButtonState.Pressed || dragStarted) return;
            var current = args.GetPosition(card);
            if (Math.Abs(current.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(current.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragStarted = true;
            var data = new DataObject();
            data.SetData(SpriteDragDataFormat, id);
            DragDrop.DoDragDrop(card, data, DragDropEffects.Copy);
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (!dragStarted) SelectObjectSpriteWithModifiers(id, Keyboard.Modifiers);
        };
        return card;
    }

    private async Task LoadObjectSpritePreviewsAsync(IReadOnlyList<uint> ids, uint pageStart)
    {
        var stream = _sprStream;
        if (stream is null) return;
        var results = await Task.WhenAll(ids.Select(async id =>
            (Id: id, Bitmap: await ReadSpriteBitmapAsync(id, stream.SafeFileHandle))));
        if (!ReferenceEquals(stream, _sprStream) || ObjectSpritePanel.Visibility != Visibility.Visible
            || _spriteCount == 0 || (uint)(((ulong)_objectSpriteId - 1) / PageSize * PageSize + 1) != pageStart) return;
        foreach (var (id, bitmap) in results)
        {
            if (bitmap is null) continue;
            _objectSpriteBitmaps[id] = bitmap;
            foreach (var card in ObjectSpriteGrid.Children.OfType<Border>())
            {
                if (card.Tag is uint cardId && cardId == id
                    && card.Child is StackPanel stack && stack.Children[0] is Border thumbnail && thumbnail.Child is Image image)
                {
                    image.Source = bitmap;
                    break;
                }
            }
        }
        UpdateSpriteActionToolbar();
    }

    private void SetObjectSpriteSelection(uint id, bool resetMultipleSelection = true)
    {
        if (_spriteCount == 0 || id == 0 || id > _spriteCount) return;
        if (resetMultipleSelection)
        {
            _selectedObjectSpriteIds.Clear();
            _selectedObjectSpriteIds.Add(id);
            _objectSpriteSelectionAnchorId = id;
        }
        var previousPage = _objectSpriteId == 0 ? -1L : ((long)_objectSpriteId - 1) / PageSize;
        _objectSpriteId = id;
        SetObjectSpriteIdInput(id.ToString());
        UpdateObjectSpriteCountText();
        if (previousPage != ((long)id - 1) / PageSize)
        {
            RenderObjectSpritePage();
            return;
        }
        UpdateObjectSpriteSelectionVisuals();
        UpdateSpriteActionToolbar();
    }

    private void UpdateObjectSpriteCountText()
    {
        if (ObjectSpriteCountText is null) return;
        ObjectSpriteCountText.Text = _spriteCount == 0 ? "0" : _selectedObjectSpriteIds.Count == 0
            ? _spriteCount.ToString("N0")
            : $"{_spriteCount:N0} · {_selectedObjectSpriteIds.Count:N0} {Text("selecionados", "selected")}";
    }

    private void UpdateObjectSpriteSelectionVisuals()
    {
        if (ObjectSpriteGrid is null) return;
        foreach (var card in ObjectSpriteGrid.Children.OfType<Border>())
        {
            var selected = card.Tag is uint id && _selectedObjectSpriteIds.Contains(id);
            card.BorderBrush = selected ? (Brush)FindResource("AccentBrush") : ThemeBrush("CardBorderBrush");
            card.Background = (selected ? ThemeBrush("SelectedBackgroundBrush") : ThemeBrush("CardBackgroundBrush"));
        }
        UpdateObjectSpriteCountText();
        UpdateSpriteActionToolbar();
    }

    private void SelectObjectSpriteWithModifiers(uint id, ModifierKeys modifiers)
    {
        if (_spriteCount == 0 || id == 0 || id > _spriteCount) return;
        if (modifiers.HasFlag(ModifierKeys.Shift) && _objectSpriteSelectionAnchorId is uint anchor)
        {
            if (!modifiers.HasFlag(ModifierKeys.Control)) _selectedObjectSpriteIds.Clear();
            var first = Math.Min(anchor, id);
            var last = Math.Max(anchor, id);
            for (var current = first; current <= last; current++) _selectedObjectSpriteIds.Add(current);
        }
        else if (modifiers.HasFlag(ModifierKeys.Control))
        {
            if (!_selectedObjectSpriteIds.Add(id)) _selectedObjectSpriteIds.Remove(id);
            _objectSpriteSelectionAnchorId = id;
        }
        else
        {
            _selectedObjectSpriteIds.Clear();
            _selectedObjectSpriteIds.Add(id);
            _objectSpriteSelectionAnchorId = id;
        }
        SetObjectSpriteSelection(id, resetMultipleSelection: false);
    }

    private void SelectSpritePageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_spriteCount == 0) return;
        var start = (uint)(((ulong)_objectSpriteId - 1) / PageSize * PageSize + 1);
        var end = (uint)Math.Min(_spriteCount, (ulong)start + PageSize - 1);
        _selectedObjectSpriteIds.Clear();
        for (var id = start; id <= end; id++) _selectedObjectSpriteIds.Add(id);
        _objectSpriteSelectionAnchorId = start;
        UpdateObjectSpriteSelectionVisuals();
    }

    private void SetObjectSpriteIdInput(string value)
    {
        _updatingObjectSpriteIdInput = true;
        try { ObjectSpriteIdInput.Text = value; }
        finally { _updatingObjectSpriteIdInput = false; }
    }

    private void CommitObjectSpriteIdInput()
    {
        if (_updatingObjectSpriteIdInput || _spriteCount == 0) return;
        if (uint.TryParse(ObjectSpriteIdInput.Text, out var id) && id > 0 && id <= _spriteCount)
            SetObjectSpriteSelection(id);
        else
            SetObjectSpriteIdInput(_objectSpriteId.ToString());
    }

    private void ObjectSpriteListButton_Click(object sender, RoutedEventArgs e)
    {
        _objectSpriteGridMode = false;
        UpdateViewButtons();
        RenderObjectSpritePage();
    }

    private void ObjectSpriteGridButton_Click(object sender, RoutedEventArgs e)
    {
        _objectSpriteGridMode = true;
        UpdateViewButtons();
        RenderObjectSpritePage();
    }

    private void ObjectSpriteFirstButton_Click(object sender, RoutedEventArgs e) => SetObjectSpriteSelection(1);
    private void ObjectSpritePreviousButton_Click(object sender, RoutedEventArgs e) =>
        SetObjectSpriteSelection(_objectSpriteId > (uint)PageSize ? _objectSpriteId - (uint)PageSize : 1, resetMultipleSelection: false);
    private void ObjectSpriteNextButton_Click(object sender, RoutedEventArgs e) =>
        SetObjectSpriteSelection((uint)Math.Min((ulong)_spriteCount, (ulong)_objectSpriteId + (ulong)PageSize), resetMultipleSelection: false);
    private void ObjectSpriteLastButton_Click(object sender, RoutedEventArgs e) => SetObjectSpriteSelection(_spriteCount);

    private void ObjectSpriteIdInput_LostFocus(object sender, RoutedEventArgs e) => CommitObjectSpriteIdInput();

    private void ObjectSpriteIdInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        CommitObjectSpriteIdInput();
        e.Handled = true;
    }

    private void SetPreviewBitmap(BitmapSource? bitmap)
    {
        SetCanvasPreviewBitmap(bitmap);
        SetInfoPreviewBitmap(bitmap);
    }

    private void SetCanvasPreviewBitmap(BitmapSource? bitmap)
    {
        PreviewImage.Source = bitmap;
        CanvasEmptyState.Visibility = bitmap is null ? Visibility.Visible : Visibility.Collapsed;
        ApplyPreviewZoom();
    }

    private void SetInfoPreviewBitmap(BitmapSource? bitmap)
    {
        InfoPreviewImage.Source = bitmap;
        ActualSizeCheckBox.IsEnabled = bitmap is not null;
        InfoPreviewImage.Stretch = ActualSizeCheckBox.IsChecked == true
            ? System.Windows.Media.Stretch.None
            : System.Windows.Media.Stretch.Uniform;
        InfoPreviewEmptyText.Visibility = bitmap is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetExportEnabled(bool enabled)
    {
        ExportButton.IsEnabled = enabled;
        CanvasExportButton.IsEnabled = enabled;
        UpdateSpriteActionToolbar();
    }

    private void UpdateSpriteActionToolbar()
    {
        if (SpriteActionToolbar is null) return;
        var hasSpr = ObjectSpritePanel?.Visibility == Visibility.Visible && _sprStream is not null && _filePath is not null;
        SpriteActionToolbar.Visibility = hasSpr ? Visibility.Visible : Visibility.Collapsed;
        SpriteAssemblyToolbar.Visibility = hasSpr ? Visibility.Visible : Visibility.Collapsed;
        SelectSpritePageButton.IsEnabled = hasSpr && !_sprEditInProgress;
        var selectedCount = _selectedObjectSpriteIds.Count;
        AutoAssembleSpritesButton.Content = selectedCount == 0
            ? Text("Montar novo", "Assemble new")
            : Text($"Montar novo ({selectedCount:N0})", $"Assemble new ({selectedCount:N0})");
        AutoAssembleSpritesButton.IsEnabled = hasSpr && !_sprEditInProgress && _datModel is not null;
        var hasSelectedSprite = hasSpr && _objectSpriteId > 0 && _objectSpriteId <= _spriteCount;
        var hasSelectedBitmap = hasSelectedSprite && _objectSpriteBitmaps.ContainsKey(_objectSpriteId);
        var deletableSelectionCount = _selectedObjectSpriteIds.Count(id => id > 0 && id <= _spriteCount);
        NewSpriteButton.IsEnabled = hasSpr && !_sprEditInProgress;
        ImportSpriteButton.IsEnabled = hasSpr && !_sprEditInProgress;
        ReplaceSpriteButton.IsEnabled = hasSelectedSprite && !_sprEditInProgress;
        ReplaceSpritesAcrossWindowsButton.IsEnabled = hasSpr && deletableSelectionCount > 0 && !_sprEditInProgress;
        DuplicateSpriteButton.IsEnabled = hasSelectedBitmap && !_sprEditInProgress;
        ExportSpriteButton.IsEnabled = hasSelectedBitmap;
        DeleteSpriteButton.IsEnabled = deletableSelectionCount > 0 && !_sprEditInProgress;
        DeleteSpriteButton.ToolTip = deletableSelectionCount > 1
            ? Text($"Excluir {deletableSelectionCount:N0} sprites selecionados", $"Delete {deletableSelectionCount:N0} selected sprites")
            : Text("Excluir o sprite selecionado conforme o comportamento do Object Build",
                "Delete the selected sprite using Object Build behavior");
    }

    private void SetSelectionSummary(string id, string dimensions)
    {
        SelectedIdText.Text = id;
        SelectedDimensionsText.Text = dimensions;
    }

    private void UpdateClientInfo()
    {
        if (ClientVersionValue is null) return;
        ClientVersionValue.Text = _datModel?.VersionName ?? (_filePath is not null ? GetVersionName() : "—");
        DatSignatureValue.Text = _datModel is null ? "—" : _datModel.Signature.ToString("X4");
        SprSignatureValue.Text = _sprStream is null ? "—" : _signature.ToString("X8");
        ItemsCountValue.Text = _datModel is null ? "—" : _datModel.ItemCount.ToString("N0");
        OutfitsCountValue.Text = _datModel is null ? "—" : _datModel.OutfitCount.ToString("N0");
        EffectsCountValue.Text = _datModel is null ? "—" : _datModel.EffectCount.ToString("N0");
        MissilesCountValue.Text = _datModel is null ? "—" : _datModel.MissileCount.ToString("N0");
        SpritesCountValue.Text = _spriteCount == 0 ? "—" : _spriteCount.ToString("N0");
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Text("Abrir arquivo de sprites", "Open sprite file"),
            Filter = "Tibia SPR (*.spr)|*.spr|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (_pendingPixelSprites.Count > 0)
        {
            MessageBox.Show(this,
                Text("Salve tudo antes de abrir outro SPR para preservar os pixels editados.",
                    "Save all before opening another SPR to preserve the edited pixels."),
                Text("Pixels ainda não salvos", "Unsaved pixels"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await LoadSprAsync(dialog.FileName);
    }

    private async void CompileAsButton_Click(object sender, RoutedEventArgs e) => await CompileAssetsAsAsync();

    private async void CompileAsCommand_Executed(object sender, ExecutedRoutedEventArgs e) => await CompileAssetsAsAsync();

    private async Task CompileAssetsAsAsync()
    {
        if (_isPixelEditInProgress || _queuedPixelNudges.Count > 0)
        {
            SetError(Text("Aguarde o movimento dos pixels terminar antes de compilar.", "Wait for the pixel movement to finish before compiling."));
            return;
        }
        if (_pendingPixelSprites.Count > 0)
        {
            try { await FlushPendingPixelSpritesAsync(); }
            catch (Exception ex)
            {
                SetError(Text($"Não foi possível preparar os pixels para compilar: {ex.Message}",
                    $"Could not prepare the pixels for compilation: {ex.Message}"));
                return;
            }
        }
        var sourceModel = _datModel;
        var datSourcePath = _datPath;
        var sprSourcePath = _filePath;
        var sourceOffsets = (uint[])_offsets.Clone();
        var serverFiles = _serverItemFiles;
        if (sourceModel is null || datSourcePath is null || sprSourcePath is null || _sprStream is null || sourceOffsets.Length == 0)
            return;

        try
        {
            var sourcePixelStride = await DetectSpritePixelStrideAsync(_sprStream);
            var inputFolder = Path.GetDirectoryName(datSourcePath) ?? Environment.CurrentDirectory;
            var outputFolder = Path.Combine(inputFolder, "Compiled");
            var dialog = new CompileAssetDialog(Path.GetFileNameWithoutExtension(datSourcePath), outputFolder,
                outputFolder, sourceModel.VersionValue, sourceModel.Extended, sourcePixelStride == 4,
                sourceModel.ImprovedAnimations, sourceModel.FrameGroups,
                serverFiles is not null, serverFiles?.HasXml == true) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Options is not { } options) return;

            var datTargetPath = Path.Combine(options.OutputFolder, options.Name + ".dat");
            var sprTargetPath = Path.Combine(options.OutputFolder, options.Name + ".spr");
            var otfiTargetPath = Path.Combine(options.OutputFolder, options.Name + ".otfi");
            var serverTargetPath = options.ExportServerItems
                ? Path.Combine(options.ServerItemsFolder, options.ServerItemsFormat == "xml" ? "items.xml" : "items.otb")
                : null;
            var targetPaths = new[] { datTargetPath, sprTargetPath, otfiTargetPath, serverTargetPath }
                .Where(path => path is not null).Select(path => Path.GetFullPath(path!)).ToArray();
            if (targetPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targetPaths.Length)
                throw new InvalidDataException("The output DAT, SPR, and server file paths must be different.");
            if (targetPaths.Any(path => string.Equals(path, Path.GetFullPath(datSourcePath), StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, Path.GetFullPath(sprSourcePath), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Choose an output name or folder different from the open DAT and SPR files.");

            var existingTargets = targetPaths.Where(File.Exists).ToArray();
            if (existingTargets.Length > 0 && !ConfirmWithTheme(
                    Text("Substituir arquivos?", "Replace files?"),
                    Text($"Estes arquivos já existem e serão substituídos:\n\n{string.Join("\n", existingTargets)}\n\nDeseja continuar?",
                        $"These files already exist and will be replaced:\n\n{string.Join("\n", existingTargets)}\n\nContinue?")))
                return;

            var serverBytes = options.ExportServerItems
                ? options.ServerItemsFormat == "xml"
                    ? serverFiles?.SerializeXml() ?? throw new InvalidDataException("The loaded server files do not contain items.xml.")
                    : serverFiles?.SerializeOtb() ?? throw new InvalidDataException("No items.otb file is loaded.")
                : null;
            var datTemporaryPath = CreateCompileTemporaryPath(datTargetPath);
            var sprTemporaryPath = CreateCompileTemporaryPath(sprTargetPath);
            var otfiTemporaryPath = CreateCompileTemporaryPath(otfiTargetPath);
            var temporaryPaths = new List<string> { datTemporaryPath, sprTemporaryPath, otfiTemporaryPath };
            string? serverTemporaryPath = serverTargetPath is null ? null : CreateCompileTemporaryPath(serverTargetPath);
            if (serverTemporaryPath is not null) temporaryPaths.Add(serverTemporaryPath);
            CompileAsButton.IsEnabled = false;
            StatusText.Text = Text("Compilando arquivos…", "Compiling assets…");

            try
            {
                Directory.CreateDirectory(options.OutputFolder);
                if (options.ExportServerItems) Directory.CreateDirectory(options.ServerItemsFolder);
                var compileResult = await Task.Run(() => DatCompiler.Prepare(sourceModel, sourceOffsets.Length, new DatCompileOptions(
                    options.TargetVersion, options.Extended, options.Transparency, options.ImprovedAnimations, options.FrameGroups)));
                var datBytes = await Task.Run(() => DatFileWriter.Serialize(compileResult.Model,
                    options.Extended, options.ImprovedAnimations, options.FrameGroups));
                await File.WriteAllBytesAsync(datTemporaryPath, datBytes);

                await Task.Run(() => SprFileCompiler.Compile(sprSourcePath, sourceOffsets,
                    compileResult.SourceSpriteIds, sourcePixelStride, options.TargetVersion.SprSignature,
                    options.Extended, options.Transparency, sprTemporaryPath));

                if (serverTemporaryPath is not null && serverBytes is not null)
                    await File.WriteAllBytesAsync(serverTemporaryPath, serverBytes);

                var otfiContent = string.Join(Environment.NewLine,
                    "DatSpr",
                    $"  extended: {options.Extended.ToString().ToLowerInvariant()}",
                    $"  transparency: {options.Transparency.ToString().ToLowerInvariant()}",
                    $"  frame-durations: {options.ImprovedAnimations.ToString().ToLowerInvariant()}",
                    $"  frame-groups: {options.FrameGroups.ToString().ToLowerInvariant()}",
                    "  metadata-controller: default",
                    "  attribute-server: tfs1.4") + Environment.NewLine;
                await File.WriteAllTextAsync(otfiTemporaryPath, otfiContent);

                _ = await Task.Run(() => DatFileParser.Parse(datTemporaryPath, options.TargetVersion.Value,
                    options.Extended, options.ImprovedAnimations, options.FrameGroups));

                File.Move(datTemporaryPath, datTargetPath, overwrite: true);
                File.Move(sprTemporaryPath, sprTargetPath, overwrite: true);
                File.Move(otfiTemporaryPath, otfiTargetPath, overwrite: true);
                if (serverTemporaryPath is not null && serverTargetPath is not null)
                    File.Move(serverTemporaryPath, serverTargetPath, overwrite: true);

                StatusText.Text = Text($"Compilado para Tibia {options.TargetVersion.Name}", $"Compiled for Tibia {options.TargetVersion.Name}");
                SetError(null);
            }
            finally
            {
                foreach (var temporaryPath in temporaryPaths)
                {
                    try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                    catch (IOException) { }
                }
                UpdateMetadataEditingState();
            }
        }
        catch (Exception ex)
        {
            SetError(Text($"Falha ao compilar os arquivos: {ex.Message}", $"Asset compilation failed: {ex.Message}"));
            MessageBox.Show(this, ex.Message, Text("Falha ao compilar", "Compilation failed"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string CreateCompileTemporaryPath(string targetPath) => Path.Combine(
        Path.GetDirectoryName(targetPath) ?? Environment.CurrentDirectory,
        $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

    private void SlicerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_slicerWindow is { IsVisible: true })
        {
            _slicerWindow.Activate();
            return;
        }

        _slicerWindow = new SlicerWindow(IsEnglish, () => _sprStream is not null && _filePath is not null, ImportSlicedSpritesAsync) { Owner = this };
        _slicerWindow.Closed += (_, _) => _slicerWindow = null;
        _slicerWindow.Show();
    }

    private async Task ImportSlicedSpritesAsync(IReadOnlyList<BitmapSource> sprites)
    {
        if (_sprStream is null || _filePath is null)
            throw new InvalidOperationException(Text("Abra um SPR antes de importar.", "Open an SPR before importing."));
        if (sprites.Count == 0 || sprites.Any(sprite => sprite.PixelWidth != SpriteSide || sprite.PixelHeight != SpriteSide))
            throw new InvalidDataException(Text("Todos os sprites importados precisam ter 32 × 32 pixels.", "Every imported sprite must be 32 × 32 pixels."));

        var imported = sprites.ToArray();
        var firstNewId = await SaveSprChangesAsync(imported);
        if (firstNewId is uint id)
            MessageBox.Show(this,
                Text($"{imported.Length} sprites adicionados. IDs novos: {id:N0}–{_spriteCount:N0}.",
                    $"{imported.Length} sprites added. New IDs: {id:N0}–{_spriteCount:N0}."),
                Text("Importação concluída", "Import complete"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task FlushPendingPixelSpritesAsync()
    {
        if (_pendingPixelSprites.Count == 0) return;
        if (_sprStream is null || _filePath is null)
            throw new InvalidOperationException(Text("Abra o SPR original antes de salvar os pixels editados.",
                "Open the original SPR before saving the edited pixels."));

        var pending = _pendingPixelSprites.ToArray();
        var expectedFirstId = checked(_spriteCount + 1);
        for (var index = 0; index < pending.Length; index++)
            if (pending[index].Key != checked(expectedFirstId + (uint)index))
                throw new InvalidOperationException(Text("A sequência de sprites editados está inconsistente. Reabra o cliente antes de salvar.",
                    "The edited sprite sequence is inconsistent. Reopen the client files before saving."));

        var firstAppendedId = await SaveSprChangesCoreAsync(pending.Length,
            () => pending.Select(entry => entry.Value), refreshUiAfterSave: false);
        if (firstAppendedId != expectedFirstId)
            throw new InvalidOperationException(Text("Os IDs dos sprites mudaram durante a edição. Reabra o cliente antes de salvar.",
                "Sprite IDs changed during the edit. Reopen the client before saving."));

        _pendingPixelSprites.Clear();
        _selectedThingSpriteBitmaps.Clear();
        _pageBitmaps.Clear();
    }

    private async Task<uint?> SaveSprChangesAsync(IReadOnlyList<BitmapSource> appendSprites,
        IReadOnlyDictionary<uint, BitmapSource>? replacements = null, IReadOnlySet<uint>? deletions = null,
        bool refreshUiAfterSave = true)
    {
        if (appendSprites.Any(sprite => sprite.PixelWidth != SpriteSide || sprite.PixelHeight != SpriteSide)
            || replacements?.Values.Any(sprite => sprite.PixelWidth != SpriteSide || sprite.PixelHeight != SpriteSide) == true)
            throw new InvalidDataException(Text("Todos os sprites precisam ter 32 × 32 pixels.", "All sprites must be 32 × 32 pixels."));
        await FlushPendingPixelSpritesAsync();
        return await SaveSprChangesCoreAsync(appendSprites.Count, () => appendSprites, replacements, deletions, refreshUiAfterSave);
    }

    private async Task<uint?> SaveSprChangesFromFactoryAsync(int appendSpriteCount, Func<IEnumerable<BitmapSource>> spriteFactory)
    {
        if (appendSpriteCount < 0) throw new ArgumentOutOfRangeException(nameof(appendSpriteCount));
        await FlushPendingPixelSpritesAsync();
        return await SaveSprChangesCoreAsync(appendSpriteCount, spriteFactory);
    }

    private async Task<uint?> SaveSprChangesCoreAsync(int appendSpriteCount, Func<IEnumerable<BitmapSource>> spriteFactory,
        IReadOnlyDictionary<uint, BitmapSource>? replacements = null, IReadOnlySet<uint>? deletions = null,
        bool refreshUiAfterSave = true)
    {
        if (_sprStream is null || _filePath is null)
            throw new InvalidOperationException(Text("Abra um SPR antes de editar sprites.", "Open an SPR before editing sprites."));
        if (_sprEditInProgress)
            throw new InvalidOperationException(Text("Já existe uma alteração do SPR em andamento.", "An SPR update is already in progress."));
        if (appendSpriteCount < 0) throw new ArgumentOutOfRangeException(nameof(appendSpriteCount));
        if (replacements?.Values.Any(sprite => sprite.PixelWidth != SpriteSide || sprite.PixelHeight != SpriteSide) == true)
            throw new InvalidDataException(Text("Todos os sprites precisam ter 32 × 32 pixels.", "All sprites must be 32 × 32 pixels."));

        var sourceStream = _sprStream;
        var sourcePath = _filePath;
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var oldCount = _spriteCount;
        var offsets = (uint[])_offsets.Clone();
        var signature = _signature;
        var headerSize = _sprHeaderSize;
        var deleteOnly = appendSpriteCount == 0 && replacements is not { Count: > 0 }
            && deletions is { Count: > 0 };
        var tempPath = Path.Combine(Path.GetDirectoryName(sourceFullPath)!, $".{Path.GetFileName(sourceFullPath)}.{Guid.NewGuid():N}.tmp");
        _sprEditInProgress = true;
        UpdateSpriteActionToolbar();
        try
        {
            if (!ReferenceEquals(sourceStream, _sprStream) || !string.Equals(sourcePath, _filePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Text("O SPR aberto mudou durante a edição. Abra novamente e tente outra vez.", "The open SPR changed during the edit. Reopen it and try again."));

            if (deleteOnly)
            {
                // Deleting entries only changes the index. Keep the sprite payload in place
                // and patch the selected offsets/count directly instead of rebuilding every
                // sprite block in a large SPR file.
                sourceStream.Dispose();
                _sprStream = null;
                try
                {
                    await Task.Run(() => DeleteSprEntriesInPlace(sourceFullPath, signature, offsets,
                        headerSize, deletions!));
                }
                catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
                {
                    throw new IOException(Text(
                        "O arquivo SPR está aberto por outro programa. Feche o outro editor e tente novamente.",
                        "The SPR file is open in another program. Close the other editor and try again."), ex);
                }
                await ReloadSprIndexWithoutUiAsync(sourceFullPath);
            }
            else
            {
                var stride = await DetectSpritePixelStrideAsync(sourceStream);
                await Task.Run(() => WriteSprWithAppendedSprites(sourcePath, tempPath, spriteFactory(), appendSpriteCount,
                    stride, signature, offsets, headerSize, replacements, deletions));

                sourceStream.Dispose();
                _sprStream = null;
                try
                {
                    File.Replace(tempPath, sourceFullPath, null);
                }
                catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
                {
                    throw new IOException(Text(
                        "O arquivo SPR está aberto por outro programa. Feche o outro editor e tente importar novamente.",
                        "The SPR file is open in another program. Close the other editor and try importing again."), ex);
                }
                if (refreshUiAfterSave)
                    await LoadSprAsync(sourceFullPath);
                else
                    await ReloadSprIndexWithoutUiAsync(sourceFullPath);
            }
            if (_sprStream is null) throw new InvalidDataException(Text("O SPR atualizado não pôde ser reaberto.", "The updated SPR could not be reopened."));

            _slicerWindow?.RefreshImportAvailability();
            uint? selectedAfterUpdate = appendSpriteCount > 0 ? checked(oldCount + 1)
                : replacements?.Keys.FirstOrDefault() ?? deletions?.Min();
            if (refreshUiAfterSave && selectedAfterUpdate is uint selectedId && selectedId > 0 && selectedId <= _spriteCount)
            {
                if (ObjectSpritePanel.Visibility == Visibility.Visible)
                {
                    SetObjectSpriteSelection(selectedId);
                }
                else
                {
                    ClearSearchForNavigation();
                    _page = (int)((selectedId - 1) / PageSize);
                    RenderPage();
                    SelectSprite(selectedId);
                    ScrollSearchResultIntoView(selectedId);
                }
            }
            if (!refreshUiAfterSave)
            {
                SpriteCountText.Text = $"{_spriteCount:N0} {Text("sprites", "sprites")}";
                VersionText.Text = GetVersionName();
                UpdateClientInfo();
                UpdateMetadataEditingState();
            }
            SetError(null);
            return selectedAfterUpdate;
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (_sprStream is null && File.Exists(sourceFullPath))
                await LoadSprAsync(sourceFullPath);
            throw;
        }
        finally
        {
            _sprEditInProgress = false;
            UpdateSpriteActionToolbar();
        }
    }

    private async Task ReloadSprIndexWithoutUiAsync(string path)
    {
        var newStream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess);
        try
        {
            var prefix = new byte[136];
            var prefixRead = await RandomAccess.ReadAsync(newStream.SafeFileHandle, prefix, 0);
            if (prefixRead < 8)
                throw new InvalidDataException(Text("Cabeçalho SPR inválido.", "Invalid SPR header."));

            var signature = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
            var smallCount = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(4));
            var largeCount = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(4));
            var format = ChooseFormat(newStream.Length, prefix.AsSpan(0, prefixRead), smallCount, largeCount);
            if (format.Count == 0 || format.Count > int.MaxValue / 4)
                throw new InvalidDataException(Text("Não foi possível interpretar a tabela de sprites.", "Could not read the sprite index."));

            var table = new byte[checked((int)format.Count * 4)];
            await ReadExactlyAsync(newStream.SafeFileHandle, table, format.HeaderSize);
            var offsets = new uint[format.Count];
            for (var index = 0; index < offsets.Length; index++)
                offsets[index] = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(index * 4, 4));

            _sprStream?.Dispose();
            _sprStream = newStream;
            newStream = null!;
            _filePath = path;
            _signature = signature;
            _spriteCount = format.Count;
            _sprHeaderSize = format.HeaderSize;
            _offsets = offsets;
            StatusDot.Fill = ThemeBrush("SuccessBrush");
            StatusText.Text = GetVersionName();
            UpdateEmptyState();
        }
        finally
        {
            newStream?.Dispose();
        }
    }

    private async void NewSpriteButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSpriteOperationAsync(async () =>
        {
            var id = await SaveSprChangesAsync([CreateTransparentBitmap()]);
            if (id is uint spriteId) ShowSpriteOperationComplete(Text($"Sprite vazio #{spriteId:N0} criado.", $"Blank sprite #{spriteId:N0} created."));
        });
    }

    private async void ReplaceSpriteButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedId = _objectSpriteId;
        if (selectedId == 0 || selectedId > _spriteCount) return;
        var dialog = new OpenFileDialog
        {
            Title = Text("Escolher imagem para substituir o sprite", "Choose an image to replace the sprite"),
            Filter = "Imagens PNG ou BMP|*.png;*.bmp|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        await RunSpriteOperationAsync(async () =>
        {
            var bitmap = await Task.Run(() => LoadSpriteImage(dialog.FileName));
            ValidateSpriteImage(bitmap, Path.GetFileName(dialog.FileName));
            await SaveSprChangesAsync([], new Dictionary<uint, BitmapSource> { [selectedId] = bitmap });
            ShowSpriteOperationComplete(Text($"Sprite #{selectedId:N0} substituído.", $"Sprite #{selectedId:N0} replaced."));
        });
    }

    private async void ReplaceSpritesAcrossWindowsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sprStream is null || _sprEditInProgress) return;
        var sourceIds = _selectedObjectSpriteIds.Where(id => id > 0 && id <= _spriteCount).Order().ToArray();
        if (sourceIds.Length == 0) return;
        var targets = OpenEditorWindows
            .Where(window => window != this && window._sprStream is not null && window._filePath is not null
                && !window._sprEditInProgress && window.IsEnabled)
            .Select(window => new DatTransferTargetOption(window,
                $"{window.Title} — {window._filePath}"))
            .ToArray();
        if (targets.Length == 0)
        {
            MessageBox.Show(this, Text("Abra um SPR em outra janela e selecione nela os sprites que serão substituídos.",
                "Open an SPR in another window and select the sprites to replace there."),
                Text("Destino indisponível", "Destination unavailable"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MainWindow? destination = targets[0].Window;
        if (targets.Length > 1)
        {
            var picker = new DatTransferTargetDialog(targets, IsEnglish, replaceSprites: true) { Owner = this };
            if (picker.ShowDialog() != true) return;
            destination = picker.SelectedTarget;
        }
        if (destination is null || !OpenEditorWindows.Contains(destination) || destination._sprStream is null
            || destination._sprEditInProgress || !destination.IsEnabled) return;
        var destinationIds = destination._selectedObjectSpriteIds
            .Where(id => id > 0 && id <= destination._spriteCount).Order().ToArray();
        if (sourceIds.Length != destinationIds.Length)
        {
            MessageBox.Show(this, Text(
                $"Selecione a mesma quantidade de sprites nas duas janelas. Origem: {sourceIds.Length:N0}; destino: {destinationIds.Length:N0}. Use Ctrl ou Shift para marcar vários sprites, ou Selecionar página.",
                $"Select the same number of sprites in both windows. Source: {sourceIds.Length:N0}; destination: {destinationIds.Length:N0}. Use Ctrl or Shift to select multiple sprites, or Select page."),
                Text("Seleções diferentes", "Selection counts differ"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var sourceStream = _sprStream;
        var destinationStream = destination._sprStream;
        var pairs = string.Join("\n", sourceIds.Zip(destinationIds).Take(8)
            .Select(pair => $"#{pair.First} → #{pair.Second}"));
        if (sourceIds.Length > 8) pairs += "\n…";
        if (!ConfirmWithTheme(Text("Substituir sprites em outra janela", "Replace sprites in another window"),
            Text($"Substituir {sourceIds.Length:N0} sprite(s) em {destination._filePath}?\n\n{pairs}\n\nA ordem é crescente por ID. Os IDs do destino serão mantidos e os objetos que os utilizam passarão a exibir as novas imagens. O SPR de destino será gravado.",
                $"Replace {sourceIds.Length:N0} sprite(s) in {destination._filePath}?\n\n{pairs}\n\nPairs follow ascending ID order. Destination IDs will be preserved and objects using them will display the new images. The destination SPR will be saved."), defaultNo: true)) return;
        if (!OpenEditorWindows.Contains(destination) || _sprStream != sourceStream
            || destination._sprStream != destinationStream || _sprEditInProgress || destination._sprEditInProgress
            || !destination.IsEnabled) return;

        IsEnabled = false;
        destination.IsEnabled = false;
        try
        {
            var replacements = new Dictionary<uint, BitmapSource>(sourceIds.Length);
            for (var index = 0; index < sourceIds.Length; index++)
            {
                StatusText.Text = Text($"Preparando sprite {index + 1:N0}/{sourceIds.Length:N0}…",
                    $"Preparing sprite {index + 1:N0}/{sourceIds.Length:N0}…");
                var offset = _offsets[sourceIds[index] - 1];
                if (offset != 0 && offset + 5L > RandomAccess.GetLength(sourceStream.SafeFileHandle))
                    throw new InvalidDataException(Text($"O sprite #{sourceIds[index]} está incompleto.",
                        $"Sprite #{sourceIds[index]} is truncated."));
                var bitmap = await ReadSpriteBitmapAsync(sourceIds[index], sourceStream.SafeFileHandle)
                    ?? throw new InvalidDataException(Text($"Não foi possível ler o sprite #{sourceIds[index]}.",
                        $"Could not read sprite #{sourceIds[index]}."));
                replacements.Add(destinationIds[index], bitmap);
            }
            await destination.SaveSprChangesAsync([], replacements);
            StatusText.Text = Text($"{sourceIds.Length:N0} sprite(s) substituído(s) na outra janela.",
                $"{sourceIds.Length:N0} sprite(s) replaced in the other window.");
            destination.IsEnabled = true;
            if (destination.WindowState == WindowState.Minimized) destination.WindowState = WindowState.Normal;
            destination.Activate();
            destination.ShowSpriteOperationComplete(destination.Text(
                $"{sourceIds.Length:N0} sprite(s) substituído(s), mantendo os IDs do destino.",
                $"{sourceIds.Length:N0} sprite(s) replaced, preserving destination IDs."));
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            MessageBox.Show(this, ex.Message, Text("Falha ao substituir sprites", "Sprite replacement failed"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsEnabled = true;
            destination.IsEnabled = true;
        }
    }

    private async void DuplicateSpriteButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedId = _objectSpriteId;
        if (!_objectSpriteBitmaps.TryGetValue(selectedId, out var bitmap)) return;
        await RunSpriteOperationAsync(async () =>
        {
            var id = await SaveSprChangesAsync([bitmap]);
            if (id is uint spriteId) ShowSpriteOperationComplete(Text($"Sprite #{selectedId:N0} duplicado como #{spriteId:N0}.", $"Sprite #{selectedId:N0} duplicated as #{spriteId:N0}."));
        });
    }

    private async void ImportSpriteButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Text("Importar sprites", "Import sprites"),
            Filter = "Imagens PNG ou BMP|*.png;*.bmp|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true || dialog.FileNames.Length == 0) return;
        await RunSpriteOperationAsync(async () =>
        {
            var images = await Task.Run(() => dialog.FileNames.Select(LoadSpriteImage).ToArray());
            for (var index = 0; index < images.Length; index++) ValidateSpriteImage(images[index], Path.GetFileName(dialog.FileNames[index]));
            var id = await SaveSprChangesAsync(images);
            if (id is uint firstId)
                ShowSpriteOperationComplete(Text($"{images.Length} sprite(s) importado(s): IDs {firstId:N0}–{_spriteCount:N0}.", $"{images.Length} sprite(s) imported: IDs {firstId:N0}–{_spriteCount:N0}."));
        });
    }

    private void ExportSpriteButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedId = _objectSpriteId;
        if (_objectSpriteBitmaps.TryGetValue(selectedId, out var bitmap))
            ExportSpriteAsPng(selectedId, bitmap);
    }

    private async void DeleteSpriteButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedIds = _selectedObjectSpriteIds
            .Where(id => id > 0 && id <= _spriteCount)
            .Order()
            .ToArray();
        if (selectedIds.Length == 0) return;

        var deletionSet = selectedIds.ToHashSet();
        var newCount = _spriteCount;
        while (newCount > 1 && deletionSet.Contains(newCount)) newCount--;
        var removedFromList = _spriteCount - newCount;
        var leftAsEmpty = selectedIds.Length - removedFromList;
        if (!ConfirmWithTheme(Text("Excluir sprites", "Delete sprites"),
                Text(
                    $"Excluir {selectedIds.Length:N0} sprites selecionados? {removedFromList:N0} IDs no final serão removidos da lista e {leftAsEmpty:N0} IDs do meio ficarão vazios para preservar as referências do DAT, como no Object Build.",
                    $"Delete {selectedIds.Length:N0} selected sprites? {removedFromList:N0} IDs at the end will be removed from the list and {leftAsEmpty:N0} IDs in the middle will remain empty to preserve DAT references, as in Object Build."),
                defaultNo: true)) return;
        await RunSpriteOperationAsync(async () =>
        {
            await SaveSprChangesAsync([], deletions: deletionSet, refreshUiAfterSave: false);
            _selectedThingSpriteBitmaps.Clear();
            _selectedObjectSpriteIds.Clear();
            if (_spriteCount > 0)
            {
                _objectSpriteId = Math.Min(selectedIds[0], _spriteCount);
                _selectedObjectSpriteIds.Add(_objectSpriteId);
                _objectSpriteSelectionAnchorId = _objectSpriteId;
            }
            RenderObjectSpritePage();
            if (_selectedThing is not null) _ = LoadSelectedThingPreviewAsync();
            ShowSpriteOperationStatus(Text(
                $"{selectedIds.Length:N0} sprites processados: {removedFromList:N0} removidos da lista, {leftAsEmpty:N0} IDs mantidos vazios. Total: {_spriteCount:N0}.",
                $"{selectedIds.Length:N0} sprites processed: {removedFromList:N0} removed from the list, {leftAsEmpty:N0} IDs kept empty. Total: {_spriteCount:N0}."));
        });
    }

    private void ShowSpriteOperationStatus(string message)
    {
        ObjectSpriteRangeText.Text = message;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            UpdateObjectSpriteRangeText();
        };
        timer.Start();
    }

    private static BitmapSource LoadSpriteImage(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void ValidateSpriteImage(BitmapSource bitmap, string fileName)
    {
        if (bitmap.PixelWidth != SpriteSide || bitmap.PixelHeight != SpriteSide)
            throw new InvalidDataException(Text($"A imagem {fileName} mede {bitmap.PixelWidth} × {bitmap.PixelHeight}; sprites precisam medir 32 × 32 pixels.",
                $"Image {fileName} is {bitmap.PixelWidth} × {bitmap.PixelHeight}; sprites must be 32 × 32 pixels."));
    }

    private async Task RunSpriteOperationAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception ex)
        {
            SetError(ex.Message);
            MessageBox.Show(this, ex.Message, Text("Falha ao editar SPR", "SPR edit failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowSpriteOperationComplete(string message)
    {
        MessageBox.Show(this, message, Text("SPR atualizado", "SPR updated"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void OpenDatButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Text("Abrir arquivo de objetos", "Open object file"),
            Filter = "Tibia DAT (*.dat)|*.dat|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!ConfirmDiscardUnsavedMetadata()) return;
        await LoadDatAsync(dialog.FileName);
    }

    private async void OpenServerItemsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Text("Abrir items.otb (o items.xml na mesma pasta)", "Open items.otb (and items.xml from the same folder)"),
            Filter = "Items OTB (*.otb)|*.otb|Todos os arquivos (*.*)|*.*",
            FileName = "items.otb",
            InitialDirectory = _datPath is null ? null : Path.GetDirectoryName(_datPath),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!ConfirmDiscardServerItemChanges()) return;
        try
        {
            await LoadServerItemsAsync(dialog.FileName);
            SetError(null);
        }
        catch (Exception ex)
        {
            SetError(Text($"Não foi possível abrir items.otb/items.xml: {ex.Message}", $"Could not open items.otb/items.xml: {ex.Message}"));
            MessageBox.Show(this, ex.Message, Text("Falha ao abrir dados do servidor", "Could not open server item files"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ImportFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_datModel is null || _datPath is null || _sprStream is null || _filePath is null)
        {
            MessageBox.Show(this,
                Text("Abra o DAT e o SPR de destino antes de importar os objetos OBD.",
                    "Open the destination DAT and SPR before importing OBD objects."),
                Text("DAT e SPR necessários", "DAT and SPR required"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_sprEditInProgress)
        {
            SetError(Text("Aguarde a edição atual do SPR terminar.", "Wait for the current SPR edit to finish."));
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = Text("Selecionar pasta com objetos OBD exportados", "Select a folder with exported OBD objects"),
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        string[] obdPaths;
        try
        {
            obdPaths = Directory.EnumerateFiles(dialog.FolderName, "*", SearchOption.AllDirectories)
                .Where(path => string.Equals(Path.GetExtension(path), ".obd", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex)
        {
            SetError(Text($"Não foi possível ler a pasta: {ex.Message}", $"Could not read the folder: {ex.Message}"));
            return;
        }
        if (obdPaths.Length == 0)
        {
            MessageBox.Show(this,
                Text("Não encontrei arquivos .obd nessa pasta. O DAT, SPR e items.otb abertos não foram alterados.",
                    "No .obd files were found in this folder. The open DAT, SPR and items.otb were not changed."),
                Text("Nenhum OBD encontrado", "No OBD files found"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ImportFolderButton.IsEnabled = false;
        try
        {
            await ImportObdFolderAsync(obdPaths);
            SetError(null);
        }
        catch (Exception ex)
        {
            SetError(Text($"Não foi possível importar a pasta OBD: {ex.Message}", $"Could not import the OBD folder: {ex.Message}"));
            MessageBox.Show(this, ex.Message, Text("Falha ao importar objetos OBD", "Could not import OBD objects"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ImportFolderButton.IsEnabled = _datModel is not null && _sprStream is not null;
        }
    }

    private async Task ImportObdFolderAsync(IReadOnlyList<string> paths, IReadOnlyList<DatThing>? replacementTargets = null)
    {
        var model = _datModel ?? throw new InvalidOperationException(Text("Abra um DAT primeiro.", "Open a DAT file first."));
        if (_sprStream is null || _filePath is null)
            throw new InvalidOperationException(Text("Abra também o SPR de destino.", "Open the destination SPR too."));

        var records = new List<(string Path, DatThing Thing, uint[] SpriteIds)>(paths.Count);
        if (replacementTargets is not null && replacementTargets.Count != paths.Count)
            throw new InvalidOperationException(Text("A quantidade de objetos de destino mudou.", "The destination object count changed."));
        for (var index = 0; index < paths.Count; index++)
        {
            if (index % 20 == 0)
                StatusText.Text = Text($"Analisando OBDs {index + 1:N0}/{paths.Count:N0}…", $"Scanning OBD files {index + 1:N0}/{paths.Count:N0}…");
            var decoded = await Task.Run(() => ObdFileReader.Read(paths[index], model.VersionValue));
            if (replacementTargets is not null && (decoded.Thing.Category != replacementTargets[index].Category
                || !model.Things.Any(thing => thing.Category == replacementTargets[index].Category && thing.Id == replacementTargets[index].Id)))
                throw new InvalidDataException(Text("Um objeto de destino não existe ou tem categoria incompatível.", "A destination object is missing or has an incompatible category."));
            var spriteIds = decoded.SpritePixels.Keys.Where(id => id != 0).OrderBy(id => id).ToArray();
            var spriteIdSet = spriteIds.ToHashSet();
            foreach (var frame in decoded.Thing.FrameGroups.SelectMany(group => group.Frames))
                if (frame.SpriteIds.Any(id => id != 0 && !spriteIdSet.Contains(id)))
                    throw new InvalidDataException(Text($"O arquivo {Path.GetFileName(paths[index])} referencia um sprite sem pixels.",
                        $"File {Path.GetFileName(paths[index])} references a sprite with no pixel data."));
            _ = ValidateDatThingExchange(decoded.Thing, model, decoded.Thing.Category, uint.MaxValue);
            records.Add((paths[index], decoded.Thing, spriteIds));
        }

        long spriteTotalLong = records.Sum(record => (long)record.SpriteIds.Length);
        if (spriteTotalLong > int.MaxValue || (ulong)_spriteCount + (ulong)spriteTotalLong > uint.MaxValue)
            throw new InvalidDataException(Text("A pasta excede a quantidade de sprites que o SPR suporta.",
                "The folder exceeds the number of sprites supported by the SPR format."));
        var spriteTotal = (int)spriteTotalLong;
        var nextSpriteId = (ulong)_spriteCount + 1;
        var spriteOffset = 0UL;
        var nextIds = new Dictionary<string, uint>(StringComparer.Ordinal);
        var usedIds = model.Things.Select(thing => (thing.Category, thing.Id)).ToHashSet();
        var additions = new List<DatThing>(records.Count);
        var updatedCounts = model;
        var maximumSpriteId = checked(_spriteCount + (uint)spriteTotal);

        foreach (var record in records)
        {
            var category = record.Thing.Category;
            uint newId;
            if (replacementTargets is not null)
                newId = replacementTargets[additions.Count].Id;
            else if (!nextIds.TryGetValue(category, out newId))
                newId = GetNextDatThingId(model, category);
            if (newId > DatFileParser.MaximumThingCountPerCategory)
                throw new InvalidOperationException(Text($"A categoria {CategoryName(category)} não tem mais IDs livres no DAT.",
                    $"Category {CategoryName(category)} has no free IDs left in the DAT."));
            nextIds[category] = newId + 1;

            var spriteMap = new Dictionary<uint, uint>(record.SpriteIds.Length);
            for (var i = 0; i < record.SpriteIds.Length; i++)
                spriteMap.Add(record.SpriteIds[i], checked((uint)(nextSpriteId + spriteOffset + (uint)i)));
            spriteOffset = checked(spriteOffset + (uint)record.SpriteIds.Length);
            var remapped = CloneDatThing(record.Thing, newId, category) with
            {
                FrameGroups = record.Thing.FrameGroups.Select(group => group with
                {
                    Frames = group.Frames.Select(frame => frame with
                    {
                        SpriteIds = frame.SpriteIds.Select(id => id == 0 ? 0u : spriteMap[id]).ToArray()
                    }).ToArray()
                }).ToArray()
            };
            if (replacementTargets is null && !usedIds.Add((category, newId)))
                throw new InvalidOperationException(Text("Um dos novos IDs já está em uso no DAT.", "One of the new IDs is already used in the DAT."));
            _ = ValidateDatThingExchange(remapped, model, category, maximumSpriteId);
            additions.Add(remapped);
            if (replacementTargets is null) updatedCounts = WithDatCategoryCount(updatedCounts, category, newId);
        }

        var replacementMap = additions.ToDictionary(thing => (thing.Category, thing.Id));
        var candidate = updatedCounts with { Things = OrderDatThings(replacementTargets is null
            ? model.Things.Concat(additions)
            : model.Things.Select(thing => replacementMap.GetValueOrDefault((thing.Category, thing.Id), thing))) };
        _ = DatFileWriter.Serialize(candidate);
        if (replacementTargets is null) _serverItemFiles?.ValidateCanAddFromDat(additions, candidate.VersionValue);

        if (spriteTotal > 0)
        {
            StatusText.Text = Text($"Adicionando {spriteTotal:N0} sprites ao SPR…", $"Adding {spriteTotal:N0} sprites to the SPR…");
            await SaveSprChangesFromFactoryAsync(spriteTotal, () => EnumerateFolderObdSprites(records, model.VersionValue));
        }

        var itemCountBeforeOtbImport = _serverItemFiles?.ItemCount ?? 0;
        if (replacementTargets is null) ApplyDatThingAdditions(additions);
        else ApplyDatThingBatchChanges(additions, additions[^1]);
        UpdateItemAttributeEditor();
        UpdateMetadataEditingState();
        var importedItemCount = _serverItemFiles is null ? 0 : _serverItemFiles.ItemCount - itemCountBeforeOtbImport;
        var otbSuffix = _serverItemFiles is null ? string.Empty
            : Text($" {importedItemCount:N0} item(ns) adicionados ao items.otb.", $" {importedItemCount:N0} item(s) added to items.otb.");
        StatusText.Text = Text($"{additions.Count:N0} objeto(s) importado(s); {spriteTotal:N0} sprite(s) adicionados.{otbSuffix} Salve tudo para gravar.",
            $"Imported {additions.Count:N0} object(s); added {spriteTotal:N0} sprite(s).{otbSuffix} Save all to write the changes.");
        if (replacementTargets is not null)
            StatusText.Text = Text($"{additions.Count:N0} objeto(s) substituído(s), mantendo os IDs DAT. Salve tudo para gravar.",
                $"{additions.Count:N0} object(s) replaced, preserving DAT IDs. Save all to write the changes.");
    }

    private static IEnumerable<BitmapSource> EnumerateFolderObdSprites(
        IReadOnlyList<(string Path, DatThing Thing, uint[] SpriteIds)> records, int clientVersion)
    {
        foreach (var record in records)
        {
            var decoded = ObdFileReader.Read(record.Path, clientVersion);
            var currentIds = decoded.SpritePixels.Keys.Where(id => id != 0).OrderBy(id => id).ToArray();
            if (!currentIds.SequenceEqual(record.SpriteIds))
                throw new InvalidDataException($"O arquivo {Path.GetFileName(record.Path)} mudou durante a importação.");
            foreach (var id in currentIds)
            {
                var bitmap = ToBitmap(ConvertObdArgbToBgra(decoded.SpritePixels[id]));
                if (bitmap.PixelWidth != SpriteSide || bitmap.PixelHeight != SpriteSide)
                    throw new InvalidDataException($"O sprite {id} em {Path.GetFileName(record.Path)} tem dimensões inválidas.");
                yield return bitmap;
            }
        }
    }

    private bool ConfirmDiscardServerItemChanges() => _serverItemFiles?.IsDirty != true
        || ConfirmWithTheme(Text("Alterações não salvas", "Unsaved changes"),
            Text("Os arquivos OTB/XML atuais têm alterações não salvas. Deseja descartá-las e carregar outros arquivos?",
                "The current OTB/XML files have unsaved changes. Discard them and load other files?"));

    private async Task LoadServerItemsAsync(string otbPath)
    {
        var folder = Path.GetDirectoryName(otbPath) ?? Environment.CurrentDirectory;
        var xmlPath = Directory.EnumerateFiles(folder, "*.xml", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetFileName(path).Equals("items.xml", StringComparison.OrdinalIgnoreCase));
        var serverItems = await Task.Run(() => ServerItemFiles.Load(otbPath, xmlPath));
        _serverItemFiles = serverItems;
        UpdateOtbStatusIndicator();
        MetadataSaveStatus.Text = _metadataHasUnsavedChanges
            ? Text("Alterações não salvas", "Unsaved changes") : string.Empty;
        UpdateItemAttributeEditor();
        UpdateMetadataEditingState();
    }

    private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = Text("Importar pasta com os arquivos do cliente ou items.otb", "Import a folder with client assets or items.otb"),
                Multiselect = false
            };
            if (dialog.ShowDialog(this) == true && ConfirmDiscardUnsavedMetadata())
                await LoadClientFolderAsync(dialog.FolderName);
        }
        catch (Exception ex)
        {
            SetError(Text($"Não foi possível abrir a pasta: {ex.Message}", $"Could not open the folder: {ex.Message}"));
        }
    }

    private async Task LoadClientFolderAsync(string folderPath)
    {
        SetError(null);
        try
        {
            var datFiles = Directory.EnumerateFiles(folderPath, "*.dat", SearchOption.TopDirectoryOnly).ToArray();
            var sprFiles = Directory.EnumerateFiles(folderPath, "*.spr", SearchOption.TopDirectoryOnly).ToArray();
            var otbPath = Directory.EnumerateFiles(folderPath, "*.otb", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => Path.GetFileName(path).Equals("items.otb", StringComparison.OrdinalIgnoreCase));
            if (datFiles.Length == 0 && sprFiles.Length == 0)
            {
                if (otbPath is not null)
                {
                    await LoadServerItemsAsync(otbPath);
                    StatusText.Text = Text("items.otb carregado", "items.otb loaded");
                    SetError(null);
                    return;
                }
                SetError(Text("Não encontrei DAT, SPR ou items.otb nessa pasta.", "I couldn't find DAT, SPR, or items.otb in this folder."));
                return;
            }
            if (datFiles.Length == 0 || sprFiles.Length == 0)
            {
                if (otbPath is not null) await LoadServerItemsAsync(otbPath);
                SetError(Text("A pasta precisa conter DAT e SPR compatíveis para carregar os arquivos do cliente. O items.otb foi carregado, se estava presente.",
                    "The folder needs matching DAT and SPR files to load client assets. items.otb was loaded if present."));
                return;
            }

            string datPath;
            string sprPath;
            if (datFiles.Length == 1 && sprFiles.Length == 1)
            {
                datPath = datFiles[0];
                sprPath = sprFiles[0];
            }
            else
            {
                var pairs = new List<(string Dat, string Spr, bool SameName)>();
                foreach (var dat in datFiles)
                {
                    var datSignature = ReadFileSignature(dat);
                    foreach (var spr in sprFiles)
                    {
                        var sprSignature = ReadFileSignature(spr);
                        if (ClientVersionCatalog.AreMatchingSignatures(datSignature, sprSignature))
                        {
                            var sameName = Path.GetFileNameWithoutExtension(dat).Equals(
                                Path.GetFileNameWithoutExtension(spr), StringComparison.OrdinalIgnoreCase);
                            pairs.Add((dat, spr, sameName));
                        }
                    }
                }

                var namedPairs = pairs.Where(pair => pair.SameName).ToArray();
                var candidates = namedPairs.Length > 0 ? namedPairs : pairs.ToArray();
                if (candidates.Length != 1)
                {
                    SetError(candidates.Length == 0
                        ? Text("Há vários arquivos DAT/SPR e não consegui identificar o par. Abra cada arquivo pelos botões DAT e SPR.",
                            "There are multiple DAT/SPR files and I couldn't identify the matching pair. Open each file with the DAT and SPR buttons.")
                        : Text("Encontrei mais de um par compatível de DAT + SPR. Deixe apenas o par desejado nessa pasta ou abra cada arquivo separadamente.",
                            "I found more than one compatible DAT + SPR pair. Keep only the pair you want in this folder or open each file separately."));
                    return;
                }

                datPath = candidates[0].Dat;
                sprPath = candidates[0].Spr;
            }

            await LoadSprAsync(sprPath, discardPendingPixelEdits: true);
            if (_filePath is null) return;
            await LoadDatAsync(datPath);
            if (_datModel is not null && otbPath is not null)
            {
                try { await LoadServerItemsAsync(otbPath); }
                catch (Exception ex) { SetError(Text($"items.otb/items.xml não foram carregados: {ex.Message}", $"Could not load items.otb/items.xml: {ex.Message}")); }
            }
        }
        catch (Exception ex)
        {
            SetError(Text($"Não foi possível ler a pasta selecionada: {ex.Message}",
                $"Could not read the selected folder: {ex.Message}"));
        }
    }

    private static uint ReadFileSignature(string path)
    {
        Span<byte> signature = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        stream.ReadExactly(signature);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(signature);
        if (Path.GetExtension(path).Equals(".dat", StringComparison.OrdinalIgnoreCase) && value == DatFileParser.Dat32Magic)
        {
            stream.ReadExactly(signature);
            return BinaryPrimitives.ReadUInt32LittleEndian(signature);
        }
        return value;
    }

    private async Task LoadDatAsync(string path)
    {
        ResetThingControls();
        SetError(null);
        StatusText.Text = Text("Lendo o arquivo DAT…", "Reading DAT file…");
        StatusDot.Fill = ThemeBrush("WarningBrush");
        try
        {
            var fallbackVersion = _filePath is null ? null : ClientVersionCatalog.FindBySprSignature(_signature)?.Value;
            var model = await Task.Run(() => DatFileParser.Parse(path, fallbackVersion));
            if (_datPath is not null && !string.Equals(Path.GetFullPath(_datPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            {
                _serverItemFiles = null;
                UpdateOtbStatusIndicator();
            }
            else if (_serverItemFiles?.IsDirty == true)
                _serverItemFiles = await Task.Run(() => ServerItemFiles.Load(_serverItemFiles.OtbPath, _serverItemFiles.XmlPath));
            _datModel = model;
            _pendingPixelSprites.Clear();
            _datPath = path;
            _selectedThing = null;
            _selectedThingIds.Clear();
            _thingSelectionAnchorId = null;
            _copiedDatThing = null;
            _copiedDatPatterns = null;
            _copiedDatPatternCategory = null;
            _copiedDatVersion = null;
            _copiedDatProperties = null;
            _copiedDatAttributes = null;
            _copiedDatRenderOrder = null;
            _metadataHasUnsavedChanges = false;
            MetadataSaveStatus.Text = string.Empty;
            UpdateMetadataEditingState();
            _selectedId = null;
            SetPreviewBitmap(null);
            SetExportEnabled(false);
            SetSelectionSummary("—", "—");
            ViewModeCombo.IsEnabled = true;
            SearchBox.IsEnabled = true;
            SelectComboTag(ViewModeCombo, "things");
            SelectComboTag(CategoryCombo, "item");
            _page = 0;
            UpdateObjectSpritePanelVisibility();
            StatusDot.Fill = ThemeBrush("SuccessBrush");
            StatusText.Text = model.VersionName;
            VersionText.Text = model.VersionName;
            SelectedFileText.Text = Path.GetFileName(path);
            UpdateClientInfo();
            SpriteCountText.Text = $"{model.Things.Count:N0} {Text("objetos", "objects")}  ·  {model.ItemCount:N0} {Text("itens", "items")}";
            RenderPage();
            UpdateMetadataEditingState();
        }
        catch (Exception ex)
        {
            StatusDot.Fill = ThemeBrush("ErrorBrush");
            StatusText.Text = Text("Falha ao abrir DAT", "Could not open DAT");
            SetError(ex.Message);
            MessageBox.Show(this, ex.Message,
                Text("Detalhes ao abrir o DAT", "DAT opening details"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
        }
    }

    private static void SelectComboTag(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == tag);
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var folder = files.FirstOrDefault(Directory.Exists);
        if (folder is not null)
        {
            if (ConfirmDiscardUnsavedMetadata()) await LoadClientFolderAsync(folder);
            return;
        }
        var spr = files.FirstOrDefault(path => Path.GetExtension(path).Equals(".spr", StringComparison.OrdinalIgnoreCase));
        var dat = files.FirstOrDefault(path => Path.GetExtension(path).Equals(".dat", StringComparison.OrdinalIgnoreCase));
        if (dat is not null && !ConfirmDiscardUnsavedMetadata()) return;
        if (spr is not null && dat is null && _pendingPixelSprites.Count > 0)
        {
            MessageBox.Show(this,
                Text("Salve tudo antes de abrir outro SPR para preservar os pixels editados.",
                    "Save all before opening another SPR to preserve the edited pixels."),
                Text("Pixels ainda não salvos", "Unsaved pixels"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (spr is not null) await LoadSprAsync(spr, discardPendingPixelEdits: dat is not null);
        if (dat is not null) await LoadDatAsync(dat);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async Task LoadSprAsync(string path, bool discardPendingPixelEdits = false)
    {
        if (_pendingPixelSprites.Count > 0 && !discardPendingPixelEdits)
            throw new InvalidOperationException(Text("Salve tudo antes de abrir outro SPR para preservar os pixels editados.",
                "Save all before opening another SPR to preserve the edited pixels."));
        if (discardPendingPixelEdits) _pendingPixelSprites.Clear();
        ResetThingControls();
        _selectedThingSpriteBitmaps.Clear();
        SetError(null);
        StatusText.Text = Text("Lendo o arquivo…", "Reading file…");
        StatusDot.Fill = ThemeBrush("WarningBrush");
        SearchBox.IsEnabled = false;
        SetExportEnabled(false);
        SetPreviewBitmap(null);
        SetSelectionSummary("—", "—");
        SpriteGrid.Children.Clear();
        _pageBitmaps.Clear();
        _selectedId = null;
        _page = 0;
        _sprStream?.Dispose();
        _sprStream = null;
        _filePath = null;
        _offsets = [];
        _spriteCount = 0;
        _selectedObjectSpriteIds.Clear();
        _objectSpriteSelectionAnchorId = null;
        _sprHeaderSize = 0;
        _signature = 0;
        _slicerWindow?.RefreshImportAvailability();
        _objectSpriteId = 1;
        _objectSpriteBitmaps.Clear();
        RenderObjectSpritePage();

        try
        {
            var newStream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess);
            var prefix = new byte[136];
            var prefixRead = await RandomAccess.ReadAsync(newStream.SafeFileHandle, prefix, 0);
            if (prefixRead < 8)
            {
                newStream.Dispose();
                throw new InvalidDataException(Text("Cabeçalho SPR inválido.", "Invalid SPR header."));
            }

            var signature = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
            var smallCount = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(4));
            var largeCount = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(4));
            var format = ChooseFormat(newStream.Length, prefix.AsSpan(0, prefixRead), smallCount, largeCount);
            if (format.Count == 0 || format.Count > int.MaxValue / 4)
            {
                newStream.Dispose();
                throw new InvalidDataException(Text("Não foi possível interpretar a tabela de sprites.", "Could not read the sprite index."));
            }

            var tableLength = checked((int)format.Count * 4);
            var table = new byte[tableLength];
            await ReadExactlyAsync(newStream.SafeFileHandle, table, format.HeaderSize);
            var offsets = new uint[format.Count];
            for (var i = 0; i < offsets.Length; i++) offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(i * 4, 4));

            _sprStream?.Dispose();
            _sprStream = newStream;
            _filePath = path;
            _signature = signature;
            _spriteCount = format.Count;
            _sprHeaderSize = format.HeaderSize;
            _offsets = offsets;
            _objectSpriteId = 1;
            _selectedObjectSpriteIds.Clear();
            if (_spriteCount > 0) _selectedObjectSpriteIds.Add(_objectSpriteId);
            _objectSpriteSelectionAnchorId = _spriteCount > 0 ? _objectSpriteId : null;
            _page = 0;
            _selectedId = null;
            StatusDot.Fill = ThemeBrush("SuccessBrush");
            StatusText.Text = GetVersionName();
            SpriteCountText.Text = $"{_spriteCount:N0} {Text("sprites", "sprites")}";
            SearchBox.IsEnabled = true;
            DropPanel.Visibility = Visibility.Collapsed;
            SelectedFileText.Text = Path.GetFileName(path);
            VersionText.Text = GetVersionName();
            UpdateClientInfo();
            UpdateEmptyState();
            RenderPage();
            RenderObjectSpritePage();
            UpdateMetadataEditingState();
            _slicerWindow?.RefreshImportAvailability();
        }
        catch (Exception ex)
        {
            StatusDot.Fill = ThemeBrush("ErrorBrush");
            StatusText.Text = Text("Falha ao abrir", "Could not open file");
            SetError(ex.Message);
            UpdateEmptyState();
            RenderObjectSpritePage();
        }
    }

    private static (uint Count, int HeaderSize) ChooseFormat(long fileSize, ReadOnlySpan<byte> prefix, ushort smallCount, uint largeCount)
    {
        var options = new List<(uint Count, int HeaderSize)>();
        if (smallCount > 0 && 6L + smallCount * 4L <= fileSize) options.Add((smallCount, 6));
        if (largeCount > 0 && 8L + largeCount * 4L <= fileSize) options.Add((largeCount, 8));
        if (options.Count == 0) return (0, 0);
        if (options.Count == 1) return options[0];

        long bestScore = long.MaxValue;
        var best = options[0];
        foreach (var option in options)
        {
            var tableEnd = option.HeaderSize + (long)option.Count * 4;
            long score = long.MaxValue / 2;
            var available = Math.Min((int)option.Count, Math.Max(0, (prefix.Length - option.HeaderSize) / 4));
            for (var i = 0; i < available; i++)
            {
                var address = BinaryPrimitives.ReadUInt32LittleEndian(prefix.Slice(option.HeaderSize + i * 4, 4));
                if (address == 0) continue;
                score = address >= tableEnd ? address - tableEnd : long.MaxValue / 2;
                break;
            }
            if (score < bestScore) { bestScore = score; best = option; }
        }
        return best;
    }

    private static async Task ReadExactlyAsync(Microsoft.Win32.SafeHandles.SafeFileHandle handle, Memory<byte> buffer, long offset)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var amount = await RandomAccess.ReadAsync(handle, buffer[read..], offset + read);
            if (amount == 0) throw new EndOfStreamException();
            read += amount;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _isNavigatingSearch) return;
        _page = 0;
        RenderPage();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !uint.TryParse(SearchBox.Text.Trim(), out var id)) return;
        if (NavigateToSearchResult(id)) e.Handled = true;
    }

    private bool NavigateToSearchResult(uint id)
    {
        if (IsObjectMode && _datModel is not null)
        {
            var category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "item";
            var categoryThings = _datModel.Things
                .Where(thing => thing.Category == category)
                .OrderBy(thing => thing.Id)
                .ToList();
            var target = categoryThings.FirstOrDefault(thing => thing.Id == id);
            if (target is null) return false;

            _page = (int)(((long)target.Id - categoryThings[0].Id) / PageSize);
            ClearSearchForNavigation();
            RenderPage();
            SelectThing(target);
            ScrollSearchResultIntoView(id);
            return true;
        }

        if (id == 0 || id > _spriteCount) return false;
        _page = (int)((id - 1) / PageSize);
        ClearSearchForNavigation();
        RenderPage();
        SelectSprite(id);
        ScrollSearchResultIntoView(id);
        return true;
    }

    private void ClearSearchForNavigation()
    {
        _isNavigatingSearch = true;
        try { SearchBox.Clear(); }
        finally { _isNavigatingSearch = false; }
    }

    private void ScrollSearchResultIntoView(uint id)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var card = SpriteGrid.Children.OfType<Border>()
                .FirstOrDefault(candidate => candidate.Tag is uint cardId && cardId == id);
            card?.BringIntoView();
        }), DispatcherPriority.Loaded);
    }

    private void ViewModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        var objectMode = (ViewModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "things";
        _selectedThingIds.Clear();
        _thingSelectionAnchorId = null;
        ResetThingControls();
        SetPreviewBitmap(null);
        SetExportEnabled(false);
        _selectedId = null;
        var showCategory = objectMode && _datModel is not null;
        CategoryCombo.Visibility = showCategory ? Visibility.Visible : Visibility.Collapsed;
        CategorySectionLabel.Visibility = showCategory ? Visibility.Visible : Visibility.Collapsed;
        UpdateObjectSpritePanelVisibility();
        DetailIdLabel.Text = objectMode ? Text("ID DO OBJETO", "OBJECT ID") : Text("ID DO SPRITE", "SPRITE ID");
        _page = 0;
        RenderPage();
    }

    private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        _selectedThingIds.Clear();
        _thingSelectionAnchorId = null;
        ResetThingControls();
        SetPreviewBitmap(null);
        SetExportEnabled(false);
        _selectedId = null;
        _page = 0;
        RenderPage();
    }

    private void RenderPage()
    {
        if (!_initialized) return;
        UpdateDatActionToolbar();
        if (IsObjectMode && _datModel is not null)
        {
            RenderThingPage();
            return;
        }
        RenderSpritePage();
    }

    private bool IsObjectMode => (ViewModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "things";

    private void RenderSpritePage()
    {
        SpritesTitle.Text = Text("Sprites", "Sprites");
        SpriteGrid.Children.Clear();
        _pageBitmaps.Clear();
        var query = SearchBox.Text.Trim();
        var ids = new List<uint>();
        if (_spriteCount > 0 && query.Length > 0 && uint.TryParse(query, out var exactId))
        {
            if (exactId > 0 && exactId <= _spriteCount) ids.Add(exactId);
        }
        else if (_spriteCount > 0)
        {
            var start = (long)_page * PageSize + 1;
            var end = Math.Min(_spriteCount, start + PageSize - 1);
            for (var value = start; value <= end; value++)
            {
                if (query.Length == 0 || value.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) ids.Add((uint)value);
            }
        }

        if (ids.Count == 0)
        {
            EmptyTitle.Text = _spriteCount == 0 ? Text("Nenhum arquivo aberto", "No file open") : Text("Nenhum sprite encontrado", "No sprites found");
            EmptyBody.Text = _spriteCount == 0 ? Text("Escolha um arquivo SPR para ver os sprites aqui.", "Choose an SPR file to see its sprites here.") : Text("Tente outro ID de sprite.", "Try another sprite ID.");
            EmptyState.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyState.Visibility = Visibility.Collapsed;
            foreach (var id in ids) SpriteGrid.Children.Add(CreateSpriteCard(id));
            _ = LoadPageBitmapsAsync(ids);
        }

        var hasSearch = query.Length > 0;
        PagerPanel.Visibility = !hasSearch && _spriteCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePagerText();
        UpdateEmptyState();
    }

    private void RenderThingPage()
    {
        SpritesTitle.Text = Text("Objetos", "Objects");
        SpriteGrid.Children.Clear();
        _pageBitmaps.Clear();
        var category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "item";
        var query = SearchBox.Text.Trim();
        var categoryThings = _datModel!.Things.Where(thing => thing.Category == category).OrderBy(thing => thing.Id).ToList();
        List<DatThing> visibleThings;
        if (query.Length > 0 && uint.TryParse(query, out var exactId))
            visibleThings = categoryThings.Where(thing => thing.Id == exactId).ToList();
        else if (query.Length > 0)
            visibleThings = categoryThings.Where(thing => thing.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)).Take(PageSize).ToList();
        else if (categoryThings.Count > 0)
        {
            var rangeStart = (long)categoryThings[0].Id + (long)_page * PageSize;
            var rangeEnd = rangeStart + PageSize - 1;
            visibleThings = categoryThings.Where(thing => (long)thing.Id >= rangeStart && (long)thing.Id <= rangeEnd).ToList();
        }
        else
            visibleThings = [];

        if (visibleThings.Count == 0)
        {
            EmptyTitle.Text = Text("Nenhum objeto encontrado", "No objects found");
            EmptyBody.Text = Text("Tente outro ID ou selecione outra categoria.", "Try another ID or choose another category.");
            EmptyState.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyState.Visibility = Visibility.Collapsed;
            foreach (var thing in visibleThings) SpriteGrid.Children.Add(CreateThingCard(thing));
            _ = LoadThingPreviewsAsync(visibleThings);
        }

        var categoryLabel = category switch
        {
            "outfit" => Text("outfits", "outfits"), "effect" => Text("efeitos", "effects"),
            "missile" => Text("projéteis", "missiles"), _ => Text("itens", "items")
        };
        SpriteCountText.Text = $"{categoryThings.Count:N0} {categoryLabel}  ·  {_datModel.VersionName}";
        PagerPanel.Visibility = query.Length == 0 && categoryThings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePagerText();
        UpdateEmptyState();
    }

    private Border CreateThingCard(DatThing thing)
    {
        var imageSize = _gridMode ? 48 : 30;
        var tileSize = _gridMode ? 62 : 34;
        var image = new Image { Width = imageSize, Height = imageSize, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var thumbnail = new Border
        {
            Width = tileSize, Height = tileSize, CornerRadius = new CornerRadius(4), Background = ThemeBrush("CardBackgroundBrush"),
            Child = image, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = $"{thing.Id}", Foreground = ThemeBrush("TextBrush"), FontSize = 10,
            HorizontalAlignment = _gridMode ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Margin = _gridMode ? new Thickness(0, 6, 0, 0) : new Thickness(9, 0, 0, 0)
        };
        var content = new StackPanel
        {
            Orientation = _gridMode ? Orientation.Vertical : Orientation.Horizontal,
            HorizontalAlignment = _gridMode ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { thumbnail, label }
        };
        var selected = _selectedThingIds.Contains(thing.Id) || _selectedId == thing.Id;
        var card = new Border
        {
            Width = _gridMode ? 82 : 254, Height = _gridMode ? 98 : 43, Margin = new Thickness(1), Padding = new Thickness(_gridMode ? 7 : 4), CornerRadius = new CornerRadius(4),
            Background = (selected ? ThemeBrush("SelectedBackgroundBrush") : ThemeBrush("CardBackgroundBrush")), BorderBrush = ThemeBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1), Child = content, Tag = thing.Id, Cursor = System.Windows.Input.Cursors.Hand
        };
        card.MouseLeftButtonUp += (_, args) =>
        {
            if (args.ChangedButton != MouseButton.Left) return;
            SelectThingWithModifiers(thing, Keyboard.Modifiers);
            args.Handled = true;
        };
        card.PreviewMouseRightButtonDown += (_, args) =>
        {
            if (!_selectedThingIds.Contains(thing.Id))
                SelectThing(thing);
            else
                SelectThing(thing, preserveMultiSelection: true);
            card.ContextMenu = CreateThingContextMenu(thing);
            args.Handled = false;
        };
        if (selected) card.BorderBrush = (Brush)FindResource("AccentBrush");
        return card;
    }

    private ContextMenu CreateThingContextMenu(DatThing thing)
    {
        var menu = new ContextMenu
        {
            Background = ThemeBrush("PanelAlt"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            Padding = new Thickness(4),
            MinWidth = 224
        };

        StackPanel CreateHeader(string label, string glyph, bool destructive = false)
        {
            var iconColor = ThemeBrush(destructive ? "ErrorBrush" : "MutedBrush");
            var textColor = ThemeBrush(destructive ? "ErrorBrush" : "TextBrush");
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = iconColor,
                Width = 22,
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = textColor,
                VerticalAlignment = VerticalAlignment.Center
            });
            return header;
        }

        MenuItem CreateItem(string pt, string en, string glyph, bool enabled = true, bool destructive = false) => new()
        {
            Header = CreateHeader(Text(pt, en), glyph, destructive),
                IsEnabled = enabled,
            Foreground = ThemeBrush(destructive ? "ErrorBrush" : "TextBrush"),
            Padding = new Thickness(8, 5, 10, 5)
        };

        MenuItem Add(string pt, string en, string glyph, Action action, bool enabled = true, bool destructive = false)
        {
            var item = CreateItem(pt, en, glyph, enabled, destructive);
            item.Click += (_, _) => RunDatThingOperation(action);
            menu.Items.Add(item);
            return item;
        }

        MenuItem AddSubmenu(string pt, string en, string glyph, bool enabled = true)
        {
            var item = CreateItem(pt, en, glyph, enabled);
            menu.Items.Add(item);
            return item;
        }

        void Separator() => menu.Items.Add(new Separator());

        var selectionCount = GetSelectedDatThings().Count;
        Add("Editar", "Edit", "\uE70F", () => SelectThing(thing));
        Add("Duplicar", "Duplicate", "\uE8C8", () => DatDuplicateButton_Click(this, new RoutedEventArgs()));
        Add("Substituir", "Replace", "\uE8AB", () => DatReplaceButton_Click(this, new RoutedEventArgs()));
        Add("Substituir na outra janela", "Replace in another window", "\uE8AB", () => DatReplaceAcrossWindowsButton_Click(this, new RoutedEventArgs()));
        Add("Exportar", "Export", "\uEDE1", ExportSelectedDatThings);
        Add("Exportar objeto DAT (JSON)", "Export DAT object (JSON)", "\uE8A5", ExportSelectedDatThingsJson);

        var bulk = AddSubmenu("Edição em lote", "Bulk Edit", "\uE713", selectionCount > 1);
        void AddBulk(string pt, string en, string glyph, Action apply, bool itemsOnly = false)
        {
            var child = CreateItem(pt, en, glyph, selectionCount > 1 && (!itemsOnly || thing.Category == "item"));
            child.Click += (_, _) => RunDatThingOperation(apply);
            bulk.Items.Add(child);
        }
        AddBulk("Aplicar padrões", "Apply patterns", "\uE8A5", () => ApplyThingPatternsToSelection(thing));
        AddBulk("Aplicar propriedades", "Apply properties", "\uE8A5", () => ApplyThingPropertyGroupToSelection(thing, attributes: false), itemsOnly: true);
        AddBulk("Aplicar atributos", "Apply attributes", "\uE8D7", () => ApplyThingPropertyGroupToSelection(thing, attributes: true), itemsOnly: true);

        Separator();
        var copy = AddSubmenu("Copiar", "Copy", "\uE8C8");
        MenuItem AddCopy(string pt, string en, string glyph, Action action, bool enabled = true)
        {
            var child = CreateItem(pt, en, glyph, enabled);
            child.Click += (_, _) => RunDatThingOperation(action);
            copy.Items.Add(child);
            return child;
        }
        AddCopy("Copiar objeto", "Copy Object", "\uE8C8", () =>
        {
            _copiedDatThing = CloneDatThing(thing, thing.Id, thing.Category);
            _copiedDatVersion = _datModel?.VersionValue;
        });
        AddCopy("Copiar padrões", "Copy Patterns", "\uE8A5", () =>
        {
            _copiedDatPatterns = CloneDatThing(thing, thing.Id, thing.Category).FrameGroups;
            _copiedDatPatternCategory = thing.Category;
            _copiedDatVersion = _datModel?.VersionValue;
        });
        AddCopy("Copiar propriedades", "Copy Properties", "\uE8A5", () => CopyThingPropertyGroup(thing, attributes: false), thing.Category == "item");
        AddCopy("Copiar atributos", "Copy Attributes", "\uE8D7", () => CopyThingPropertyGroup(thing, attributes: true), thing.Category == "item");

        var paste = AddSubmenu("Colar", "Paste", "\uE77F");
        MenuItem AddPaste(string pt, string en, string glyph, Action action, bool enabled)
        {
            var child = CreateItem(pt, en, glyph, enabled);
            child.Click += (_, _) => RunDatThingOperation(action);
            paste.Items.Add(child);
            return child;
        }
        AddPaste("Colar objeto", "Paste Object", "\uE8C8", PasteCopiedDatObject, _copiedDatThing is not null
            && _copiedDatThing.Category == thing.Category && _copiedDatVersion == _datModel?.VersionValue);
        AddPaste("Colar padrões", "Paste Patterns", "\uE8A5", PasteCopiedDatPatterns, _copiedDatPatterns is not null
            && _copiedDatPatternCategory == thing.Category && _copiedDatVersion == _datModel?.VersionValue);
        AddPaste("Colar propriedades", "Paste Properties", "\uE8A5", () => PasteThingPropertyGroup(attributes: false), thing.Category == "item" && _copiedDatProperties is not null);
        AddPaste("Colar atributos", "Paste Attributes", "\uE8D7", () => PasteThingPropertyGroup(attributes: true), thing.Category == "item" && _copiedDatAttributes is not null);
        paste.IsEnabled = paste.Items.OfType<MenuItem>().Any(item => item.IsEnabled);

        Add("Excluir", "Remove", "\uE74D", DeleteSelectedDatThings, destructive: true);
        Separator();
        Add("Comparar…", "Compare…", "\uE9D9", CompareSelectedDatThings, selectionCount == 2);
        Separator();
        Add($"Copiar ID do cliente: {thing.Id}", $"Copy Client ID: {thing.Id}", "\uE8C8", () => Clipboard.SetText(thing.Id.ToString()));
        Add("Copiar ID do servidor", "Copy Server ID", "\uE8C8", () => { }, enabled: false);
        return menu;
    }

    private IReadOnlyList<DatThing> GetSelectedDatThings()
    {
        if (_datModel is null) return [];
        var category = SelectedDatCategory;
        var selected = _datModel.Things.Where(thing => thing.Category == category && _selectedThingIds.Contains(thing.Id))
            .OrderBy(thing => thing.Id).ToArray();
        if (selected.Length == 0 && _selectedThing is { } current && current.Category == category)
            return [current];
        return selected;
    }

    private static IReadOnlyList<DatFrameGroup> CloneDatFrameGroups(IEnumerable<DatFrameGroup> groups) => groups.Select(group => group with
    {
        Frames = group.Frames.Select(frame => frame with { SpriteIds = (uint[])frame.SpriteIds.Clone() }).ToArray()
    }).ToArray();

    private void PasteCopiedDatObject()
    {
        if (_copiedDatThing is null || _copiedDatVersion != _datModel?.VersionValue) return;
        var replacements = GetSelectedDatThings().Select(target => CloneDatThing(_copiedDatThing, target.Id, target.Category)).ToArray();
        if (replacements.Length > 0) ApplyDatThingBatchChanges(replacements, replacements[^1]);
    }

    private void PasteCopiedDatPatterns()
    {
        if (_copiedDatPatterns is null || _copiedDatVersion != _datModel?.VersionValue
            || _copiedDatPatternCategory != SelectedDatCategory) return;
        var replacements = GetSelectedDatThings().Select(target =>
        {
            var groups = CloneDatFrameGroups(_copiedDatPatterns);
            var spriteCount = groups.Sum(group => group.Frames.Sum(frame => frame.SpriteIds.Length));
            return target with { FrameGroups = groups, SpriteCount = spriteCount };
        }).ToArray();
        if (replacements.Length > 0) ApplyDatThingBatchChanges(replacements, replacements[^1]);
    }

    private void CopyThingPropertyGroup(DatThing thing, bool attributes)
    {
        if (_datModel is null || thing.Category != "item") return;
        var fields = DatPropertiesCodec.Read(thing.RawProperties, _datModel.VersionValue, _datModel.MetadataReaderVersion);
        var attributeKeys = _attributeFlagControls.Keys.Concat(["groundBorder", "onBottom", "onTop"]).ToHashSet(StringComparer.Ordinal);
        var copied = fields.Where(entry => entry.Key != "groundBorder" && entry.Key != "onBottom" && entry.Key != "onTop"
                && attributeKeys.Contains(entry.Key) == attributes)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var renderOrder = DatPropertiesCodec.Get(fields, "groundBorder").Enabled ? "groundBorder"
            : DatPropertiesCodec.Get(fields, "onBottom").Enabled ? "onBottom"
            : DatPropertiesCodec.Get(fields, "onTop").Enabled ? "onTop" : "common";
        if (attributes)
        {
            _copiedDatAttributes = copied;
            _copiedDatRenderOrder = renderOrder;
        }
        else _copiedDatProperties = copied;
    }

    private void PasteThingPropertyGroup(bool attributes)
    {
        var copied = attributes ? _copiedDatAttributes : _copiedDatProperties;
        if (_datModel is null || copied is null) return;
        var targets = GetSelectedDatThings().Where(thing => thing.Category == "item").ToArray();
        var replacements = targets.Select(target => target with
        {
            RawProperties = ApplyCopiedProperties(target.RawProperties, copied, attributes ? _copiedDatRenderOrder : null)
        }).ToArray();
        if (replacements.Length > 0) ApplyDatThingBatchChanges(replacements, replacements[^1]);
    }

    private void ApplyThingPropertyGroupToSelection(DatThing source, bool attributes)
    {
        if (_datModel is null || source.Category != "item") return;
        var fields = DatPropertiesCodec.Read(source.RawProperties, _datModel.VersionValue, _datModel.MetadataReaderVersion);
        var attributeKeys = _attributeFlagControls.Keys.Concat(["groundBorder", "onBottom", "onTop"]).ToHashSet(StringComparer.Ordinal);
        var copied = fields.Where(entry => entry.Key != "groundBorder" && entry.Key != "onBottom" && entry.Key != "onTop"
                && attributeKeys.Contains(entry.Key) == attributes)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var order = DatPropertiesCodec.Get(fields, "groundBorder").Enabled ? "groundBorder"
            : DatPropertiesCodec.Get(fields, "onBottom").Enabled ? "onBottom"
            : DatPropertiesCodec.Get(fields, "onTop").Enabled ? "onTop" : "common";
        var replacements = GetSelectedDatThings().Where(target => target.Category == "item" && target.Id != source.Id)
            .Select(target => target with { RawProperties = ApplyCopiedProperties(target.RawProperties, copied, attributes ? order : null) }).ToArray();
        if (replacements.Length > 0) ApplyDatThingBatchChanges(replacements, source);
    }

    private byte[] ApplyCopiedProperties(byte[] raw, IReadOnlyDictionary<string, DatPropertyField> copied, string? renderOrder)
    {
        var result = (byte[])raw.Clone();
        if (_datModel is null) return result;
        if (renderOrder is not null) result = DatPropertiesCodec.SetRenderOrder(result, _datModel.VersionValue, renderOrder, _datModel.MetadataReaderVersion);
        foreach (var (key, field) in copied)
            if (DatPropertiesCodec.Supports(_datModel.VersionValue, key, _datModel.MetadataReaderVersion))
                result = DatPropertiesCodec.Set(result, _datModel.VersionValue, key, field, _datModel.MetadataReaderVersion);
        return result;
    }

    private void ApplyThingPatternsToSelection(DatThing source)
    {
        var replacements = GetSelectedDatThings().Where(target => target.Id != source.Id).Select(target =>
        {
            var groups = CloneDatFrameGroups(source.FrameGroups);
            var spriteCount = groups.Sum(group => group.Frames.Sum(frame => frame.SpriteIds.Length));
            return target with { FrameGroups = groups, SpriteCount = spriteCount };
        }).ToArray();
        if (replacements.Length > 0) ApplyDatThingBatchChanges(replacements, source);
    }

    private void CompareSelectedDatThings()
    {
        var pair = GetSelectedDatThings();
        if (_datModel is null || pair.Count != 2) return;
        var first = pair[0];
        var second = pair[1];
        var firstProperties = DatPropertiesCodec.Read(first.RawProperties, _datModel.VersionValue, _datModel.MetadataReaderVersion);
        var secondProperties = DatPropertiesCodec.Read(second.RawProperties, _datModel.VersionValue, _datModel.MetadataReaderVersion);
        var changedProperties = firstProperties.Keys.Union(secondProperties.Keys, StringComparer.Ordinal)
            .Where(key =>
            {
                var left = DatPropertiesCodec.Get(firstProperties, key);
                var right = DatPropertiesCodec.Get(secondProperties, key);
                return left.Enabled != right.Enabled || left.Text != right.Text || !left.Values.SequenceEqual(right.Values);
            }).ToArray();
        var patternsEqual = DatFrameGroupsEqual(first.FrameGroups, second.FrameGroups);
        var message = Text(
            $"Objeto #{first.Id:N0} vs. #{second.Id:N0}\n\nGrupos: {first.FrameGroups.Count} / {second.FrameGroups.Count}\nReferências de sprite: {first.SpriteCount:N0} / {second.SpriteCount:N0}\nPadrões, quadros e sprites: {(patternsEqual ? "iguais" : "diferentes")}\nPropriedades diferentes: {(changedProperties.Length == 0 ? "nenhuma" : string.Join(", ", changedProperties))}",
            $"Object #{first.Id:N0} vs. #{second.Id:N0}\n\nFrame groups: {first.FrameGroups.Count} / {second.FrameGroups.Count}\nSprite references: {first.SpriteCount:N0} / {second.SpriteCount:N0}\nPatterns, frames, and sprites: {(patternsEqual ? "same" : "different")}\nDifferent properties: {(changedProperties.Length == 0 ? "none" : string.Join(", ", changedProperties))}");
        MessageBox.Show(this, message, Text("Comparar objetos", "Compare objects"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool DatFrameGroupsEqual(IReadOnlyList<DatFrameGroup> left, IReadOnlyList<DatFrameGroup> right)
    {
        if (left.Count != right.Count) return false;
        for (var groupIndex = 0; groupIndex < left.Count; groupIndex++)
        {
            var a = left[groupIndex];
            var b = right[groupIndex];
            if (a.Type != b.Type || a.Width != b.Width || a.Height != b.Height || a.CropSize != b.CropSize
                || a.Layers != b.Layers || a.PatternX != b.PatternX || a.PatternY != b.PatternY || a.PatternZ != b.PatternZ
                || a.AnimationMode != b.AnimationMode || a.LoopCount != b.LoopCount || a.StartFrame != b.StartFrame
                || a.Frames.Count != b.Frames.Count) return false;
            for (var frameIndex = 0; frameIndex < a.Frames.Count; frameIndex++)
            {
                var frameA = a.Frames[frameIndex];
                var frameB = b.Frames[frameIndex];
                if (frameA.MinimumDuration != frameB.MinimumDuration || frameA.MaximumDuration != frameB.MaximumDuration
                    || !frameA.SpriteIds.SequenceEqual(frameB.SpriteIds)) return false;
            }
        }
        return true;
    }

    private IReadOnlyList<DatThing> CreateDatThingCopies(IReadOnlyList<DatThing> sources)
    {
        var model = _datModel ?? throw new InvalidOperationException(Text("Abra um arquivo DAT primeiro.", "Open a DAT file first."));
        var nextModel = model;
        var copies = new List<DatThing>(sources.Count);
        foreach (var source in sources.OrderBy(thing => thing.Id))
        {
            var id = GetNextDatThingId(nextModel, source.Category);
            copies.Add(CloneDatThing(source, id, source.Category));
            nextModel = WithDatCategoryCount(nextModel, source.Category, id);
        }
        return copies;
    }

    private void ApplyDatThingAdditions(IReadOnlyList<DatThing> additions)
    {
        if (_datModel is not { } model || additions.Count == 0) return;
        var things = model.Things.ToList();
        var existingIds = model.Things.Select(thing => (thing.Category, thing.Id)).ToHashSet();
        var validatedAdditions = new List<DatThing>(additions.Count);
        var updatedModel = model;
        foreach (var addition in additions)
        {
            if (!existingIds.Add((addition.Category, addition.Id)))
                throw new InvalidOperationException(Text("Um dos novos IDs já está em uso no DAT.", "One of the new IDs is already used in the DAT."));
            var validated = ValidateDatThingExchange(addition, model, addition.Category);
            things.Add(validated);
            validatedAdditions.Add(validated);
            updatedModel = WithDatCategoryCount(updatedModel, addition.Category, addition.Id);
        }
        updatedModel = updatedModel with { Things = OrderDatThings(things) };
        _ = DatFileWriter.Serialize(updatedModel);
        _serverItemFiles?.ValidateCanAddFromDat(validatedAdditions, updatedModel.VersionValue);
        if (_serverItemFiles is not null)
            foreach (var addition in validatedAdditions)
                if (addition.Category == "item") _serverItemFiles.AddFromDat(addition, updatedModel.VersionValue);
        MarkDatModelChanged(updatedModel);
        _selectedThingIds.Clear();
        foreach (var addition in additions) _selectedThingIds.Add(addition.Id);
        _thingSelectionAnchorId = additions[0].Id;
        var focus = additions[^1];
        NavigateToDatThing(focus, preserveSelection: true);
        UpdateItemAttributeEditor();
        UpdateMetadataEditingState();
    }

    private void ApplyDatThingBatchChanges(IReadOnlyList<DatThing> replacements, DatThing focus)
    {
        if (_datModel is not { } model || replacements.Count == 0) return;
        var replacementsById = replacements.ToDictionary(thing => (thing.Category, thing.Id));
        var existingIds = model.Things.Select(thing => (thing.Category, thing.Id)).ToHashSet();
        if (replacementsById.Keys.Any(key => !existingIds.Contains(key)))
            throw new InvalidOperationException(Text("Um dos objetos selecionados não existe mais no DAT.", "One of the selected objects no longer exists in the DAT."));
        var validated = replacementsById.ToDictionary(pair => pair.Key,
            pair => ValidateDatThingExchange(pair.Value, model, pair.Key.Category));
        var updatedModel = model with
        {
            Things = OrderDatThings(model.Things.Select(thing => validated.TryGetValue((thing.Category, thing.Id), out var replacement)
                ? replacement : thing))
        };
        _ = DatFileWriter.Serialize(updatedModel);
        MarkDatModelChanged(updatedModel);
        var validatedFocus = validated.TryGetValue((focus.Category, focus.Id), out var updatedFocus)
            ? updatedFocus : updatedModel.Things.FirstOrDefault(thing => thing.Category == focus.Category && thing.Id == focus.Id) ?? focus;
        if (_selectedThingIds.Count == 0) _selectedThingIds.Add(validatedFocus.Id);
        NavigateToDatThing(validatedFocus, preserveSelection: true);
    }

    private void MarkDatModelChanged(DatFileModel model)
    {
        _datModel = model;
        _metadataHasUnsavedChanges = true;
        MetadataSaveStatus.Foreground = (Brush)FindResource("MutedBrush");
        MetadataSaveStatus.Text = Text("Alterações não salvas", "Unsaved changes");
        UpdateClientInfo();
        _pageBitmaps.Clear();
        ClearSearchForNavigation();
    }

    private void NavigateToDatThing(DatThing thing, bool preserveSelection = false)
    {
        var categoryThings = _datModel?.Things.Where(candidate => candidate.Category == thing.Category).OrderBy(candidate => candidate.Id).ToArray() ?? [];
        var firstId = categoryThings.Length == 0 ? thing.Id : categoryThings[0].Id;
        _page = (int)(((long)thing.Id - firstId) / PageSize);
        RenderPage();
        SelectThing(thing, preserveMultiSelection: preserveSelection);
        ScrollSearchResultIntoView(thing.Id);
    }

    private static uint GetDatCategoryCount(DatFileModel model, string category) => category switch
    {
        "item" => model.ItemCount,
        "outfit" => model.OutfitCount,
        "effect" => model.EffectCount,
        "missile" => model.MissileCount,
        _ => throw new InvalidDataException("Unknown DAT category.")
    };

    private DatFileModel WithDatCategoryCount(DatFileModel model, string category, uint count)
    {
        if (count > DatFileParser.MaximumThingCountPerCategory)
            throw new InvalidDataException(Text("O limite seguro de objetos por categoria DAT foi atingido.", "The safe DAT objects-per-category limit has been reached."));
        var uses32BitIds = model.Uses32BitIds || count > ushort.MaxValue;
        return category switch
        {
            "item" => model with { ItemCount = count, Uses32BitIds = uses32BitIds },
            "outfit" => model with { OutfitCount = count, Uses32BitIds = uses32BitIds },
            "effect" => model with { EffectCount = count, Uses32BitIds = uses32BitIds },
            "missile" => model with { MissileCount = count, Uses32BitIds = uses32BitIds },
            _ => throw new InvalidDataException("Unknown DAT category.")
        };
    }

    private async Task LoadThingPreviewsAsync(IReadOnlyList<DatThing> things)
    {
        var stream = _sprStream;
        var path = _filePath;
        if (stream is null || path is null) return;
        var results = await Task.WhenAll(things.Select(async thing =>
        {
            return (ThingId: thing.Id, Bitmap: await ReadThingBitmapAsync(thing, stream.SafeFileHandle));
        }));
        if (!ReferenceEquals(stream, _sprStream) || path != _filePath || !IsObjectMode) return;
        foreach (var (thingId, bitmap) in results)
        {
            if (bitmap is null) continue;
            _pageBitmaps[thingId] = bitmap;
            foreach (var card in SpriteGrid.Children.OfType<Border>())
            {
                if (card.Tag is uint cardId && cardId == thingId && card.Child is StackPanel stack && stack.Children[0] is Border thumbnail && thumbnail.Child is Image image)
                {
                    image.Source = bitmap;
                    break;
                }
            }
        }
    }

    private void SelectThing(DatThing thing, bool preserveMultiSelection = false)
    {
        SetError(null);
        if (!preserveMultiSelection)
        {
            _selectedThingIds.Clear();
            _selectedThingIds.Add(thing.Id);
            _thingSelectionAnchorId = thing.Id;
        }
        _previewRenderVersion++;
        _selectedFilmRollFrames.Clear();
        _filmRollSelectionAnchor = -1;
        _selectedThingSpriteBitmaps.Clear();
        _selectedId = thing.Id;
        ConfigureThingControls(thing);
        UpdateItemAttributeEditor();
        var selectedLabel = $"{CategoryName(thing.Category)} #{thing.Id}";
        var group = _selectedFrameGroup;
        SetSelectionSummary(selectedLabel, $"{(group?.Width ?? thing.Width) * SpriteSide} × {(group?.Height ?? thing.Height) * SpriteSide} px");
        SelectedFileText.Text = $"{Path.GetFileName(_datPath ?? "")}{Environment.NewLine}{thing.SpriteCount} {Text("referências de sprite", "sprite references")}";
        VersionText.Text = _datModel?.VersionName ?? GetVersionName();
        SetExportEnabled(false);
        SetPreviewBitmap(null);
        _ = LoadSelectedThingPreviewAsync();
        if (_filmRollMode && _selectedFrameGroup is { Frames.Count: > 1 })
            _ = RenderFilmRollFramesAsync();
        StartInfoPreviewAnimation();
        foreach (var card in SpriteGrid.Children.OfType<Border>())
        {
            var isSelected = card.Tag is uint cardId && _selectedThingIds.Contains(cardId);
            card.BorderBrush = isSelected ? (Brush)FindResource("AccentBrush") : ThemeBrush("CardBorderBrush");
            card.Background = isSelected ? ThemeBrush("SelectedBackgroundBrush") : ThemeBrush("CardBackgroundBrush");
        }
        UpdateDatActionToolbar();
        UpdatePagerText();
    }

    private void SelectThingWithModifiers(DatThing thing, ModifierKeys modifiers)
    {
        var categoryThings = _datModel?.Things.Where(candidate => candidate.Category == thing.Category)
            .OrderBy(candidate => candidate.Id).ToArray() ?? [];
        if (categoryThings.Length == 0)
        {
            SelectThing(thing);
            return;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift)
            && _thingSelectionAnchorId is uint anchorId
            && Array.FindIndex(categoryThings, candidate => candidate.Id == anchorId) is var anchorIndex && anchorIndex >= 0)
        {
            var targetIndex = Array.FindIndex(categoryThings, candidate => candidate.Id == thing.Id);
            if (targetIndex < 0) { SelectThing(thing); return; }
            if (!modifiers.HasFlag(ModifierKeys.Control)) _selectedThingIds.Clear();
            for (var index = Math.Min(anchorIndex, targetIndex); index <= Math.Max(anchorIndex, targetIndex); index++)
                _selectedThingIds.Add(categoryThings[index].Id);
            SelectThing(thing, preserveMultiSelection: true);
            return;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            if (_selectedThingIds.Contains(thing.Id))
            {
                _selectedThingIds.Remove(thing.Id);
                _thingSelectionAnchorId = thing.Id;
                var next = categoryThings.FirstOrDefault(candidate => candidate.Id == _selectedId && _selectedThingIds.Contains(candidate.Id))
                    ?? categoryThings.LastOrDefault(candidate => _selectedThingIds.Contains(candidate.Id));
                if (next is not null) SelectThing(next, preserveMultiSelection: true);
                else
                {
                    _selectedThing = null;
                    _selectedId = null;
                    ResetThingControls();
                    SetPreviewBitmap(null);
                    SetSelectionSummary("—", "—");
                    SelectedFileText.Text = Path.GetFileName(_datPath ?? string.Empty);
                    VersionText.Text = _datModel?.VersionName ?? GetVersionName();
                    SetExportEnabled(false);
                    foreach (var card in SpriteGrid.Children.OfType<Border>())
                    {
                        card.BorderBrush = ThemeBrush("CardBorderBrush");
                        card.Background = ThemeBrush("CardBackgroundBrush");
                    }
                }
            }
            else
            {
                _selectedThingIds.Add(thing.Id);
                _thingSelectionAnchorId = thing.Id;
                SelectThing(thing, preserveMultiSelection: true);
            }
            UpdateDatActionToolbar();
            UpdatePagerText();
            return;
        }

        SelectThing(thing);
    }

    private void UpdateDatActionToolbar()
    {
        if (DatActionToolbar is null) return;
        var datMode = IsObjectMode && _datModel is not null && _datPath is not null;
        DatActionToolbar.Visibility = datMode ? Visibility.Visible : Visibility.Collapsed;
        var hasSelectedThing = datMode && _selectedThing is not null
            && _selectedThing.Category == SelectedDatCategory
            && _datModel!.Things.Any(thing => thing.Category == _selectedThing.Category && thing.Id == _selectedThing.Id);
        DatNewButton.IsEnabled = DatImportButton.IsEnabled = datMode;
        DatReplaceButton.IsEnabled = DatDuplicateButton.IsEnabled = DatDeleteButton.IsEnabled = hasSelectedThing;
        DatExportButton.IsEnabled = datMode;
        DatTransferButton.IsEnabled = datMode && _sprStream is not null && _filePath is not null && !_sprEditInProgress;
        DatReplaceAcrossWindowsButton.IsEnabled = hasSelectedThing && _sprStream is not null && !_sprEditInProgress;
        UpdateSpriteActionToolbar();
    }

    private async Task LoadSelectedThingInfoPreviewAsync()
    {
        _infoPreviewRenderVersion++;
        if (_isRenderingInfoPreview || _selectedThing is null || _selectedFrameGroup is null || _sprStream is null)
            return;

        _isRenderingInfoPreview = true;
        try
        {
            while (_selectedThing is { } thing && _selectedFrameGroup is { } group && _sprStream is { } stream)
            {
                var currentRequest = _infoPreviewRenderVersion;
                var filePath = _filePath;
                var frameIndex = Math.Clamp(_infoPreviewFrame, 0, Math.Max(0, group.Frames.Count - 1));
                var bitmap = await ReadThingBitmapAsync(thing, group, frameIndex,
                    _selectedPatternX, _selectedPatternY, _selectedPatternZ, -1,
                    stream.SafeFileHandle, cacheSelectedSprites: false);
                if (currentRequest != _infoPreviewRenderVersion || !ReferenceEquals(stream, _sprStream)
                    || filePath != _filePath || !ReferenceEquals(thing, _selectedThing))
                    continue;

                SetInfoPreviewBitmap(bitmap);
                return;
            }
        }
        catch (Exception ex)
        {
            if (_selectedThing is not null)
                SetError(Text($"Não foi possível renderizar o preview: {ex.Message}", $"Could not render the preview: {ex.Message}"));
        }
        finally { _isRenderingInfoPreview = false; }
    }

    private async Task LoadSelectedThingPreviewAsync()
    {
        var requestVersion = ++_previewRenderVersion;
        if (_isRenderingThingPreview || _selectedThing is null || _selectedFrameGroup is null || _sprStream is null)
            return;

        _isRenderingThingPreview = true;
        try
        {
            while (_selectedThing is { } thing && _selectedFrameGroup is { } group && _sprStream is { } stream)
            {
                var currentRequest = _previewRenderVersion;
                var filePath = _filePath;
                var frameIndex = Math.Clamp(_selectedAnimationFrame, 0, Math.Max(0, group.Frames.Count - 1));
                var bitmap = await ReadThingBitmapAsync(thing, group, frameIndex,
                    _selectedPatternX, _selectedPatternY, _selectedPatternZ, _selectedLayer,
                    stream.SafeFileHandle, cacheSelectedSprites: true);
                if (currentRequest != _previewRenderVersion || !ReferenceEquals(stream, _sprStream)
                    || filePath != _filePath || !ReferenceEquals(thing, _selectedThing))
                {
                    if (_previewRenderVersion != requestVersion && _selectedThing is not null && _sprStream is not null)
                        continue;
                    return;
                }
                if (bitmap is not null)
                {
                    SetCanvasPreviewBitmap(bitmap);
                    SetExportEnabled(true);
                }
                else
                {
                    SetCanvasPreviewBitmap(null);
                    SetExportEnabled(false);
                }
                return;
            }
        }
        catch (Exception ex)
        {
            if (_selectedThing is not null)
                SetError(Text($"Não foi possível renderizar a animação: {ex.Message}", $"Could not render the animation: {ex.Message}"));
        }
        finally { _isRenderingThingPreview = false; }
    }

    private async Task<BitmapSource?> ReadThingBitmapAsync(DatThing thing, Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        var group = thing.PreviewGroup;
        if (group is null) return null;
        var defaultPatternX = thing.Category == "outfit" ? Math.Min(2, Math.Max(0, group.PatternX - 1)) : 0;
        var thumbnailLayer = thing.Category == "outfit" ? 0 : -1;
        return await ReadThingBitmapAsync(thing, group, 0, defaultPatternX, 0, 0, thumbnailLayer, handle, cacheSelectedSprites: false);
    }

    private async Task<BitmapSource?> ReadThingBitmapAsync(DatThing thing, DatFrameGroup group, int frameIndex,
        int patternX, int patternY, int patternZ, int selectedLayer,
        Microsoft.Win32.SafeHandles.SafeFileHandle handle, bool cacheSelectedSprites, bool colorizeOutfitPreview = true)
    {
        if (group.Width <= 0 || group.Height <= 0 || group.Layers <= 0
            || group.PatternX <= 0 || group.PatternY <= 0 || group.PatternZ <= 0
            || group.Frames.Count == 0) return null;
        frameIndex = Math.Clamp(frameIndex, 0, group.Frames.Count - 1);
        patternX = Math.Clamp(patternX, 0, group.PatternX - 1);
        patternY = Math.Clamp(patternY, 0, group.PatternY - 1);
        patternZ = Math.Clamp(patternZ, 0, group.PatternZ - 1);
        selectedLayer = selectedLayer < 0 ? -1 : Math.Clamp(selectedLayer, 0, group.Layers - 1);

        var tileCount = group.Width * group.Height;
        var spritesPerPattern = tileCount * group.Layers;
        var patternIndex = (patternZ * group.PatternY + patternY) * group.PatternX + patternX;
        var spriteOffset = patternIndex * spritesPerPattern;
        var frameSprites = group.Frames[frameIndex].SpriteIds;
        var colorizeOutfit = colorizeOutfitPreview && thing.Category == "outfit" && group.Layers >= 2 && selectedLayer <= 0;
        var renderedLayers = colorizeOutfit ? [0, 1]
            : selectedLayer < 0 ? Enumerable.Range(0, group.Layers).ToArray() : [selectedLayer];
        var spriteRefs = new uint[renderedLayers.Length * tileCount];
        var spriteIndex = 0;
        foreach (var layer in renderedLayers)
        for (var tile = 0; tile < tileCount; tile++)
        {
            var sourceIndex = spriteOffset + layer * tileCount + tile;
            spriteRefs[spriteIndex++] = sourceIndex < frameSprites.Length ? frameSprites[sourceIndex] : 0;
        }
        var sources = await Task.WhenAll(spriteRefs.Select(async spriteId =>
            spriteId == 0 ? null : await ReadSelectedThingSpriteBitmapAsync(spriteId, handle, cacheSelectedSprites)));
        var outputWidth = group.Width * SpriteSide;
        var outputHeight = group.Height * SpriteSide;
        var output = new byte[outputWidth * outputHeight * 4];
        var tilePixels = new byte[SpritePixelCount * 4];
        var blendPixels = colorizeOutfit ? new byte[SpritePixelCount * 4] : null;
        spriteIndex = 0;
        for (var tile = 0; tile < tileCount; tile++)
        {
            var tileX = tile % group.Width;
            var tileY = tile / group.Width;
            // Object Builder anchors multi-tile things at their bottom-right tile.
            // DAT indexes still advance left-to-right, top-to-bottom within each layer,
            // but the rendered canvas places those tiles in reverse coordinates.
            var destinationTileX = group.Width - tileX - 1;
            var destinationTileY = group.Height - tileY - 1;
            if (colorizeOutfit)
            {
                var baseSource = tile < sources.Length ? sources[tile] : null;
                var blendIndex = tileCount + tile;
                var blendSource = blendIndex < sources.Length ? sources[blendIndex] : null;
                if (baseSource is not null) baseSource.CopyPixels(tilePixels, 32 * 4, 0);
                else Array.Clear(tilePixels);
                if (blendSource is not null) blendSource.CopyPixels(blendPixels!, 32 * 4, 0);
                else Array.Clear(blendPixels!);
                CompositeColorizedOutfitTile(tilePixels, blendPixels!, output, outputWidth,
                    destinationTileX * SpriteSide, destinationTileY * SpriteSide);
                continue;
            }

            for (var layer = 0; layer < renderedLayers.Length; layer++)
            {
                var refIndex = layer * tileCount + tile;
                if (refIndex >= sources.Length || sources[refIndex] is not BitmapSource source) continue;
                source.CopyPixels(tilePixels, 32 * 4, 0);
                for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    var local = (y * 32 + x) * 4;
                    var destination = ((destinationTileY * 32 + y) * outputWidth + destinationTileX * 32 + x) * 4;
                    var sourceAlpha = tilePixels[local + 3];
                    if (sourceAlpha == 0) continue;
                    var destAlpha = output[destination + 3];
                    var outAlpha = sourceAlpha + destAlpha * (255 - sourceAlpha) / 255;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var numerator = tilePixels[local + channel] * sourceAlpha + output[destination + channel] * destAlpha * (255 - sourceAlpha) / 255;
                        output[destination + channel] = (byte)(outAlpha == 0 ? 0 : numerator / outAlpha);
                    }
                    output[destination + 3] = (byte)outAlpha;
                }
            }
        }
        var bitmap = BitmapSource.Create(outputWidth, outputHeight, 96, 96, PixelFormats.Bgra32, null, output, outputWidth * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void CompositeColorizedOutfitTile(byte[] basePixels, byte[] blendPixels,
        byte[] output, int outputWidth, int destinationX, int destinationY)
    {
        for (var y = 0; y < SpriteSide; y++)
        for (var x = 0; x < SpriteSide; x++)
        {
            var source = (y * SpriteSide + x) * 4;
            var destination = ((destinationY + y) * outputWidth + destinationX + x) * 4;
            var red = (int)blendPixels[source + 2];
            var green = (int)blendPixels[source + 1];
            var alpha = (int)basePixels[source + 3];

            // ObjectBuilder uses the blue mask for feet, then splits the remaining
            // mask channels into head, body, and legs. Its default outfit palette is white.
            alpha = CompositeMask(alpha, blendPixels[source]);
            alpha = CompositeMask(alpha, Math.Clamp(red + green - 255, 0, 255));
            alpha = CompositeMask(alpha, Math.Clamp(red - green, 0, 255));
            alpha = CompositeMask(alpha, Math.Clamp(green - red, 0, 255));

            output[destination] = basePixels[source];
            output[destination + 1] = basePixels[source + 1];
            output[destination + 2] = basePixels[source + 2];
            output[destination + 3] = (byte)alpha;
        }
    }

    private static int CompositeMask(int destinationAlpha, int maskAlpha) =>
        maskAlpha + destinationAlpha * (255 - maskAlpha) / 255;

    private async Task<BitmapSource?> ReadSelectedThingSpriteBitmapAsync(uint id,
        Microsoft.Win32.SafeHandles.SafeFileHandle handle, bool useSelectionCache)
    {
        if (useSelectionCache && _selectedThingSpriteBitmaps.TryGetValue(id, out var cached)) return cached;
        var bitmap = await ReadSpriteBitmapAsync(id, handle);
        if (useSelectionCache && bitmap is not null) _selectedThingSpriteBitmaps[id] = bitmap;
        return bitmap;
    }

    private string CategoryName(string category) => category switch
    {
        "outfit" => Text("Outfit", "Outfit"), "effect" => Text("Efeito", "Effect"),
        "missile" => Text("Projétil", "Missile"), _ => Text("Item", "Item")
    };

    private Border CreateSpriteCard(uint id)
    {
        var imageSize = _gridMode ? 48 : 30;
        var tileSize = _gridMode ? 62 : 34;
        var image = new Image { Width = imageSize, Height = imageSize, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var checker = new Border
        {
            Width = tileSize, Height = tileSize, CornerRadius = new CornerRadius(4), Background = ThemeBrush("CardBackgroundBrush"),
            Child = image, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = id.ToString(), Foreground = ThemeBrush("TextBrush"), FontSize = 10,
            HorizontalAlignment = _gridMode ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Margin = _gridMode ? new Thickness(0, 6, 0, 0) : new Thickness(9, 0, 0, 0)
        };
        var content = new StackPanel
        {
            Orientation = _gridMode ? Orientation.Vertical : Orientation.Horizontal,
            HorizontalAlignment = _gridMode ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { checker, label }
        };
        var selected = _selectedId == id;
        var card = new Border
        {
            Width = _gridMode ? 82 : 254, Height = _gridMode ? 98 : 43, Margin = new Thickness(1), Padding = new Thickness(_gridMode ? 7 : 4), CornerRadius = new CornerRadius(4),
            Background = (selected ? ThemeBrush("SelectedBackgroundBrush") : ThemeBrush("CardBackgroundBrush")), BorderBrush = ThemeBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1), Child = content, Tag = id, Cursor = System.Windows.Input.Cursors.Hand
        };
        card.MouseLeftButtonUp += (_, _) => SelectSprite(id);
        if (selected) card.BorderBrush = (Brush)FindResource("AccentBrush");
        return card;
    }

    private async Task LoadPageBitmapsAsync(IEnumerable<uint> ids)
    {
        var stream = _sprStream;
        var epochPath = _filePath;
        if (stream is null || epochPath is null) return;
        var results = await Task.WhenAll(ids.Select(async id => (Id: id, Bitmap: await ReadSpriteBitmapAsync(id, stream.SafeFileHandle))));
        if (!ReferenceEquals(stream, _sprStream) || epochPath != _filePath) return;
        foreach (var (id, bitmap) in results)
        {
            if (bitmap is null) continue;
            _pageBitmaps[id] = bitmap;
            foreach (var card in SpriteGrid.Children.OfType<Border>())
            {
                if (card.Tag is uint cardId && cardId == id && card.Child is StackPanel stack && stack.Children[0] is Border thumbnail && thumbnail.Child is Image image)
                {
                    image.Source = bitmap;
                    break;
                }
            }
        }
        if (!IsObjectMode && _selectedId is uint selected && _pageBitmaps.TryGetValue(selected, out var selectedImage)) SetPreviewBitmap(selectedImage);
    }

    private async Task<BitmapSource?> ReadSpriteBitmapAsync(uint id, Microsoft.Win32.SafeHandles.SafeFileHandle handle)
    {
        if (_pendingPixelSprites.TryGetValue(id, out var pendingBitmap)) return pendingBitmap;
        if (id == 0 || id > _offsets.Length) return null;
        var offset = _offsets[id - 1];
        if (offset == 0 || offset + 5L > RandomAccess.GetLength(handle)) return CreateTransparentBitmap();
        var spriteHeader = new byte[5];
        await ReadExactlyAsync(handle, spriteHeader, offset);
        var length = BinaryPrimitives.ReadUInt16LittleEndian(spriteHeader.AsSpan(3));
        if (length == 0) return CreateTransparentBitmap();
        if (offset + 5L + length > RandomAccess.GetLength(handle)) return null;
        var compressed = new byte[length];
        await ReadExactlyAsync(handle, compressed, offset + 5L);
        var pixels = DecodePixels(compressed);
        return pixels is null ? null : ToBitmap(pixels);
    }

    private async Task<int> DetectSpritePixelStrideAsync(FileStream stream)
    {
        var fileLength = RandomAccess.GetLength(stream.SafeFileHandle);
        var offsets = _offsets;
        foreach (var offset in offsets)
        {
            if (offset == 0 || offset + 5L > fileLength) continue;
            var header = new byte[5];
            await ReadExactlyAsync(stream.SafeFileHandle, header, offset);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(3));
            if (length == 0 || offset + 5L + length > fileLength) continue;
            var compressed = new byte[length];
            await ReadExactlyAsync(stream.SafeFileHandle, compressed, offset + 5L);
            if (IsValidCompressedPixels(compressed, 4)) return 4;
            if (IsValidCompressedPixels(compressed, 3)) return 3;
        }
        return 4;
    }

    private static bool IsValidCompressedPixels(ReadOnlySpan<byte> compressed, int stride)
    {
        var position = 0;
        var pixel = 0;
        while (position + 4 <= compressed.Length && pixel < SpritePixelCount)
        {
            var transparent = BinaryPrimitives.ReadUInt16LittleEndian(compressed.Slice(position, 2));
            var colored = BinaryPrimitives.ReadUInt16LittleEndian(compressed.Slice(position + 2, 2));
            position += 4;
            if (pixel + transparent + colored > SpritePixelCount || position + (long)colored * stride > compressed.Length)
                return false;
            pixel += transparent + colored;
            position += colored * stride;
        }
        return position == compressed.Length && pixel <= SpritePixelCount;
    }

    private static void WriteSprWithAppendedSprites(string sourcePath, string temporaryPath,
        IEnumerable<BitmapSource> sprites, int appendedSpriteCount, int pixelStride, uint signature,
        uint[] originalOffsets, int originalHeaderSize,
        IReadOnlyDictionary<uint, BitmapSource>? replacements = null, IReadOnlySet<uint>? deletions = null)
    {
        var originalCount = originalOffsets.Length;
        var newCount = checked((ulong)originalCount + (ulong)appendedSpriteCount);
        if (newCount > uint.MaxValue) throw new InvalidDataException("The SPR file cannot contain more sprites.");
        var headerSize = originalHeaderSize == 8 || newCount >= ushort.MaxValue ? 8 : 6;
        var oldDataStart = checked(originalHeaderSize + (long)originalCount * 4);
        var newDataStart = checked(headerSize + (long)newCount * 4);
        var offsetShift = newDataStart - oldDataStart;
        var encodedReplacements = new List<(int Index, byte[] Data)>();
        if (replacements is not null)
        {
            foreach (var (id, bitmap) in replacements)
            {
                if (id == 0 || id > originalCount)
                    throw new InvalidDataException($"Sprite ID {id} is outside the existing SPR range.");
                encodedReplacements.Add(((int)id - 1, EncodeSpriteData(bitmap, pixelStride)));
            }
        }
        if (deletions is not null && deletions.Any(id => id == 0 || id > originalCount))
            throw new InvalidDataException("A sprite ID to delete is outside the existing SPR range.");
        var newOffsets = new uint[(int)newCount];

        for (var index = 0; index < originalOffsets.Length; index++)
        {
            var originalOffset = originalOffsets[index];
            if (originalOffset == 0) continue;
            var shifted = checked((ulong)originalOffset + (ulong)offsetShift);
            if (shifted > uint.MaxValue) throw new InvalidDataException("The updated SPR offsets exceed the file format limit.");
            newOffsets[index] = (uint)shifted;
        }
        if (deletions is not null)
            foreach (var id in deletions) newOffsets[id - 1] = 0;

        const int copyBufferSize = 1024 * 1024;
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, copyBufferSize, FileOptions.SequentialScan);
        if (source.Length < oldDataStart) throw new InvalidDataException("The SPR data section is truncated.");

        using var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, copyBufferSize, FileOptions.SequentialScan);
        using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
        writer.Write(signature);
        if (headerSize == 8) writer.Write((uint)newCount);
        else writer.Write((ushort)newCount);
        var offsetTable = new byte[checked(newOffsets.Length * sizeof(uint))];
        destination.Write(offsetTable);

        source.Position = oldDataStart;
        source.CopyTo(destination, copyBufferSize);
        foreach (var (index, encoded) in encodedReplacements)
        {
            if (encoded.Length == 0)
            {
                newOffsets[index] = 0;
                continue;
            }
            if (destination.Position > uint.MaxValue) throw new InvalidDataException("The updated SPR is too large for its offset table.");
            newOffsets[index] = (uint)destination.Position;
            destination.Write(encoded);
        }

        var appendedIndex = 0;
        foreach (var sprite in sprites)
        {
            if (appendedIndex >= appendedSpriteCount)
                throw new InvalidDataException("The sprite source produced more sprites than expected.");
            if (sprite.PixelWidth != SpriteSide || sprite.PixelHeight != SpriteSide)
                throw new InvalidDataException("All sprites must be 32 × 32 pixels.");
            var encoded = EncodeSpriteData(sprite, pixelStride);
            if (encoded.Length > 0)
            {
                if (destination.Position > uint.MaxValue) throw new InvalidDataException("The updated SPR is too large for its offset table.");
                newOffsets[originalCount + appendedIndex] = (uint)destination.Position;
                destination.Write(encoded);
            }
            appendedIndex++;
        }
        if (appendedIndex != appendedSpriteCount)
            throw new InvalidDataException("The sprite source produced fewer sprites than expected.");

        var finalLength = destination.Position;
        destination.Position = 4;
        if (headerSize == 8) writer.Write((uint)newCount);
        else writer.Write((ushort)newCount);
        for (var index = 0; index < newOffsets.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(offsetTable.AsSpan(index * sizeof(uint), sizeof(uint)), newOffsets[index]);
        destination.Write(offsetTable);
        writer.Flush();
        destination.Position = finalLength;
        destination.Flush(flushToDisk: true);
    }

    private static void DeleteSprEntriesInPlace(string path, uint expectedSignature,
        uint[] originalOffsets, int headerSize, IReadOnlySet<uint> deletions)
    {
        var originalCount = originalOffsets.Length;
        if (deletions.Any(id => id == 0 || id > originalCount))
            throw new InvalidDataException("A sprite ID to delete is outside the existing SPR range.");

        // Match Object Build: trailing IDs leave the index, while deleted IDs in the
        // middle become empty so DAT references to later sprites keep their IDs.
        var newCount = originalCount;
        while (newCount > 1 && deletions.Contains((uint)newCount)) newCount--;

        if (headerSize is not (6 or 8)) throw new InvalidDataException("Invalid SPR header size.");
        var oldDataStart = checked(headerSize + (long)originalCount * 4);
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None,
            64 * 1024, FileOptions.RandomAccess);
        if (file.Length < oldDataStart) throw new InvalidDataException("The SPR data section is truncated.");

        Span<byte> header = stackalloc byte[8];
        file.Position = 0;
        file.ReadExactly(header[..headerSize]);
        var signature = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var fileCount = headerSize == 8
            ? BinaryPrimitives.ReadUInt32LittleEndian(header[4..])
            : BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        if (signature != expectedSignature || fileCount != originalCount)
            throw new IOException("The SPR file changed after it was opened. Reopen it and try again.");

        // Zero contiguous ranges in one write each. The sprite payload remains at the
        // same absolute offsets, so deletion is proportional to the selected ranges.
        var middleIds = deletions.Where(id => id <= newCount).Order().ToArray();
        var first = 0;
        while (first < middleIds.Length)
        {
            var last = first;
            while (last + 1 < middleIds.Length && middleIds[last + 1] == middleIds[last] + 1) last++;
            var bytesToClear = checked((last - first + 1) * sizeof(uint));
            var zeros = new byte[bytesToClear];
            file.Position = checked(headerSize + ((long)middleIds[first] - 1) * sizeof(uint));
            file.Write(zeros);
            first = last + 1;
        }

        file.Position = 4;
        if (headerSize == 8)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], checked((uint)newCount));
            file.Write(header[4..8]);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..], checked((ushort)newCount));
            file.Write(header[4..6]);
        }
        file.Flush(flushToDisk: true);
    }

    private static byte[] EncodeSpriteData(BitmapSource bitmap, int stride)
    {
        if (bitmap.PixelWidth != SpriteSide || bitmap.PixelHeight != SpriteSide)
            throw new InvalidDataException("Imported sprites must be 32 × 32 pixels.");
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[SpritePixelCount * 4];
        converted.CopyPixels(pixels, SpriteSide * 4, 0);
        using var compressedStream = new MemoryStream();
        using (var writer = new BinaryWriter(compressedStream, Encoding.UTF8, leaveOpen: true))
        {
            var index = 0;
            while (index < SpritePixelCount)
            {
                var transparentCount = 0;
                while (index < SpritePixelCount && pixels[index * 4 + 3] == 0)
                {
                    transparentCount++;
                    index++;
                }
                if (index >= SpritePixelCount) break;

                var coloredStart = index;
                while (index < SpritePixelCount && pixels[index * 4 + 3] != 0) index++;
                var coloredCount = index - coloredStart;
                writer.Write((ushort)transparentCount);
                writer.Write((ushort)coloredCount);
                for (var pixel = coloredStart; pixel < index; pixel++)
                {
                    var pixelOffset = pixel * 4;
                    writer.Write(pixels[pixelOffset + 2]);
                    writer.Write(pixels[pixelOffset + 1]);
                    writer.Write(pixels[pixelOffset]);
                    if (stride == 4) writer.Write(pixels[pixelOffset + 3]);
                }
            }
        }

        var compressed = compressedStream.ToArray();
        if (compressed.Length == 0) return [];
        if (compressed.Length > ushort.MaxValue) throw new InvalidDataException("A sprite is too large to store in the SPR format.");
        using var spriteStream = new MemoryStream(compressed.Length + 5);
        using (var writer = new BinaryWriter(spriteStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)0xFF);
            writer.Write((byte)0x00);
            writer.Write((byte)0xFF);
            writer.Write((ushort)compressed.Length);
            writer.Write(compressed);
        }
        return spriteStream.ToArray();
    }

    private static byte[]? DecodePixels(byte[] compressed)
    {
        foreach (var stride in new[] { 4, 3 })
        {
            var pixels = new byte[SpritePixelCount * 4];
            var position = 0;
            var pixel = 0;
            var valid = true;
            while (position + 4 <= compressed.Length && pixel < SpritePixelCount)
            {
                var transparent = BinaryPrimitives.ReadUInt16LittleEndian(compressed.AsSpan(position, 2));
                var colored = BinaryPrimitives.ReadUInt16LittleEndian(compressed.AsSpan(position + 2, 2));
                position += 4;
                if (pixel + transparent + colored > SpritePixelCount || position + (long)colored * stride > compressed.Length)
                {
                    valid = false;
                    break;
                }
                pixel += transparent;
                for (var i = 0; i < colored; i++)
                {
                    var index = pixel++ * 4;
                    pixels[index] = compressed[position + 2];
                    pixels[index + 1] = compressed[position + 1];
                    pixels[index + 2] = compressed[position];
                    pixels[index + 3] = stride == 4 ? compressed[position + 3] : (byte)255;
                    position += stride;
                }
            }
            if (valid && position == compressed.Length && pixel <= SpritePixelCount) return pixels;
        }
        return null;
    }

    private static BitmapSource ToBitmap(byte[] pixels) => ToBitmap(pixels, SpriteSide, SpriteSide);

    private static BitmapSource ToBitmap(byte[] pixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || pixels.LongLength != (long)width * height * 4)
            throw new ArgumentException("Pixel data does not match the requested bitmap size.", nameof(pixels));
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CreateTransparentBitmap() => ToBitmap(new byte[SpritePixelCount * 4]);

    private void SelectSprite(uint id)
    {
        ResetThingControls();
        _selectedThingSpriteBitmaps.Clear();
        _selectedId = id;
        SelectedFileText.Text = _filePath is null ? "—" : Path.GetFileName(_filePath);
        VersionText.Text = GetVersionName();
        SetSelectionSummary(id.ToString(), $"{SpriteSide} × {SpriteSide} px");
        SetPreviewBitmap(null);
        if (_pageBitmaps.TryGetValue(id, out var bitmap))
        {
            SetPreviewBitmap(bitmap);
            SetExportEnabled(true);
        }
        else if (_sprStream is not null)
        {
            SetExportEnabled(false);
            _ = LoadSelectedBitmapAsync(id, _sprStream, _filePath);
        }
        foreach (var card in SpriteGrid.Children.OfType<Border>())
        {
            var isSelected = card.Tag is uint cardId && cardId == id;
            card.BorderBrush = isSelected ? (Brush)FindResource("AccentBrush") : ThemeBrush("CardBorderBrush");
            card.Background = isSelected ? ThemeBrush("SelectedBackgroundBrush") : ThemeBrush("CardBackgroundBrush");
        }
    }

    private async Task LoadSelectedBitmapAsync(uint id, FileStream stream, string? path)
    {
        var bitmap = await ReadSpriteBitmapAsync(id, stream.SafeFileHandle);
        if (!ReferenceEquals(stream, _sprStream) || path != _filePath || _selectedId != id || bitmap is null) return;
        _pageBitmaps[id] = bitmap;
        SetPreviewBitmap(bitmap);
        SetExportEnabled(true);
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedId is not uint id || PreviewImage.Source is not BitmapSource bitmap) return;
        ExportSpriteAsPng(id, bitmap);
    }

    private void ExportSpriteAsPng(uint id, BitmapSource bitmap)
    {
        var dialog = new SaveFileDialog
        {
            Title = Text("Exportar sprite como PNG", "Export sprite as PNG"),
            FileName = $"sprite-{id}.png", Filter = "Imagem PNG (*.png)|*.png", AddExtension = true, DefaultExt = ".png"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(dialog.FileName);
            encoder.Save(file);
        }
        catch (IOException ex) { SetError(ex.Message); }
    }

    private void PreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_page == 0) return;
        _page--;
        RenderPage();
    }

    private void FirstPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_page == 0) return;
        _page = 0;
        RenderPage();
    }

    private void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_page + 1 >= PageCount) return;
        _page++;
        RenderPage();
    }

    private void LastPageButton_Click(object sender, RoutedEventArgs e)
    {
        var lastPage = Math.Max(0, PageCount - 1);
        if (_page == lastPage) return;
        _page = lastPage;
        RenderPage();
    }

    private int PageCount
    {
        get
        {
            if (IsObjectMode && _datModel is not null)
            {
                var category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "item";
                var things = _datModel.Things.Where(thing => thing.Category == category).ToList();
                if (things.Count == 0) return 1;
                var firstId = things.Min(thing => thing.Id);
                var lastId = things.Max(thing => thing.Id);
                return (int)(((ulong)lastId - firstId) / PageSize) + 1;
            }
            return _spriteCount == 0 ? 1 : (int)Math.Ceiling((double)_spriteCount / PageSize);
        }
    }

    private void UpdatePagerText()
    {
        if (PageText is null) return;
        var rangeStart = 1L + (long)_page * PageSize;
        var rangeEnd = Math.Min((long)_spriteCount, rangeStart + PageSize - 1);
        if (IsObjectMode && _datModel is not null)
        {
            var category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "item";
            var things = _datModel.Things.Where(thing => thing.Category == category).ToList();
            if (things.Count > 0)
            {
                rangeStart = (long)things.Min(thing => thing.Id) + (long)_page * PageSize;
                rangeEnd = Math.Min((long)things.Max(thing => thing.Id), rangeStart + PageSize - 1);
            }
        }
        var rangeLabel = rangeEnd >= rangeStart ? $"{rangeStart}–{rangeEnd}" : "—";
        var selectedCount = IsObjectMode ? GetSelectedDatThings().Count : 0;
        var selectedSuffix = selectedCount > 1 ? Text($" · {selectedCount} selecionados", $" · {selectedCount} selected") : string.Empty;
        PageText.Text = Text($"{rangeLabel} · Página {_page + 1} de {PageCount}{selectedSuffix}", $"{rangeLabel} · Page {_page + 1} of {PageCount}{selectedSuffix}");
        PreviousPageButton.IsEnabled = _page > 0;
        NextPageButton.IsEnabled = _page + 1 < PageCount;
        FirstPageButton.IsEnabled = _page > 0;
        LastPageButton.IsEnabled = _page + 1 < PageCount;
        UpdateSpriteActionToolbar();
    }

    private string GetVersionName()
    {
        var key = _signature.ToString("X8");
        return SpriteVersions.TryGetValue(key, out var version) ? $"Tibia {version}" : Text("Versão não identificada", "Unknown version");
    }

    private void UpdateEmptyState()
    {
        if (DropPanel is null) return;
        DropPanel.Visibility = _spriteCount > 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = SpriteGrid.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetError(string? message)
    {
        ErrorText.Text = message ?? string.Empty;
        ErrorText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }
}
