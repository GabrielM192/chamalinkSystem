import { useState } from 'react';
import { Download, FileSpreadsheet } from 'lucide-react';
import { benefitsApi } from '../../api/benefits';
import { apiErrorMessage } from '../../api/truth';
import { StatusBadge } from '../../components/StatusBadge';
import { Button, EmptyState, Notice, Skeleton, Stat } from '../../components/ui';
import { caseStatus, channelLabel, saveBlob, todayIso } from '../../lib/benefits';
import { tArg } from '../../lib/loc';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { num, wallDate } from '../../lib/truth';
import { t } from '../../i18n';

/** Daftari la mafao (viongozi): nani alilipwa nini, kwa hesabu gani, nani aliidhinisha, ref ya M-Koba. Linapakuliwa kama CSV. */
export function LedgerTab({ groupId, version, onOpen }: { groupId: string; version: number; onOpen: (id: string) => void }) {
  const toast = useToast();
  const { data, error } = useApi(() => benefitsApi.ledger(groupId), groupId, [version]);
  const [busy, setBusy] = useState(false);

  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!data) return <Skeleton className="h-48" />;

  const paid = data.filter((r) => r.status === 'Paid');
  const total = paid.reduce((s, r) => s + r.net, 0);
  const deducted = paid.reduce((s, r) => s + dedOf(r), 0);

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
        <Stat label={t('Kesi zilizolipwa')} value={paid.length} />
        <Stat label={t('Jumla iliyolipwa')} value={num(total)} />
        <Stat label={t('Makato (Kianzio + deni)')} value={num(deducted)} />
        <Stat label={t('Zinasubiri malipo/uthibitisho')} value={data.length - paid.length} tone={data.length - paid.length ? 'warn' : 'good'} />
      </div>
      <div className="flex justify-end">
        <Button
          variant="secondary"
          icon={Download}
          loading={busy}
          onClick={async () => {
            setBusy(true);
            try {
              saveBlob(await benefitsApi.ledgerCsv(groupId), `daftari-la-mafao-${todayIso()}.csv`);
            } catch (e) {
              toast(apiErrorMessage(e), 'overdue');
            } finally {
              setBusy(false);
            }
          }}
        >
          {t('Pakua CSV (Excel)')}
        </Button>
      </div>
      {data.length === 0 ? (
        <EmptyState icon={FileSpreadsheet} title={t('Daftari ni tupu')} />
      ) : (
        <div className="overflow-x-auto rounded-2xl border border-ink-100 bg-paper-raised">
          <table className="w-full min-w-[900px] text-sm">
            <thead className="border-b border-ink-100 text-left text-[11px] uppercase tracking-wide text-ink-600">
              <tr>
                <th className="px-3 py-2">{t('Kesi')}</th>
                <th className="px-3 py-2">{t('Tarehe ya malipo')}</th>
                <th className="px-3 py-2">{t('Mwanachama')}</th>
                <th className="px-3 py-2">{t('Aina')}</th>
                <th className="px-3 py-2 text-right">{t('Hesabu')}</th>
                <th className="px-3 py-2 text-right">{t('Makato')}</th>
                <th className="px-3 py-2 text-right">{t('Kiasi halisi')}</th>
                <th className="px-3 py-2">{t('Walioidhinisha')}</th>
                <th className="px-3 py-2">{t('Ref M-Koba')}</th>
                <th className="px-3 py-2">{t('Hali')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-100">
              {data.map((r) => {
                const st = caseStatus(r.status);
                return (
                  <tr key={r.caseId} className="cursor-pointer hover:bg-ink-50" onClick={() => onOpen(r.caseId)}>
                    <td className="figure px-3 py-2 text-xs">{r.caseNumber}</td>
                    <td className="px-3 py-2">{wallDate(r.paidAt)}</td>
                    <td className="px-3 py-2">
                      <div className="font-medium text-ink-950">{r.fullName}</div>
                      <div className="figure text-[11px] text-ink-600">
                        {r.memberNumber} · {r.phones || '—'}
                      </div>
                    </td>
                    <td className="px-3 py-2">
                      {tArg(r.category)} · {tArg(r.relationship)}
                      <div className="text-[11px] text-ink-600">
                        {r.subjectName} · {wallDate(r.eventDate)}
                      </div>
                    </td>
                    <td className="figure px-3 py-2 text-right">
                      {num(r.perMemberAmount)} × {r.headcount} = {num(r.gross)}
                    </td>
                    <td className="figure px-3 py-2 text-right">{dedOf(r) ? `−${num(dedOf(r))}` : '—'}</td>
                    <td className="figure px-3 py-2 text-right font-semibold">{num(r.net)}</td>
                    <td className="px-3 py-2 text-xs text-ink-700">{r.approvers}</td>
                    <td className="figure px-3 py-2 text-xs">{r.mkobaReference ?? '—'}
                      {r.paymentChannel === 'Cash' && <div className="text-ink-500">{channelLabel('Cash')}</div>}</td>
                    <td className="px-3 py-2">
                      <StatusBadge label={st.label} tone={st.tone} />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

const dedOf = (r: { joinFeeDeducted: number; arrearsDeducted: number; loanArrearsDeducted?: number }) => r.joinFeeDeducted + r.arrearsDeducted + (r.loanArrearsDeducted ?? 0);
