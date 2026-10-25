import type { Cashbook, GroupTruth, MemberTruth, PeriodStatus, TruthSettings } from '../../api/truth';
import { csvCell } from '../../lib/csv';
import { monthLabel, num, tsh, wallDate } from '../../lib/truth';

import { t } from '../../i18n';
// =====================================================================
// Ripoti: HAKUNA mgawanyo wa fedha hapa. Tunajumlisha tu namba ambazo
// Injini ya Ukweli (seva) imeshazihesabu kwa kila mwanachama/mwezi — kwa
// hiyo jumla za ripoti ni lazima zilingane na "Taarifa Yangu" ya kila mtu.
// =====================================================================

export type ClosedStatus = Exclude<PeriodStatus, 'NotYetOpen'>;
export const STATUS_ORDER: ClosedStatus[] = ['PaidOnTime', 'PaidLate', 'PartiallyPaid', 'Unpaid', 'Open'];

export interface MonthMember {
  memberId: string;
  name: string;
  outstanding: number;
  paid: number;
}

export interface MonthAgg {
  month: string; // lebo ya seva: "Mar 2026"
  label: string; // "Mac 2026"
  deadline: string; // wall-clock EAT
  open: boolean;
  /** Tarehe ya mwisho ya mwezi huu iko BAADA ya muamala wa mwisho wa PDF iliyoingizwa:
   *  malipo yaliyofanyika baadaye bado hayajaonekana, kwa hiyo "hawajalipa" hapa si hakika. */
  stale: boolean;
  members: number;
  expected: number;
  paid: number;
  fineCharged: number;
  finePaid: number;
  finesWaived: number;
  outstanding: number;
  rate: number; // 0..1 (michango iliyolipwa / inayotarajiwa)
  counts: Record<ClosedStatus, number>;
  byStatus: Record<ClosedStatus, MonthMember[]>;
}

const EN = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
function monthKey(label: string): number {
  const [m, y] = label.split(' ');
  return Number(y) * 12 + Math.max(0, EN.indexOf(m));
}

function emptyCounts<T>(make: () => T): Record<ClosedStatus, T> {
  return { PaidOnTime: make(), PaidLate: make(), PartiallyPaid: make(), Unpaid: make(), Open: make() };
}

/** Jumla ya kila mwezi kwa wanachama wote (kutoka monthlyStatement ya seva). */
export function aggregateMonths(members: MemberTruth[], dataUntil: string | null = null): MonthAgg[] {
  const map = new Map<string, MonthAgg>();
  for (const m of members) {
    for (const line of m.monthlyStatement) {
      if (line.status === 'NotYetOpen') continue;
      let a = map.get(line.month);
      if (!a) {
        a = {
          month: line.month,
          label: monthLabel(line.month),
          deadline: line.deadline,
          open: false,
          stale: false,
          members: 0,
          expected: 0,
          paid: 0,
          fineCharged: 0,
          finePaid: 0,
          finesWaived: 0,
          outstanding: 0,
          rate: 0,
          counts: emptyCounts(() => 0),
          byStatus: emptyCounts<MonthMember[]>(() => []),
        };
        map.set(line.month, a);
      }
      const st = line.status as ClosedStatus;
      a.members += 1;
      a.expected += line.contributionDue;
      a.paid += line.contributionPaid;
      a.fineCharged += line.fineWaived ? 0 : line.fineDue;
      a.finePaid += line.finePaid;
      a.finesWaived += line.fineWaived ? 1 : 0;
      a.outstanding += line.outstanding;
      a.open ||= st === 'Open';
      a.counts[st] += 1;
      a.byStatus[st].push({ memberId: m.memberId, name: m.fullName, outstanding: line.outstanding, paid: line.contributionPaid });
    }
  }
  const out = [...map.values()].sort((x, y) => monthKey(x.month) - monthKey(y.month));
  for (const a of out) {
    a.rate = a.expected > 0 ? a.paid / a.expected : 0;
    // Tarehe zote ni saa za ukutani za EAT (ISO) — kulinganisha maandishi kunatosha
    a.stale = !a.open && !!dataUntil && a.deadline.slice(0, 19) > dataUntil.slice(0, 19);
    for (const s of STATUS_ORDER) a.byStatus[s].sort((p, q) => q.outstanding - p.outstanding || p.name.localeCompare(q.name));
  }
  return out;
}

/**
 * Kiwango cha ukusanyaji kwa miezi iliyofungwa NA iliyofunikwa kikamilifu na PDF
 * (mwezi unaoendelea au usio na data kamili ungeshusha kiwango bila sababu ya kweli).
 */
