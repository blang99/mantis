# MANTIS

**AI-powered natural language → native Grasshopper scripts for Rhino 8.**

Describe what you want to build in plain language; MANTIS generates real, native
Grasshopper components — wired and ready on your canvas. Not code generation, not a
black box: the actual nodes a human expert would place, live as you describe them.

🌿 **Website:** https://blang99.github.io/mantis/

---

## Features

- **Natural language → real components** — native Grasshopper nodes on the canvas, fully editable.
- **Live build** — components appear and wire themselves as the AI responds.
- **Works from Rhino** — open the MANTIS panel or run the `Mantis` command; Grasshopper loads in the background if needed.
- **Iterative** — follow up in plain language ("make the tower taller", "add a fillet") and MANTIS edits in place.
- **Multi-provider** — Claude, OpenAI, Gemini, OpenRouter, and Ollama. Several free options.

## Install

In Rhino 8:

1. `Tools ▸ Package Manager` (or run the `_PackageManager` command)
2. Search **MANTIS**
3. Click **Install**, then restart Rhino

## First use

- Run the `Mantis` command, or open the **MANTIS** panel from the Panels menu.
- Pick an AI provider and enter a key (Ollama and Gemini are free).
- Type what you want to build — MANTIS builds it on the Grasshopper canvas.

## Free AI providers (no payment needed)

- **Ollama** — runs locally, no key: https://ollama.com
- **Gemini** — free API key: https://aistudio.google.com/apikey
- **OpenRouter** — free models available: https://openrouter.ai

## Build from source

Requires the .NET 7 SDK and Rhino 8 (RhinoCommon / Grasshopper from the McNeel NuGet feed).

```
dotnet build src/Mantis.Plugin/Mantis.Plugin.csproj -c Release
```

The build produces `Mantis.Plugin.rhp` — a single Rhino plugin that also registers the
Grasshopper components.

## Links

- Website: https://blang99.github.io/mantis/
- Issues: https://github.com/blang99/mantis/issues
