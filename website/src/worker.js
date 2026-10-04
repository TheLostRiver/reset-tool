import { GITHUB_API, releaseCatalog } from './releases.js';

const jsonHeaders = { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'public, max-age=300', 'X-Content-Type-Options': 'nosniff' };

async function catalog(request, env, ctx) {
  const key = new Request(new URL('/api/releases', request.url), { method: 'GET' });
  const cache = caches.default;
  const cached = await cache.match(key);
  if (cached) return cached;
  try {
    const headers = { 'Accept': 'application/vnd.github+json', 'User-Agent': 'Frostbound-Website', 'X-GitHub-Api-Version': '2022-11-28' };
    const responses = await Promise.all([
      fetch(GITHUB_API + '/releases?per_page=100', { headers, signal: AbortSignal.timeout(7000) }),
      fetch(GITHUB_API + '/releases/latest', { headers, signal: AbortSignal.timeout(7000) })
    ]);
    if (!responses[0].ok) throw new Error('Release catalog unavailable');
    const releases = await responses[0].json();
    const latest = responses[1].ok ? await responses[1].json() : null;
    const response = Response.json(releaseCatalog(releases, latest?.tag_name), { headers: jsonHeaders });
    ctx.waitUntil(cache.put(key, response.clone())); return response;
  } catch {
    const fallback = await env.ASSETS.fetch(new Request(new URL('/releases.json', request.url)));
    if (!fallback.ok) return Response.json({ error: '暂时无法获取版本，请稍后重试。' }, { status: 503, headers: { ...jsonHeaders, 'Cache-Control': 'no-store' } });
    return new Response(fallback.body, { headers: { ...jsonHeaders, 'Cache-Control': 'public, max-age=60' } });
  }
}

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    if (url.pathname === '/api/releases') {
      if (!['GET', 'HEAD'].includes(request.method)) return new Response('Method not allowed', { status: 405, headers: { Allow: 'GET, HEAD' } });
      const response = await catalog(request, env, ctx);
      return request.method === 'HEAD' ? new Response(null, response) : response;
    }
    if (url.pathname.startsWith('/download/')) {
      if (!['GET', 'HEAD'].includes(request.method)) return new Response('Method not allowed', { status: 405, headers: { Allow: 'GET, HEAD' } });
      let tag;
      try { tag = decodeURIComponent(url.pathname.slice('/download/'.length)); } catch { return new Response('Version not found', { status: 404 }); }
      if (!/^(latest|v?\d[\w.+-]{0,63})$/.test(tag)) return new Response('Version not found', { status: 404 });
      const format = url.searchParams.get('format') ?? 'exe';
      if (!['exe', 'zip', 'checksums'].includes(format)) return new Response('Format not found', { status: 404 });
      const response = await catalog(request, env, ctx);
      if (!response.ok) return response;
      const data = await response.json();
      const release = data.versions.find(item => item.tag === (tag === 'latest' ? data.latest : tag));
      const asset = release?.downloads[format];
      if (!asset) return new Response('Download not found', { status: 404 });
      return new Response(null, { status: 302, headers: { Location: asset.url, 'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer' } });
    }
    return env.ASSETS.fetch(request);
  }
};
