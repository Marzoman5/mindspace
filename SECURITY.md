# Safety and privacy

Mindspace is an always-on overlay that can see which window is in front and can type into other apps. That deserves a precise account of what it can and cannot do.

## What the app guarantees

| Guarantee | Where it is enforced |
|---|---|
| **One network destination.** The only connection is to Ollama on this PC (`127.0.0.1:11434`). No proxy, no redirects, no telemetry, no update check. | `Services/Ollama.cs` is the only network code |
| **On-device models only.** Ollama can forward "cloud" model tags to a remote service; Mindspace refuses them. | `Ollama.IsRemoteTag` |
| **Agents only produce text.** Model output is never run, never used as a path, URL or command, and has no tools. Links in answers are not clickable. | `Controls/Markdown.cs`, `Views/Gen.cs` |
| **Nothing is typed without a click.** Text reaches another app only when you press Insert. It goes to the window named on the button, and only if that window really took focus. | `PanelWindow.InsertText` |
| **Never into a terminal or remote session.** Pasted text can run as commands there, so Insert becomes Copy. The selection hotkeys also skip terminals, where Ctrl+C means "interrupt". | `Native.IsTerminal` |
| **Clipboard is read only when you act.** Opening Quill or Sage, or pressing a selection hotkey, reads it once. Background clipboard sensing is off by default, uses a local pattern (no model), and stores nothing. | `Views/Gen.cs`, `Services/ContextSensor.cs` |
| **Copied text is treated as data.** It is wrapped in a marked section and its closing tag is defanged, so text you copied cannot pose as instructions to the model. | `Gen.AsContext` |
| **Sensitive windows are skipped.** No suggestions, sensing or synthetic copy when the window title mentions passwords, password managers, banking or private browsing. | `ContextSensor.IsPrivate` |
| **Patch is read-only.** It cannot end processes or change settings. Its two buttons open Windows' own Task Manager and Storage page. | `Views/PatchView.xaml.cs` |
| **No elevation, no drivers, no services.** The only registry write is the opt-in "Start with Windows" value under your own user. | `app.manifest`, `Views/SettingsView.cs` |
| **The taskbar always comes back.** The optional dock hides it without changing any setting. It is restored when the dock is switched off, on exit, on handled crashes, and at next start. | `DockWindow.cs`, `App.xaml.cs` |
| **Nothing sensitive on disk.** `%AppData%\Mindspace` holds settings, your reminders and a size-capped error log. Prompts, answers, clipboard text and window titles are never written. | `Services/Settings.cs`, `Services/Reminders.cs` |
| **No third-party code.** Zero NuGet packages; the app is this repository plus the .NET runtime. | `Mindspace.csproj` |

## How it was checked

- A second reviewer (a separate AI agent that had not written the code) audited every source file against the list above. It confirmed the network, text-only, process-launch and elevation guarantees, and found gaps in the others. Those fixed: cloud model tags, the paste target drifting while the panel is pinned, clipboard restore side effects (removed; Insert now behaves like Copy + Paste), a missing re-check before the synthetic copy, the context tag break-out, and several smaller races and hangs.
- Insert was tested end to end against a throwaway text window.
- `tests/Mindspace.Tests` covers the date rules, promise detection and the private-window filter (32 checks).

## Known limits

- **IDE terminals are not detected.** The terminal check goes by program name, so the terminal pane inside VS Code or a JetBrains IDE looks like the editor. Do not press Insert, or a selection hotkey, with the cursor in one.
- **A hard kill leaves the taskbar hidden** if the dock was on (End Task, power loss). Starting Mindspace again restores it; so does restarting Explorer or signing out.
- **Other programs running as you** can send the app UI commands through its local control channel: open a panel, ask an agent a question, show a suggestion card, quit. They cannot press Insert, read anything back, or run anything. A program already running as you can do far more by itself.
- **The private-window filter is a keyword list.** It will miss a banking site whose title does not say so.
- **Ollama is trusted.** Mindspace starts `ollama serve` from its standard install folder if it is not running, and believes whatever answers on port 11434.
- **Suggestion cards are on screen.** If clipboard sensing is on, a card can quote a few words of copied text, which a screen share would show.
