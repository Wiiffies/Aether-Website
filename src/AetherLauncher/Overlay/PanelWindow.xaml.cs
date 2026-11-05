using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AetherLauncher.Controls;
using AetherLauncher.Services;

namespace AetherLauncher.Overlay;







public partial class PanelWindow : Window
{
    private readonly OverlayHost _host;
    private readonly DispatcherTimer _follow;
    private bool _closing;

    
    
    private bool _dragging;
    private bool _settling;
    private double _dragCursorX;
    private double _dragCursorY;
    private double _dragStartLeft;
    private double _dragStartTop;
    private double _targetLeft;
    private double _targetTop;
    private double _settleFromLeft;
    private double _settleFromTop;
    private double _settleToLeft;
    private double _settleToTop;
    private DateTime _settleStarted;
    private const double SettleMilliseconds = 170;
    private const double SnapDistance = 24;
    private const double ScreenGap = 6;

    internal PanelWindow(OverlayHost host, string panelId, string title, string hint, FrameworkElement body)
    {
        InitializeComponent();

        _host = host;
        PanelId = panelId;

        _follow = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _follow.Tick += (_, _) => TickFollow();
        Header.LostMouseCapture += (_, _) => FinishDrag();

        
        
        
        
        
        
        
        
        SourceInitialized += (_, _) => HideFromTaskSwitcher();
        Loaded += (_, _) => ScheduleTaskSwitcherFix();
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true) ScheduleTaskSwitcherFix();
        };

        TitleText.Text = title;
        HintText.Text = hint;
        BodyHost.Content = body;

        Closing += (_, args) =>
        {
            
            if (_host.Quitting) return;
            args.Cancel = true;
            _host.HidePanel(PanelId, remember: true);
        };
    }

    public string PanelId { get; }

    
    
    
    
    private void HideFromTaskSwitcher()
    {
        try
        {
            TaskSwitcher.Hide(this);
        }
        catch (Exception ex)
        {
            Log.Warn($"The overlay window could not be hidden from Alt+Tab: {ex.Message}");
        }
    }

    
    internal bool HiddenFromTaskSwitcher => TaskSwitcher.IsHidden(this);

    
    
    
    
    
    private void ScheduleTaskSwitcherFix()
    {
        Dispatcher.BeginInvoke(new Action(HideFromTaskSwitcher), DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(new Action(HideFromTaskSwitcher), DispatcherPriority.ApplicationIdle);
    }

    
    public FrameworkElement Body => (FrameworkElement)BodyHost.Content;

    
    public void ShowAt(double left, double top, bool animate, int delay)
    {
        _closing = false;

        var first = !IsVisible;
        Left = left;
        Top = top;

        if (first)
        {
            Show();
            UpdateLayout();
        }
        else
        {
            UpdateLayout();
        }

        
        HideFromTaskSwitcher();
        ScheduleTaskSwitcherFix();

        ClampToScreen();

        if (animate) AnimateIn(delay);
        else
        {
            ResetCardMotion();
            Card.BeginAnimation(OpacityProperty, null);
            Card.Opacity = 1;
        }
    }

    private void AnimateIn(int delay)
    {
        var spring = _host.Playful;
        Card.Opacity = 1;

        Anim.Fade(Card, 0, 1, spring ? 220 : 170, delay);
        Anim.Pop(Card, open: true, milliseconds: spring ? 300 : 220);
        Anim.Slide(Card, spring ? -10 : -6, 0, spring ? 300 : 220, delay, spring);
    }

    
    public void HideSoftly(bool animate, Action? done = null)
    {
        if (!IsVisible)
        {
            done?.Invoke();
            return;
        }

        if (!animate)
        {
            ResetCardMotion();
            Card.BeginAnimation(OpacityProperty, null);
            Card.Opacity = 1;
            Hide();
            done?.Invoke();
            return;
        }

        if (_closing) return;
        _closing = true;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160))
        {
            From = Card.Opacity,
            EasingFunction = Anim.Out(),
        };

        
        
        Anim.Slide(Card, 0, 4, 160);
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            CardScale.BeginAnimation(property, new DoubleAnimation(0.985, TimeSpan.FromMilliseconds(160))
            {
                From = 1,
                EasingFunction = Anim.Out(),
            });
        }

        fade.Completed += (_, _) =>
        {
            _closing = false;
            if (!_host.IsPanelVisible(PanelId) || !_host.PanelsOpen)
            {
                ResetCardMotion();
                Hide();
                Card.BeginAnimation(OpacityProperty, null);
                Card.Opacity = 1;
            }

            done?.Invoke();
        };

        Card.BeginAnimation(OpacityProperty, fade);
    }

    
    private void ResetCardMotion()
    {
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            CardScale.BeginAnimation(property, null);
        }

        CardScale.ScaleX = 1;
        CardScale.ScaleY = 1;

        CardShift.BeginAnimation(TranslateTransform.XProperty, null);
        CardShift.BeginAnimation(TranslateTransform.YProperty, null);
        CardShift.X = 0;
        CardShift.Y = 0;
    }

    
    public void ClampToScreen()
    {
        var width = ActualWidth > 1 ? ActualWidth : Width;
        var height = ActualHeight > 1 ? ActualHeight : 300;
        var area = GameOverlay.WorkAreaAt((int)(Left + (width / 2)), (int)(Top + 20));

        Left = Math.Clamp(Left, area.Left + 4, Math.Max(area.Left + 4, area.Right - width - 4));
        Top = Math.Clamp(Top, area.Top + 4, Math.Max(area.Top + 4, area.Bottom - height - 4));
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        
        if (IsFromButton(e.OriginalSource)) return;

        
        _settling = false;
        _follow.Stop();

        var (x, y) = CursorDip();
        _dragCursorX = x;
        _dragCursorY = y;
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _targetLeft = Left;
        _targetTop = Top;
        _dragging = true;

        Header.CaptureMouse();
        SetLift(true);
        _follow.Start();
        e.Handled = true;
    }

    private void Header_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishDrag();
            return;
        }

        var (x, y) = CursorDip();
        _targetLeft = _dragStartLeft + (x - _dragCursorX);
        _targetTop = _dragStartTop + (y - _dragCursorY);
        ClampTarget();
    }

    private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        FinishDrag();
        e.Handled = true;
    }

    
    private void FinishDrag()
    {
        if (!_dragging) return;

        _dragging = false;
        if (Header.IsMouseCaptured) Header.ReleaseMouseCapture();
        _follow.Stop();
        SetLift(false);
        BeginSettle();
    }

    private void TickFollow()
    {
        if (_dragging)
        {
            var dx = _targetLeft - Left;
            var dy = _targetTop - Top;

            
            if (Math.Abs(dx) < 0.35 && Math.Abs(dy) < 0.35)
            {
                Left = _targetLeft;
                Top = _targetTop;
                return;
            }

            Left += dx * 0.55;
            Top += dy * 0.55;
            return;
        }

        if (!_settling) return;

        var progress = Math.Clamp((DateTime.UtcNow - _settleStarted).TotalMilliseconds / SettleMilliseconds, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);

        Left = _settleFromLeft + ((_settleToLeft - _settleFromLeft) * eased);
        Top = _settleFromTop + ((_settleToTop - _settleFromTop) * eased);

        if (progress < 1) return;

        _settling = false;
        _follow.Stop();
        Left = _settleToLeft;
        Top = _settleToTop;
        _host.SaveSpot(this);
    }

    
    private void BeginSettle()
    {
        var width = ActualWidth > 1 ? ActualWidth : Width;
        var height = ActualHeight > 1 ? ActualHeight : 300;
        var area = GameOverlay.WorkAreaAt((int)(_targetLeft + (width / 2)), (int)(_targetTop + 20));

        var left = Math.Clamp(_targetLeft, area.Left + 4, Math.Max(area.Left + 4, area.Right - width - 4));
        var top = Math.Clamp(_targetTop, area.Top + 4, Math.Max(area.Top + 4, area.Bottom - height - 4));

        if (Math.Abs(left - (area.Left + ScreenGap)) <= SnapDistance) left = area.Left + ScreenGap;
        if (Math.Abs(left - (area.Right - width - ScreenGap)) <= SnapDistance) left = area.Right - width - ScreenGap;
        if (Math.Abs(top - (area.Top + ScreenGap)) <= SnapDistance) top = area.Top + ScreenGap;
        if (Math.Abs(top - (area.Bottom - height - ScreenGap)) <= SnapDistance) top = area.Bottom - height - ScreenGap;

        _settleFromLeft = Left;
        _settleFromTop = Top;
        _settleToLeft = left;
        _settleToTop = top;
        _settleStarted = DateTime.UtcNow;
        _settling = true;
        _follow.Start();
    }

    private void ClampTarget()
    {
        var width = ActualWidth > 1 ? ActualWidth : Width;
        var height = ActualHeight > 1 ? ActualHeight : 300;
        var area = GameOverlay.WorkAreaAt((int)(_targetLeft + (width / 2)), (int)(_targetTop + 20));

        _targetLeft = Math.Clamp(_targetLeft, area.Left + 4, Math.Max(area.Left + 4, area.Right - width - 4));
        _targetTop = Math.Clamp(_targetTop, area.Top + 4, Math.Max(area.Top + 4, area.Bottom - height - 4));
    }

    
    private void SetLift(bool lifted)
    {
        var duration = TimeSpan.FromMilliseconds(lifted ? 120 : 190);
        var scale = lifted ? 1.012 : 1.0;
        var easing = Anim.Out();

        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(scale, duration) { EasingFunction = easing });
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(scale, duration) { EasingFunction = easing });
        Lift.BeginAnimation(OpacityProperty,
            new DoubleAnimation(lifted ? 0.55 : 0, duration) { EasingFunction = easing });
    }

    private bool IsFromButton(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is ButtonBase) return true;
            if (ReferenceEquals(current, Header)) return false;
            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private (double X, double Y) CursorDip()
    {
        if (!GetCursorPos(out var point)) return (Left, Top);

        var dpi = VisualTreeHelper.GetDpi(this);
        return (point.X / dpi.DpiScaleX, point.Y / dpi.DpiScaleY);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    private void ResetPosition_Click(object sender, RoutedEventArgs e) => _host.ResetSpot(PanelId);

    private void Close_Click(object sender, RoutedEventArgs e) => _host.HidePanel(PanelId, remember: true);
}
