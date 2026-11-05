using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace AetherLauncher.Controls;







public class RippleButton : Button
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(RippleButton), new PropertyMetadata(Brushes.Transparent));

    public static readonly DependencyProperty HoverFillProperty = DependencyProperty.Register(
        nameof(HoverFill), typeof(Brush), typeof(RippleButton), new PropertyMetadata(null));

    public static readonly DependencyProperty StrokeBrushProperty = DependencyProperty.Register(
        nameof(StrokeBrush), typeof(Brush), typeof(RippleButton), new PropertyMetadata(null));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(RippleButton), new PropertyMetadata(0.0));

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(CornerRadius), typeof(RippleButton), new PropertyMetadata(new CornerRadius(9)));

    public static readonly DependencyProperty RippleFillProperty = DependencyProperty.Register(
        nameof(RippleFill), typeof(Brush), typeof(RippleButton),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF))));

    public static readonly DependencyProperty PressScaleProperty = DependencyProperty.Register(
        nameof(PressScale), typeof(double), typeof(RippleButton), new PropertyMetadata(0.965));

    private ScaleTransform? _scale;
    private Canvas? _rippleHost;
    private FrameworkElement? _clipTarget;
    private CornerRadius _clipRadius;

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

    public Brush? RippleFill
    {
        get => (Brush?)GetValue(RippleFillProperty);
        set => SetValue(RippleFillProperty, value);
    }

    
    public double PressScale
    {
        get => (double)GetValue(PressScaleProperty);
        set => SetValue(PressScaleProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        
        
        
        if (GetTemplateChild("PART_Root") is FrameworkElement root)
        {
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            _scale = new ScaleTransform(1, 1);
            root.RenderTransform = _scale;
        }

        _rippleHost = GetTemplateChild("PART_Ripple") as Canvas;
        _clipTarget = _rippleHost;
        _clipRadius = Radius;

        if (_clipTarget is not null)
        {
            _clipTarget.SizeChanged += (_, _) => UpdateRippleClip();
            UpdateRippleClip();
        }
    }

    
    private void UpdateRippleClip()
    {
        if (_clipTarget is null) return;
        _clipTarget.Clip = new RectangleGeometry(
            new Rect(0, 0, _clipTarget.ActualWidth, _clipTarget.ActualHeight),
            _clipRadius.TopLeft, _clipRadius.TopLeft);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (IsEnabled && _scale is not null) Anim.Press(_scale, 1.006, 180);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (_scale is not null) Anim.Press(_scale, PressScale, 70);
        SpawnRipple(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_scale is not null) Anim.Press(_scale, 1, 300, spring: true);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_scale is not null && e.LeftButton == MouseButtonState.Released) Anim.Press(_scale, 1, 220);
    }

    
    private void SpawnRipple(Point origin)
    {
        if (_rippleHost is null || RippleFill is null) return;

        var width = Math.Max(1, ActualWidth);
        var height = Math.Max(1, ActualHeight);

        
        var radius = new[]
        {
            Distance(origin, 0, 0), Distance(origin, width, 0),
            Distance(origin, 0, height), Distance(origin, width, height),
        }.Max();

        var ring = new Ellipse
        {
            Width = 0,
            Height = 0,
            Opacity = 0,
            Fill = RippleFill,
            IsHitTestVisible = false,
        };

        
        
        
        Canvas.SetLeft(ring, origin.X);
        Canvas.SetTop(ring, origin.Y);

        
        var duration = TimeSpan.FromMilliseconds(440);
        var easing = Anim.Out();

        ring.BeginAnimation(FrameworkElement.WidthProperty,
            new DoubleAnimation(radius * 2, duration) { From = 0, EasingFunction = easing });
        ring.BeginAnimation(FrameworkElement.HeightProperty,
            new DoubleAnimation(radius * 2, duration) { From = 0, EasingFunction = easing });
        ring.BeginAnimation(Canvas.LeftProperty,
            new DoubleAnimation(origin.X - radius, duration) { From = origin.X, EasingFunction = easing });
        ring.BeginAnimation(Canvas.TopProperty,
            new DoubleAnimation(origin.Y - radius, duration) { From = origin.Y, EasingFunction = easing });

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(350))
        {
            From = 0.55,
            BeginTime = TimeSpan.FromMilliseconds(70),
            EasingFunction = easing,
        };

        fade.Completed += (_, _) =>
        {
            _rippleHost?.Children.Remove(ring);
        };

        _rippleHost.Children.Add(ring);
        ring.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static double Distance(Point from, double x, double y)
        => Math.Sqrt(((from.X - x) * (from.X - x)) + ((from.Y - y) * (from.Y - y)));
}
