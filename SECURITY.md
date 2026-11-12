# Safety and privacy

Mindspace is an always-on overlay. It can see which window is in front, read that window's text when you ask, listen to the microphone while you hold a key, and type into other apps. That deserves a precise account of what it can and cannot do.

## What the app guarantees

| Guarantee | Where it is enforced |
|---|---|
| **One network destination.** The only connection is to Ollama on this PC (`127.0.0.1:11434`). No proxy, no redirects, no telemetry, no update check. The app never downloads anything. | `Services/Ollama.cs` is the only network code |
| **On-device models only.** Ollama can forward "cloud" model tags to a remote service; Mindspace refuses them. Speech is turned into text by a local program with no network use. | `Ollama.IsRemoteTag`, `Services/Speech.cs` |
| **Agents only produce text.** Model output is never run, never used as a path, URL or command, and has no tools. Links in answers are not clickable. This includes agents you define yourself. | `Controls/Markdown.cs`, `Views/Gen.cs`, `Agents.CustomSystem` |
| **No model controls the mouse or keyboard.** The two things Iris can do to a window (scroll it to collect text, tap Space in a runner game) are fixed loops written in code. Text on screen or in a transcript cannot change what they do. | `Services/ScreenSkills.cs` |
| **Nothing is typed, scrolled or pressed without you.** Text reaches another app only when you press Insert, or when you dictate it yourself by holding the key. Collect and Autoplay start only from the Start button, on the window named on the card; a spoken or typed request only prepares the card, and the card is dropped if you move to another window. | `PanelWindow.PasteIntoAsync`, `Views/IrisView.cs` |
| **It goes to the window you meant.** Insert and dictation paste into one specific window and only if that window really has focus. Autoplay presses nothing until it has recognised a running game, re-checks once a second that the game is still on screen, and stops the moment the window, tab or page changes. Collect never takes focus or types: it only turns the mouse wheel over its window. | `PanelWindow.PasteIntoAsync`, `ScreenSkills` |
| **Never into a terminal or remote session.** Pasted text can run as commands there, so Insert and dictation leave the text on the clipboard instead, and the screen skills refuse. | `Native.IsTerminal`, `ScreenSkills.Allowed` |
| **Windows are read only when you ask.** Opening Quill in a mail or chat window, pressing a chip or hotkey, or asking Iris reads the window once, through the accessibility interface a screen reader uses. No screenshots are taken for reading. The text is passed to the local model and not stored, unless you choose Save or Collect. | `Services/WindowReader.cs` |
| **Sensitive windows are skipped.** No reading, suggestions, pasting or skills when the window title mentions passwords, password managers, banking or private browsing. Password boxes are never read. | `ContextSensor.IsPrivate`, `WindowReader` |
| **The microphone is open only while you record.** That is while you hold (or have tapped) the dictation key, or while a note is recording in Echo. A card under the island says "Listening" or "Taking notes" the whole time, and Windows shows its own microphone indicator. There is no wake word, and only your own key press or click can open the microphone: a spoken or typed request to Echo just opens it. | `HaloWindow.Voice.cs`, `Views/EchoView.cs` |
| **Audio is never kept.** Sound is held in memory, written to one temporary file for the speech engine, and that file is deleted as soon as the text comes back. Anything a crash left behind is removed at the next start. | `Speech.TranscribeAsync` |
| **Copied and on-screen text is treated as data.** It is wrapped in a marked section and its closing tag is defanged, so it cannot pose as instructions to the model. | `Gen.AsContext` |
| **Patch is read-only.** It cannot end processes or change settings. | `Views/PatchView.xaml.cs` |
| **No elevation, no drivers, no services.** | `app.manifest` |
| **No third-party code in the app.** Zero NuGet packages. The speech engine is a separate open-source program, fetched by a script that checks every file against a pinned SHA-256. | `Mindspace.csproj`, `tools/get-speech.ps1` |

## What is stored on this PC

Everything is under `%AppData%\Mindspace` (and the speech engine under `%LocalAppData%\Mindspace\speech`):

| File | Contents |
|---|---|
| `settings.json` | Your settings, your name, and the agents you defined |
| `reminders.json` | Your reminders |
| `notes.json` | The **text** of what you dictated, notes you recorded (with their AI key points) and window text you saved or collected (with that window's title). Words dictated at a private window are not kept. Switch "Keep history" off and nothing is written; "Delete all" removes the file |
| `error.log` | Crash details, size-capped. No prompts, answers or window text |

Apart from what `notes.json` holds, prompts, answers, clipboard text, window text and window titles are never written anywhere.

## What it changes on your system

| Change | When | Put back |
|---|---|---|
| "Start with Windows" value under your own user | Only if you switch it on | When you switch it off |
| Taskbar hidden and its auto-hide switch turned on | Only while the optional dock is on | When you turn the dock off or quit, on handled crashes, and at the next start. Your previous auto-hide choice is remembered in `settings.json` |

## How it was checked

- For 0.1 and again for 0.2, a second reviewer (a separate AI agent that had not written the code) audited the source against this list by reading it. The 0.2 review confirmed the text-only, click-to-start, paste-target, own-window and no-new-network guarantees, and found eleven gaps, all fixed:
  - the Start card could run on a different window than the one it named;
  - Autoplay pressed Space before it had found a game, and kept going after a tab switch;
  - a local program could start an Echo recording or a dictation through the control channel;
  - a cancelled transcription could leave its temporary audio file behind;
  - dictation at a private window was kept in history;
  - Esc did not reliably stop Collect;
  - sentences starting with a word like "Patch" or "Tick" were sent to that agent (it now takes "Tick, …" or "hey Tick …");
  - three faults in microphone capture (last 100 ms dropped, a buffer freed too early, pieces out of order);
  - a status line could cover a due reminder;
  - slow apps could pile up reader threads, and long documents lost their end;
  - a window title reached the model outside the data fence.
- Insert was tested end to end against a throwaway text window. Collect was tested on a long page in Chrome, Autoplay on Chrome's offline game, window reading on Chrome and an Electron app, and transcription on recorded audio.
- `tests/Mindspace.Tests` covers the date rules, promise detection, the private-window filter, voice routing, window-text clean-up, custom-agent limits and note search (49 checks).

## Known limits

- **IDE terminals are not detected.** The terminal check goes by program name, so the terminal pane inside VS Code or a JetBrains IDE looks like the editor. Do not press Insert, dictate, or use a selection hotkey with the cursor in one.
- **Dictation types what the speech model heard.** It can mishear. Text goes in where your cursor is, so look before pressing Enter in a chat box.
- **A hard kill leaves the taskbar hidden and on auto-hide** if the dock was on (End Task, power loss). Starting Mindspace again restores both.
- **Other programs running as you** can send the app UI commands through its local control channel: open a panel, ask an agent a question (which, for Iris or Sage, reads the window in front and shows the answer in the panel; for Tick, adds a reminder), show a suggestion card, quit. They cannot press Insert or Start, cannot open the microphone, and nothing is sent back to them. A program already running as you can do far more by itself.
- **The speech engine is trusted.** It lives in a folder you can write to; a program running as you could replace it. The setup script verifies it when it is installed, not each time it runs.
- **The private-window filter is a keyword list.** It will miss a banking site whose title does not say so.
- **Window text is whatever the app exposes.** It can include text you cannot see (collapsed sections, off-screen messages).
- **Ollama is trusted.** Mindspace starts `ollama serve` from its standard install folder if it is not running, and believes whatever answers on port 11434.
- **Suggestion cards are on screen.** A screen share would show them.
