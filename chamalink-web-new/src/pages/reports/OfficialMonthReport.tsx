import { useMemo, useState } from 'react';
import { Copy, Download, Share2 } from 'lucide-react';
import { Button, Card, Notice, Select, Skeleton, Stat } from '../../components/ui';
import { getMonthlyReport, type MonthlyReport as Report } from '../../api/truth';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { num, periodStatus, wallDate } from '../../lib/truth';
import { downloadCsv, type MonthAgg } from './reportData';

import { t } from '../../i18n';
const EN = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const TONE_CHIP: Record<string, string> = {
  good: 'bg-standing-good-bg text-standing-good',
  warn: 'bg-standing-warn-bg text-standing-warn',
  overdue: 'bg-standing-overdue-bg text-standing-overdue',
  neutral: 'bg-ink-50 text-ink-700',
};

/** "Mar 2026" (lebo ya seva) → { year, month } */
function parse(m: MonthAgg) {
  const [mon, y] = m.month.split(' ');
  return { year: Number(y), month: EN.indexOf(mon) + 1, label: m.label, open: m.open, stale: m.stale };
}

/**
 * Taarifa rasmi ya mwezi mmoja: kila mwanachama na hali yake, kama inavyohesabiwa na seva
 * (GET /truth/{gid}/monthly-report). Maandishi ya WhatsApp yanatengenezwa na seva pia, kwa hiyo
 * yanalingana neno kwa neno na jedwali hili.
 */
