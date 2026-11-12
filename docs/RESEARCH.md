# Which model, and why

Goal: the least powerful model that is still good enough, on this machine (i9-13900H, 32 GB RAM, RTX 4060 Laptop 8 GB, Ollama 0.34.2).

**Result: one model, `qwen3.5:4b`, shared by all four agents (2.9 GB VRAM, ~46 tokens/s, first word in ~0.2 s once loaded). Dates are parsed by rules, not by a model.**

## How it was chosen

1. A web survey (Nov 2026) of small open-weight models available as Ollama tags produced a shortlist.
2. Six of them were run on this laptop with `tools/bench.mjs`, using the same prompts the app uses.
3. I read every output (`docs/bench/outputs.md`, `docs/bench/outputs-round2.md`) and tightened the prompts once (round 2).

`lfm3:1.7b` was on the shortlist but would not pull on Ollama 0.34.2. I did not update Ollama, since that is your system; it is worth retrying after you update. `gemma4:e2b` was skipped because it needs about 5 GB, which defeats the purpose.

## Measurements (this laptop)

| Model | VRAM | Tokens/s | First word | Reminder dates, round 1 | Round 2 |
|---|---|---|---|---|---|
| qwen3.5:0.8b | 0.5 GB | 127 | 0.09 s | 2/8 | – |
| gemma3:1b | 0.8 GB | 157 | 0.07 s | 2/8 | – |
| qwen3.5:2b | 2.2 GB | 78 | 0.09 s | 4/8 | 5/8 |
| granite4:micro | 2.3 GB | 76 | 0.08 s | 3/8 | 5/8 |
| llama3.2:3b | 2.4 GB | 79 | 0.07 s | 4/8 | 4/8 |
| **qwen3.5:4b** | **2.9 GB** | **46** | **0.2 s** | 6/8 | **8/8** |
| Rules (`WhenParser`) | 0 | instant | – | – | **8/8** |

Cold load (model not in memory) adds 3–7 s. The app hides most of that by starting the load when you hover the island or open an agent.

## What the outputs showed

- **Under 2 GB is not enough.** `qwen3.5:0.8b` called a PC at 91% CPU "Healthy" and turned a friendly request into a demand. `gemma3:1b` dropped the greeting and sign-off from a reply.
- **The 2–3B group is close but each failed something.** `qwen3.5:2b` added things nobody said ("please keep your updates concise"). `granite4:micro` wrote the cleanest short emails but got a team message wrong ("The client call has been scheduled"). `llama3.2:3b` invented a GPU overheating problem at 54 °C.
- **`qwen3.5:4b` was the smallest that passed everything**, after one prompt fix. In round 1 it invented an address for the landlord email; adding "use only facts the user gave" removed that in round 2. It costs 0.7 GB more VRAM than the 2B models.
- **Every model put a `Subject:` line on replies** despite being told not to. The app removes it in code.
- **Small models are bad at dates.** Round 1 listed two Fridays in the calendar hint and every model picked the wrong one. With that fixed, only the 4B model got 8/8. A rule-based parser gets the same 8/8 (plus 14 more everyday phrasings in `tests/`) instantly and with no GPU, so Tick uses rules first and only falls back to the model for unusual wording.

## Design choices that follow

- **One model for everything.** Two different models would both sit in VRAM. One shared model stays at 2.9 GB.
- **`keep_alive` of 3 minutes.** The model leaves VRAM shortly after you stop using it. Adjustable in `settings.json`.
- **Thinking mode off** (`think: false`). These are quick tasks; reasoning tokens only add delay.
- **4k context.** Enough for an email or a short chat; larger contexts cost VRAM.
- **No model where rules do.** Reminder dates and spotting promises ("I'll send it Friday") are regular expressions.

## If you want it lighter

Set any agent to `qwen3.5:2b` in Settings: 2.2 GB and 78 tokens/s, with slightly looser writing. `granite4:micro` is a good choice for Quill alone if you mostly write short, plain emails.

## Reproduce

```bash
node tools/bench.mjs
```
