/**
 * MANTIS — community lesson-sharing backend (reference implementation, Cloudflare Worker).
 *
 * Phase 5 of MANTIS: an OPT-IN service that lets MANTIS installs share anonymized, geometry-free
 * "lessons" (a bad component name → the right one, etc.) so a mistake one user hits is a mistake
 * no user repeats. The MANTIS plugin only ever talks to this if the user explicitly opts in AND
 * sets the endpoint — see src/Mantis.Plugin/Knowledge/LessonSync.cs (inert by default).
 *
 * WHAT IT STORES (and nothing else): an opaque random install id (no account/email/IP-derived id),
 * plus the generalized correction text (trigger + remedy + tags). No geometry, no prompts, no designs.
 *
 * MODERATION = CORROBORATION: a lesson only enters the public /bundle once it has been reported by
 * at least MIN_INSTALLS DISTINCT installs — so a single actor can't poison the shared knowledge.
 * Plus hard length caps and a coarse PII/coordinate guard on ingest.
 *
 * DEPLOY (free tier):
 *   1. npm i -g wrangler && wrangler login
 *   2. wrangler kv namespace create LESSONS        # then put the id in wrangler.toml
 *   3. wrangler deploy
 *   4. In MANTIS: set lessonSyncEndpoint = https://<your-worker>.workers.dev  and shareLessons = on
 *
 * PRIVACY OBLIGATION (yours, as operator): publish a short policy describing exactly the above,
 * make sharing opt-in (the plugin already defaults it OFF), and honor POST /forget for erasure.
 */

const MIN_INSTALLS = 2;     // a lesson must be corroborated by >=2 installs before it's shared
const MAX_LEN = 200;        // hard cap on trigger/remedy length
const MAX_BUNDLE = 500;     // cap the bundle size

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const cors = {
      'Access-Control-Allow-Origin': '*',
      'Access-Control-Allow-Methods': 'GET,POST,OPTIONS',
      'Access-Control-Allow-Headers': 'Content-Type',
    };
    if (request.method === 'OPTIONS') return new Response(null, { headers: cors });

    try {
      if (url.pathname === '/health') return json({ ok: true }, cors);
      if (url.pathname === '/lessons' && request.method === 'POST') return await ingest(request, env, cors);
      if (url.pathname === '/bundle' && request.method === 'GET') return await bundle(env, cors);
      if (url.pathname === '/forget' && request.method === 'POST') return await forget(request, env, cors);
      return json({ error: 'not found' }, cors, 404);
    } catch (_) {
      return json({ error: 'server error' }, cors, 500);
    }
  },
};

function json(obj, cors, status = 200) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { 'Content-Type': 'application/json', ...cors },
  });
}

// Coarse guard: reject anything that smells like geometry/PII (emails, long coordinate runs).
function looksUnsafe(s) {
  if (!s) return false;
  if (/[\w.+-]+@[\w-]+\.[\w.-]+/.test(s)) return true;               // email
  if (/(-?\d+\.\d+[,\s]+){3,}/.test(s)) return true;                  // coordinate lists
  return false;
}

function clean(s) {
  return String(s || '').replace(/\s+/g, ' ').trim().slice(0, MAX_LEN);
}

async function ingest(request, env, cors) {
  const body = await request.json().catch(() => null);
  if (!body) return json({ error: 'bad json' }, cors, 400);

  const key = clean(body.key);
  const trigger = clean(body.trigger);
  const remedy = clean(body.remedy);
  const tags = clean(body.tags);
  const installId = clean(body.installId);
  if (!key || !remedy || !installId) return json({ error: 'missing fields' }, cors, 400);
  if (looksUnsafe(trigger) || looksUnsafe(remedy) || looksUnsafe(tags))
    return json({ error: 'rejected' }, cors, 422);

  const id = 'lesson:' + key;
  const existing = JSON.parse((await env.LESSONS.get(id)) || 'null') || {
    key, trigger, remedy, tags, installs: [], count: 0,
  };
  if (!existing.installs.includes(installId)) existing.installs.push(installId);
  existing.count = (existing.count || 0) + 1;
  existing.trigger = trigger || existing.trigger;
  existing.remedy = remedy;            // latest remedy wins
  existing.tags = tags || existing.tags;
  await env.LESSONS.put(id, JSON.stringify(existing));

  return json({ ok: true, corroborated: existing.installs.length >= MIN_INSTALLS }, cors);
}

async function bundle(env, cors) {
  const list = await env.LESSONS.list({ prefix: 'lesson:' });
  const out = [];
  for (const k of list.keys) {
    const l = JSON.parse((await env.LESSONS.get(k.name)) || 'null');
    if (l && (l.installs?.length || 0) >= MIN_INSTALLS) {
      out.push({ Key: l.key, Trigger: l.trigger, Remedy: l.remedy, Tags: l.tags });
      if (out.length >= MAX_BUNDLE) break;
    }
  }
  return json(out, cors);
}

// Erasure: drop one install's contribution from every lesson it touched.
async function forget(request, env, cors) {
  const body = await request.json().catch(() => null);
  const installId = clean(body?.installId);
  if (!installId) return json({ error: 'missing installId' }, cors, 400);

  const list = await env.LESSONS.list({ prefix: 'lesson:' });
  let touched = 0;
  for (const k of list.keys) {
    const l = JSON.parse((await env.LESSONS.get(k.name)) || 'null');
    if (l && l.installs?.includes(installId)) {
      l.installs = l.installs.filter((x) => x !== installId);
      if (l.installs.length === 0) await env.LESSONS.delete(k.name);
      else await env.LESSONS.put(k.name, JSON.stringify(l));
      touched++;
    }
  }
  return json({ ok: true, touched }, cors);
}
