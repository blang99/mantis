# MANTIS — Graphic Templates

Screenshot-ready, brand-accurate social graphics. **No API key required** — these are HTML/CSS you render in a browser and screenshot to PNG. Each file's body is sized to the exact export dimensions (in the filename).

## Files

| File | Size | Use |
|------|------|-----|
| `x-card-1600x900.html` | 1600×900 | X/Twitter in-stream card, blog header, general 16:9 |
| `before-after-1080x1080.html` | 1080×1080 | "Same result. One was a sentence." — IG/X square |
| `quote-card-1080x1080.html` | 1080×1080 | Quotable manifesto card (swap `.statement` text to reuse) |
| `carousel-1080x1350.html` | 1080×1350 ×6 | IG/LinkedIn carousel — "5 things MANTIS does" |
| `../og-image.html` | 1200×630 | Existing OG/social share card (already rendered to `og-image.png`) |

## How to export to PNG

**Option A — browser + screenshot tool**
1. Open the file in Chrome.
2. DevTools → Device Toolbar (Cmd/Ctrl+Shift+M) → set a custom device at the exact size in the filename, set DPR to 2 for crisp output.
3. DevTools menu → "Capture screenshot" (for full single-frame files) or use the area capture.

**Option B — headless (cleanest)**
```bash
# requires a headless screenshot tool, e.g. a Chrome-based CLI
chrome --headless --screenshot=x-card.png --window-size=1600,900 --force-device-scale-factor=2 x-card-1600x900.html
```

**Carousel slides** — the 6 slides are stacked vertically (each 1080×1350). Set the viewport to 1080×1350 and capture at scroll positions:
`0, 1350, 2700, 4050, 5400, 6750` → slides 1–6.

## Editing

- All brand tokens are inline at the top of each file (`--mantis:#5CDB7A`, fonts Orbitron / Space Grotesk / JetBrains Mono).
- The constellation mark SVG is reused across files — copy it from any file's `<svg class="mark">`.
- The `quote-card` is a template: change the `.statement` and `.attrib` text to spin up new quote cards in the same style.

## Pairing with copy

These map directly to `../social-content-pack.md`:
- `before-after` → §2B "before/after" post, §11 IG carousel
- `quote-card` → §2B "real components" / "magic reframe" posts
- `x-card` → launch thread header (§2A), profile pinned visual
- `carousel` → §11 "5 things MANTIS does"

> The one asset NOT here: the **Magic Moment screen recording** (§0). It must be captured in live Rhino — it can't be generated. It's the highest-value asset; record it first.
