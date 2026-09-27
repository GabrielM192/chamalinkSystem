import { useMemo, useState } from 'react';
import { motion } from 'framer-motion';
import { ChevronDown, Receipt, ShieldCheck, ShieldAlert, Smartphone, Landmark } from 'lucide-react';
import type { AllocationLine, MemberTruth, MonthLine } from '../../api/truth';
import { StatusBadge } from '../StatusBadge';
import { Notice, Stat } from '../ui';
import {
  allocationTarget,
  monthLabel,
  num,
  periodStatus,
  tsh,
  wallDate,
  wallDateTime,
} from '../../lib/truth';

import { t } from '../../i18n';
/**
 * Taarifa kamili ya mwanachama kutoka Injini ya Ukweli.
 * Inatumika na "Taarifa Yangu" (mwanachama) na paneli ya kiongozi.
 * Kila shilingi inaelezwa: ililipwa lini, kupitia wapi, na ilikwenda wapi.
 */
export function MemberStatement({
  data,
  compact,
  showWarnings = true,
}: {
  data: MemberTruth;
  compact?: boolean;
  // Tahadhari za kiufundi (mf. JoinedAt ya DB) ni za viongozi
  showWarnings?: boolean;
}) {
  const tot = data.totals;
  const months = data.monthlyStatement.filter((m) => m.status !== 'NotYetOpen');
  const [openMonth, setOpenMonth] = useState<string | null>(null);

  const jfPct = tot.joinFeeDue > 0 ? Math.min(100, Math.round((tot.joinFeePaid / tot.joinFeeDue) * 100)) : 100;

  return (
    <div className="space-y-5">
      {/* Muhtasari */}
      <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
        <Stat
          label={t("Deni la michango")}
          value={tsh(tot.arrears)}
          tone={tot.arrears > 0 ? 'overdue' : 'good'}
          hint={tot.monthsInArrears.length ? tot.monthsInArrears.map((m) => monthLabel(m)).join(', ') : t("Hakuna mwezi unaodaiwa")}
        />
        <Stat
          label={t("Kianzio kilichobaki")}
          value={tsh(tot.joinFeeOutstanding)}
          tone={tot.joinFeeOutstanding > 0 ? 'warn' : 'good'}
          hint={tot.joinFeeDue > 0 ? t("Amelipa {0} kati ya {1}", { 0: num(tot.joinFeePaid), 1: num(tot.joinFeeDue) }) : t("Hakidaiwi")}
        />
        <Stat label={t("Akiba")} value={tsh(tot.savings)} hint={t("Ziada — itakatwa mwezi usipolipa")} />
        <Stat
          label={t("Jumla ya deni")}
          value={tsh(tot.totalDebtIncludingJoinFee)}
          tone={tot.totalDebtIncludingJoinFee > 0 ? 'overdue' : 'good'}
          hint={tot.currentDue > 0 ? t("Mwezi huu unadaiwa {0} (bado muda upo)", { 0: num(tot.currentDue) }) : t("Pamoja na Kianzio")}
        />
      </div>

      {/* Kianzio */}
      {tot.joinFeeDue > 0 && (
        <div className="rounded-xl border border-ink-100 bg-paper-raised px-4 py-3">
          <div className="flex items-center justify-between text-sm">
            <span className="font-medium text-ink-900">{t("Kianzio (Join fee)")}</span>
            <span className="figure text-ink-700">
              {num(tot.joinFeePaid)} / {num(tot.joinFeeDue)}
            </span>
          </div>
          <div className="mt-2 h-2 overflow-hidden rounded-full bg-ink-100">
            <motion.div
              className={`h-full rounded-full ${jfPct === 100 ? 'bg-standing-good' : 'bg-gold-600'}`}
              initial={{ width: 0 }}
              animate={{ width: `${jfPct}%` }}
              transition={{ duration: 0.6 }}
            />
          </div>
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2 text-xs">
        {data.invariantOk ? (
          <span className="inline-flex items-center gap-1 rounded-full bg-standing-good-bg px-2.5 py-1 font-medium text-standing-good">
            <ShieldCheck size={13} />{' '}{t("Hesabu zimejikagua: kila shilingi imegawanywa")}
          </span>
        ) : (
          <span className="inline-flex items-center gap-1 rounded-full bg-standing-overdue-bg px-2.5 py-1 font-medium text-standing-overdue">
            <ShieldAlert size={13} />{' '}{t("Hesabu hazilingani — wajulishe viongozi")}
          </span>
        )}
        <span className="text-ink-600">{t("Hadi")}{' '}{wallDateTime(data.asOf)}</span>
        {data.phones.length > 0 && (
          <span className="figure inline-flex items-center gap-1 text-ink-600">
            <Smartphone size={12} /> {data.phones.join(', ')}
          </span>
        )}
      </div>

      {showWarnings && data.warnings.map((w, i) => (
        <Notice key={i} tone="warn">
          {w}
        </Notice>
      ))}

      {/* Ramani ya miezi */}
      <div>
        <h3 className="mb-2 text-sm font-semibold text-ink-900">{t("Miezi")}</h3>
        <div className={`grid gap-2 ${compact ? 'grid-cols-3 sm:grid-cols-4' : 'grid-cols-3 sm:grid-cols-4 lg:grid-cols-6'}`}>
          {months.map((m) => (
            <MonthChip
              key={m.month}
              m={m}
              active={openMonth === m.month}
              onClick={() => setOpenMonth(openMonth === m.month ? null : m.month)}
            />
          ))}
        </div>
        {openMonth && (
          <MonthDetail
            m={months.find((x) => x.month === openMonth)!}
            allocations={(data.allocations ?? []).filter((a) => a.period === openMonth)}
          />
        )}
      </div>

      {/* Jedwali */}
      <MonthTable months={months} />

      {/* Historia ya malipo na mgawanyo wake */}
      {data.allocations && <PaymentHistory allocations={data.allocations} />}

      {data.rejected.length > 0 && (
        <div className="space-y-2">
          <h3 className="text-sm font-semibold text-ink-900">{t("Malipo yasiyohesabiwa")}</h3>
          {data.rejected.map((r) => (
            <Notice key={r.reference} tone="overdue" title={`${tsh(r.amount)} · ${wallDate(r.timestamp)}`}>
              {r.reason}
            </Notice>
          ))}
        </div>
      )}

      {data.loans.length > 0 && (
        <div className="space-y-2">
          <h3 className="flex items-center gap-1.5 text-sm font-semibold text-ink-900">
            <Landmark size={15} />{' '}{t("Mikopo")}
          </h3>
          {data.loans.map((l) => (
            <div key={l.loanId} className="grid grid-cols-2 gap-2 rounded-xl border border-ink-100 bg-paper-raised p-3 text-sm sm:grid-cols-4">
              <div>
                <div className="text-xs text-ink-600">{t("Ilitolewa")}</div>
                {wallDate(l.disbursedAt)}
              </div>
              <div>
                <div className="text-xs text-ink-600">{t("Jumla ya kurejesha")}</div>
                <span className="figure">{num(l.totalRepayable)}</span>
              </div>
              <div>
                <div className="text-xs text-ink-600">{t("Imerejeshwa")}</div>
                <span className="figure text-standing-good">{num(l.repaid)}</span>
              </div>
              <div>
                <div className="text-xs text-ink-600">{t("Imebaki")}</div>
                <span className={`figure ${l.overdueInstallments > 0 ? 'text-standing-overdue' : ''}`}>{num(l.outstanding)}</span>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

const CHIP_TONE: Record<string, string> = {
  good: 'border-standing-good/25 bg-standing-good-bg text-standing-good',
  warn: 'border-standing-warn/25 bg-standing-warn-bg text-standing-warn',
  overdue: 'border-standing-overdue/25 bg-standing-overdue-bg text-standing-overdue',
  neutral: 'border-ink-100 bg-paper-raised text-ink-700',
};

function MonthChip({ m, active, onClick }: { m: MonthLine; active: boolean; onClick: () => void }) {
  const st = periodStatus(m.status);
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={`rounded-xl border px-2.5 py-2 text-left transition-shadow ${CHIP_TONE[st.tone]} ${active ? 'ring-2 ring-ink-800' : 'hover:shadow-sm'}`}
    >
      <div className="text-xs font-semibold">{monthLabel(m.month)}</div>
      <div className="figure mt-0.5 text-sm font-semibold">{m.status === 'Unpaid' || m.status === 'PartiallyPaid' ? `−${num(m.outstanding)}` : num(m.contributionPaid)}</div>
      <div className="truncate text-[10px] opacity-80">{st.short}{m.fineWaived ? t(" · msamaha") : ''}</div>
    </button>
  );
}

function MonthDetail({ m, allocations }: { m: MonthLine; allocations: AllocationLine[] }) {
  const st = periodStatus(m.status);
  return (
    <motion.div
      initial={{ opacity: 0, y: -4 }}
      animate={{ opacity: 1, y: 0 }}
      className="mt-3 rounded-xl border border-ink-100 bg-paper-raised p-4 text-sm"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="font-semibold text-ink-950">{monthLabel(m.month, true)}</div>
        <StatusBadge label={st.label} tone={st.tone} />
      </div>
      <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-2 sm:grid-cols-4">
        <Dd k={t("Mchango")} v={`${num(m.contributionPaid)} / ${num(m.contributionDue)}`} />
        <Dd k={t("Faini")} v={m.fineWaived ? t("Imesamehewa") : `${num(m.finePaid)} / ${num(m.fineDue)}`} />
        <Dd k={t("Deni lililobaki")} v={num(m.outstanding)} strong={m.outstanding > 0} />
        <Dd k={t("Mwisho wa kulipa")} v={wallDateTime(m.deadline)} />
        {m.extraGraceDays > 0 && <Dd k={t("Neema ya ziada")} v={t("Siku {0}", { 0: m.extraGraceDays })} />}
        {m.coveredBySavings > 0 && <Dd k={t("Kutoka akiba")} v={num(m.coveredBySavings)} />}
        {m.loanDue > 0 && <Dd k={t("Rejesho la mkopo")} v={`${num(m.loanPaid)} / ${num(m.loanDue)}`} />}
      </dl>
      {allocations.length > 0 && (
        <ul className="mt-3 space-y-1.5 border-t border-ink-100 pt-3">
          {allocations.map((a, i) => (
            <li key={i} className="flex items-start gap-2 text-xs text-ink-700">
              <span className={`mt-1 h-2 w-2 shrink-0 rounded-full ${allocationTarget(a.target).dot}`} />
              <span className="flex-1">
                <span className="text-ink-600">{wallDate(a.timestamp)} — </span>
                {a.explanation}
              </span>
              {a.amount > 0 && <span className="figure font-semibold">{num(a.amount)}</span>}
            </li>
          ))}
        </ul>
      )}
    </motion.div>
  );
}

function Dd({ k, v, strong }: { k: string; v: string; strong?: boolean }) {
  return (
    <div>
      <dt className="text-xs text-ink-600">{k}</dt>
      <dd className={`figure ${strong ? 'font-semibold text-standing-overdue' : 'text-ink-950'}`}>{v}</dd>
    </div>
  );
}

function MonthTable({ months }: { months: MonthLine[] }) {
  return (
    <div>
      <h3 className="mb-2 text-sm font-semibold text-ink-900">{t("Jedwali la miezi")}</h3>
      {/* Desktop */}
      <div className="hidden overflow-hidden rounded-xl border border-ink-100 sm:block">
        <table className="w-full text-sm">
          <thead className="bg-ink-50 text-left text-xs uppercase tracking-wide text-ink-600">
            <tr>
              <th className="px-3 py-2">{t("Mwezi")}</th>
              <th className="px-3 py-2">{t("Hali")}</th>
              <th className="px-3 py-2 text-right">{t("Mchango")}</th>
              <th className="px-3 py-2 text-right">{t("Faini")}</th>
              <th className="px-3 py-2 text-right">{t("Deni")}</th>
              <th className="px-3 py-2">{t("Mwisho")}</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-ink-100 bg-paper-raised">
            {months.map((m) => {
              const st = periodStatus(m.status);
              return (
                <tr key={m.month}>
                  <td className="px-3 py-2 font-medium">{monthLabel(m.month)}</td>
                  <td className="px-3 py-2">
                    <StatusBadge label={st.short} tone={st.tone} />
                  </td>
                  <td className="figure px-3 py-2 text-right">
                    {num(m.contributionPaid)}
                    <span className="text-ink-500">/{num(m.contributionDue)}</span>
                  </td>
                  <td className="figure px-3 py-2 text-right">
                    {m.fineWaived ? <span className="text-xs text-ink-600">{t("msamaha")}</span> : m.fineDue > 0 ? `${num(m.finePaid)}/${num(m.fineDue)}` : '—'}
                  </td>
                  <td className={`figure px-3 py-2 text-right ${m.outstanding > 0 ? 'font-semibold text-standing-overdue' : 'text-ink-500'}`}>
                    {m.outstanding > 0 ? num(m.outstanding) : '0'}
                  </td>
                  <td className="px-3 py-2 text-xs text-ink-600">{wallDate(m.deadline)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {/* Simu */}
      <ul className="space-y-2 sm:hidden">
        {months.map((m) => {
          const st = periodStatus(m.status);
          return (
            <li key={m.month} className="rounded-xl border border-ink-100 bg-paper-raised px-3 py-2.5">
              <div className="flex items-center justify-between">
                <span className="font-medium">{monthLabel(m.month, true)}</span>
                <StatusBadge label={st.short} tone={st.tone} />
              </div>
              <div className="figure mt-1.5 grid grid-cols-3 gap-2 text-xs">
                <span>
                  <span className="block text-ink-600">{t("Mchango")}</span>
                  {num(m.contributionPaid)}/{num(m.contributionDue)}
                </span>
                <span>
                  <span className="block text-ink-600">{t("Faini")}</span>
                  {m.fineWaived ? 'msamaha' : m.fineDue > 0 ? `${num(m.finePaid)}/${num(m.fineDue)}` : '—'}
                </span>
                <span className={m.outstanding > 0 ? 'font-semibold text-standing-overdue' : ''}>
                  <span className="block font-normal text-ink-600">{t("Deni")}</span>
                  {num(m.outstanding)}
                </span>
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

/** Malipo yamepangwa kwa rejea: kila malipo yanaonyesha yalikokwenda. */
function PaymentHistory({ allocations }: { allocations: AllocationLine[] }) {
  const payments = useMemo(() => {
    const map = new Map<string, { ref: string; at: string; source: string; lines: AllocationLine[] }>();
    for (const a of allocations) {
      if (a.target === 'FineWaived') continue;
      const key = `${a.reference}|${a.reference === 'AKIBA' ? a.timestamp : ''}`;
      let p = map.get(key);
      if (!p) {
        const src = a.explanation.includes(':') ? a.explanation.split(':')[0] : '';
        p = { ref: a.reference, at: a.timestamp, source: src, lines: [] };
        map.set(key, p);
      }
      p.lines.push(a);
    }
    return [...map.values()].sort((x, y) => (x.at < y.at ? 1 : -1));
  }, [allocations]);
  const [expanded, setExpanded] = useState<string | null>(null);

  if (payments.length === 0) return null;
  return (
    <div>
      <h3 className="mb-2 flex items-center gap-1.5 text-sm font-semibold text-ink-900">
        <Receipt size={15} />{' '}{t("Malipo na yalikokwenda")}
      </h3>
      <ul className="space-y-2">
        {payments.map((p) => {
          const total = p.lines.reduce((s, l) => s + l.amount, 0);
          const key = `${p.ref}${p.at}`;
          const isOpen = expanded === key;
          const manual = p.ref.startsWith('MANUAL');
          return (
            <li key={key} className="rounded-xl border border-ink-100 bg-paper-raised">
              <button
                type="button"
                onClick={() => setExpanded(isOpen ? null : key)}
                className="w-full px-3 py-2.5 text-left"
                aria-expanded={isOpen}
              >
                <div className="flex items-start gap-3">
                  <div className="min-w-0 flex-1">
                    <div className="text-sm font-medium text-ink-950">{wallDateTime(p.at)}</div>
                    <div className="mt-0.5 flex min-w-0 items-center gap-1.5 text-xs text-ink-600">
                      <span className={`shrink-0 rounded px-1.5 py-0.5 text-[10px] font-semibold ${manual ? 'bg-gold-100 text-gold-700' : 'bg-ink-50 text-ink-700'}`}>
                        {p.source || (manual ? t("Manual") : 'M-Koba')}
                      </span>
                      {!manual && <span className="figure truncate">{p.ref}</span>}
                    </div>
                  </div>
                  <span className="figure text-sm font-semibold">{num(total)}</span>
                  <ChevronDown size={15} className={`mt-0.5 shrink-0 text-ink-500 transition-transform ${isOpen ? 'rotate-180' : ''}`} />
                </div>
                <div className="mt-2 flex flex-wrap gap-1">
                  {p.lines.map((l, i) => (
                    <span key={i} className="inline-flex items-center gap-1 rounded-full bg-ink-50 px-2 py-0.5 text-[11px] text-ink-700">
                      <span className={`h-1.5 w-1.5 rounded-full ${allocationTarget(l.target).dot}`} />
                      {allocationTarget(l.target).label}
                      {l.period ? ` ${monthLabel(l.period)}` : ''}
                      <span className="figure text-ink-500">· {num(l.amount)}</span>
                    </span>
                  ))}
                </div>
              </button>
              {isOpen && (
                <ul className="space-y-1 border-t border-ink-100 px-3 py-2.5">
                  {p.lines.map((l, i) => (
                    <li key={i} className="flex items-start gap-2 text-xs text-ink-700">
                      <span className={`mt-1 h-2 w-2 shrink-0 rounded-full ${allocationTarget(l.target).dot}`} />
                      <span className="flex-1">{l.explanation}</span>
                      <span className="figure font-semibold">{num(l.amount)}</span>
                    </li>
                  ))}
                </ul>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
