using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SpriteStudio;

internal sealed record DatTransferTargetOption(MainWindow Window, string Label);

internal sealed class DatTransferTargetDialog : Window
{
    private readonly ComboBox _targets = new();

    public MainWindow? SelectedTarget => (_targets.SelectedItem as DatTransferTargetOption)?.Window;

    public DatTransferTargetDialog(IReadOnlyList<DatTransferTargetOption> targets, bool isEnglish)
    {
        string Text(string pt, string en) => isEnglish ? en : pt;
        Brush Brush(string key) =>
            (Brush)(Application.Current?.TryFindResource(key) ?? Brushes.Gray);

        Title = Text("Escolher editor de destino", "Choose destination editor");
        Width = 430;
        Height = 220;
        MinWidth = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brush("AppBackground");
        Foreground = Brush("TextBrush");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = Text("Para qual janela transferir?", "Which window should receive the objects?"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var description = new TextBlock
        {
            Text = Text("O editor escolhido receberá os objetos selecionados e seus sprites.",
                "The selected editor will receive the selected objects and their sprites."),
            Foreground = Brush("MutedBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(description, 1);
        root.Children.Add(description);

        _targets.ItemsSource = targets;
        _targets.DisplayMemberPath = nameof(DatTransferTargetOption.Label);
        _targets.SelectedIndex = targets.Count == 0 ? -1 : 0;
        Grid.SetRow(_targets, 2);
        root.Children.Add(_targets);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button
        {
            Content = Text("Cancelar", "Cancel"),
            MinWidth = 82,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        buttons.Children.Add(cancel);
        var transfer = new Button
        {
            Content = Text("Transferir", "Transfer"),
            Style = (Style)Application.Current.FindResource("PrimaryButton"),
            MinWidth = 92,
            Padding = new Thickness(10, 5, 10, 5),
            IsDefault = true
        };
        transfer.Click += (_, _) =>
        {
            if (SelectedTarget is not null) DialogResult = true;
        };
        buttons.Children.Add(transfer);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);
        Content = root;
    }
}