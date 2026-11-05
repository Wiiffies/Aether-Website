using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AetherLauncher.Controls;







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
