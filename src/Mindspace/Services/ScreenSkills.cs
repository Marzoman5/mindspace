using System.Diagnostics;

namespace Mindspace.Services;

/// <summary>
/// The two things Iris can do to a window, as opposed to reading it. Both are deliberately narrow:
///  • they start only from the Start button, on the one window named on that button;
///  • Collect only turns the mouse wheel over that window; Autoplay only taps the Space bar in it;
///  • no model decides anything here — these are fixed loops, so on-screen text cannot steer them;
///  • both stop on Esc, on Stop, when the window closes, and after a fixed limit; Autoplay also stops the moment
///    another window comes to the front, so a keystroke can never land somewhere else.
/// </summary>
public static class ScreenSkills
{
    public static bool Allowed(nint hwnd) => WindowReader.CanRead(hwnd) && !Native.IsTerminal(hwnd);

    /// <summary>
    /// Reads the window, scrolls one screenful, reads again, and keeps every new line until nothing new turns up.
    /// Lines that repeat word for word are kept once. Returns the collected text (possibly empty).
    /// </summary>
    public static async Task<string> CollectAsync(nint hwnd, bool up, Action<int, int> progress, CancellationToken ct, int maxSteps = 40)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>();
        int stale = 0, chars = 0;
        for (int step = 1; step <= maxSteps && !ct.IsCancellationRequested && chars < 110_000; step++)
        {
            if (!Allowed(hwnd) || Native.KeyDown(0x1B /*Esc*/)) break;
            var read = await WindowReader.ReadAsync(hwnd, 30_000, timeoutMs: 4000);
            var fresh = read == null ? [] : read.Text.Split('\n').Where(l => l.Length > 0 && seen.Add(l)).ToList();
            if (fresh.Count == 0) { if (++stale >= 2) break; }
            else
            {
                stale = 0; chars += fresh.Sum(l => l.Length + 1);
                if (up) lines.InsertRange(0, fresh); else lines.AddRange(fresh);
            }
            progress(lines.Count, step);
            Native.WheelScroll(hwnd, up ? 5 : -5);
            bool esc = false;
            try { for (int i = 0; i < 13 && !esc; i++) { await Task.Delay(50, ct); esc = Native.KeyDown(0x1B); } }      // Esc is noticed within 50 ms
            catch (OperationCanceledException) { break; }
            if (esc) break;
        }
        return string.Join("\n", lines);
    }

    public sealed record PlayResult(bool Found, int Jumps, int Restarts, TimeSpan Played, string Why);

    /// <summary>
    /// Keeps a side-scrolling runner game going (the kind where you jump over what comes at you, such as the one a
    /// browser shows when it is offline): watches a thin strip just ahead of the runner and taps Space when something
    /// enters it. Looks at pixels of that one window only, in memory, and never saves them.
    /// </summary>
    public static async Task<PlayResult> AutoplayAsync(nint hwnd, TimeSpan limit, Action<string> status, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        int jumps = 0, restarts = 0;
        string Why() => ct.IsCancellationRequested ? "stopped" : Native.KeyDown(0x1B) ? "Esc pressed" : Native.GetForegroundWindow() != hwnd ? "another window came to the front" : "time limit reached";
        bool Go() => !ct.IsCancellationRequested && !Native.KeyDown(0x1B) && Native.GetForegroundWindow() == hwnd && clock.Elapsed < limit;

        if (!Allowed(hwnd)) return new(false, 0, 0, TimeSpan.Zero, "that window can't be used");
        Native.SetForegroundWindow(hwnd);
        await Task.Delay(350);
        if (Native.GetForegroundWindow() != hwnd) return new(false, 0, 0, TimeSpan.Zero, "the window did not come to the front");

        // Find the track first. Nothing is pressed until a running game has been recognised in that window.
        string title = Native.WindowTitle(hwnd);
        Track? track = null;
        for (int attempt = 0; attempt < 10 && Go() && track == null; attempt++)
        {
            track = FindTrack(hwnd);
            if (track == null) await Task.Delay(400);
        }
        if (track == null) return new(false, 0, 0, clock.Elapsed, Go() ? "I couldn't see a runner game in that window. Start the game, then press Start again" : Why());
        var t = track.Value;
        status("Playing — Esc to stop");

        var run = Stopwatch.StartNew();          // time in the current run: these games speed up as they go
        long lastJump = 0, lastChange = 0, lastCheck = 0; int lastSum = -1, missing = 0;
        string why = "";
        while (Go())
        {
            // Once a second: is this still the same page, and is the ground line still where it was? If the user
            // switched tab or the page changed, stop — Space must never go to anything but the game.
            if (clock.ElapsedMilliseconds - lastCheck > 1000)
            {
                lastCheck = clock.ElapsedMilliseconds;
                if (Native.WindowTitle(hwnd) != title || !Allowed(hwnd)) { why = "the window changed"; break; }
                missing = GroundStillThere(t) ? 0 : missing + 1;
                if (missing >= 3) { why = "the game is no longer on screen"; break; }
            }
            if (missing > 0) { try { await Task.Delay(100, ct); } catch (OperationCanceledException) { break; } continue; }
            // The strip starts just in front of the runner and reaches further ahead the faster the game is going.
            double speed = Math.Min(13, 6 + run.Elapsed.TotalSeconds * 0.06);
            int x0 = t.Left + (int)(94 * t.Scale), w = Math.Max(8, (int)(speed * 13 * t.Scale)), y0 = t.Ground - (int)(48 * t.Scale), h = (int)(40 * t.Scale);   // from the runner's head down to just above the ground
            var px = Native.Grab(x0, y0, w, h);
            if (px == null) break;
            // Sky colour = the middle of three samples along the top of the strip (one may be covered); follows day/night changes.
            int s0 = Lum(px, 0), s1 = Lum(px, (w / 2) * 4), s2 = Lum(px, (w - 1) * 4);
            int bg = Math.Max(Math.Min(s0, s1), Math.Min(Math.Max(s0, s1), s2)), ink = 0, sum = 0;
            for (int row = 0; row < h; row += 3)
                for (int col = 0; col < w; col += 2)
                {
                    int l = Lum(px, (row * w + col) * 4);
                    sum += l;
                    if (Math.Abs(l - bg) > 70) ink++;
                }
            long now = clock.ElapsedMilliseconds;
            if (sum != lastSum) { lastSum = sum; lastChange = now; }
            if (ink >= 3 && now - lastJump > 120) { Native.TapKey(0x20); jumps++; lastJump = now; Trace($"jump at {now} ms ink={ink} w={w}"); }
            else if (now - lastChange > 2500 && now - lastJump > 2500)
            {
                // Nothing has moved for a while: the run is over. Start another, as the user asked us to keep it going.
                Native.TapKey(0x20); restarts++; lastJump = lastChange = now; run.Restart(); Trace($"restart at {now} ms");
                status($"Playing — run {restarts + 1} — Esc to stop");
            }
            try { await Task.Delay(8, ct); } catch (OperationCanceledException) { break; }
        }
        if (why.Length == 0) why = Why();
        Trace($"end: jumps={jumps} restarts={restarts} why={why}");
        return new(true, jumps, restarts, clock.Elapsed, why);
    }

    [Conditional("DEBUG")]
    static void Trace(string line) { try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ms-play.txt"), line + "\n"); } catch { } }

    readonly record struct Track(int Left, int Ground, double Scale, int Length);

    /// <summary>The line the runner stands on is still drawn where it was found (in either day or night colours).</summary>
    static bool GroundStillThere(Track t)
    {
        int up = (int)(70 * t.Scale);
        var px = Native.Grab(t.Left, t.Ground - up, t.Length, up + 1);
        if (px == null) return false;
        int sky = Lum(px, (t.Length - 1) * 4), ink = 0, n = 0;       // top-right corner of the play area is empty sky
        for (int col = 0; col < t.Length; col += 3) { n++; if (Math.Abs(Lum(px, (up * t.Length + col) * 4) - sky) > 70) ink++; }
        return ink > n * 0.4;
    }

    static int Lum(byte[] px, int i) => (px[i] * 11 + px[i + 1] * 59 + px[i + 2] * 30) / 100;

    /// <summary>Finds the line the runner stands on, or null when the window does not look like a runner game.</summary>
    static Track? FindTrack(nint hwnd)
    {
        if (!Native.GetWindowRect(hwnd, out var r)) return null;
        var (sw, sh) = Native.ScreenPixels();
        int x = Math.Max(0, r.Left), y = Math.Max(0, r.Top), w = Math.Min(sw, r.Right) - x, h = Math.Min(sh, r.Bottom) - y;
        if (w < 300 || h < 200) return null;
        var px = Native.Grab(x, y, w, h);
        if (px == null) return null;
        int light = 0, total = 0;
        for (int row = 0; row < h; row += 16)
            for (int col = 0; col < w; col += 16) { total++; if (Lum(px, (row * w + col) * 4) > 128) light++; }
        int bg = light * 2 > total ? 255 : 0;                               // light or dark page
        double k = Native.DpiScale(hwnd);          // the game draws in CSS pixels; assumes the page is at 100% zoom
        double Ink(int x0, int y0, int x1, int y1)
        {
            int ink = 0, n = 0;
            for (int row = Math.Max(0, y0); row < Math.Min(h, y1); row += 2)
                for (int col = Math.Max(0, x0); col < Math.Min(w, x1); col += 2) { n++; if (Math.Abs(Lum(px, (row * w + col) * 4) - bg) > 90) ink++; }
            return n == 0 ? 1 : ink / (double)n;
        }
        // Candidate ground lines: rows that are mostly "ink" from side to side, strongest first (not the window's own edges).
        var rows = new List<(int count, int row)>();
        int edge = (int)(30 * k);
        for (int row = h / 6; row < h - edge; row++)
        {
            int count = 0;
            for (int col = 0; col < w; col += 2) if (Math.Abs(Lum(px, (row * w + col) * 4) - bg) > 90) count++;
            if (count * 2 >= w * 0.45) rows.Add((count, row));
        }
        // A long line is not enough (pages are full of them). It must look like a track with a runner on it:
        // bare background under the line, mostly empty sky above it, and one small figure standing near its left end.
        foreach (var (_, gy) in rows.OrderByDescending(c => c.count).Take(12))
        {
            int gx = -1, last = 0;
            for (int col = 0; col < w; col++) if (Math.Abs(Lum(px, (gy * w + col) * 4) - bg) > 90) { if (gx < 0) gx = col; last = col; }
            int len = last - gx + 1;
            double below = Ink(gx, gy + (int)(14 * k), gx + len, gy + (int)(24 * k));
            double sky = Ink(gx + (int)(90 * k), gy - (int)(120 * k), gx + len, gy - (int)(60 * k));
            double runner = Ink(gx + (int)(18 * k), gy - (int)(44 * k), gx + (int)(66 * k), gy - (int)(4 * k));
            Trace($"track? y={y + gy} x={x + gx} len={len} k={k:0.00} below={below:0.000} sky={sky:0.000} runner={runner:0.00}");
            if (below <= 0.01 && sky <= 0.06 && runner >= 0.12 && runner <= 0.75) return new Track(x + gx, y + gy, k, len);
        }
        return null;
    }
}
