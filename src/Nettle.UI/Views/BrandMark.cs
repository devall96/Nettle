using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nettle.UI.Views;

/// <summary>Nettle's own logo: a theme-coloured tile with the sea nettle (<see cref="NettleMark"/>), plus the wordmark.</summary>
public sealed class BrandMark : StackPanel
{
    public static readonly StyledProperty<double> ScaleProperty = AvaloniaProperty.Register<BrandMark, double>(nameof(Scale), 1.0);
    public static readonly StyledProperty<bool> ShowWordmarkProperty = AvaloniaProperty.Register<BrandMark, bool>(nameof(ShowWordmark), true);

    public double Scale { get => GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    public bool ShowWordmark { get => GetValue(ShowWordmarkProperty); set => SetValue(ShowWordmarkProperty, value); }

    private readonly Border _tile;
    private readonly TextBlock _word;

    public BrandMark()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 12;
        _tile = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(12),
            Child = new NettleMark { Margin = new Thickness(5.5) },
        };
        _tile.Bind(Border.BackgroundProperty, _tile.GetResourceObservable("AccentGradientBrush"));
        ((NettleMark)_tile.Child!).Bind(NettleMark.FillProperty, _tile.GetResourceObservable("OnAccentBrush"));
        _word = new TextBlock
        {
            Text = "Nettle",
            FontSize = 26,
            FontWeight = FontWeight.ExtraBold,
            LetterSpacing = -0.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Children.Add(_tile);
        Children.Add(_word);
        Apply();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ScaleProperty || change.Property == ShowWordmarkProperty) Apply();
    }

    private void Apply()
    {
        if (_tile == null) return;
        _tile.Width = _tile.Height = 40 * Scale;
        _tile.CornerRadius = new CornerRadius(12 * Scale);
        _word.FontSize = 26 * Scale;
        _word.IsVisible = ShowWordmark;
        Spacing = 12 * Scale;
    }
}
