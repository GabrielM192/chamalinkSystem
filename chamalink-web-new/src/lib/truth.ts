import type { StatusTone } from './status';
import type { PeriodStatus } from '../api/truth';
import { t, getLang, getLocale, tk } from '../i18n';

// Msaada wa kuonyesha data ya Injini ya Ukweli. HAKUNA hesabu za fedha hapa —
// ni maandishi, rangi na tarehe tu.

export const LEADER_ROLES = new Set(['Chairperson', 'Treasurer', 'Secretary']);
export const RESET_ROLES = new Set(['Chairperson', 'Treasurer']);

export function tsh(n: number | null | undefined): string {
  return `TSH ${Math.round(n ?? 0).toLocaleString('en-US')}`;
}

export function num(n: number | null | undefined): string {
  return Math.round(n ?? 0).toLocaleString('en-US');
}

const SW_MONTHS_SHORT = ['Jan', 'Feb', 'Mac', 'Apr', 'Mei', 'Jun', 'Jul', 'Ago', 'Sep', 'Okt', 'Nov', 'Des'];
const SW_MONTHS_LONG = [
  'Januari', 'Februari', 'Machi', 'Aprili', 'Mei', 'Juni',
  'Julai', 'Agosti', 'Septemba', 'Oktoba', 'Novemba', 'Desemba',
];
const EN_SHORT = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const EN_LONG = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

/** Majina ya miezi kwa lugha iliyochaguliwa (index 0 = Januari). */
export function monthsShort(): string[] {
  return getLang() === 'en' ? EN_SHORT : SW_MONTHS_SHORT;
}
export function monthsLong(): string[] {
  return getLang() === 'en' ? EN_LONG : SW_MONTHS_LONG;
}

/** "Mar 2026" (seva, Kiingereza daima) -> "Mac 2026" / "Machi 2026" (sw) au "Mar 2026" / "March 2026" (en) */
export function monthLabel(label: string | null | undefined, long = false): string {
  if (!label) return '';
  const [m, y] = label.split(' ');
  const i = EN_SHORT.indexOf(m);
  if (i < 0) return label;
  return `${long ? monthsLong()[i] : monthsShort()[i]} ${y}`;
}

export function monthName(month: number, year?: number): string {
  return `${monthsLong()[month - 1] ?? month}${year ? ` ${year}` : ''}`;
}

/**
 * Tarehe za M-Koba/manual ni saa za EAT zilizohifadhiwa kama "wall-clock"
 * (baadhi zina "Z" ingawa si UTC halisi). Tunasoma tarakimu zenyewe bila
 * kubadilisha saa za eneo — vinginevyo 23:30 ingeonekana kesho yake.
 */
function wallParts(iso: string) {
  const m = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2}))?/.exec(iso);
  if (!m) return null;
  return { y: +m[1], mo: +m[2], d: +m[3], h: m[4] ? +m[4] : 0, mi: m[5] ? +m[5] : 0 };
}

export function wallDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  const p = wallParts(iso);
  if (!p) return iso;
  return `${p.d} ${monthsShort()[p.mo - 1]} ${p.y}`;
}

export function wallDateTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  const p = wallParts(iso);
  if (!p) return iso;
  return `${wallDate(iso)}, ${String(p.h).padStart(2, '0')}:${String(p.mi).padStart(2, '0')}`;
}

/** Muda halisi wa UTC (CreatedAt, ApprovedAt...) -> saa za Dar es Salaam */
export function realDateTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(/[zZ]|[+-]\d{2}:\d{2}$/.test(iso) ? iso : `${iso}Z`);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString(getLocale(), {
    timeZone: 'Africa/Dar_es_Salaam',
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/** Leo kwa saa za EAT, kama "YYYY-MM-DD" (kwa <input type="date">). */
export function todayEat(): string {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Africa/Dar_es_Salaam',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
  return parts; // en-CA => YYYY-MM-DD
}

/** Sehemu ya tarehe ya ISO bila kubadilisha saa za eneo: "2026-03-31T..." -> "2026-03-31" */
export function isoDateOnly(iso: string | null | undefined): string {
  return iso ? iso.slice(0, 10) : '';
}

export const PERIOD_STATUS: Record<PeriodStatus, { label: string; short: string; tone: StatusTone }> = {
  PaidOnTime: { label: tk('Amelipa kwa wakati'), short: tk('Kwa wakati'), tone: 'good' },
  PaidLate: { label: tk('Amelipa (alichelewa)'), short: tk('Alichelewa'), tone: 'warn' },
  PartiallyPaid: { label: tk('Amelipa sehemu'), short: tk('Sehemu'), tone: 'warn' },
  Unpaid: { label: tk('Hajalipa — deni'), short: tk('Deni'), tone: 'overdue' },
  Open: { label: tk('Mwezi unaendelea'), short: tk('Unaendelea'), tone: 'neutral' },
  NotYetOpen: { label: tk('Bado haujaanza'), short: tk('Bado'), tone: 'neutral' },
};

export function periodStatus(s: string) {
  const v = PERIOD_STATUS[s as PeriodStatus];
  return v ? { ...v, label: t(v.label), short: t(v.short) } : { label: s, short: s, tone: 'neutral' as StatusTone };
}

export const ALLOCATION_TARGET: Record<string, { label: string; dot: string }> = {
  Contribution: { label: tk('Mchango'), dot: 'bg-standing-good' },
  Fine: { label: tk('Faini'), dot: 'bg-standing-overdue' },
  JoinFee: { label: tk('Kianzio'), dot: 'bg-gold-600' },
  Savings: { label: tk('Akiba'), dot: 'bg-ink-500' },
  LoanRepayment: { label: tk('Rejesho la mkopo'), dot: 'bg-ink-700' },
  LoanPenalty: { label: tk('Adhabu ya mkopo'), dot: 'bg-standing-warn' },
  FineWaived: { label: tk('Faini imesamehewa'), dot: 'bg-ink-100' },
};

export function allocationTarget(target: string) {
  const v = ALLOCATION_TARGET[target];
  return v ? { ...v, label: t(v.label) } : { label: target, dot: 'bg-ink-100' };
}

export const REQUEST_STATUS: Record<string, { label: string; tone: StatusTone }> = {
  Pending: { label: tk('Inasubiri idhini'), tone: 'warn' },
  Approved: { label: tk('Imeidhinishwa'), tone: 'good' },
  Rejected: { label: tk('Imekataliwa'), tone: 'overdue' },
  Cancelled: { label: tk('Imesitishwa'), tone: 'neutral' },
  Unresolved: { label: tk('Haijatatuliwa'), tone: 'overdue' },
};

export function requestStatus(s: string) {
  const v = REQUEST_STATUS[s];
  return v ? { ...v, label: t(v.label) } : { label: s, tone: 'neutral' as StatusTone };
}

/** Utafutaji usiojali herufi kubwa/ndogo wala nafasi, pia kwa namba ya simu. */
export function matches(q: string, ...fields: (string | null | undefined)[]): boolean {
  const n = q.trim().toLowerCase().replace(/\s+/g, ' ');
  if (!n) return true;
  const digits = n.replace(/\D/g, '');
  return fields.some((f) => {
    if (!f) return false;
    const v = f.toLowerCase();
    if (v.includes(n)) return true;
    if (digits.length >= 4) {
      const fd = v.replace(/\D/g, '');
      // 0712... == 255712...
      const tail = digits.replace(/^0/, '').replace(/^255/, '');
      return fd.includes(tail);
    }
    return false;
  });
}

export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase())
    .join('');
}
