// Ukaguzi wa usomekaji (WCAG 2.x contrast) wa tokens za rangi, light NA dark.
// Inasoma src/index.css moja kwa moja — ukibadilisha rangi, endesha:  npm run contrast:check
//   Maandishi ya kawaida >= 4.5:1  |  maandishi hafifu/madogo ya pembeni (ink-500) >= 3:1  |  mipaka >= 1.3:1
import { readFileSync } from 'node:fs';

const css = readFileSync(new URL('../src/index.css', import.meta.url), 'utf8');

function block(re) {
  const m = css.match(re);
  if (!m) throw new Error('block haikupatikana: ' + re);
  const vars = {};
  for (const [, k, v] of m[1].matchAll(/--color-([\w-]+):\s*(#[0-9a-fA-F]{6})/g)) vars[k] = v;
  return vars;
}
const light = block(/@theme\s*\{([\s\S]*?)\n\}/);
const dark = { ...light, ...block(/html\[data-theme='dark'\],\s*html\[data-theme='dark'\] \.theme-chrome \.theme-surface\s*\{([\s\S]*?)\}/) };
const chromeDark = { ...dark, ...block(/html\[data-theme='dark'\] \.theme-chrome\s*\{([\s\S]*?)\}/) };

const lum = (hex) => {
  const c = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4));
  return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
};
const ratio = (a, b) => {
  const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p);
  return (x + 0.05) / (y + 0.05);
};

// [maandishi, mandharinyuma, kiwango cha chini, maelezo]
const PAIRS = [
  ...['ink-950', 'ink-900', 'ink-800', 'ink-700', 'ink-600'].flatMap((fg) =>
    ['paper', 'paper-raised', 'ink-50'].map((bg) => [fg, bg, 4.5, 'maandishi'])),
  ...['paper', 'paper-raised'].map((bg) => ['ink-500', bg, 3, 'maandishi hafifu/aikoni']),
  ['ink-50', 'ink-900', 4.5, 'kitufe kikuu'],
  ['ink-50', 'ink-800', 4.5, 'kichupo/chip kilichochaguliwa'],
  ...['good', 'warn', 'overdue'].flatMap((s) => [
    [`standing-${s}`, `standing-${s}-bg`, 4.5, 'beji/onyo'],
    [`standing-${s}`, 'paper-raised', 4.5, 'namba za hali'],
    [`standing-${s}`, 'paper', 4.5, 'namba za hali'],
  ]),
  ['on-accent', 'standing-good', 4.5, 'kitufe "good"'],
  ['on-accent', 'standing-overdue', 4.5, 'kitufe "danger"'],
  ['gold-700', 'gold-100', 4.5, 'beji ya gold'],
  ['gold-700', 'paper-raised', 4.5, 'maandishi ya gold'],
  ['on-gold', 'gold-500', 4.5, 'idadi kwenye kichupo'],
  ['ink-100', 'paper-raised', 1.3, 'mipaka ya kadi'],
];
const CHROME = [
  ['ink-50', 'ink-900', 4.5, 'kichwa: maandishi'],
  ['gold-500', 'ink-900', 4.5, 'kichwa: ChamaLink / kodi'],
];

// Light imeidhinishwa na mtumiaji kama ilivyo — inaripotiwa kama TAARIFA; dark ndiyo lazima ipite.
let failed = 0;
function run(name, vars, pairs, strict = true) {
  console.log(`\n== ${name}`);
  for (const [fg, bg, min, what] of pairs) {
    const r = ratio(vars[fg], vars[bg]);
    const ok = r >= min;
    if (!ok && strict) failed++;
    console.log(`${ok ? 'OK  ' : strict ? 'FAIL' : 'INFO'} ${r.toFixed(2).padStart(5)}:1 (>= ${min})  ${fg} juu ya ${bg}  — ${what}`);
  }
}
run('LIGHT (taarifa)', light, PAIRS, false);
run('LIGHT chrome (taarifa)', light, CHROME, false);
run('DARK', dark, PAIRS);
run('DARK chrome', chromeDark, CHROME);
if (failed) {
  console.error(`\n${failed} jozi hazisomeki vya kutosha.`);
  process.exit(1);
}
console.log('\nJozi zote zinasomeka.');
