using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Mindspace.Services;

namespace Mindspace.Controls;

/// <summary>
/// An agent's face: a gradient orb with two eyes that blink now and then, and that
/// bounce while the agent is thinking. Drawn directly (no visual tree) so dozens are free.
/// </summary>
public sealed class AgentOrb : FrameworkElement
{
    public static readonly DependencyProperty AgentProperty = DependencyProperty.Register(nameof(Agent), typeof(Agent), typeof(AgentOrb),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((AgentOrb)d)._fill = null));
    public static readonly DependencyProperty BlinkProperty = DependencyProperty.Register(nameof(Blink), typeof(double), typeof(AgentOrb),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThinkProperty = DependencyProperty.Register(nameof(Think), typeof(double), typeof(AgentOrb),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsThinkingProperty = DependencyProperty.Register(nameof(IsThinking), typeof(bool), typeof(AgentOrb),
        new PropertyMetadata(false, (d, e) => ((AgentOrb)d).OnThinking((bool)e.NewValue)));
    public static readonly DependencyProperty FaceProperty = DependencyProperty.Register(nameof(Face), typeof(bool), typeof(AgentOrb),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public Agent? Agent { get => (Agent?)GetValue(AgentProperty); set => SetValue(AgentProperty, value); }
    public double Blink { get => (double)GetValue(BlinkProperty); set => SetValue(BlinkProperty, value); }
    public double Think { get => (double)GetValue(ThinkProperty); set => SetValue(ThinkProperty, value); }
    public bool IsThinking { get => (bool)GetValue(IsThinkingProperty); set => SetValue(IsThinkingProperty, value); }
    public bool Face { get => (bool)GetValue(FaceProperty); set => SetValue(FaceProperty, value); }

    Brush? _fill;
    static readonly Brush Shine = MakeShine();
    static readonly Brush Eye = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255));
    static readonly Random Rng = new();

    public AgentOrb()
    {
        Width = Height = 28;
        IsVisibleChanged += (_, _) => { if (IsVisible) ScheduleBlink(); else BeginAnimation(BlinkProperty, null); };
    }

    static Brush MakeShine()
    {
        var b = new RadialGradientBrush { GradientOrigin = new(0.3, 0.25), Center = new(0.3, 0.25), RadiusX = 0.7, RadiusY = 0.7 };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 255, 255), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.7));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(46, 0, 0, 0), 1));
        b.Freeze();
        return b;
    }

    void ScheduleBlink()
    {
        // one short blink every 3–7 s; only while visible, so idle cost is nil
        var a = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(2.5 + Rng.NextDouble() * 4.5) };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
        Timeline.SetDesiredFrameRate(a, 30);
        a.Completed += (_, _) => { if (IsVisible) ScheduleBlink(); };
        BeginAnimation(BlinkProperty, a);
    }

    void OnThinking(bool on)
    {
        if (!on) { BeginAnimation(ThinkProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))); return; }
        var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(a, 30);
        BeginAnimation(ThinkProperty, a);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var agent = Agent;
        if (agent == null) return;
        double w = ActualWidth, h = ActualHeight, r = Math.Min(w, h) / 2;
        var c = new Point(w / 2, h / 2);
        _fill ??= agent.Gradient();
        dc.DrawEllipse(_fill, null, c, r, r);
        dc.DrawEllipse(Shine, null, c, r, r);
        if (!Face || r < 7) return;

        // eyes: two rounded bars. Thinking makes them hop one after the other.
        double eyeW = r * 0.2, eyeH = r * 0.46 * (1 - 0.86 * Blink), gap = r * 0.3;
        double t = Think * Math.PI * 2;
        double hopL = Think > 0 ? -Math.Max(0, Math.Sin(t)) * r * 0.16 : 0;
        double hopR = Think > 0 ? -Math.Max(0, Math.Sin(t - 1.1)) * r * 0.16 : 0;
        double y = c.Y - r * 0.08;
        switch (agent.Mood)
        {
            case "wink":   // Tick: right eye is a happy arc
                DrawEye(dc, c.X - gap, y + hopL, eyeW, eyeH);
                DrawArc(dc, c.X + gap, y + hopR, r * 0.24, true);
                break;
            case "calm":   // Sage: soft closed-arc eyes
                DrawArc(dc, c.X - gap, y + hopL + r * 0.04, r * 0.24, Blink < 0.5);
                DrawArc(dc, c.X + gap, y + hopR + r * 0.04, r * 0.24, Blink < 0.5);
                break;
            case "focus":  // Patch: wide, low eyes
                DrawEye(dc, c.X - gap, y + hopL, eyeW * 1.5, eyeH * 0.62);
                DrawEye(dc, c.X + gap, y + hopR, eyeW * 1.5, eyeH * 0.62);
                break;
            default:       // Quill: classic
                DrawEye(dc, c.X - gap, y + hopL, eyeW, eyeH);
                DrawEye(dc, c.X + gap, y + hopR, eyeW, eyeH);
                break;
        }
    }

    static void DrawEye(DrawingContext dc, double cx, double cy, double w, double h)
    {
        h = Math.Max(h, w * 0.5);
        dc.DrawRoundedRectangle(Eye, null, new Rect(cx - w / 2, cy - h / 2, w, h), w / 2, w / 2);
    }

    static void DrawArc(DrawingContext dc, double cx, double cy, double w, bool up)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(cx - w / 2, cy + (up ? w * 0.22 : -w * 0.1)), false, false);
            ctx.ArcTo(new Point(cx + w / 2, cy + (up ? w * 0.22 : -w * 0.1)), new Size(w / 2, w / 2), 0, false, up ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, true);
        }
        g.Freeze();
        var pen = new Pen(Eye, Math.Max(1.2, w * 0.36)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, g);
    }
}

/// <summary>Thin progress ring used by Patch's gauges.</summary>
public sealed class Ring : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Ring),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Ring),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    static readonly Pen Track = MakeTrack();
    static Pen MakeTrack() { var p = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 5); p.Freeze(); return p; }

    public void AnimateTo(double v) => BeginAnimation(ValueProperty, new DoubleAnimation(Math.Clamp(v, 0, 1), TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase() });

    protected override void OnRender(DrawingContext dc)
    {
        double r = Math.Min(ActualWidth, ActualHeight) / 2 - 3;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, Track, c, r, r);
        double v = Math.Clamp(Value, 0, 0.9999);
        if (v <= 0.001) return;
        double a = v * 2 * Math.PI - Math.PI / 2;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X, c.Y - r), false, false);
            ctx.ArcTo(new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)), new Size(r, r), 0, v > 0.5, SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, new Pen(Stroke, 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
    }
}

/// <summary>
/// A soft drop shadow painted once as a dozen translucent rounded rectangles. Replaces DropShadowEffect,
/// which forces the whole panel through an offscreen shader pass on every repaint (e.g. each streamed token).
/// </summary>
public sealed class SoftShadow : FrameworkElement
{
    public double Radius { get; set; } = 24;
    static readonly Brush Layer = Freeze(new SolidColorBrush(Color.FromArgb(7, 0, 0, 0)));
    static Brush Freeze(Brush b) { b.Freeze(); return b; }

    protected override void OnRender(DrawingContext dc)
    {
        for (int i = 1; i <= 12; i++)
        {
            double grow = i * 1.45;
            var r = new Rect(-grow * 0.7, -grow * 0.3 + 6, ActualWidth + grow * 1.4, ActualHeight + grow * 1.3);
            dc.DrawRoundedRectangle(Layer, null, r, Radius + grow, Radius + grow);
        }
    }
}
