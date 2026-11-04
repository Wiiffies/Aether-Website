using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AetherLauncher.Controls;

/// <summary>
/// Weiches Mausrad-Scrollen für einen <see cref="ScrollViewer"/>.
///
/// Anders als eine WPF-Animation pro Radschritt sammelt diese Variante die Eingaben ein und
/// bewegt den Inhalt mit einer kurzen, zusammenlaufenden Bewegung zum Ziel. Dadurch kämpfen
/// schnelle Radstöße oder ein Trackpad nicht mehr gegeneinander, und der Inhalt springt auch
/// am Rand nicht. Verschachtelte Scroller werden von innen nach außen bedient: erst die Liste
/// unter dem Zeiger, erst wenn die am Rand ist, die Seite.
///
/// Einfach in XAML anhängen mit <c>c:SmoothScroll.Enabled="True"</c>.
/// </summary>
public static class SmoothScroll
{
    private sealed class State
    {
        /// <summary>Wo der Inhalt gerade steht (nicht der evtl. verzögerte VerticalOffset).</summary>
        public double Position;

        /// <summary>Wohin die laufende Bewegung noch will - mehrere Radstöße sammeln sich hier.</summary>
        public double Target;

        public bool Active;
    }

    private static readonly ConditionalWeakTable<ScrollViewer, State> States = new();

    private static DispatcherTimer? _timer;
    private static Dispatcher? _timerDispatcher;

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer) return;

        viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        if (e.NewValue is true)
        {
            viewer.PreviewMouseWheel += OnPreviewMouseWheel;
        }
        else
        {
            States.Remove(viewer);
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0) return;

        // Der äußerste Scroller sieht das Preview zuerst. Deshalb wird hier der innerste
        // Scroller unter dem Zeiger gesucht - sonst würde eine Liste in einer Seite immer
        // die ganze Seite rollen.
        var viewer = FindScrollable(e.OriginalSource as DependencyObject, e.Delta);
        if (viewer is null) return;

        var state = States.GetOrCreateValue(viewer);

        // Nach einer Pause an der echten Position anfangen; während einer Bewegung an deren
        // Ziel anschließen, damit schnelle Radstöße sich addieren statt neu zu starten.
        if (!state.Active)
        {
            state.Position = viewer.VerticalOffset;
            state.Target = state.Position;
        }

        var step = Math.Max(48, viewer.ViewportHeight * 0.14);
        var wanted = state.Target - (e.Delta / 120.0 * step);
        var target = Math.Clamp(wanted, 0, viewer.ScrollableHeight);

        if (Math.Abs(target - state.Target) < 0.01) return; // schon am Rand - nicht abfangen

        state.Target = target;

        if (!SystemParameters.ClientAreaAnimation)
        {
            viewer.ScrollToVerticalOffset(target);
            e.Handled = true;
            return;
        }

        state.Active = true;
        EnsureTimer(viewer.Dispatcher);
        e.Handled = true;
    }

    /// <summary>
    /// Sucht vom Original-Source aufwärts den ersten Scroller, der in die gewünschte
    /// Richtung noch Platz hat. So rollt am Ende der inneren Liste die äußere Seite weiter.
    /// </summary>
    private static ScrollViewer? FindScrollable(DependencyObject? source, int delta)
    {
        for (var node = source; node is not null; node = ParentOf(node))
        {
            if (node is not ScrollViewer viewer || !GetEnabled(viewer)) continue;
            if (CanScroll(viewer, delta)) return viewer;
        }

        return null;
    }

    private static bool CanScroll(ScrollViewer viewer, int delta)
    {
        if (viewer.ScrollableHeight <= 0.5) return false;

        if (States.TryGetValue(viewer, out var state) && state.Active)
            return delta > 0
                ? state.Target > 0.5
                : state.Target < viewer.ScrollableHeight - 0.5;

        return delta > 0
            ? viewer.VerticalOffset > 0.5
            : viewer.VerticalOffset < viewer.ScrollableHeight - 0.5;
    }

    private static DependencyObject? ParentOf(DependencyObject node)
    {
        if (node is Visual or System.Windows.Media.Media3D.Visual3D) return VisualTreeHelper.GetParent(node);
        return LogicalTreeHelper.GetParent(node);
    }

    private static void EnsureTimer(Dispatcher dispatcher)
    {
        if (_timer is { IsEnabled: true } && ReferenceEquals(_timerDispatcher, dispatcher)) return;

        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= Tick;
        }

        _timerDispatcher = dispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Input, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(15),
        };

        _timer.Tick += Tick;
        _timer.Start();
    }

    private static void Tick(object? sender, EventArgs e)
    {
        var any = false;

        foreach (var (viewer, state) in States)
        {
            if (!state.Active) continue;

            if (!viewer.IsLoaded)
            {
                state.Active = false;
                continue;
            }

            var max = viewer.ScrollableHeight;
            state.Position = Math.Clamp(state.Position, 0, max);
            state.Target = Math.Clamp(state.Target, 0, max);

            var diff = state.Target - state.Position;
            if (Math.Abs(diff) < 0.4)
            {
                state.Position = state.Target;
                viewer.ScrollToVerticalOffset(state.Target);
                state.Active = false;
                continue;
            }

            // Exponentielles Nachziehen: schnell los, weich ankommen. Bei 15 ms sind das
            // rund 150-200 ms, unabhängig davon, wie viele Radstöße dazukommen.
            state.Position += diff * 0.33;
            viewer.ScrollToVerticalOffset(state.Position);
            any = true;
        }

        if (!any) _timer?.Stop();
    }
}
