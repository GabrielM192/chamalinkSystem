import { Eye } from 'lucide-react';
import { benefitsApi } from '../../api/benefits';
import { StatusBadge } from '../../components/StatusBadge';
import { EmptyState, Notice, Skeleton, Stat } from '../../components/ui';
import { caseStatus, categoryLabel, channelLabel, closedReasonText, relationshipLabel } from '../../lib/benefits';
import { useApi } from '../../lib/useApi';
import { num, wallDate } from '../../lib/truth';
import { t } from '../../i18n';

/**
 * Uwazi (wanachama WOTE): kila kesi — tukio, mnufaika, jumla, makato, kilicholipwa, rejea ya M-Koba na hali.
 * Hakuna vyeti wala namba za simu hapa: ushahidi ni siri, matokeo ni ya wazi.
 */
export function TransparencyTab({ groupId, version, onOpen }: { groupId: string; version: number; onOpen: (id: string) => void }) {
  const { data, error } = useApi(() => benefitsApi.transparency(groupId), groupId, [version]);
  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!data) return <Skeleton className="h-48" />;
  const tt = data.totals;
  return (
    <div className="space-y-4">
      <Notice>{t('Kila mwanachama anaona matokeo ya kila kesi. Vyeti na namba za simu havionyeshwi hapa — vinaonwa na viongozi na mnufaika tu.')}</Notice>
      <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-6">
        <Stat label={t('Kesi zilizolipwa')} value={tt.paidCases} />
        <Stat label={t('Jumla ya mafao')} value={num(tt.gross)} />
        <Stat label={t('Kianzio kilichokatwa')} value={num(tt.joinFeeOffset)} />
        <Stat label={t('Deni la michango lililokatwa')} value={num(tt.arrearsOffset)} />
        <Stat label={t('Deni la mkopo lililokatwa')} value={num(tt.loanArrearsOffset ?? 0)} />
        <Stat label={t('Pesa iliyolipwa')} value={num(tt.cashPaid)} hint={[tt.awaitingPayment ? t('{0} inasubiri malipo', { 0: num(tt.awaitingPayment) }) : '', tt.awaitingFunds ? t('{0} inasubiri pesa', { 0: num(tt.awaitingFunds) }) : ''].filter(Boolean).join(' · ') || undefined} />
      </div>
      {data.rows.length === 0 ? (
        <EmptyState icon={Eye} title={t('Bado hakuna kesi')} />
      ) : (
        <div className="overflow-x-auto rounded-2xl border border-ink-100 bg-paper-raised">
          <table className="w-full min-w-[960px] text-sm">
            <thead className="border-b border-ink-100 text-left text-[11px] uppercase tracking-wide text-ink-600">
              <tr>
                <th className="px-3 py-2">{t('Kesi')}</th>
                <th className="px-3 py-2">{t('Tukio')}</th>
                <th className="px-3 py-2">{t('Mnufaika')}</th>
                <th className="px-3 py-2 text-right">{t('Jumla')}</th>
                <th className="px-3 py-2 text-right">{t('Makato')}</th>
                <th className="px-3 py-2 text-right">{t('Imelipwa')}</th>
                <th className="px-3 py-2">{t('M-Koba ref')}</th>
                <th className="px-3 py-2">{t('Hali')}</th>
              </tr>
            </thead>
            <tbody>
              {data.rows.map((r) => {
                const st = caseStatus(r.status);
                return (
                  <tr key={r.id} className="cursor-pointer border-t border-ink-100 hover:bg-ink-50" onClick={() => onOpen(r.id)}>
                    <td className="figure px-3 py-2 font-medium">
                      {r.caseNumber}
                      {r.policyVersion != null && <div className="text-[10px] font-normal text-ink-500">{t('sera v{0}', { 0: r.policyVersion })}</div>}
                    </td>
                    <td className="px-3 py-2">
                      {categoryLabel(r.category)} · {relationshipLabel(r.relationship)}
                      <div className="text-xs text-ink-600">
                        {r.subjectName} · {wallDate(r.eventDate)}
                      </div>
                    </td>
                    <td className="px-3 py-2">
                      {r.beneficiaryName}
                      {r.memberNumber && <div className="figure text-xs text-ink-500">{r.memberNumber}</div>}
                    </td>
                    <td className="figure px-3 py-2 text-right">
                      {num(r.grossAmount)}
                      <div className="text-[10px] text-ink-500">
                        {num(r.perMemberAmount)} × {r.headcount}
                      </div>
                    </td>
                    <td className="figure px-3 py-2 text-right text-standing-warn">{dedOf(r) ? `−${num(dedOf(r))}` : '—'}</td>
                    <td className="figure px-3 py-2 text-right font-semibold">{r.status === 'Paid' ? num(r.netAmount) : '—'}</td>
                    <td className="figure px-3 py-2 text-xs">
                      {r.paymentChannel === 'Cash' ? channelLabel('Cash') : r.mkobaReference ?? '—'}
                      {r.paidAt && <div className="text-ink-500">{wallDate(r.paidAt)}</div>}
                    </td>
                    <td className="px-3 py-2">
                      <StatusBadge label={st.label} tone={st.tone} />
                      {r.closedReason && <div className="mt-0.5 max-w-[14rem] truncate text-xs text-ink-600" title={closedReasonText(r.closedReason)}>{closedReasonText(r.closedReason)}</div>}
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
