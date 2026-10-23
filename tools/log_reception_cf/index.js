// Iron Admiral - log reception (Cloudflare Worker + Workers KV)
// Receives game logs from players who switched on "Share logs" in the mod. Each upload is stored compressed in KV under
// logs/<yyyy-mm-dd>/<hhmmss>_<kind>_<version>_<id>, kept for 90 days (KV expiration), then removed automatically.
const TOKEN = 'ia-logs-v1';            // sent by the mod; not a secret - it only filters random requests
const MAX_BYTES = 3 * 1024 * 1024;     // compressed size limit per upload
const MAX_PER_HOUR = 40;               // uploads per player id per hour
const KEEP_SECONDS = 90 * 24 * 3600;

export default {
  async fetch(req, env) {
    if (req.method === 'GET') return new Response('Iron Admiral log reception is running');
    if (req.method !== 'POST') return new Response('method', { status: 405 });
    try {
      const len = Number(req.headers.get('content-length') || 0);
      if (len > MAX_BYTES * 1.4 + 2000) return new Response('size', { status: 413 });
      const d = await req.json();
      if (d.token !== TOKEN) return new Response('bad token', { status: 403 });
      const id = String(d.id || 'anon').replace(/[^A-Za-z0-9-]/g, '').slice(0, 40) || 'anon';
      const kind = String(d.kind || 'log').replace(/[^a-z]/g, '').slice(0, 12) || 'log';
      const ver = String(d.ver || '').replace(/[^0-9A-Za-z.\-]/g, '').slice(0, 30);
      const bytes = Uint8Array.from(atob(String(d.gz || '')), c => c.charCodeAt(0));
      if (bytes.length === 0 || bytes.length > MAX_BYTES) return new Response('size', { status: 413 });
      // rate limit per player id and hour
      const now = new Date();
      const hourKey = 'rate/' + id + '/' + now.toISOString().slice(0, 13);
      const n = Number((await env.LOGS.get(hourKey)) || 0);
      if (n >= MAX_PER_HOUR) return new Response('rate limit', { status: 429 });
      await env.LOGS.put(hourKey, String(n + 1), { expirationTtl: 7200 });
      const day = now.toISOString().slice(0, 10);
      const time = now.toISOString().slice(11, 19).replace(/:/g, '');
      const key = `logs/${day}/${time}_${kind}_${ver}_${id.slice(0, 8)}_${Math.random().toString(36).slice(2, 6)}`;
      await env.LOGS.put(key, bytes, { expirationTtl: KEEP_SECONDS, metadata: { kind, ver, id, size: bytes.length } });
      return new Response('ok');
    } catch (e) {
      return new Response('error ' + e, { status: 400 });
    }
  }
};
