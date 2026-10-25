import { useState } from 'react';
import { Gavel, ThumbsDown, ThumbsUp } from 'lucide-react';
import { getReinstatement } from '../../api/events';
import { governanceApi, MEMBER_STATUS, type SettableStatus, type StatusChangeRequest } from '../../api/benefits';
import { apiErrorMessage } from '../../api/truth';
import { StatusBadge } from '../StatusBadge';
import { Button, EmptyState, Field, Input, Modal, Notice, Segmented, Skeleton, Textarea } from '../ui';
import { ReasonModal } from '../../components/benefits/common';
import { memberStatusLabel, todayIso } from '../../lib/benefits';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { num, realDateTime, wallDate } from '../../lib/truth';
import { t } from '../../i18n';

/**
 * Kura za kubadilisha hali ya mwanachama. Hakuna kiongozi mmoja anayeweza kumtoa mtu:
 *  - kufukuza: asilimia ya sera ya wanachama WOTE walio kikundini (mhusika hapigi kura);
 *  - mengine: viongozi N wa sera, mbali na aliyeomba na mhusika.
 * Mwanachama hafutwi — historia yake ya fedha inabaki.
 */
export function StatusVotesPanel({ groupId, version, onChanged }: { groupId: string; version: number; onChanged: () => void }) {
  const toast = useToast();
  const { data, error } = useApi(() => governanceApi.list(groupId), groupId, [version]);
  const [vote, setVote] = useState<{ r: StatusChangeRequest; approve: boolean } | null>(null);

  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!data) return <Skeleton className="h-40" />;

  const pending = data.filter((r) => r.status === 'Pending');
  const done = data.filter((r) => r.status !== 'Pending');

  return (
    <div className="space-y-4">
      {pending.length === 0 ? (
        <EmptyState icon={Gavel} title={t('Hakuna kura inayoendelea')}>
          {t('Kiongozi anaweza kuomba kubadilisha hali ya mwanachama kutoka orodha ya wanachama (kitufe "Hali").')}
        </EmptyState>
      ) : (
        <ul className="space-y-3">
          {pending.map((r) => (
            <RequestCard
              key={r.id}
              r={r}
              onVote={(approve) => setVote({ r, approve })}
              onCancel={async () => {
                try {
                  await governanceApi.cancel(groupId, r.id);
                  toast(t('Ombi limeghairiwa.'), 'good');
                  onChanged();
                } catch (e) {
                  toast(apiErrorMessage(e), 'overdue');
                }
              }}
            />
          ))}
        </ul>
      )}
      {done.length > 0 && (
        <details className="rounded-2xl border border-ink-100 bg-paper-raised px-4 py-3 text-sm">
          <summary className="cursor-pointer font-medium text-ink-800">{t('Maamuzi ya zamani ({0})', { 0: done.length })}</summary>
          <ul className="mt-3 space-y-3">
            {done.map((r) => (
              <RequestCard key={r.id} r={r} />
            ))}
          </ul>
        </details>
      )}
      <ReasonModal
        open={!!vote}
        title={vote?.approve ? t('Kura ya NDIYO') : t('Kura ya HAPANA')}
        summary={vote ? `${vote.r.memberName}: ${memberStatusLabel(vote.r.fromStatus)} → ${memberStatusLabel(vote.r.toStatus)}. ${vote.r.reason}` : undefined}
        requireReason={!!vote && !vote.approve}
        hint={t('Kura haibadilishwi baada ya kupigwa.')}
        variant={vote?.approve ? 'good' : 'danger'}
        confirmLabel={t('Piga kura')}
        onClose={() => setVote(null)}
        onConfirm={async (reason) => {
          if (!vote) return;
          try {
            await governanceApi.vote(groupId, vote.r.id, vote.approve, reason || undefined);
            toast(t('Kura yako imehifadhiwa.'), 'good');
            setVote(null);
            onChanged();
          } catch (e) {
            toast(apiErrorMessage(e), 'overdue');
          }
        }}
      />
    </div>
  );
}

