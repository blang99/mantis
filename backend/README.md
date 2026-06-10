# MANTIS — community lesson-sharing backend (Phase 5)

Opt-in, anonymized cross-user learning. MANTIS installs that **explicitly opt in** share generalized,
geometry-free "lessons" (e.g. *"Circle CNR" is not a real component → use "Circle"*) so a mistake one
user hits becomes one no user repeats. **The plugin is inert until you host this and a user turns it on.**

## What it stores (and nothing else)
- An **opaque random install id** (a GUID — no account, email, IP-derived id).
- The **generalized correction**: `trigger`, `remedy`, `tags`.

No geometry, no prompts, no designs, no file contents. Ever.

## Safety built in
- **Corroboration moderation** — a lesson only reaches the public bundle after **≥2 distinct installs**
  report it, so one actor can't poison shared knowledge.
- **Hard caps + PII/coordinate guard** on ingest (rejects emails, coordinate lists, over-long text).
- **Erasure** — `POST /forget {installId}` removes that install's contribution everywhere.

## Endpoints
| Method | Path | Purpose |
|---|---|---|
| `POST` | `/lessons` | ingest one lesson (`installId, key, trigger, remedy, tags`) |
| `GET`  | `/bundle`  | corroborated lessons as `[{Key,Trigger,Remedy,Tags}]` |
| `POST` | `/forget`  | erase an install's contributions (`installId`) |
| `GET`  | `/health`  | liveness |

## Deploy (Cloudflare, free tier ≈ $0)
```bash
npm i -g wrangler && wrangler login
wrangler kv namespace create LESSONS     # paste the id into wrangler.toml
wrangler deploy
```
(Any host works — it's one stateless function + a key/value store. Cloudflare Workers is just the cheapest.)

## Turn it on in MANTIS
Set these in `%AppData%/Mantis/settings.json` (or the future Settings → Sharing toggle):
```json
{ "lessonSyncEndpoint": "https://mantis-lessons.<you>.workers.dev", "shareLessons": "on" }
```
Both default OFF/empty, so nothing is shared until you do this deliberately.

## Your obligation as operator
Before flipping this on for users, **update the privacy copy** (site + plugin) to state exactly the above,
keep sharing **opt-in**, and honor erasure. This is the one step only you can own.
