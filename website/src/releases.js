export const REPOSITORY = 'TheLostRiver/reset-tool';
export const GITHUB_API = 'https://api.github.com/repos/' + REPOSITORY;
const downloadPrefix = 'https://github.com/' + REPOSITORY + '/releases/download/';

function assetLink(asset) {
  return asset && asset.browser_download_url?.startsWith(downloadPrefix)
    ? { url: asset.browser_download_url, size: asset.size, digest: asset.digest ?? null }
    : null;
}

export function normalizeRelease(release) {
  if (release.draft || release.prerelease || !release.tag_name) return null;
  const assets = release.assets ?? [];
  const exe = assetLink(assets.find(asset => asset.name.toLowerCase() === 'frostbound.exe'));
  const zip = assetLink(assets.find(asset => /win-x64\.zip$/i.test(asset.name)));
  if (!exe && !zip) return null;
  const changes = [];
  for (const line of (release.body ?? '').split('\n')) {
    if (/^下载[：:]/.test(line.trim())) break;
    const item = line.match(/^\s*[-*]\s+(.+)$/)?.[1];
    if (item) changes.push(item.replace(/`([^`]+)`/g, '$1'));
  }
  return {
    tag: release.tag_name,
    name: release.name ?? release.tag_name,
    summary: (release.body ?? '').split('\n').map(line => line.trim()).find(line => line && !/^[-*#]/.test(line)) ?? release.name ?? release.tag_name,
    date: release.published_at ?? release.created_at,
    url: 'https://github.com/' + REPOSITORY + '/releases/tag/' + encodeURIComponent(release.tag_name),
    changes,
    downloads: { exe, zip, checksums: assetLink(assets.find(asset => /SHA256\.txt$/i.test(asset.name))) }
  };
}

export function releaseCatalog(releases, latestTag) {
  const versions = releases.map(normalizeRelease).filter(Boolean).sort((a, b) => new Date(b.date) - new Date(a.date));
  if (!versions.length) throw new Error('No downloadable releases');
  return { latest: versions.find(release => release.tag === latestTag)?.tag ?? versions[0].tag, versions };
}
