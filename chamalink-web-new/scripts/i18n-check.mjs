// npm run i18n:check
// Inakusanya kila maandishi ya t('...'), tk('...') na tn(n, '...', '...') kwenye src/ na kuhakikisha
// yana tafsiri ya Kiingereza kwenye src/i18n/en.ts. Pia inaonya kuhusu vigeu ({x}) visivyolingana.
//   node scripts/i18n-check.mjs            → ripoti; exit 1 kama kuna yanayokosekana
//   node scripts/i18n-check.mjs --keys     → orodha ya funguo zote (JSON) kwa stdout
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';

const require = createRequire(import.meta.url);
const ts = require('typescript');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const src = path.join(root, 'src');

function files(dir) {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((d) => {
    const p = path.join(dir, d.name);
    if (d.isDirectory()) return d.name === 'i18n' ? [] : files(p);
    return /\.tsx?$/.test(d.name) && !/\.d\.ts$/.test(d.name) ? [p] : [];
  });
}

const keys = new Map(); // key -> "file:line"
const dynamic = [];
for (const f of files(src)) {
  const text = fs.readFileSync(f, 'utf8');
  const sf = ts.createSourceFile(f, text, ts.ScriptTarget.Latest, true, f.endsWith('x') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
  const where = (n) => `${path.relative(root, f)}:${sf.getLineAndCharacterOfPosition(n.getStart()).line + 1}`;
  const take = (arg, n) => {
    if (arg && (ts.isStringLiteral(arg) || ts.isNoSubstitutionTemplateLiteral(arg))) {
      if (!keys.has(arg.text)) keys.set(arg.text, where(n));
    } else if (arg) dynamic.push(where(n));
  };
  (function visit(n) {
    if (ts.isCallExpression(n) && ts.isIdentifier(n.expression)) {
      const name = n.expression.text;
      if (name === 't' || name === 'tk') take(n.arguments[0], n);
      if (name === 'tn') { take(n.arguments[1], n); take(n.arguments[2], n); }
    }
    ts.forEachChild(n, visit);
  })(sf);
}

if (process.argv.includes('--keys')) {
  process.stdout.write(JSON.stringify([...keys.keys()], null, 1));
  process.exit(0);
}

// en.ts ni TypeScript rahisi: `export const en: Record<string,string> = { ... }` — isome kwa AST
const enFile = path.join(src, 'i18n', 'en.ts');
const enSf = ts.createSourceFile(enFile, fs.readFileSync(enFile, 'utf8'), ts.ScriptTarget.Latest, true);
const en = new Map();
(function visit(n) {
  if (ts.isPropertyAssignment(n) && n.initializer && (ts.isStringLiteral(n.initializer) || ts.isNoSubstitutionTemplateLiteral(n.initializer))) {
    const k = ts.isStringLiteral(n.name) || ts.isNoSubstitutionTemplateLiteral(n.name) ? n.name.text : n.name.getText(enSf);
    en.set(k, n.initializer.text);
  }
  ts.forEachChild(n, visit);
})(enSf);

const vars = (s) => [...s.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join(',');
const missing = [...keys].filter(([k]) => !en.has(k));
const unused = [...en.keys()].filter((k) => !keys.has(k));
const badVars = [...keys].filter(([k]) => en.has(k) && vars(k) !== vars(en.get(k)));

console.log(`i18n: funguo ${keys.size}, tafsiri ${en.size}`);
if (badVars.length) {
  console.log(`\n⚠ Vigeu havilingani (${badVars.length}):`);
  badVars.forEach(([k, w]) => console.log(`  ${w}  ${JSON.stringify(k)}  →  ${JSON.stringify(en.get(k))}`));
}
if (unused.length) console.log(`\nℹ Tafsiri zisizotumika (${unused.length}) — zinaweza kufutwa:\n  ` + unused.map((k) => JSON.stringify(k)).join('\n  '));
if (missing.length) {
  console.log(`\n✗ Hazina tafsiri ya Kiingereza (${missing.length}):`);
  missing.forEach(([k, w]) => console.log(`  ${w}  ${JSON.stringify(k)}`));
  process.exit(1);
}
if (badVars.length) process.exit(1);
console.log('✓ Kila maandishi yana tafsiri ya Kiingereza.');
void pathToFileURL;