export function OfficialMonthReport({ groupId, months, version }: { groupId: string; months: MonthAgg[]; version: number }) {
  const toast = useToast();
  const options = useMemo(() => months.map(parse).filter((o) => o.month > 0).reverse(), [months]);
  // Chaguo la kwanza: mwezi wa karibuni uliofungwa NA wenye data kamili ya M-Koba; vinginevyo uliofungwa karibuni
  const initial = options.find((o) => !o.open && !o.stale) ?? options.find((o) => !o.open) ?? options[0];
  const [picked, setPicked] = useState<string>(initial ? `${initial.year}-${initial.month}` : '');
  const [y, m] = picked.split('-').map(Number);
  const report = useApi(picked ? () => getMonthlyReport(groupId, y, m) : null, [groupId, picked], [version]);

  async function copyText(r: Report) {
    try {
      await navigator.clipboard.writeText(r.whatsAppText);
      toast(t("Taarifa imenakiliwa — bandika kwenye kundi la WhatsApp."), 'good');
    } catch {
      toast(t("Imeshindwa kunakili."), 'warn');
    }
  }

  function csv(r: Report) {
    downloadCsv(
      `taarifa-${r.year}-${String(r.month).padStart(2, '0')}.csv`,
      [t('Namba'), t('Jina'), t('Lengo'), t('Amelipa'), t('Tarehe'), t('Hali'), t('Faini'), t('Deni la mwezi'), t('Madeni yote'), t('Kianzio kilichobaki')],
      r.members.map((x) => [
        x.memberNumber,
        x.fullName,
        x.target,
        x.paid,
        x.paidAt ? wallDate(x.paidAt) : '',
        periodStatus(x.status).label,
        x.fineCharged,
        x.outstanding,
        x.totalArrears,
        x.joinFeeOutstanding,
      ])
    );
  }

  const r = report.data;
  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <Select value={picked} onChange={(e) => setPicked(e.target.value)} className="sm:w-56" aria-label={t("Chagua mwezi")}>
          {options.map((o) => (
            <option key={`${o.year}-${o.month}`} value={`${o.year}-${o.month}`}>
              {o.label}
              {o.open ? t(" (bado wazi)") : o.stale ? t(" (M-Koba haijakamilika)") : ''}
            </option>
          ))}
        </Select>
        {r && (
          <div className="flex gap-2">
            <Button size="sm" variant="secondary" icon={Download} onClick={() => csv(r)}>
              CSV
            </Button>
            <Button size="sm" icon={Share2} onClick={() => copyText(r)}>
              {t("Nakili kwa WhatsApp")}
            </Button>
          </div>
        )}
      </div>

      {report.error && <Notice tone="overdue">{report.error}</Notice>}
      {!r ? (
        <Skeleton className="h-72" />
      ) : (
        <>
          {!r.monthClosed && (
            <Notice tone="neutral" title={t("{0} bado uko wazi", { 0: r.monthLabel })}>
              {t("Tarehe ya mwisho ni")}{' '}{wallDate(r.deadline)}{t(". Walio \"bado\" wanaweza kulipa bila faini hadi hapo.")}
            </Notice>
          )}
          {r.monthClosed && !r.dataComplete && (
            <Notice tone="warn" title={t("Taarifa ya M-Koba haijakamilika kwa mwezi huu")}>
              {t("Muamala wa mwisho ulioingizwa ni wa")}{' '}{wallDate(r.dataUntil)}{t(", kabla ya tarehe ya mwisho (")}{wallDate(r.deadline)}{t("). Walioonyeshwa “hawajalipa” huenda walilipa baadaye — ingiza PDF mpya ya M-Koba.")}
            </Notice>
          )}

          <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
            <Stat label={t("Wamelipa kwa wakati")} value={`${r.paidOnTime}/${r.totalMembers}`} />
            <Stat label={t("Wamechelewa / sehemu")} value={`${r.paidLate} / ${r.partiallyPaid}`} />
            <Stat label={t("Hawajalipa")} value={String(r.notPaid + (r.monthClosed ? 0 : r.stillOpen))} />
            <Stat label={t("Makusanyo")} value={`${num(r.totalCollected)} / ${num(r.totalExpected)}`} hint={`${r.collectionRatePercent}%`} />
          </div>

          <Card bodyClassName="p-0" title={t("Taarifa ya {0}", { 0: r.monthLabel })} subtitle={t("Faini: {0} · Deni la mwezi huu: {1}", { 0: num(r.finesCharged), 1: num(r.outstandingForMonth) })}>
            {/* Simu: kadi; kompyuta: jedwali */}
            <ul className="divide-y divide-ink-100 md:hidden">
              {r.members.map((x) => {
                const st = periodStatus(x.status);
                return (
                  <li key={x.memberId} className="px-4 py-3">
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <div className="truncate font-medium text-ink-950">
                          <span className="figure mr-1.5 text-xs text-ink-500">{x.memberNumber}</span>
                          {x.fullName}
                        </div>
                        <div className="figure mt-0.5 text-xs text-ink-600">
                          {num(x.paid)} / {num(x.target)}
                          {x.paidAt ? ` · ${wallDate(x.paidAt)}` : ''}
                        </div>
                      </div>
                      <span className={`shrink-0 rounded-full px-2 py-0.5 text-[11px] font-medium ${TONE_CHIP[st.tone]}`}>{st.short}</span>
                    </div>
                    {(x.outstanding > 0 || x.totalArrears > 0 || x.joinFeeOutstanding > 0) && (
                      <div className="figure mt-1.5 flex flex-wrap gap-x-3 text-xs text-standing-overdue">
                        {x.outstanding > 0 && <span>{t("Mwezi huu:")}{' '}{num(x.outstanding)}</span>}
                        {x.totalArrears > 0 && <span>{t("Madeni yote:")}{' '}{num(x.totalArrears)}</span>}
                        {x.joinFeeOutstanding > 0 && <span className="text-gold-700">{t("Kianzio:")}{' '}{num(x.joinFeeOutstanding)}</span>}
                      </div>
                    )}
                  </li>
                );
              })}
            </ul>
            <div className="hidden overflow-x-auto md:block">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-ink-100 text-left text-xs text-ink-600">
                    <th className="px-4 py-2 font-medium">{t("Namba")}</th>
                    <th className="px-4 py-2 font-medium">{t("Jina")}</th>
                    <th className="px-4 py-2 text-right font-medium">{t("Amelipa")}</th>
                    <th className="px-4 py-2 font-medium">{t("Tarehe")}</th>
                    <th className="px-4 py-2 font-medium">{t("Hali")}</th>
                    <th className="px-4 py-2 text-right font-medium">{t("Deni la mwezi")}</th>
                    <th className="px-4 py-2 text-right font-medium">{t("Madeni yote")}</th>
                    <th className="px-4 py-2 text-right font-medium">{t("Kianzio")}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-ink-100">
                  {r.members.map((x) => {
                    const st = periodStatus(x.status);
                    return (
                      <tr key={x.memberId}>
                        <td className="figure px-4 py-2 text-xs text-ink-600">{x.memberNumber}</td>
                        <td className="px-4 py-2 font-medium text-ink-950">{x.fullName}</td>
                        <td className="figure px-4 py-2 text-right">
                          {num(x.paid)} <span className="text-ink-500">/ {num(x.target)}</span>
                        </td>
                        <td className="figure px-4 py-2 text-xs text-ink-600">{x.paidAt ? wallDate(x.paidAt) : '—'}</td>
                        <td className="px-4 py-2">
                          <span className={`rounded-full px-2 py-0.5 text-[11px] font-medium ${TONE_CHIP[st.tone]}`}>{st.label}</span>
                          {x.fineWaived && <span className="ml-1 text-[11px] text-ink-500">{t("faini imesamehewa")}</span>}
                        </td>
                        <td className={`figure px-4 py-2 text-right ${x.outstanding > 0 ? 'text-standing-overdue' : 'text-ink-500'}`}>
                          {x.outstanding > 0 ? num(x.outstanding) : '—'}
                        </td>
                        <td className={`figure px-4 py-2 text-right ${x.totalArrears > 0 ? 'text-standing-overdue' : 'text-ink-500'}`}>
                          {x.totalArrears > 0 ? num(x.totalArrears) : '—'}
                        </td>
                        <td className={`figure px-4 py-2 text-right ${x.joinFeeOutstanding > 0 ? 'text-gold-700' : 'text-ink-500'}`}>
                          {x.joinFeeOutstanding > 0 ? num(x.joinFeeOutstanding) : '—'}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </Card>

          <Card title={t("Ujumbe wa WhatsApp")} subtitle={t("Namba za simu zimefichwa sehemu ya kati.")} action={
            <Button size="sm" variant="ghost" icon={Copy} onClick={() => copyText(r)}>{t("Nakili")}</Button>
          }>
            <pre className="max-h-72 overflow-auto whitespace-pre-wrap rounded-lg bg-ink-50 p-3 text-xs text-ink-800">{r.whatsAppText}</pre>
          </Card>
        </>
      )}
    </div>
  );
}
