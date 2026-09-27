import { ArrowDownLeft, ArrowUpRight, CheckCircle2, AlertTriangle, Download, Wallet } from 'lucide-react';
import type { Cashbook, GroupTruth } from '../../api/truth';
import { listGaps } from '../../api/truth';
import { Button, Card, EmptyState, Stat } from '../../components/ui';
import { useApi } from '../../lib/useApi';
import { num, tsh, wallDate, wallDateTime } from '../../lib/truth';
import { useTruth } from '../truth/TruthContext';
import { downloadCsv } from './reportData';

import { t } from '../../i18n';
/** Daftari la fedha: M-Koba ghafi — ingia, toka, salio kwa mwezi, na nani alilipwa. */
export function CashReport({ cash, truth }: { cash: Cashbook | null; truth: GroupTruth }) {
  const { goTab, openMember } = useTruth();
  if (!cash) return <EmptyState icon={Wallet} title={t("Daftari la M-Koba linapakia...")} />;
  if (cash.months.length === 0)
    return (
      <EmptyState icon={Wallet} title={t("Hakuna taarifa ya M-Koba bado")}>
        <span>
          {t("Ingiza PDF ya M-Koba kwenye")}{' '}
          <button onClick={() => goTab('import')} className="font-semibold underline underline-offset-2">
            {t("Import M-Koba")}
          </button>
          .
        </span>
      </EmptyState>
    );

  const net = cash.deposits - cash.withdrawals;

  function exportCsv() {
    if (!cash) return;
    downloadCsv(
      'daftari_mkoba.csv',
      [t('Mwezi'), t('Pesa iliyoingia'), t('Miamala'), t('Pesa iliyotoka'), t('Malipo'), t('Salio mwisho wa mwezi')],
      cash.months.map((m) => [m.month, m.deposits, m.depositCount, m.withdrawals, m.withdrawalCount, m.closingBalance ?? ''])
    );
  }

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
        <Stat label={t("Salio la M-Koba")} value={cash.lastBalance != null ? tsh(cash.lastBalance) : '—'} hint={t("Hadi {0}", { 0: wallDate(cash.lastTransaction) })} />
        <Stat label={t("Iliyoingia")} value={tsh(cash.deposits)} hint={t("Tangu {0}", { 0: wallDate(cash.firstTransaction) })} />
        <Stat label={t("Iliyotoka")} value={tsh(cash.withdrawals)} hint={t("Malipo {0}", { 0: cash.withdrawalList.length })} />
        <Stat label={t("Tofauti (ingia − toka)")} value={tsh(net)} tone={net >= 0 ? 'good' : 'overdue'} />
      </div>

      <div className="grid gap-5 lg:grid-cols-5">
        <Card
          title={t("Kwa kila mwezi")}
          subtitle={t("Kutoka taarifa za M-Koba kama zilivyo")}
          className="min-w-0 lg:col-span-3"
          bodyClassName="p-0"
          action={
            <Button size="sm" variant="secondary" icon={Download} onClick={exportCsv} className="print:hidden">
              CSV
            </Button>
          }
        >
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-ink-50 text-left text-[11px] uppercase tracking-wide text-ink-600">
                <tr>
                  <th className="px-4 py-2.5">{t("Mwezi")}</th>
                  <th className="px-3 py-2.5 text-right">{t("Iliyoingia")}</th>
                  <th className="px-3 py-2.5 text-right">{t("Iliyotoka")}</th>
                  <th className="px-4 py-2.5 text-right">{t("Salio")}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-100">
                {cash.months.map((m) => (
                  <tr key={m.month}>
                    <td className="whitespace-nowrap px-4 py-2.5 font-medium text-ink-950">{m.month}</td>
                    <td className="figure px-3 py-2.5 text-right text-standing-good">
                      {num(m.deposits)} <span className="hidden text-[11px] text-ink-500 sm:inline">({m.depositCount})</span>
                    </td>
                    <td className={`figure px-3 py-2.5 text-right ${m.withdrawals > 0 ? 'text-standing-overdue' : 'text-ink-500'}`}>
                      {m.withdrawals > 0 ? num(m.withdrawals) : '—'}
                    </td>
                    <td className="figure px-4 py-2.5 text-right font-semibold">{m.closingBalance != null ? num(m.closingBalance) : '—'}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot className="border-t-2 border-ink-100 bg-ink-50/60 font-semibold">
                <tr>
                  <td className="px-4 py-2.5">{t("Jumla")}</td>
                  <td className="figure px-3 py-2.5 text-right">{num(cash.deposits)}</td>
                  <td className="figure px-3 py-2.5 text-right">{num(cash.withdrawals)}</td>
                  <td className="figure px-4 py-2.5 text-right">{cash.lastBalance != null ? num(cash.lastBalance) : '—'}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        </Card>

        <Card title={t("Uhakiki wa salio")} subtitle={t("Salio la M-Koba linaelezwa na miamala yote?")} className="min-w-0 lg:col-span-2">
          <Reconciliation cash={cash} />
        </Card>
      </div>

      <Card title={t("Pesa iliyotoka (malipo kutoka M-Koba)")} subtitle={t("Msiba, sherehe au malipo mengine — kama yalivyo kwenye taarifa ya M-Koba")} bodyClassName="p-0">
        {cash.withdrawalList.length === 0 ? (
          <p className="p-4 text-sm text-ink-600">{t("Hakuna pesa iliyotoka kwenye kipindi hiki.")}</p>
        ) : (
          <ul className="divide-y divide-ink-100">
            {cash.withdrawalList.map((w) => (
              <li key={w.reference} className="flex items-center gap-3 px-4 py-3">
                <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-standing-overdue-bg text-standing-overdue">
                  <ArrowUpRight size={16} />
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-x-2 text-sm font-medium text-ink-950">
                    {w.memberId ? (
                      <button onClick={() => openMember(w.memberId!)} className="truncate hover:underline">
                        {w.memberName}
                      </button>
                    ) : (
                      <span className="truncate">{w.payerName || w.phone}</span>
                    )}
                    {w.memberId && w.payerName && w.payerName.toLowerCase() !== (w.memberName ?? '').toLowerCase() && <span className="truncate text-xs font-normal text-ink-600">({w.payerName})</span>}
                  </div>
                  <div className="figure truncate text-xs text-ink-600">
                    {wallDateTime(w.timestamp)} · {w.reference} · {w.phone}
                  </div>
                </div>
                <div className="text-right">
                  <div className="figure text-sm font-semibold text-standing-overdue">−{num(w.amount)}</div>
                  {w.balanceAfter != null && <div className="figure text-[11px] text-ink-500">{t("salio")}{' '}{num(w.balanceAfter)}</div>}
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>

      {truth.totals.acceptedManual > 0 && (
        <p className="flex items-start gap-2 text-xs text-ink-600">
          <ArrowDownLeft size={14} className="mt-0.5 shrink-0" />
          {t("Pia kuna")}{' '}{tsh(truth.totals.acceptedManual)}{' '}{t("za malipo ya mkononi (kabla ya M-Koba) zilizoidhinishwa — hazipo kwenye salio la M-Koba.")}
        </p>
      )}
    </div>
  );
}

/**
 * Mnyororo wa salio: salio la kuanzia + iliyoingia − iliyotoka (+ mistari iliyokosekana) = salio la M-Koba.
 * Mistari iliyokosekana inalinganishwa na mapengo yaliyogunduliwa wakati wa import.
 */
export function Reconciliation({ cash, compact = false }: { cash: Cashbook | null; compact?: boolean }) {
  const { groupId, version, goTab } = useTruth();
  const gaps = useApi(() => listGaps(groupId), groupId, [version]);

  if (!cash || cash.lastBalance == null || cash.openingBalance == null)
    return <p className="text-sm text-ink-600">{t("Ingiza PDF ya M-Koba kuona uhakiki.")}</p>;

  const missing = cash.missingFromStatement ?? 0;
  const gapTotal = (gaps.data ?? []).filter((g) => g.amount > 0).reduce((s, g) => s + g.amount, 0);
  const unresolved = (gaps.data ?? []).filter((g) => g.status === 'Unresolved' || g.status === 'Pending').length;
  const explained = !gaps.data ? null : Math.abs(missing - gapTotal) < 1;

  const line = (label: string, value: number, sign: '' | '+' | '−', cls = 'text-ink-900') => (
    <div className="flex items-center justify-between gap-3 py-1.5">
      <span className="text-ink-700">{label}</span>
      <span className={`figure ${cls}`}>
        {sign}
        {sign && ' '}
        {num(value)}
      </span>
    </div>
  );

  return (
    <div className="text-sm">
      <div className="divide-y divide-dashed divide-ink-100">
        {line(t("Salio la kuanzia"), cash.openingBalance, '')}
        {line(t("Pesa iliyoingia"), cash.deposits, '+', 'text-standing-good')}
        {line(t("Pesa iliyotoka"), cash.withdrawals, '−', 'text-standing-overdue')}
        {missing !== 0 && line(t("Mistari isiyokuwepo kwenye PDF"), missing, missing > 0 ? '+' : '−', 'text-standing-warn')}
      </div>
      <div className="mt-1 flex items-center justify-between gap-3 border-t-2 border-ink-900 pt-2 font-semibold">
        <span>{t("Salio la M-Koba")}</span>
        <span className="figure">{tsh(cash.lastBalance)}</span>
      </div>

      <div className={compact ? 'mt-3' : 'mt-4'}>
        {missing === 0 ? (
          <p className="flex items-start gap-2 rounded-lg bg-standing-good-bg px-3 py-2 text-xs text-standing-good">
            <CheckCircle2 size={14} className="mt-0.5 shrink-0" />{' '}{t("Mnyororo wa salio umekamilika — kila muamala upo kwenye PDF.")}
          </p>
        ) : explained ? (
          <p className="flex items-start gap-2 rounded-lg bg-standing-warn-bg px-3 py-2 text-xs text-standing-warn">
            <AlertTriangle size={14} className="mt-0.5 shrink-0" />
            <span>
              {num(missing)}{' '}{t("zinaelezwa na mapengo")}{' '}{gaps.data?.filter((g) => g.amount > 0).length}{' '}{t("ya PDF")}
              {unresolved > 0 ? t(" ({0} bado hayajatatuliwa). ", { 0: unresolved }) : '. '}
              <button onClick={() => goTab('mapengo')} className="font-semibold underline underline-offset-2 print:hidden">
                {t("Mapengo")}
              </button>
            </span>
          </p>
        ) : explained === false ? (
          <p className="flex items-start gap-2 rounded-lg bg-standing-overdue-bg px-3 py-2 text-xs text-standing-overdue">
            <AlertTriangle size={14} className="mt-0.5 shrink-0" />{' '}{t("Salio halielezeki kikamilifu: tofauti ya")}{' '}{num(missing - gapTotal)}{' '}{t("haijulikani. Hakiki PDF zote zimeingizwa.")}
          </p>
        ) : null}
      </div>
    </div>
  );
}
