# Roadmap

The core idea does not change: small helpers at the top of the screen that run on this PC, stay out of the way,
cost almost nothing while idle, and never do anything to another app that you did not start yourself.
Anything that would break one of those four is out, however useful.

## Shipped

**0.1** Island and drop-down panel. Quill (writer), Tick (reminders), Sage (explainer), Patch (IT). Selection hotkeys. Optional dock.

**0.2**
- Frosted glass panels and dock.
- Dock that drops out of the way and lets windows use the whole screen.
- Agents read the window in front instead of relying on the clipboard.
- Voice: hold `Alt + Shift + D` to dictate anywhere, or say an agent's name to talk to it.
- Echo: recorded notes with live transcript, optional AI key points, searchable history.
- Iris: answers questions about the window in front, saves or collects its text, and can keep a runner game going.
- Your own agents (up to three), with their own instructions, face and trigger words.

## Next (0.3)

| Feature | Why | Notes |
|---|---|---|
| Rewrite in place | Select text, pick "shorter" or "fix grammar", and it is replaced where it stands | Builds on the selection hotkeys and Insert |
| Reading the open message only | Mail windows expose folders and old mail around the message; Quill should get just the message | Per-app rules for Outlook, Gmail, Slack, Discord |
| Tick: recurring reminders and a morning card | "every Monday 9am"; one card with what is due today | Date rules, no model |
| Patch history | "What was hogging the PC at 3pm?" | A small rolling log of the heartbeat it already takes |
| Dock: pinned apps and a system tray | The two things the dock still lacks compared with the taskbar | |
| More than one screen | Island and dock on the screen you are using | |
| Installer and signed build | So it can be given to someone else safely | Includes the voice setup step |

## Later

| Feature | Why | What has to be true first |
|---|---|---|
| Patch can act | End a runaway app, switch off a startup item | Every action shown and confirmed; an undo where one exists |
| Iris skills beyond scrolling | Fill a form from your notes, click through a known routine | Each skill a fixed, reviewable loop with a Start button; no free-form model control of mouse and keyboard |
| Meeting notes from system audio | Notes for calls, not just your own microphone | A clear on-screen indicator and per-call consent |
| Calendar and To Do sync | Reminders where you already look | Opt-in; it would be the first feature that leaves this PC |
| Voice replies | Agents that answer aloud | A small local text-to-speech model that passes the same benchmark bar |
| Search across everything saved | "What did Dana say about the invoice?" over notes and captures | A small local embedding model |
| Other languages | Dictation and agents beyond English | Multilingual speech and chat models benchmarked the same way |

## Not planned

- Cloud models, accounts or telemetry.
- An agent that drives the mouse and keyboard from a model's own decisions.
- Always-on listening or a wake word. The microphone opens only while you hold the key or a note is recording.
