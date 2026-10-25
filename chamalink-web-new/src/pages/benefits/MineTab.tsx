import { useState } from 'react';
import { HandHeart, Plus, Vote } from 'lucide-react';
import { benefitsApi } from '../../api/benefits';
import { StatusBadge } from '../../components/StatusBadge';
import { Button, Card, EmptyState, Notice, Skeleton, Stat } from '../../components/ui';
import { caseStatus, categoryLabel } from '../../lib/benefits';
import { useApi } from '../../lib/useApi';
import { num, realDateTime, tsh, wallDate } from '../../lib/truth';
import { t } from '../../i18n';
import { OpenCaseModal } from './CasesTab';

/** Mwanachama: kura zinazomsubiri, mgao wake kwenye kila tukio, na mafao aliyopokea. */
export function MineTab({
  groupId,
  version,
  onOpen,
  onChanged,
  canOpen,
}: {
  groupId: string;
  version: number;
  onOpen: (id: string) => void;
  onChanged: () => void;
  /** Sera ya mafao imeidhinishwa */
  canOpen: boolean;
}) {
  const { data, error } = useApi(() => benefitsApi.me(groupId), groupId, [version]);
  const [requesting, setRequesting] = useState(false);
  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!data) return <Skeleton className="h-48" />;
  const waitingMine = data.received.filter((r) => r.status === 'AwaitingEvidence');

  return (
    <div className="space-y-5">
      <Card>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div className="min-w-0">
            <div className="font-semibold text-ink-950">{t('Umepatwa na msiba au sherehe?')}</div>
            <div className="text-sm text-ink-600">
              {canOpen
                ? t('Tuma ombi hapa kisha pakia cheti (kifo, ndoa au kuzaliwa). Viongozi watakihakiki na wanachama watapiga kura.')
                : t('Maombi yataanza kupokelewa viongozi wakishaweka sera ya mafao.')}
            </div>
          </div>
          <Button icon={Plus} onClick={() => setRequesting(true)} disabled={!canOpen || !data.canRequest} className="shrink-0">
            {t('Omba mafao')}
          </Button>
        </div>
      </Card>
      {waitingMine.length > 0 && (
        <Notice tone="warn" title={t('Ombi lako linasubiri cheti')}>
          {t('Fungua ombi lako na upakie cheti ili viongozi waanze kulihakiki:')}{' '}
          {waitingMine.map((r, i) => (
            <span key={r.caseId}>
              {i > 0 && ', '}
              <button className="font-semibold underline" onClick={() => onOpen(r.caseId)}>
                {r.caseNumber}
              </button>
            </span>
          ))}
        </Notice>
      )}
      <OpenCaseModal
        open={requesting}
        groupId={groupId}
        selfMemberId={data.memberId}
        onClose={() => setRequesting(false)}
        onDone={(id) => {
          setRequesting(false);
          onChanged();
          onOpen(id);
        }}
      />
      {data.votesNeeded.length > 0 && (
        <Card title={<span className="inline-flex items-center gap-2"><Vote size={16} /> {t('Kura zinazokusubiri')}</span>} bodyClassName="p-0">
          <ul className="divide-y divide-ink-100">
            {data.votesNeeded.map((v) => (
              <li key={v.caseId} className="flex flex-wrap items-center gap-3 px-4 py-3 sm:px-5">
                <div className="min-w-0 flex-1">
                  <div className="font-medium text-ink-950">
                    {v.caseNumber} · {categoryLabel(v.category)} · {v.beneficiary}
                  </div>
                  <div className="text-xs text-ink-600">
                    {v.subjectName} · {t('mwisho {0}', { 0: realDateTime(v.votingDeadline) })}
                  </div>
                </div>
                <Button size="sm" onClick={() => onOpen(v.caseId)}>
                  {t('Piga kura')}
                </Button>
              </li>
            ))}
          </ul>
        </Card>
      )}

      <div className="grid grid-cols-2 gap-2.5">
        <Stat label={t('Umechangia mafao')} value={tsh(data.totalContributed)} hint={t('Mgao wako kwenye matukio yaliyolipwa')} />
        <Stat label={t('Umepokea mafao')} value={tsh(data.totalReceived)} tone={data.totalReceived > 0 ? 'good' : 'neutral'} />
      </div>

      {data.received.length > 0 && (
        <Card title={t('Maombi na mafao yangu')} bodyClassName="p-0">
          <ul className="divide-y divide-ink-100">
            {data.received.map((r) => {
              const st = caseStatus(r.status);
              return (
                <li key={r.caseId}>
                  <button onClick={() => onOpen(r.caseId)} className="flex w-full flex-wrap items-center gap-3 px-4 py-3 text-left hover:bg-ink-50 sm:px-5">
                    <div className="min-w-0 flex-1">
                      <div className="font-medium text-ink-950">
                        {r.caseNumber} · {categoryLabel(r.category)} · {r.subjectName}
                      </div>
                      <div className="figure text-xs text-ink-600">
                        {num(r.grossAmount)}
                        {r.joinFeeDeducted ? ` − ${t('Kianzio')} ${num(r.joinFeeDeducted)}` : ''}
                        {r.arrearsDeducted ? ` − ${t('deni')} ${num(r.arrearsDeducted)}` : ''} = <b>{num(r.netAmount)}</b>
                        {r.paidAt ? ` · ${wallDate(r.paidAt)}` : ''}
                      </div>
                    </div>
                    <StatusBadge label={st.label} tone={st.tone} />
                  </button>
                </li>
              );
            })}
          </ul>
        </Card>
      )}

      <Card title={t('Matukio uliyochangia')} bodyClassName="p-0">
        {data.contributed.length === 0 ? (
          <div className="p-4">
            <EmptyState icon={HandHeart} title={t('Bado hakuna tukio')} />
          </div>
        ) : (
          <ul className="divide-y divide-ink-100">
            {data.contributed.map((c) => {
              const st = caseStatus(c.status);
              return (
                <li key={c.caseId}>
                  <button onClick={() => onOpen(c.caseId)} className="flex w-full flex-wrap items-center gap-3 px-4 py-3 text-left hover:bg-ink-50 sm:px-5">
                    <div className="min-w-0 flex-1">
                      <div className="font-medium text-ink-950">
                        {c.caseNumber} · {c.category} · {c.beneficiary}
                      </div>
                      <div className="text-xs text-ink-600">
                        {c.relationship}: {c.subjectName} · {wallDate(c.eventDate)}
                        {c.note ? ` · ${c.note}` : ''}
                      </div>
                    </div>
                    <span className="figure text-sm font-semibold text-ink-900">{c.share !== null ? num(c.share) : '—'}</span>
                    <StatusBadge label={st.label} tone={st.tone} />
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      </Card>
    </div>
  );
}
