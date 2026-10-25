import { useMemo, useState } from 'react';
import { History, Landmark, MessageSquareText } from 'lucide-react';
import { benefitsApi, EXPLANATION_KIND, type ExplanationKind, type WithdrawalRow } from '../../api/benefits';
import { apiErrorMessage } from '../../api/truth';
import { StatusBadge } from '../../components/StatusBadge';
import { Button, EmptyState, Field, Notice, Select, Skeleton, Stat } from '../../components/ui';
import { ReasonModal } from '../../components/benefits/common';
import { kindLabel } from '../../lib/benefits';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { num, wallDateTime } from '../../lib/truth';
import { HistoricalCaseModal, type HistoricalPreset } from './CasesTab';
import { t } from '../../i18n';

/**
 * KILA pesa iliyotoka M-Koba lazima ielezwe: kesi ya mafao iliyolipwa, au maelezo yaliyoidhinishwa na viongozi wawili
 * (mkopo, gharama, uhamisho...). Kila mwanachama anaona orodha hii — utoaji usioelezwa unaonekana kwa wote.
 */
export function MkobaTab({
  groupId,
  isLeader,
  version,
  onOpen,
  onChanged,
}: {
  groupId: string;
  isLeader: boolean;
  version: number;
  onOpen: (id: string) => void;
  onChanged: () => void;
}) {
  const toast = useToast();
  const { data, error } = useApi(() => benefitsApi.withdrawals(groupId), groupId, [version]);
  const [historical, setHistorical] = useState<HistoricalPreset | null>(null);
  const [explain, setExplain] = useState<WithdrawalRow | null>(null);
  const [decide, setDecide] = useState<{ row: WithdrawalRow; approve: boolean } | null>(null);
  const [kind, setKind] = useState<ExplanationKind>('LoanDisbursement');

  const stats = useMemo(() => {
    const rows = data ?? [];
    const sum = (f: (r: WithdrawalRow) => boolean) => rows.filter(f).reduce((s, r) => s + r.amount, 0);
    return {
      total: sum(() => true),
      explained: sum((r) => r.state === 'Explained'),
      unexplained: sum((r) => r.state === 'Unexplained'),
      unexplainedCount: rows.filter((r) => r.state === 'Unexplained').length,
    };
  }, [data]);

  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!data) return <Skeleton className="h-48" />;

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-3">
        <Stat label={t('Jumla iliyotoka')} value={num(stats.total)} hint={t('{0} utoaji', { 0: data.length })} />
        <Stat label={t('Imeelezwa')} value={num(stats.explained)} tone="good" />
        <Stat label={t('Haijaelezwa')} value={num(stats.unexplained)} tone={stats.unexplainedCount ? 'overdue' : 'good'} hint={stats.unexplainedCount ? t('{0} utoaji', { 0: stats.unexplainedCount }) : undefined} />
      </div>

      {data.length === 0 ? (
        <EmptyState icon={Landmark} title={t('Hakuna utoaji kwenye PDF za M-Koba zilizoingizwa')} />
      ) : (
        <ul className="divide-y divide-ink-100 overflow-hidden rounded-2xl border border-ink-100 bg-paper-raised">
          {data.map((r) => (
            <li key={r.reference} className="flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
              <div className="w-full min-w-0 sm:w-auto sm:flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="figure font-semibold text-ink-950">{num(r.amount)}</span>
                  <span className="figure text-xs text-ink-600">{r.reference}</span>
                  <span className="text-xs text-ink-600">{wallDateTime(r.timestamp)}</span>
                </div>
                <div className="truncate text-xs text-ink-700">
                  {rowText(r)}
                  {r.payerName ? ` · ${r.payerName}` : ''}
                </div>
              </div>
              <StatusBadge
                label={r.state === 'Explained' ? t('Imeelezwa') : r.state === 'Pending' ? t('Inasubiri') : t('Haijaelezwa')}
                tone={r.state === 'Explained' ? 'good' : r.state === 'Pending' ? 'warn' : 'overdue'}
              />
              {r.caseId && (
                <Button size="sm" variant="secondary" onClick={() => onOpen(r.caseId!)}>
                  {r.caseNumber}
                </Button>
              )}
              {isLeader && r.state === 'Unexplained' && (
                <div className="flex flex-wrap gap-1.5">
                  <Button size="sm" icon={History} onClick={() => setHistorical({ mkobaReference: r.reference, amount: r.amount, date: r.timestamp.slice(0, 10) })}>
                    {t('Ni mafao')}
                  </Button>
                  <Button size="sm" variant="secondary" icon={MessageSquareText} onClick={() => setExplain(r)}>
                    {t('Eleza')}
                  </Button>
                </div>
              )}
              {isLeader && r.canDecideExplanation && (
                <div className="flex gap-1.5">
                  <Button size="sm" variant="good" onClick={() => setDecide({ row: r, approve: true })}>
                    {t('Idhinisha')}
                  </Button>
                  <Button size="sm" variant="danger" onClick={() => setDecide({ row: r, approve: false })}>
                    {t('Kataa')}
                  </Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}

      {isLeader && (
        <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-dashed border-ink-200 px-4 py-3 text-sm">
          <span className="text-ink-700">{t('Mafao ya zamani yaliyolipwa kwa taslimu (hayapo kwenye M-Koba)?')}</span>
          <Button size="sm" variant="secondary" icon={History} onClick={() => setHistorical({ cash: true })}>
            {t('Rekodi taslimu ya zamani')}
          </Button>
        </div>
      )}

      <HistoricalCaseModal
        preset={historical}
        groupId={groupId}
        onClose={() => setHistorical(null)}
        onDone={(id) => {
          setHistorical(null);
          onChanged();
          onOpen(id);
        }}
      />
      <ReasonModal
        open={!!explain}
        title={t('Eleza utoaji huu')}
        summary={explain ? `${explain.reference} · ${num(explain.amount)} · ${wallDateTime(explain.timestamp)}` : undefined}
        label={t('Maelezo')}
        hint={t('Mf. "Mkopo wa Juma, kikao cha 12/03" — kiongozi mwingine ataidhinisha, kisha wanachama wote wataona.')}
        requireReason
        confirmLabel={t('Tuma')}
        onClose={() => setExplain(null)}
        onConfirm={async (desc) => {
          if (!explain) return;
          try {
            await benefitsApi.explain(groupId, explain.reference, EXPLANATION_KIND[kind], desc);
            toast(t('Maelezo yametumwa kwa kiongozi wa pili.'), 'good');
            setExplain(null);
            onChanged();
          } catch (e) {
            toast(apiErrorMessage(e), 'overdue');
          }
        }}
      >
        <Field label={t('Aina')}>
          <Select value={kind} onChange={(e) => setKind(e.target.value as ExplanationKind)}>
            {(Object.keys(EXPLANATION_KIND) as ExplanationKind[]).map((k) => (
              <option key={k} value={k}>
                {kindLabel(k)}
              </option>
            ))}
          </Select>
        </Field>
      </ReasonModal>
      <ReasonModal
        open={!!decide}
        title={decide?.approve ? t('Idhinisha maelezo') : t('Kataa maelezo')}
        summary={decide ? `${decide.row.reference} · ${num(decide.row.amount)} — ${kindLabel(decide.row.explanationKind ?? '')}: ${decide.row.explanationText ?? ''}` : undefined}
        requireReason={!!decide && !decide.approve}
        variant={decide?.approve ? 'good' : 'danger'}
        confirmLabel={decide?.approve ? t('Idhinisha') : t('Kataa')}
        onClose={() => setDecide(null)}
        onConfirm={async (note) => {
          if (!decide?.row.explanationId) return;
          try {
            await benefitsApi.decideExplanation(groupId, decide.row.explanationId, decide.approve, note || undefined);
            toast(t('Uamuzi umehifadhiwa.'), 'good');
            setDecide(null);
            onChanged();
          } catch (e) {
            toast(apiErrorMessage(e), 'overdue');
          }
        }}
      />
    </div>
  );
}

/** Maelezo ya mstari yanajengwa hapa (yanatafsirika) kutoka kwenye sehemu za seva; `hali` ni akiba. */
function rowText(r: WithdrawalRow): string {
  if (r.caseNumber)
    return r.state === 'Explained' ? t('Mafao {0}', { 0: r.caseNumber }) : t('Mafao {0} — inasubiri uthibitisho', { 0: r.caseNumber });
  if (r.explanationKind)
    return r.state === 'Explained'
      ? `${kindLabel(r.explanationKind)}: ${r.explanationText ?? ''}`
      : t('{0} — inasubiri kiongozi wa pili', { 0: kindLabel(r.explanationKind) });
  if (r.state === 'Unexplained') return t('HAIJAELEZWA — pesa imetoka bila kesi ya mafao wala maelezo yaliyoidhinishwa');
  return r.hali;
}
