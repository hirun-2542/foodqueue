// Renders every design/src/frames/*.html to design/out/<id>.png.
//   node design/build.mjs            -> all frames
//   node design/build.mjs c1-menu o1 -> frames whose id starts with any argument
// Each frame declares its size: <meta name="frame" content="390x844@2">  (WxH @ deviceScaleFactor, optional ",full").
// Icons: <svg class="i"><use href="#i-NAME"/></svg> -> lucide NAME, sprite injected per frame from ICON_DIR.
import { readFileSync, writeFileSync, mkdirSync, readdirSync, existsSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { createRequire } from 'node:module';

const here = dirname(fileURLToPath(import.meta.url));
const FRAMES = join(here, 'src/frames');
const BUILT = join(here, 'src/_built');
const OUT = join(here, 'out');
const ICON_DIR = process.env.ICON_DIR || join(here, 'icons');
// Playwright comes from the frontend install (pnpm store); override with PW_CORE for another copy.
const testJson = createRequire(join(here, '..', 'frontend', 'package.json')).resolve('@playwright/test/package.json');
const testRequire = createRequire(testJson);
const PW = process.env.PW_CORE
  || join(dirname(testRequire.resolve('playwright-core/package.json')), 'index.mjs');
const { chromium } = await import(pathToFileURL(PW).href);

mkdirSync(BUILT, { recursive: true });
mkdirSync(OUT, { recursive: true });

const filters = process.argv.slice(2);
const ids = readdirSync(FRAMES).filter((f) => f.endsWith('.html')).map((f) => f.slice(0, -5)).sort((a, b) => (a.startsWith('d1') - b.startsWith('d1')) || a.localeCompare(b))
  .filter((id) => !filters.length || filters.some((p) => id.startsWith(p)));

function sprite(html) {
  const names = [...new Set([...html.matchAll(/href="#i-([a-z0-9-]+)"/g)].map((m) => m[1]))];
  const missing = [];
  const symbols = names.map((n) => {
    const p = join(ICON_DIR, `${n}.svg`);
    if (!existsSync(p)) { missing.push(n); return ''; }
    const inner = readFileSync(p, 'utf8').replace(/<!--[\s\S]*?-->/g, '').replace(/^[\s\S]*?<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '');
    return `<symbol id="i-${n}" viewBox="0 0 24 24">${inner}</symbol>`;
  });
  return { svg: `<svg xmlns="http://www.w3.org/2000/svg" style="position:absolute;width:0;height:0" aria-hidden="true">${symbols.join('')}</svg>`, missing };
}

const clippedScan = `(() => [...document.querySelectorAll('td,th,.badge,.btn,.pill,.nav,.num,.mono,.kpi-value,.kpi-label,.t-small,.t-micro,h1,h2,h3,label,.chip,.thumb,.input')]
  .filter((el) => el.scrollWidth > el.clientWidth + 1 && el.textContent.trim() !== '' && !el.closest('[class*="clamp"]'))
  .map((el) => el.tagName + '.' + el.className + ':' + el.textContent.trim().slice(0, 30) + ':' + el.scrollWidth + '/' + el.clientWidth))()`;
const overflowScan = `(() => { const W = document.documentElement.clientWidth; const out = [];
  const clipped = (el) => { for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) { if (getComputedStyle(p).overflowX === 'hidden' && p.getBoundingClientRect().right <= W + 1) return true; } return false; };
  for (const el of document.querySelectorAll('body *')) { const r = el.getBoundingClientRect(); if (r.width && r.right > W + 1 && !el.closest('.allow-overflow') && !clipped(el)) out.push(el.tagName + '.' + el.className + ' right=' + Math.round(r.right)); }
  return out.slice(0, 8); })()`;

const browser = await chromium.launch();
let bad = 0;
for (const id of ids) {
  const src = readFileSync(join(FRAMES, `${id}.html`), 'utf8');
  const m = src.match(/<meta name="frame" content="(\d+)x(\d+)(?:@([\d.]+))?(,full)?"/);
  if (!m) { console.log(`✗ ${id}: missing <meta name="frame">`); bad++; continue; }
  const [w, h, dsf, full] = [+m[1], +m[2], +(m[3] || 1), !!m[4]];
  const { svg, missing } = sprite(src);
  if (missing.length) console.log(`! ${id}: missing icons ${missing.join(', ')}`);
  const html = src.replace(/(<body[^>]*>)/, `$1${svg}`);
  const bp = join(BUILT, `${id}.html`);
  writeFileSync(bp, html);
  const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: dsf });
  const page = await ctx.newPage();
  await page.goto(pathToFileURL(bp).href, { waitUntil: 'load' });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(150);
  const clipped = await page.evaluate(clippedScan);
  const over = await page.evaluate(overflowScan);
  const tofu = await page.evaluate(() => { // glyphs the resolved fonts cannot draw show as tofu; probe common culprits
    const t = document.body.innerText; return [...new Set(t.match(/[\u2190-\u21FF\u2600-\u27BF\u{1F300}-\u{1FAFF}]/gu) || [])].join(''); });
  const png = join(OUT, `${id}.png`);
  await page.screenshot({ path: png, fullPage: full });
  await ctx.close();
  const flag = clipped.length || over.length || tofu;
  if (flag) bad++;
  console.log(`${flag ? '✗' : '✓'} ${id} ${w}x${h}@${dsf}${full ? ' full' : ''}${clipped.length ? '\n   clipped: ' + clipped.join('\n            ') : ''}${over.length ? '\n   overflow-x: ' + over.join('\n               ') : ''}${tofu ? '\n   non-font glyphs: ' + tofu : ''}`);
}
await browser.close();
console.log(`${ids.length} frames, ${bad} flagged`);
process.exit(bad ? 2 : 0);
