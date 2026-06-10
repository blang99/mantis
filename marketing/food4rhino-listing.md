# MANTIS — food4rhino listing (draft)

> Draft copy for the food4rhino package page. Honest, strengths-first, no reliability
> guarantees. Positioned broadly (not over-committed to the still-unvalidated
> "offline + verified vertical" wedge) so it holds regardless of where positioning lands.
> Submission needs the founder's account + screenshots/GIF; this is the copy to paste/adapt.

---

## Title
**MANTIS — AI that builds native Grasshopper definitions from plain language**

## Tagline (one line)
Describe what you want; MANTIS plans it and wires up real Grasshopper components on your canvas.

## Short description (≈40 words)
MANTIS turns a plain-language request into a live, native Grasshopper definition — actual
components wired on the canvas, not a code black box. It plans the steps first, builds them, and
learns from its own corrections. Works with free local models or the cloud.

## Long description
MANTIS is an AI assistant for Grasshopper. You type what you're trying to make — "a twisting tower
of stacked floor plates", "a hexagonal site grid sized to the plate" — and MANTIS:

1. **Plans first.** It reads the request and lays out an ordered set of reasoned steps before it
   touches the canvas, so you can see its approach.
2. **Builds native components.** It places and wires real Grasshopper components — the same ones
   you'd drop yourself — so the result is fully editable, not generated code you can't open up.
3. **Shows its work.** Each plan step maps to a labelled group on the canvas; click a step to jump
   straight to it.
4. **Repairs as it goes.** A validation pass catches unrecognized component names and bad wiring
   before they reach the canvas and asks the model to fix them, and MANTIS remembers those
   corrections so it's sharper next time.

**Your choice of model — including free ones.** MANTIS works with Claude, OpenAI, Gemini and
OpenRouter, and with **Ollama running locally on your machine** (no API key, fully offline). Your
API key stays on your computer; MANTIS has no backend and sends nothing anywhere except the
provider you pick.

It's early and it won't nail every complex ask yet — that's exactly the feedback we want. But the
first time you watch an idea become a working definition, the tool gets out of the way and the
thinking comes back.

## Key features (bullets)
- Natural language → **native** Grasshopper components (not GHPython/code)
- Plans the workflow first, then builds it
- Clickable plan steps that jump to the matching group on the canvas
- Validate-and-repair pass for component names and wiring
- Remembers its own corrections locally
- Multiple providers: Claude, OpenAI, Gemini, OpenRouter, **and local Ollama (free/offline)**
- Local-only: your API key never leaves your machine; no backend, no telemetry
- Windows **and** macOS, Rhino 8

## Requirements
- Rhino 8 (Windows or macOS)
- An LLM provider: a free local model via [Ollama](https://ollama.com), a free
  [Gemini](https://aistudio.google.com/apikey) key, OpenRouter, or a Claude/OpenAI key

## Install
Rhino 8 → Package Manager → search **MANTIS** → Install → restart. Then run the `Mantis` command or
open the MANTIS panel.

## Links
- Website: https://blang99.github.io/mantis/
- Source: https://github.com/blang99/mantis
- Issues / feedback: https://github.com/blang99/mantis/issues

## Tags
ai, parametric, natural-language, generative-design, grasshopper, computational-design, llm,
ollama, claude, gemini

## Assets needed before submitting
- [ ] A short screen-recording GIF of a real build (the strongest asset — a recording, not a mockup)
- [ ] 2–3 screenshots: the panel, a plan with clickable steps, a finished definition on the canvas
- [ ] The MANTIS icon (already in the repo)
