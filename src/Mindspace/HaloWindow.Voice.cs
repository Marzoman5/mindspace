using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Mindspace.Services;
using Mindspace.Views;

namespace Mindspace;

/// <summary>
/// Dictation. Hold Alt+Shift+D and speak; let go and the words are typed where the cursor is. Tap it instead and it
/// keeps listening until the next press. Start with an agent's name ("Tick, remind me…") and the words go to that agent.
/// The microphone is open only between those two moments, and the island shows "Listening" the whole time.
/// </summary>
public partial class HaloWindow
{
    const int HotkeyDictate = 0x4D56;
    readonly DispatcherTimer _talk = new() { Interval = TimeSpan.FromMilliseconds(40) };
    DateTime _talkStart;
    bool _handsFree, _talkBusy, _toPanel;
    nint _talkTarget;

    public bool IsDictating => _talk.IsEnabled || _talkBusy;
    /// <summary>A suggestion or reminder card (not a status line) is on screen and should not be replaced.</summary>
    public bool ShowingCard => _peek != null && _peek.Kind != "status";

    void InitVoice() => _talk.Tick += (_, _) => TalkTick();

    public void ToggleDictation(bool handsFree = false)
    {
        if (_talkBusy) return;
        var mic = App.Mind.Mic;
        if (_talk.IsEnabled) { _ = FinishDictationAsync(); return; }
        if (mic.IsRecording) { Status(Agents.Echo, "Echo is already recording a note", 3); return; }
        if (!App.Mind.Speech.Installed) { Status(Agents.Echo, "Voice needs a one-time setup — open Echo", 5); return; }
        var fg = Native.GetForegroundWindow();
        _toPanel = Native.ProcessName(fg) == "Mindspace";
        _talkTarget = _toPanel ? 0 : fg;
        if (!mic.Start()) { Status(Agents.Echo, "No microphone found", 4); return; }
        _talkStart = DateTime.Now; _handsFree = handsFree;
        Status(Agents.Echo, handsFree ? "Listening…  click to finish" : "Listening…  let go to finish");
        _talk.Start();
    }

    void TalkTick()
    {
        var mic = App.Mind.Mic;
        if (!mic.IsRecording) { _talk.Stop(); return; }
        double sec = (DateTime.Now - _talkStart).TotalSeconds;
        if (!_handsFree && !Native.KeyDown(0x44 /*D*/))
        {
            if (sec >= 0.45) { _ = FinishDictationAsync(); return; }
            _handsFree = true;                                   // a tap, not a hold: keep listening
            PeekText.Text = "Listening…  Alt+Shift+D to finish";
        }
        if (sec > 120) { _ = FinishDictationAsync(); return; }
        PeekName.Text = "Echo  " + new string('•', 1 + (int)(Math.Min(1, mic.Level * 3) * 9));
    }

    async Task FinishDictationAsync()
    {
        if (_talkBusy) return;
        _talk.Stop(); _talkBusy = true;
        try
        {
            var pcm = App.Mind.Mic.Stop();
            Status(Agents.Echo, "Writing it down…");
            string text;
            try { text = await App.Mind.Speech.TranscribeAsync(pcm); }
            catch (Exception ex) { Status(Agents.Echo, Gen.OneLine(ex.Message, 70), 6); return; }
            if (text.Length == 0) { Status(Agents.Echo, "Didn't catch that", 2.5); return; }

            if (Speech.Addressed(text) is { } hit)
            {
                HidePeek();
                App.Mind.Panel.Open(hit.agent.Id, hit.rest, autoRun: true);
                return;
            }
            if (_toPanel && App.Mind.Panel.IsOpen) { HidePeek(); App.Mind.Panel.TypeIntoFocus(text); return; }

            // Words spoken at a private window (password manager, banking, private browsing) are not kept in history.
            if (!ContextSensor.IsPrivate(Native.WindowTitle(_talkTarget)))
                App.Mind.Notes.Add("dictation", Gen.OneLine(text, 60), text, Native.ProcessName(_talkTarget));
            bool typed = await App.Mind.Panel.PasteIntoAsync(_talkTarget, text);
            int words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Status(Agents.Echo, typed ? $"Typed {words} word{(words == 1 ? "" : "s")}" : "Copied — press Ctrl+V to paste it", typed ? 2 : 5);
        }
        finally { _talkBusy = false; }
    }

    /// <summary>A small card under the island that says what Mindspace is doing right now. seconds = 0 keeps it until replaced.</summary>
    public void Status(Agent a, string text, double seconds = 0)
    {
        bool fresh = _peek == null;
        _peek = new Suggestion(a, text, "status");
        PeekOrb.Agent = a;
        PeekName.Text = a.Name;
        PeekText.Text = text;
        PeekActions.Children.Clear();
        PeekCard.BorderBrush = new LinearGradientBrush(Color.FromArgb(150, a.Accent.R, a.Accent.G, a.Accent.B), Color.FromArgb(40, 255, 255, 255), 90);
        PeekCard.Visibility = Visibility.Visible;
        if (fresh)
        {
            Animate(PeekCard, OpacityProperty, 1, 180);
            PeekShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = Ease });
        }
        _peekTimer.Stop();
        if (seconds > 0) { _peekTimer.Interval = TimeSpan.FromSeconds(seconds); _peekTimer.Start(); }
    }
}
