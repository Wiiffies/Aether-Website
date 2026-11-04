using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AetherLauncher.Controls;

/// <summary>
/// Animationshilfen. Alles läuft über Storyboards bzw. BeginAnimation, damit die
/// WPF-eigene Zeitachse arbeitet und keine Timer im Oberflächen-Thread hängen.
/// </summary>
public static class Anim
{
    /// <summary>Weiches Auslaufen - der Standard für Ein- und Ausblendungen.</summary>
    public static IEasingFunction Out() => new QuinticEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Leichter Überschwinger für den "Playful"-Modus (wie die Feder im Hub).</summary>
    public static IEasingFunction Spring() => new BackEase { Amplitude = 0.38, EasingMode = EasingMode.EaseOut };

    public static void Fade(UIElement element, double from, double to, int milliseconds,
        int delay = 0, IEasingFunction? easing = null)
    {
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
        {
            From = from,
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = easing ?? Out(),
        });
    }

    /// <summary>Blendet ein Element ein und schiebt es dabei von unten an seinen Platz.</summary>
    public static void Rise(FrameworkElement element, double offset = 14, int milliseconds = 280,
        int delay = 0, bool spring = false)
    {
        Fade(element, 0, 1, milliseconds, delay, spring ? Spring() : Out());
        Slide(element, offset, 0, milliseconds, delay, spring);
    }

    /// <summary>Verschiebt ein Element über seine RenderTransform (ersetzt keine vorhandene Transformation).</summary>
    public static void Slide(FrameworkElement element, double from, double to, int milliseconds,
        int delay = 0, bool spring = false)
    {
        var translate = TranslateOf(element);
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
            {
                From = from,
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = spring ? Spring() : Out(),
            });
    }

    /// <summary>Waagerechte Bewegung - für den gleitenden Reiterstrich.</summary>
    public static void Glide(FrameworkElement element, double from, double to, int milliseconds, bool spring = false)
    {
        var translate = TranslateOf(element);
        translate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
            {
                From = from,
                EasingFunction = spring ? Spring() : Out(),
            });
    }

    /// <summary>Waagerechtes Einfliegen einer Seite (die Karten darin steigen zusätzlich auf).</summary>
    public static void SlideX(FrameworkElement element, double from, double to, int milliseconds,
        int delay = 0, bool spring = false)
    {
        var translate = TranslateOf(element);
        translate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
            {
                From = from,
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = spring ? Spring() : Out(),
            });
    }

    /// <summary>
    /// Verschiebt eine bereits vorhandene Transformation - etwa das Anheben einer
    /// Schaltfläche beim Überfahren. Anders als <see cref="Slide"/> legt sie keine neue an.
    /// </summary>
    public static void Move(TranslateTransform translate, double x, double y, int milliseconds,
        bool spring = false, int delay = 0)
    {
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var easing = spring ? Spring() : Out();
        var begin = TimeSpan.FromMilliseconds(delay);

        translate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(x, duration) { EasingFunction = easing, BeginTime = begin });
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(y, duration) { EasingFunction = easing, BeginTime = begin });
    }

    /// <summary>Blendet ein Element auf und lässt es dabei auf seine Größe anwachsen.</summary>
    public static void Pop(FrameworkElement element, bool open, int milliseconds = 240, Action? done = null)
    {
        var scale = ScaleOf(element);
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var easing = open ? Spring() : Out();

        element.Visibility = Visibility.Visible;

        var fade = new DoubleAnimation(open ? 1 : 0, duration)
        {
            From = open ? 0 : 1,
            EasingFunction = Out(),
        };

        if (!open)
        {
            fade.Completed += (_, _) =>
            {
                element.Visibility = Visibility.Collapsed;
                done?.Invoke();
            };
        }

        element.BeginAnimation(UIElement.OpacityProperty, fade);

        var from = open ? 0.94 : 1.0;
        var to = open ? 1.0 : 0.97;
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            scale.BeginAnimation(property, new DoubleAnimation(to, duration)
            {
                From = from,
                EasingFunction = easing,
            });
        }
    }

    public static void Animate(FrameworkElement element, DependencyProperty property, double from, double to,
        int milliseconds, int delay = 0, IEasingFunction? easing = null)
    {
        element.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
        {
            From = from,
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = easing ?? Out(),
        });
    }

    /// <summary>Kurzer Größenstoß beim Drücken und Zurückfedern beim Loslassen.</summary>
    public static void Press(ScaleTransform scale, double to, int milliseconds, bool spring = false)
    {
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var easing = spring ? Spring() : new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, duration) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, duration) { EasingFunction = easing });
    }

    /// <summary>Das Element "atmet" einmal auf - Rückmeldung ohne Text.</summary>
    public static void Pulse(FrameworkElement element, double strength = 1.03, int milliseconds = 340)
    {
        var scale = ScaleOf(element);
        var half = TimeSpan.FromMilliseconds(milliseconds / 2);
        var grow = new DoubleAnimation(strength, half) { EasingFunction = Out() };
        var back = new DoubleAnimation(1, half) { BeginTime = half, EasingFunction = Out() };
        element.BeginAnimation(UIElement.RenderTransformProperty, null);

        var storyboard = new Storyboard();
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            Storyboard.SetTarget(grow, scale);
            Storyboard.SetTargetProperty(grow, new PropertyPath(property));
            storyboard.Children.Add(grow);
            Storyboard.SetTarget(back, scale);
            Storyboard.SetTargetProperty(back, new PropertyPath(property));
            storyboard.Children.Add(back);
        }

        storyboard.Begin();
    }

    /// <summary>Alle direkten Karten einer Seite nacheinander einfliegen lassen.</summary>
    public static void PageEntrance(FrameworkElement page, bool spring, int stagger = 38)
    {
        var children = EntranceChildren(page);
        for (var index = 0; index < children.Count; index++)
        {
            var delay = Math.Min(index, 6) * stagger;
            children[index].Opacity = 0;
            Rise(children[index], spring ? 22 : 12, spring ? 360 : 250, delay, spring);
        }
    }

    private static List<FrameworkElement> EntranceChildren(FrameworkElement page)
    {
        // Bei der Einstellungsseite liegen die Karten im Inhalt des ScrollViewers.
        var host = page is System.Windows.Controls.ScrollViewer viewer ? viewer.Content as FrameworkElement : page;
        var children = new List<FrameworkElement>();

        if (host is System.Windows.Controls.Panel panel)
        {
            foreach (var child in panel.Children.OfType<FrameworkElement>()) children.Add(child);
        }
        else if (host is System.Windows.Controls.ContentControl { Content: FrameworkElement single })
        {
            children.Add(single);
        }

        return children;
    }

    /// <summary>Liefert den TranslateTransform des Elements und legt ihn bei Bedarf an.</summary>
    public static TranslateTransform TranslateOf(FrameworkElement element)
    {
        switch (element.RenderTransform)
        {
            case TranslateTransform translate:
                return translate;
            case TransformGroup group when group.Children.OfType<TranslateTransform>().LastOrDefault() is { } found:
                return found;
            default:
            {
                var created = new TranslateTransform();
                var group = new TransformGroup();
                if (element.RenderTransform is not null && element.RenderTransform.Value != Matrix.Identity)
                    group.Children.Add(element.RenderTransform);
                group.Children.Add(created);
                element.RenderTransform = group;
                return created;
            }
        }
    }

    private static ScaleTransform ScaleOf(FrameworkElement element)
    {
        if (element.RenderTransform is ScaleTransform scale) return scale;
        if (element.RenderTransform is TransformGroup existing &&
            existing.Children.OfType<ScaleTransform>().FirstOrDefault() is { } found) return found;

        var created = new ScaleTransform(1, 1);
        var group = new TransformGroup();
        if (element.RenderTransform is not null && element.RenderTransform.Value != Matrix.Identity)
            group.Children.Add(element.RenderTransform);
        group.Children.Add(created);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = group;
        return created;
    }
}
