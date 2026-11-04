using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AetherLauncher.Controls;

/// <summary>
/// Eine Karte wie im Aether-Hub: gefüllte Fläche, feine Kante, Glasglanz an der Oberkante.
/// Beim Überfahren legt sich eine zusätzliche Fläche weich darüber (Deckkraft statt
/// Farbwechsel) - dadurch läuft die Bewegung flüssig und die geteilten Pinsel des Themes
/// bleiben unangetastet.
/// </summary>
public class CardPanel : ContentControl
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(CardPanel),
        new PropertyMetadata(null));

    public static readonly DependencyProperty HoverFillProperty = DependencyProperty.Register(
        nameof(HoverFill), typeof(Brush), typeof(CardPanel),
        new PropertyMetadata(null));

    public static readonly DependencyProperty StrokeBrushProperty = DependencyProperty.Register(
        nameof(StrokeBrush), typeof(Brush), typeof(CardPanel),
        new PropertyMetadata(null));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(CardPanel),
        new PropertyMetadata(1.0));

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(CornerRadius), typeof(CardPanel),
        new PropertyMetadata(new CornerRadius(10)));

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush? HoverFill
    {
        get => (Brush?)GetValue(HoverFillProperty);
        set => SetValue(HoverFillProperty, value);
    }

    public Brush? StrokeBrush
    {
        get => (Brush?)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public CornerRadius Radius
    {
        get => (CornerRadius)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    private ScaleTransform? _hoverScale;
    private TranslateTransform? _hoverLift;

    /// <summary>Karten heben sich beim Überfahren einen Hauch an, statt nur die Fläche zu wechseln.</summary>
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (!IsEnabled) return;

        EnsureHoverMotion();
        if (_hoverScale is not null) Anim.Press(_hoverScale, 1.004, 180);
        if (_hoverLift is not null) Anim.Move(_hoverLift, 0, -2, 180);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverScale is not null) Anim.Press(_hoverScale, 1, 230);
        if (_hoverLift is not null) Anim.Move(_hoverLift, 0, 0, 230);
    }

    /// <summary>
    /// Legt eine eigene, veränderbare Transformationsgruppe an. Eine aus einer geteilten
    /// Vorlage übernommene Gruppe wäre eingefroren und ließe sich nicht animieren.
    /// </summary>
    private void EnsureHoverMotion()
    {
        if (_hoverScale is not null && _hoverLift is not null) return;

        var group = RenderTransform as TransformGroup;
        if (group is null || group.IsFrozen)
        {
            var replacement = new TransformGroup();
            if (RenderTransform is not null) replacement.Children.Add(RenderTransform);
            RenderTransform = replacement;
            group = replacement;
        }

        _hoverScale = group.Children.OfType<ScaleTransform>().FirstOrDefault();
        if (_hoverScale is null)
        {
            _hoverScale = new ScaleTransform(1, 1);
            group.Children.Add(_hoverScale);
        }

        _hoverLift = group.Children.OfType<TranslateTransform>().LastOrDefault();
        if (_hoverLift is null)
        {
            _hoverLift = new TranslateTransform();
            group.Children.Add(_hoverLift);
        }

        RenderTransformOrigin = new Point(0.5, 0.5);
    }
}