export function collectionRate(months: MonthAgg[]): { expected: number; paid: number; rate: number } {
  const closed = months.filter((m) => !m.open && !m.stale);
  const expected = closed.reduce((s, m) => s + m.expected, 0);
  const paid = closed.reduce((s, m) => s + m.paid, 0);
  return { expected, paid, rate: expected > 0 ? paid / expected : 0 };
}

export function debtors(members: MemberTruth[]): MemberTruth[] {
  return members
    .filter((m) => m.totals.totalDebtIncludingJoinFee > 0)
    .sort((a, b) => b.totals.totalDebtIncludingJoinFee - a.totals.totalDebtIncludingJoinFee || a.fullName.localeCompare(b.fullName));
}

export function staleMonths(months: MonthAgg[]): MonthAgg[] {
  return months.filter((m) => m.stale);
}

export function pct(r: number): string {
  return `${Math.round(r * 100)}%`;
}

// ---------------------------------------------------------------- Shiriki

export function whatsappSummary(
  groupName: string,
  truth: GroupTruth,
  cash: Cashbook | null,
  settings: TruthSettings | null,
  months: MonthAgg[]
): string {
  const tot = truth.totals;
  const cr = collectionRate(months);
  const owe = debtors(truth.members);
  const lines: string[] = [];
  lines.push(t("*RIPOTI YA KIKUNDI — {0}*", { 0: groupName.toUpperCase() }));
  lines.push(t("_Imehesabiwa {0} kutoka M-Koba{1}_", { 0: wallDate(truth.asOf), 1: cash?.lastTransaction ? t(" (miamala hadi {0})", { 0: wallDate(cash.lastTransaction) }) : '' }));
  const stale = months.filter((m) => m.stale);
  if (stale.length) lines.push(t("_Kumbuka: malipo ya baada ya {0} bado hayajaingizwa ({1})._", { 0: wallDate(cash?.lastTransaction), 1: stale.map((m) => m.label).join(', ') }));
  lines.push('');
  if (cash?.lastBalance != null) lines.push(t("💰 Salio la M-Koba: *{0}* (hadi {1})", { 0: tsh(cash.lastBalance), 1: wallDate(cash.lastTransaction) }));
  lines.push(t("📥 Michango iliyolipwa: {0}", { 0: tsh(tot.contributionPaid) }));
  lines.push(t("🧾 Faini zilizolipwa: {0}", { 0: tsh(tot.finePaid) }));
  lines.push(t("🎟️ Kianzio kilicholipwa: {0}", { 0: tsh(tot.joinFeePaid) }));
  if (cr.expected > 0) lines.push(t("📊 Kiwango cha ukusanyaji: {0}", { 0: pct(cr.rate) }));
  lines.push('');
  lines.push(t("⚠️ Deni la michango: *{0}*", { 0: tsh(tot.arrears) }));
  lines.push(t("⚠️ Kianzio kinachodaiwa: *{0}*", { 0: tsh(tot.joinFeeOutstanding) }));
  if (owe.length) {
    lines.push('');
    lines.push(t("*Wanaodaiwa ({0}):*", { 0: owe.length }));
    owe.forEach((m, i) => {
      const parts: string[] = [];
      if (m.totals.arrears > 0)
        parts.push(t("michango {0} ({1})", { 0: num(m.totals.arrears), 1: m.totals.monthsInArrears.map((x) => monthLabel(x).split(' ')[0]).join(', ') }));
      if (m.totals.joinFeeOutstanding > 0) parts.push(t("kianzio {0}", { 0: num(m.totals.joinFeeOutstanding) }));
      lines.push(`${i + 1}. ${m.fullName} — ${num(m.totals.totalDebtIncludingJoinFee)}: ${parts.join(' + ')}`);
    });
  }
  if (settings) {
    lines.push('');
    lines.push(
      t('ℹ️ Mchango {amount} kwa mwezi, mwisho tarehe {day} ya mwezi unaofuata.', {
        amount: num(settings.monthlyContribution),
        day: settings.dueDateDay + settings.gracePeriodDays,
      }) +
        ' ' +
        t('Ukichelewa: faini {fine}. Malipo yanalipa deni la zamani kwanza.', { fine: num(settings.lateFine) })
    );
  }
  return lines.join('\n');
}

export function downloadCsv(filename: string, head: string[], rows: (string | number)[][]) {
  const esc = csvCell; // SASISHO16: salama dhidi ya fomula
  const body = [head.map(esc).join(','), ...rows.map((r) => r.map(esc).join(','))].join('\n');
  const blob = new Blob(['\ufeff' + body], { type: 'text/csv;charset=utf-8' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = filename;
  a.click();
  URL.revokeObjectURL(a.href);
}
