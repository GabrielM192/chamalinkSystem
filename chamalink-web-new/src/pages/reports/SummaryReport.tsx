import { ArrowRight, CheckCircle2, AlertTriangle } from 'lucide-react';
import type { Cashbook, GroupTruth } from '../../api/truth';
import { Card, Notice, Stat } from '../../components/ui';
import { Avatar } from '../../components/truth/MemberPicker';
import { monthLabel, num, tsh, wallDate } from '../../lib/truth';
import { useTruth } from '../truth/TruthContext';
import { collectionRate, debtors, pct, type MonthAgg } from './reportData';
import { Reconciliation } from './CashReport';

import { t, tk } from '../../i18n';
const SPLIT = [
  { key: 'contributionPaid', label: tk("Michango"), cls: 'bg-ink-800' },
  { key: 'finePaid', label: tk("Faini"), cls: 'bg-standing-overdue' },
  { key: 'joinFeePaid', label: tk("Kianzio"), cls: 'bg-gold-600' },
  { key: 'savings', label: tk("Akiba"), cls: 'bg-standing-good' },
  { key: 'loanRepaid', label: tk("Marejesho ya mikopo"), cls: 'bg-ink-500' },
] as const;

export function SummaryReport({
  truth,
  cash,
  months,
  onGo,
}: {
  truth: GroupTruth;
  cash: Cashbook | null;
  months: MonthAgg[];
  onGo: (tab: string) => void;
}) {
  const { openMember, goTab } = useTruth();
  const tot = truth.totals;
  const cr = collectionRate(months);
  const owe = debtors(truth.members);
  const received = tot.acceptedFromMkoba + tot.acceptedManual;
  const split = SPLIT.map((s) => ({ ...s, value: tot[s.key] })).filter((s) => s.value > 0);
  const broken = truth.members.filter((m) => !m.invariantOk);
  // Mwezi wa mwisho uliofungwa NA wenye data kamili ya M-Koba
  const lastClosed = [...months].reverse().find((m) => !m.open && !m.stale);

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
        <Stat
          label={t("Salio la M-Koba")}
          value={cash?.lastBalance != null ? tsh(cash.lastBalance) : '—'}
          hint={cash?.lastTransaction ? t("Hadi {0}", { 0: wallDate(cash.lastTransaction) }) : t("Hakuna PDF iliyoingizwa")}
        />
        <Stat label={t("Pesa iliyoingia")} value={tsh(cash?.deposits ?? tot.acceptedFromMkoba)} hint={tot.acceptedManual > 0 ? t("+ mkononi {0}", { 0: num(tot.acceptedManual) }) : t("Kutoka M-Koba")} />
        <Stat label={t("Pesa iliyotoka")} value={tsh(cash?.withdrawals ?? 0)} hint={t("Malipo {0}", { 0: cash?.withdrawalList.length ?? 0 })} />
        <Stat
          label={t("Kiwango cha ukusanyaji")}
          value={cr.expected > 0 ? pct(cr.rate) : '—'}
          hint={t("{0} kati ya {1}", { 0: num(cr.paid), 1: num(cr.expected) })}
          tone={cr.rate >= 0.9 ? 'good' : cr.rate >= 0.7 ? 'warn' : 'overdue'}
        />
      </div>

      {broken.length > 0 && (
        <Notice tone="overdue" title={t("Hesabu hazilingani kwa baadhi ya wanachama")}>
          {broken.map((b) => b.fullName).join(', ')}{' '}{t("— ripoti kwa msimamizi wa mfumo kabla ya kutumia ripoti hii.")}
        </Notice>
      )}
      {tot.unattributedMkobaMoney > 0 && (
        <Notice
          tone="warn"
          title={t("{0} hazijulikani ni za nani", { 0: tsh(tot.unattributedMkobaMoney) })}
          action={
            <button onClick={() => goTab('simu')} className="text-xs font-semibold underline underline-offset-2 print:hidden">
              {t("Tatua")}
            </button>
          }
        >
          {t("Hazijaingizwa kwenye michango ya mtu yeyote hadi namba ya simu iunganishwe au pengo la PDF litatuliwe.")}
        </Notice>
      )}

      <div className="grid gap-5 lg:grid-cols-5">
        <Card title={t("Pesa iliyopokelewa imegawanywa hivi")} subtitle={t("Jumla {0} — kila shilingi imepangiwa kazi moja", { 0: tsh(received) })} className="min-w-0 lg:col-span-3">
          {received === 0 ? (
            <p className="text-sm text-ink-600">{t("Bado hakuna malipo.")}</p>
          ) : (
            <>
              <div className="flex h-4 w-full overflow-hidden rounded-full bg-ink-50" role="img" aria-label={t("Mgawanyo wa pesa")}>
                {split.map((s) => (
                  <div key={s.key} className={s.cls} style={{ width: `${(s.value / received) * 100}%` }} title={`${t(s.label)}: ${num(s.value)}`} />
                ))}
              </div>
              <ul className="mt-4 grid gap-2 sm:grid-cols-2">
                {split.map((s) => (
                  <li key={s.key} className="flex items-center justify-between gap-3 rounded-lg bg-ink-50/60 px-3 py-2 text-sm">
                    <span className="flex items-center gap-2 text-ink-800">
                      <span className={`h-2.5 w-2.5 rounded-sm ${s.cls}`} />
                      {t(s.label)}
                    </span>
                    <span className="figure font-semibold text-ink-950">
                      {num(s.value)} <span className="text-xs font-normal text-ink-600">{pct(s.value / received)}</span>
                    </span>
                  </li>
                ))}
              </ul>
            </>
          )}
        </Card>

        <Card title={t("Uhakiki wa salio")} subtitle={t("Je, salio la M-Koba linaelezeka kikamilifu?")} className="min-w-0 lg:col-span-2">
          <Reconciliation cash={cash} compact />
        </Card>
      </div>

      <Card
        title={t("Ukusanyaji wa michango kwa mwezi")}
        subtitle={t("Kiasi kilicholipwa ukilinganisha na kinachotarajiwa (kwa wanachama wote wa mwezi huo)")}
        action={
          <button onClick={() => onGo('kila-mwezi')} className="inline-flex items-center gap-1 text-xs font-medium text-ink-700 hover:text-ink-950 print:hidden">
            {t("Kwa undani")}{' '}<ArrowRight size={13} />
          </button>
        }
      >
        <MonthBars months={months} />
      </Card>

      <div className="grid gap-5 lg:grid-cols-2">
        <Card
          title={t("Wanaodaiwa zaidi")}
          subtitle={t("{0} kati ya {1} wanadaiwa · michango {2} · kianzio {3}", { 0: owe.length, 1: truth.members.length, 2: num(tot.arrears), 3: num(tot.joinFeeOutstanding) })}
          action={
            <button onClick={() => onGo('madeni')} className="inline-flex items-center gap-1 text-xs font-medium text-ink-700 hover:text-ink-950 print:hidden">
              {t("Wote")}{' '}<ArrowRight size={13} />
            </button>
          }
          bodyClassName="p-2"
        >
          {owe.length === 0 ? (
            <p className="flex items-center gap-2 p-3 text-sm text-standing-good">
              <CheckCircle2 size={16} />{' '}{t("Hakuna mwanachama anayedaiwa.")}
            </p>
          ) : (
            <ul>
              {owe.slice(0, 6).map((m) => (
                <li key={m.memberId}>
                  <button onClick={() => openMember(m.memberId)} className="flex w-full items-center gap-3 rounded-lg px-2 py-2 text-left hover:bg-ink-50">
                    <Avatar name={m.fullName} size="sm" />
                    <div className="min-w-0 flex-1">
                      <div className="truncate text-sm font-medium text-ink-950">{m.fullName}</div>
                      <div className="truncate text-xs text-ink-600">
                        {m.totals.monthsInArrears.length > 0 ? m.totals.monthsInArrears.map((x) => monthLabel(x).split(' ')[0]).join(', ') : t("Kianzio tu")}
                      </div>
                    </div>
                    <span className="figure text-sm font-semibold text-standing-overdue">{num(m.totals.totalDebtIncludingJoinFee)}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card title={lastClosed ? t("Hali ya {0}", { 0: lastClosed.label }) : t("Hali ya mwezi uliopita")} subtitle={t("Mwezi wa mwisho uliofungwa wenye data kamili ya M-Koba")}>
          {lastClosed ? <StatusBreakdown m={lastClosed} /> : <p className="text-sm text-ink-600">{t("Bado hakuna mwezi uliofungwa.")}</p>}
        </Card>
      </div>
    </div>
  );
}

function MonthBars({ months }: { months: MonthAgg[] }) {
  if (months.length === 0) return <p className="text-sm text-ink-600">{t("Hakuna data.")}</p>;
  const max = Math.max(...months.map((m) => Math.max(m.expected, m.paid)), 1);
  return (
    <div className="overflow-x-auto [scrollbar-width:thin]">
      <div className="flex min-w-max items-end gap-3 pt-6 sm:min-w-0">
        {months.map((m) => (
          <div key={m.month} className="flex min-w-[3.25rem] flex-1 flex-col items-center gap-1.5">
            <span className={`figure text-[11px] font-semibold ${m.open || m.stale ? 'text-ink-500' : m.rate >= 0.9 ? 'text-standing-good' : m.rate >= 0.7 ? 'text-standing-warn' : 'text-standing-overdue'}`}>
              {m.stale ? t("PDF?") : pct(m.rate)}
            </span>
            <div
              className="relative flex h-40 w-full max-w-12 items-end rounded-md bg-ink-50"
              title={`${m.label}: ${num(m.paid)} / ${num(m.expected)}${m.stale ? t(' — data ya M-Koba bado haijakamilika') : ''}`}
            >
              <div className="absolute inset-x-0 bottom-0 rounded-md border border-dashed border-ink-500/40" style={{ height: `${(m.expected / max) * 100}%` }} />
              <div
                className={`relative w-full rounded-md ${m.open || m.stale ? 'bg-ink-500/50' : 'bg-ink-800'}`}
                style={{ height: `${(m.paid / max) * 100}%` }}
              />
            </div>
            <span className="text-[11px] font-medium text-ink-700">{m.label.split(' ')[0]}</span>
          </div>
        ))}
      </div>
      <div className="mt-3 flex flex-wrap gap-4 text-[11px] text-ink-600">
        <span className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-sm bg-ink-800" />{' '}{t("Kilicholipwa")}</span>
        <span className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-sm border border-dashed border-ink-500" />{' '}{t("Kinachotarajiwa")}</span>
        <span className="flex items-center gap-1.5"><span className="h-2.5 w-2.5 rounded-sm bg-ink-500/50" />{' '}{t("Unaendelea / PDF haijafika")}</span>
      </div>
    </div>
  );
}

const BREAKDOWN = [
  { key: 'PaidOnTime', label: tk("Kwa wakati"), cls: 'bg-standing-good' },
  { key: 'PaidLate', label: tk("Walichelewa"), cls: 'bg-standing-warn' },
  { key: 'PartiallyPaid', label: tk("Sehemu"), cls: 'bg-gold-500' },
  { key: 'Unpaid', label: tk("Hawajalipa"), cls: 'bg-standing-overdue' },
] as const;

export function StatusBreakdown({ m }: { m: MonthAgg }) {
  const total = BREAKDOWN.reduce((s, b) => s + m.counts[b.key], 0) || 1;
  return (
    <div>
      <div className="flex h-3 w-full overflow-hidden rounded-full bg-ink-50">
        {BREAKDOWN.map((b) => (
          <div key={b.key} className={b.cls} style={{ width: `${(m.counts[b.key] / total) * 100}%` }} />
        ))}
      </div>
      <dl className="mt-4 grid grid-cols-2 gap-2">
        {BREAKDOWN.map((b) => (
          <div key={b.key} className="rounded-lg bg-ink-50/60 px-3 py-2">
            <dt className="flex items-center gap-1.5 text-xs text-ink-600">
              <span className={`h-2 w-2 rounded-full ${b.cls}`} /> {t(b.label)}
            </dt>
            <dd className="figure text-lg font-semibold text-ink-950">{m.counts[b.key]}</dd>
          </div>
        ))}
      </dl>
      {m.outstanding > 0 && (
        <p className="mt-3 flex items-center gap-1.5 text-xs text-standing-overdue">
          <AlertTriangle size={13} />{' '}{t("Bado kinadaiwa kwa mwezi huu:")}{' '}<span className="figure font-semibold">{num(m.outstanding)}</span>
        </p>
      )}
    </div>
  );
}
