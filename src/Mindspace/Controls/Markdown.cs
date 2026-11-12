using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Mindspace.Controls;

/// <summary>
/// Tiny markdown renderer for model output: paragraphs, bullets, numbered lists, headings,
/// bold / italic / inline code, fenced code. It only builds TextBlocks: links are not made clickable
/// and nothing in the text can trigger an action.
/// </summary>
public sealed partial class Markdown : StackPanel
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(Markdown),
        new PropertyMetadata("", (d, _) => ((Markdown)d).Render()));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    static readonly Brush Ink = Frozen("#ECECF4"), Muted = Frozen("#A3A3B8"), CodeBg = Frozen("#1FFFFFFF"), CodeInk = Frozen("#C9D4FF");
    static readonly FontFamily Mono = new("Cascadia Code, Consolas");
    static Brush Frozen(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

    [GeneratedRegex(@"(\*\*|__)(.+?)\1|(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])|`([^`]+)`")] private static partial Regex InlineRx();
    [GeneratedRegex(@"^(\s*)(?:[-*•]|(\d+)[.)])\s+(.*)$")] private static partial Regex ListRx();
    [GeneratedRegex(@"^(#{1,4})\s+(.*)$")] private static partial Regex HeadRx();

    void Render()
    {
        Children.Clear();
        var lines = (Text ?? "").Replace("\r", "").Split('\n');
        var para = new List<string>();
        bool inCode = false; var code = new List<string>();

        void FlushPara()
        {
            if (para.Count == 0) return;
            var tb = Block(13.5, Ink); tb.Margin = new Thickness(0, 0, 0, 8);
            AddInlines(tb.Inlines, string.Join(" ", para));
            Children.Add(tb); para.Clear();
        }
        void FlushCode()
        {
            var tb = new TextBlock { Text = string.Join("\n", code), FontFamily = Mono, FontSize = 12, Foreground = CodeInk, TextWrapping = TextWrapping.Wrap };
            Children.Add(new Border { Background = CodeBg, CornerRadius = new CornerRadius(10), Padding = new Thickness(11, 8, 11, 9), Margin = new Thickness(0, 0, 0, 8), Child = tb });
            code.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCode) FlushCode(); else FlushPara();
                inCode = !inCode; continue;
            }
            if (inCode) { code.Add(raw); continue; }
            if (line.Length == 0) { FlushPara(); continue; }

            var h = HeadRx().Match(line);
            if (h.Success)
            {
                FlushPara();
                var tb = Block(h.Groups[1].Length <= 2 ? 15 : 13.5, Ink); tb.FontWeight = FontWeights.SemiBold; tb.Margin = new Thickness(0, 2, 0, 5);
                AddInlines(tb.Inlines, h.Groups[2].Value);
                Children.Add(tb); continue;
            }
            var li = ListRx().Match(line);
            if (li.Success)
            {
                FlushPara();
                var g = new Grid { Margin = new Thickness(Math.Min(li.Groups[1].Length, 6) * 5, 0, 0, 5) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(li.Groups[2].Success ? 20 : 15) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var bullet = Block(13.5, Muted); bullet.Text = li.Groups[2].Success ? li.Groups[2].Value + "." : "•";
                var body = Block(13.5, Ink); AddInlines(body.Inlines, li.Groups[3].Value);
                Grid.SetColumn(body, 1);
                g.Children.Add(bullet); g.Children.Add(body);
                Children.Add(g); continue;
            }
            para.Add(line.Trim());
        }
        if (inCode && code.Count > 0) FlushCode();
        FlushPara();
        if (Children.Count > 0 && Children[^1] is FrameworkElement last) last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, 0, 0);
    }

    static TextBlock Block(double size, Brush fg) => new() { FontSize = size, Foreground = fg, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.5 };

    static void AddInlines(InlineCollection target, string s)
    {
        int pos = 0;
        foreach (Match m in InlineRx().Matches(s))
        {
            if (m.Index > pos) target.Add(new Run(s[pos..m.Index]));
            if (m.Groups[2].Success) target.Add(new Run(m.Groups[2].Value) { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            else if (m.Groups[3].Success) target.Add(new Run(m.Groups[3].Value) { FontStyle = FontStyles.Italic });
            else target.Add(new Run(m.Groups[4].Value) { FontFamily = Mono, FontSize = 12, Foreground = CodeInk, Background = CodeBg });
            pos = m.Index + m.Length;
        }
        if (pos < s.Length) target.Add(new Run(s[pos..]));
    }
}