function RequestCard({ r, onVote, onCancel }: { r: StatusChangeRequest; onVote?: (approve: boolean) => void; onCancel?: () => void }) {
  const pct = r.requiredApprovals > 0 ? Math.min(100, Math.round((r.approvals / r.requiredApprovals) * 100)) : 0;
  const tone = r.status === 'Approved' ? 'good' : r.status === 'Rejected' ? 'overdue' : 'warn';
  const label = r.status === 'Approved' ? t('Imeidhinishwa') : r.status === 'Rejected' ? t('Imekataliwa') : t('Kura zinaendelea');
  return (
    <li className="rounded-2xl border border-ink-100 bg-paper-raised p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="font-semibold text-ink-950">
            {r.memberName}: {memberStatusLabel(r.fromStatus)} → {memberStatusLabel(r.toStatus)}
            {r.isExpulsion && <span className="ml-2 rounded-full bg-standing-overdue-bg px-2 py-0.5 text-[11px] font-medium text-standing-overdue">{t('Kufukuzwa')}</span>}
          </div>
          <div className="mt-0.5 text-xs text-ink-600">
            {t('Ameomba {0}, {1}', { 0: r.requestedBy ?? '—', 1: realDateTime(r.requestedAt) })} · {t('kuanzia {0}', { 0: wallDate(r.effectiveDate) })}
          </div>
        </div>
        <StatusBadge label={label} tone={tone} />
      </div>
      <p className="mt-2 text-sm text-ink-800">{r.reason}</p>
      {r.evidenceNote && <p className="mt-1 text-xs text-ink-600">{r.evidenceNote}</p>}
      <div className="mt-3 text-xs text-ink-700">
        {t('Ndiyo {0} kati ya {1} zinazohitajika', { 0: r.approvals, 1: r.requiredApprovals })} · {t('Hapana {0} (ikifika {1} inakataliwa)', { 0: r.rejections, 1: r.rejectionsToReject })}
        {' · '}
        {r.leadersOnly ? t('viongozi pekee wanapiga kura') : t('wanachama wote wanapiga kura')}
      </div>
      <div className="mt-1.5 h-2 overflow-hidden rounded-full bg-ink-100">
        <div className={`h-full rounded-full ${r.status === 'Rejected' ? 'bg-standing-overdue' : 'bg-standing-good'}`} style={{ width: `${pct}%` }} />
      </div>
      {r.votes.length > 0 && (
        <ul className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs">
          {r.votes.map((v, i) => (
            <li key={i} className="inline-flex items-center gap-1">
              {v.approve ? <ThumbsUp size={12} className="text-standing-good" /> : <ThumbsDown size={12} className="text-standing-overdue" />}
              {v.name}
              {v.reason ? <span className="text-ink-600">— {v.reason}</span> : null}
            </li>
          ))}
        </ul>
      )}
      {(r.canVote || r.canCancel) && (
        <div className="mt-3 flex flex-wrap gap-2">
          {r.canVote && onVote && (
            <>
              <Button size="sm" variant="good" icon={ThumbsUp} onClick={() => onVote(true)}>
                {t('Ndiyo')}
              </Button>
              <Button size="sm" variant="danger" icon={ThumbsDown} onClick={() => onVote(false)}>
                {t('Hapana')}
              </Button>
            </>
          )}
          {r.canCancel && onCancel && (
            <Button size="sm" variant="secondary" onClick={onCancel}>
              {t('Ghairi ombi langu')}
            </Button>
          )}
        </div>
      )}
    </li>
  );
}

/** Kiongozi anaomba kubadilisha hali ya mwanachama — inaanzisha kura. */
export function StatusRequestModal({
  groupId,
  member,
  onClose,
  onDone,
}: {
  groupId: string;
  member: { memberId: string; fullName: string; status: string } | null;
  onClose: () => void;
  onDone: () => void;
}) {
  return (
    <Modal open={!!member} onClose={onClose} title={member ? t('Badilisha hali ya {0}', { 0: member.fullName }) : ''}>
      {member && <StatusRequestForm groupId={groupId} member={member} onClose={onClose} onDone={onDone} />}
    </Modal>
  );
}

type Choice = SettableStatus | 'Expel';

