using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Slopfin.UI.Views;

/// <summary>Slopfin's own logo: a gradient tile with a stylized fin, plus the wordmark.</summary>
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
            Child = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M5,27 C11,23 15,13 27,6 C24,14 24,21 30,27 Z M4,30 L32,30"),
                Fill = Brushes.White,
                Stroke = Brushes.White,
                StrokeThickness = 2.4,
                StrokeLineCap = PenLineCap.Round,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(8),
            }
        };
        _tile.Bind(Border.BackgroundProperty, _tile.GetResourceObservable("AccentGradientBrush"));
        _word = new TextBlock
        {
            Text = "Slopfin",
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
