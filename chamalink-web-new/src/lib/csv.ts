/**
 * SASISHO16 (CSV/formula injection): kisanduku cha CSV salama.
 * Maandishi yanayoanza na = + - @ TAB au CR yangetekelezwa kama fomula na Excel/LibreOffice
 * (mf. jina "=HYPERLINK(...)"); yanatanguliwa na ' ili yasomeke kama maandishi. Namba haziguswi.
 */
export function csvCell(v: string | number | null | undefined): string {
  if (typeof v === 'number') return `"${String(v)}"`;
  let s = v ?? '';
  if (s.length > 0 && '=+-@\t\r'.includes(s[0])) s = `'${s}`;
  return `"${s.replace(/"/g, '""')}"`;
}