function StatusRequestForm({
  groupId,
  member,
  onClose,
  onDone,
}: {
  groupId: string;
  member: { memberId: string; fullName: string; status: string };
  onClose: () => void;
  onDone: () => void;
}) {
  const toast = useToast();
  const [choice, setChoice] = useState<Choice | ''>('');
  const [date, setDate] = useState(todayIso());
  const [reason, setReason] = useState('');
  const [evidence, setEvidence] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  // SASISHO19: gharama ya kurudi — inaonekana pale kiongozi anapotaka kumrejesha mwanachama
  // aliyeondolewa kwenye kikundi cha "kila tukio" (deni + faini x kizidishi + ada ya kujiunga).
  const backCost = useApi(
    choice === 'Active' && member.status !== 'Active' ? () => getReinstatement(groupId, member.memberId) : null,
    member.memberId,
    [choice],
  );

  const options: { value: Choice; label: string; hint: string }[] = [
    { value: 'Active', label: memberStatusLabel('Active'), hint: t('Kurejeshwa') },
    { value: 'Suspended', label: memberStatusLabel('Suspended'), hint: t('Kwa muda') },
    { value: 'Exited', label: t('Amejitoa'), hint: t('Kwa hiari yake') },
    { value: 'Deceased', label: memberStatusLabel('Deceased'), hint: t('Weka cheti kwenye maelezo') },
    { value: 'Expel', label: t('Kufukuzwa'), hint: t('Kura ya wanachama wote') },
  ];
  const available = options.filter((o) => o.value === 'Expel' || o.value !== member.status);

  return (
    <div className="space-y-4">
      <p className="text-sm text-ink-700">
        {t('Hali ya sasa:')} <b>{memberStatusLabel(member.status)}</b>
      </p>
      <Field label={t('Hali mpya')}>
        <Segmented<Choice> stack value={choice as Choice} onChange={setChoice} options={available} />
      </Field>
      {choice === 'Expel' && (
        <Notice tone="warn">{t('Kufukuzwa kunahitaji kura za NDIYO za asilimia iliyowekwa kwenye sera, ya wanachama WOTE walio kikundini. Wanachama wote wataarifiwa.')}</Notice>
      )}
      {backCost.data && backCost.data.total > 0 && (
        <Notice tone="warn" title={t('Gharama ya kurudi')}>
          {t(
            'Deni la tukio TSH {0} + faini ya kurudi TSH {1}{2} = TSH {3}. Mwanachama harudishwi mpaka salio la pochi lifikie kiasi hiki (amana mpya ya M-Koba inahitajika).',
            {
              0: num(backCost.data.outstandingDebt),
              1: num(backCost.data.reinstatementFine),
              2: backCost.data.joiningFeeRequired ? ` + ${t('ada ya kujiunga')} TSH ${num(backCost.data.joiningFee)}` : '',
              3: num(backCost.data.total),
            },
          )}
        </Notice>
      )}
      <Field label={t('Kuanzia tarehe')} hint={t('Madeni mapya yanasimama kuanzia tarehe hii mwanachama akitoka')}>
        <Input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
      </Field>
      <Field label={t('Sababu (na kifungu cha Katiba kama kipo)')}>
        <Textarea value={reason} onChange={(e) => setReason(e.target.value)} />
      </Field>
      <Field label={t('Ushahidi (hiari)')} hint={t('Mf. kumbukumbu za kikao, barua, namba ya cheti')}>
        <Input value={evidence} onChange={(e) => setEvidence(e.target.value)} />
      </Field>
      {err && <Notice tone="overdue">{err}</Notice>}
      <div className="flex flex-wrap justify-end gap-2 border-t border-ink-100 pt-3">
        <Button variant="secondary" onClick={onClose} disabled={busy}>
          {t('Ghairi')}
        </Button>
        <Button
          loading={busy}
          disabled={!choice || !date || reason.trim().length < 3}
          onClick={async () => {
            if (!choice) return;
            setBusy(true);
            setErr(null);
            try {
              const expel = choice === 'Expel';
              const r = await governanceApi.request(groupId, {
                memberId: member.memberId,
                toStatus: expel ? MEMBER_STATUS.Exited : MEMBER_STATUS[choice],
                isExpulsion: expel,
                effectiveDate: date,
                reason: reason.trim(),
                evidenceNote: evidence.trim() || undefined,
              });
              toast(t('Ombi limetumwa. Linahitaji kura {0} za NDIYO.', { 0: r.requiredApprovals }), 'good');
              onDone();
            } catch (e) {
              setErr(apiErrorMessage(e));
            } finally {
              setBusy(false);
            }
          }}
        >
          {t('Anzisha kura')}
        </Button>
      </div>
    </div>
  );
}
