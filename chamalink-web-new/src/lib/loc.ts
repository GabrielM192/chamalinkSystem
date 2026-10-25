// SASISHO14 (pengo 12): ujumbe wa seva unaotafsirika.
// Seva inarudisha Kiswahili kamili (message) PAMOJA na kiolezo (tpl, mf. "Pesa haitoshi: {0}") na vigeu (args).
// Kiswahili: onyesha message kama ilivyo. Kiingereza: tafsiri tpl (i18n/server-en.ts), kisha tafsiri kila kigeu kinachojulikana.
import { getLang, hasT, t } from '../i18n';
import type { LocMsg } from '../api/benefits';

/** Tafsiri kigeu kimoja: ufunguo kamili → orodha "a, b" → maandishi yenye namba (namba → {0}/{n}). Vinginevyo kama kilivyo. */
export function tArg(a: string): string {
  if (!a) return a;
  if (hasT(a)) return t(a);
  for (const sep of ['; ', ', ', ' au ']) {
    if (!a.includes(sep)) continue;
    const parts = a.split(sep);
    // " au " (orodha ya nafasi, mf. "Mwenyekiti au Katibu") → " or "
    if (parts.some((p) => hasT(p))) return parts.map((p) => (hasT(p) ? t(p) : p)).join(sep === ' au ' ? ' or ' : sep);
  }
  const m = a.match(/\d[\d,.]*/);
  if (m) {
    for (const ph of ['{0}', '{n}']) {
      const key = a.replace(m[0], ph);
      if (hasT(key)) return t(key, { 0: m[0], n: m[0] });
    }
  }
  return a;
}

function fill(tpl: string, args: readonly string[] | null | undefined): string {
  return tpl.replace(/\{(\d+)\}/g, (x, i: string) => (args && Number(i) < args.length ? args[Number(i)] : x));
}

/**
 * Maandishi ya ujumbe wa seva kwa lugha ya sasa.
 * @param message Kiswahili kamili kutoka seva (akiba)
 * @param tpl     kiolezo (Kiswahili chenye {0}…)
 * @param args    vigeu vya kiolezo
 */
export function locText(message: string | null | undefined, tpl?: string | null, args?: readonly string[] | null): string {
  const msg = message ?? (tpl ? fill(tpl, args) : '');
  if (getLang() === 'sw') return msg;
  if (tpl && hasT(tpl)) {
    const vars: Record<string, string> = {};
    (args ?? []).forEach((a, i) => (vars[String(i)] = tArg(a)));
    return t(tpl, vars);
  }
  return hasT(msg) ? t(msg) : msg;
}

/** Umbo la {tpl,args} (Details, arifa, sababu za kuzuiwa). */
export function locMsg(m: LocMsg | null | undefined, fallback?: string | null): string {
  if (!m) return fallback ? locText(fallback) : '';
  return locText(fallback ?? null, m.tpl, m.args);
}

/** Ukaguzi (Check) / bendera ya hatari / tahadhari: { message, tpl, tArgs }. */
export function checkText(c: { message: string; tpl?: string | null; tArgs?: string[] | null }): string {
  return locText(c.message, c.tpl, c.tArgs);
}

/**
 * Kosa la seva (ProblemDetails ya SASISHO14): { message, template, args, details }.
 * Details (orodha ya makosa) inatafsiriwa moja moja na kuwekwa mahali pa {0} kama kiolezo kina kigeu kimoja tu.
 */
export function problemText(d: { message?: string; template?: string | null; args?: string[] | null; details?: LocMsg[] | null }): string | null {
  const message = d.message ?? null;
  if (!d.template) return message ? locText(message) : null;
  if (getLang() !== 'sw' && d.details && d.details.length > 0 && (d.args?.length ?? 0) === 1 && hasT(d.template)) {
    const sep = (d.args?.[0] ?? '').includes('; ') ? '; ' : ' ';
    return t(d.template, { 0: d.details.map((x) => locMsg(x)).join(sep) });
  }
  return locText(message, d.template, d.args);
}

/** Arifa: kichwa na maelezo yaliyotafsiriwa kutoka locJson (kama ipo), vinginevyo maandishi ya seva. */
export function notificationText(n: { title: string; body: string; locJson?: string | null }): { title: string; body: string } {
  if (n.locJson) {
    try {
      const j = JSON.parse(n.locJson) as { title?: LocMsg; body?: LocMsg };
      return { title: locMsg(j.title ?? null, n.title), body: locMsg(j.body ?? null, n.body) };
    } catch {
      /* JSON mbovu — tumia maandishi ya seva */
    }
  }
  return { title: locText(n.title), body: locText(n.body) };
}
