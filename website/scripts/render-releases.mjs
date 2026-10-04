import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';

const publicFile = name => new URL('../public/' + name, import.meta.url);
const escape = value => String(value).replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]);
const date = value => new Intl.DateTimeFormat('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit', timeZone: 'Asia/Shanghai' }).format(new Date(value)).replaceAll('/', '.');
const download = (release, format) => '/download/' + encodeURIComponent(release.tag) + '?format=' + format;
const icon = name => '<svg aria-hidden="true"><use href="#' + name + '"/></svg>';

export async function renderReleases() {
  const catalog = JSON.parse(await readFile(publicFile('releases.json'), 'utf8'));
  const latest = catalog.versions.find(release => release.tag === catalog.latest);
  if (!latest) throw new Error('The latest release is missing from the catalog');
  let html = await readFile(publicFile('index.html'), 'utf8');
  const options = catalog.versions.map(release => '<option value="' + escape(release.tag) + '"' + (release.tag === latest.tag ? ' selected' : '') + '>' + escape(release.tag) + (release.tag === latest.tag ? ' · 最新' : '') + '</option>').join('');
  const cards = catalog.versions.map(release => {
    const label = release.tag === latest.tag ? ' <span class="latest-label">最新</span>' : '';
    const heading = release.summary ?? release.name.replace(/霜序\s*Frostbound\s*/i, '');
    return '          <article class="release-card">\n' +
      '            <div class="release-heading"><div class="release-number">' + escape(release.tag) + label + '</div><time datetime="' + escape(release.date) + '">' + date(release.date) + '</time></div>\n' +
      '            <h3>' + escape(heading) + '</h3>\n' +
      '            <ul>' + (release.changes ?? []).map(change => '<li>' + escape(change) + '</li>').join('') + '</ul>\n' +
      '            <div class="release-actions"><a href="' + escape(download(release, release.downloads.exe ? 'exe' : 'zip')) + '">下载此版本 ' + icon('download') + '</a><a href="' + escape(release.url) + '" target="_blank" rel="noopener">完整说明 ' + icon('external') + '</a></div>\n' +
      '          </article>';
  }).join('\n');
  if (!html.includes('<!-- releases:start -->') || !html.includes('<!-- releases:end -->')) throw new Error('Release section markers are missing');
  html = html.replace(/(id="version-count">)[^<]*(<\/span>)/, (_, before, after) => before + catalog.versions.length + after);
  html = html.replace(/(<select id="release-select"[^>]*>)[\s\S]*?(<\/select>)/, (_, before, after) => before + options + after);
  const asset = latest.downloads.exe ?? latest.downloads.zip;
  html = html.replace(/(id="download-size">)[^<]*(<\/small>)/, (_, before, after) => before + (latest.downloads.exe ? 'EXE' : 'ZIP') + ' · ' + (asset.size / 1000000).toFixed(1) + ' MB' + after);
  html = html.replace(/(id="release-date">)[^<]*(<\/span>)/, (_, before, after) => before + date(latest.date) + ' 发布' + after);
  html = html.replace(/<!-- releases:start -->[\s\S]*?<!-- releases:end -->/, '<!-- releases:start -->\n        <div class="release-list" id="release-list" aria-live="polite">\n' + cards + '\n        </div>\n        <!-- releases:end -->');
  for (const filename of ['app.js', 'styles.css']) {
    const hash = createHash('sha256').update(await readFile(publicFile(filename))).digest('hex').slice(0, 12);
    html = html.replace(new RegExp('((?:src|href)="/' + filename.replace('.', '\\.') + ')(?:\\?v=[^"]*)?"'), '$1?v=' + hash + '"');
  }
  await writeFile(publicFile('index.html'), html);
  console.log('Homepage release notes rendered: ' + latest.tag);
}
