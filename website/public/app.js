const select = document.querySelector('#release-select');
const releaseList = document.querySelector('#release-list');
const downloadExe = document.querySelector('#download-exe');
const downloadZip = document.querySelector('#download-zip');
const checksum = document.querySelector('#checksum-link');
const pageReleaseDate = Date.parse(document.querySelector('#release-list time')?.dateTime ?? '');
let catalog;
let userSelectedVersion = false;

function icon(name) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  const use = document.createElementNS(svg.namespaceURI, 'use'); use.setAttribute('href', '#' + name); svg.append(use); return svg;
}
function element(tag, className, text) {
  const item = document.createElement(tag); if (className) item.className = className; if (text != null) item.textContent = text; return item;
}
function date(value) { return new Intl.DateTimeFormat('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(value)).replaceAll('/', '.'); }
function downloadPath(release, format) { return '/download/' + encodeURIComponent(release.tag === catalog.latest ? 'latest' : release.tag) + '?format=' + format; }

function updateSelection() {
  const release = catalog.versions.find(item => item.tag === select.value); if (!release) return;
  downloadExe.href = downloadPath(release, release.downloads.exe ? 'exe' : 'zip');
  document.querySelector('#download-size').textContent = (release.downloads.exe ? 'EXE' : 'ZIP') + ' · ' + ((release.downloads.exe ?? release.downloads.zip).size / 1000000).toFixed(1) + ' MB';
  downloadZip.href = downloadPath(release, 'zip'); downloadZip.hidden = !release.downloads.zip;
  checksum.href = downloadPath(release, 'checksums'); checksum.hidden = !release.downloads.checksums;
  document.querySelector('#release-date').textContent = date(release.date) + ' 发布';
}

function render(data) {
  if (!Array.isArray(data.versions) || !data.versions.length) throw new Error('No releases');
  const latest = data.versions.find(release => release.tag === data.latest);
  if (!latest || Date.parse(latest.date) < pageReleaseDate) throw new Error('Stale release catalog');
  catalog = data;
  const previous = select.value;
  select.replaceChildren(...data.versions.map(release => {
    const option = element('option', '', release.tag + (release.tag === data.latest ? ' · 最新' : '')); option.value = release.tag; return option;
  }));
  select.value = userSelectedVersion && data.versions.some(release => release.tag === previous) ? previous : data.latest;
  document.querySelector('#version-count').textContent = data.versions.length;
  const cards = data.versions.map(release => {
    const card = element('article', 'release-card'); const heading = element('div', 'release-heading');
    const number = element('div', 'release-number', release.tag);
    if (release.tag === data.latest) number.append(element('span', 'latest-label', '最新'));
    const time = element('time', '', date(release.date)); time.dateTime = release.date;
    heading.append(number, time); card.append(heading);
    card.append(element('h3', '', release.summary ?? release.name.replace(/霜序\s*Frostbound\s*/i, '')));
    const changes = element('ul');
    for (const change of release.changes ?? []) changes.append(element('li', '', change));
    card.append(changes);
    const actions = element('div', 'release-actions');
    const download = element('a', '', '下载此版本'); download.href = downloadPath(release, release.downloads.exe ? 'exe' : 'zip'); download.append(icon('download'));
    const notes = element('a', '', '完整说明'); notes.href = release.url; notes.target = '_blank'; notes.rel = 'noopener'; notes.append(icon('external'));
    actions.append(download, notes); card.append(actions); return card;
  });
  releaseList.replaceChildren(...cards); updateSelection();
}

select.addEventListener('change', () => { userSelectedVersion = true; updateSelection(); });
document.querySelectorAll('[data-preview]').forEach(button => button.addEventListener('click', () => {
  const compact = button.dataset.preview === 'compact';
  for (const choice of document.querySelectorAll('[data-preview]')) { const active = choice === button; choice.classList.toggle('is-active', active); choice.setAttribute('aria-pressed', String(active)); }
  const image = document.querySelector('#app-screenshot'); image.src = compact ? '/images/compact.png' : '/images/dashboard.png';
  image.alt = compact ? '霜序精简界面：任务、综合配置与快捷键' : '霜序完整界面：任务控制、综合配置与快捷键设置';
  document.querySelector('#screenshot-stage').classList.toggle('is-compact', compact);
}));

async function loadReleases() {
  for (const path of ['/api/releases', '/releases.json']) {
    try { const response = await fetch(path, { cache: 'no-store', signal: AbortSignal.timeout(10000) }); if (!response.ok) continue; render(await response.json()); return; } catch { /* Keep the working latest download links available. */ }
  }
}
loadReleases();
