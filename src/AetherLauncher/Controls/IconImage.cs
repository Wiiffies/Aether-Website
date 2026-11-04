using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AetherLauncher.Controls;

/// <summary>
/// Ein PNG-Icon (weiße Linien auf transparentem Grund), das in eine beliebige Farbe
/// eingefärbt wird. Technik: das Bild dient als OpacityMask eines Rechtecks, das mit
/// <see cref="Tint"/> gefüllt ist. Dadurch bleibt die Kante sauber und die Farbe kommt
/// aus dem Theme - genauso wie es der Aether-Hub mit seiner Tint-Matrix macht.
/// </summary>
public class IconImage : Control
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(IconImage),
        new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(
        nameof(Tint), typeof(Brush), typeof(IconImage),
        new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty MaskBrushProperty = DependencyProperty.Register(
        nameof(MaskBrush), typeof(Brush), typeof(IconImage),
        new PropertyMetadata(null));

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Brush? Tint
    {
        get => (Brush?)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    /// <summary>Fertiger Brush für die Maske - bewusst als eigener Brush und nicht als
    /// verschachteltes ImageBrush im Template: Bindings in Freezables finden den DataContext nicht.</summary>
    public Brush? MaskBrush
    {
        get => (Brush?)GetValue(MaskBrushProperty);
        private set => SetValue(MaskBrushProperty, value);
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not IconImage icon) return;
        icon.MaskBrush = e.NewValue is ImageSource source
            ? new ImageBrush(source) { Stretch = Stretch.Uniform }
            : null;
    }
}
