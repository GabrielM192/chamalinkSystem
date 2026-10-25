import { useState } from 'react';
import { AlertTriangle, CheckCircle2, Link2, Plus, RefreshCw, ShieldAlert, ShieldCheck, Users, XCircle } from 'lucide-react';
import { benefitsApi, RELATION_TYPE, type MemberRelationRow, type RelationType } from '../../api/benefits';
import { apiErrorMessage, getTruthMembers } from '../../api/truth';
import { StatusBadge } from '../../components/StatusBadge';
import { Button, Card, EmptyState, Field, Modal, Notice, Select, Skeleton, Stat, Textarea } from '../../components/ui';
import { ReasonModal } from '../../components/benefits/common';
import { MemberPicker } from '../../components/truth/MemberPicker';
import { alertText, relationLabel, riskTitle } from '../../lib/benefits';
import { checkText, locText } from '../../lib/loc';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { num, realDateTime } from '../../lib/truth';
import { t } from '../../i18n';

/** Ulinzi (viongozi): bendera za hatari + mnyororo wa ukaguzi, ulinganisho wa fedha, na uhusiano wa wanachama (mgongano wa maslahi). */
export function SecurityTab({
  groupId,
  version,
  isLeader = true,
  onOpen,
  onChanged,
}: {
  groupId: string;
  version: number;
  /** Mkaguzi (si kiongozi) anaona tu — hawezi kusajili wala kuondoa uhusiano. */
  isLeader?: boolean;
  onOpen: (id: string) => void;
  onChanged: () => void;
}) {
  return (
    <div className="space-y-6">
      <RiskCard groupId={groupId} version={version} onOpen={onOpen} />
      <ReconciliationCard groupId={groupId} version={version} onOpen={onOpen} />
      <RelationsCard groupId={groupId} version={version} isLeader={isLeader} onChanged={onChanged} />
    </div>
  );
}

const SEV_TONE = { danger: 'overdue', warn: 'warn', info: 'neutral' } as const;

function RiskCard({ groupId, version, onOpen }: { groupId: string; version: number; onOpen: (id: string) => void }) {
  const [n, setN] = useState(0);
  const { data, error } = useApi(() => benefitsApi.risk(groupId), groupId, [version, n]);
  return (
    <Card
      title={t('Ukaguzi wa hatari')}
      subtitle={t('Mfumo unakagua kila kesi, malipo na kumbukumbu za ukaguzi kutafuta dalili za udanganyifu.')}
      action={
        <Button size="sm" variant="secondary" icon={RefreshCw} onClick={() => setN((x) => x + 1)}>
          {t('Kagua upya')}
        </Button>
      }
    >
      {error && <Notice tone="overdue">{error}</Notice>}
      {!data && !error && <Skeleton className="h-32" />}
      {data && (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
            <Stat
              label={t('Kumbukumbu za ukaguzi')}
              value={data.audit.ok ? t('Salama') : t('Zimeharibiwa')}
              tone={data.audit.ok ? 'good' : 'overdue'}
              hint={t('{0} zimefungwa kwa hash', { 0: num(data.audit.sealed) })}
            />
            <Stat label={t('Hatari kubwa')} value={data.counts.danger} tone={data.counts.danger ? 'overdue' : 'good'} />
            <Stat label={t('Tahadhari')} value={data.counts.warn} tone={data.counts.warn ? 'warn' : 'good'} />
            <Stat label={t('Taarifa')} value={data.counts.info} />
          </div>
          <div className={`flex items-start gap-2 text-xs ${data.audit.ok ? 'text-standing-good' : 'text-standing-overdue'}`}>
            {data.audit.ok ? <ShieldCheck size={14} className="mt-0.5 shrink-0" /> : <ShieldAlert size={14} className="mt-0.5 shrink-0" />}
            <span>
              {locText(data.audit.message, data.audit.tpl, data.audit.tArgs)} · {t('imekaguliwa {0}', { 0: realDateTime(data.generatedAt) })}
            </span>
          </div>
          {data.flags.length === 0 ? (
            <EmptyState icon={ShieldCheck} title={t('Hakuna dalili za hatari')} />
          ) : (
            <ul className="divide-y divide-ink-100 rounded-xl border border-ink-100">
              {data.flags.map((f, i) => (
                <li key={`${f.code}-${f.caseId ?? ''}-${i}`} className="flex flex-wrap items-start gap-x-3 gap-y-1 px-3 py-2.5">
                  <AlertTriangle size={15} className={`mt-0.5 shrink-0 ${f.severity === 'danger' ? 'text-standing-overdue' : f.severity === 'warn' ? 'text-standing-warn' : 'text-ink-500'}`} />
                  <div className="min-w-0 flex-1">
                    <div className="text-sm font-medium text-ink-950">{riskTitle(f.code)}</div>
                    <div className="text-xs text-ink-700">{f.args && Object.keys(f.args).length > 0 ? alertText(f) : checkText(f)}</div>
                    {f.at && <div className="text-[11px] text-ink-500">{realDateTime(f.at)}</div>}
                  </div>
                  <StatusBadge label={f.severity === 'danger' ? t('Hatari') : f.severity === 'warn' ? t('Tahadhari') : t('Taarifa')} tone={SEV_TONE[f.severity]} />
                  {f.caseId && f.caseNumber && (
                    <Button size="sm" variant="ghost" onClick={() => onOpen(f.caseId!)}>
                      {f.caseNumber}
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </Card>
  );
}

function ReconciliationCard({ groupId, version, onOpen }: { groupId: string; version: number; onOpen: (id: string) => void }) {
  const { data, error } = useApi(() => benefitsApi.reconciliation(groupId), groupId, [version]);
  return (
    <Card title={t('Ulinganisho wa mafao na M-Koba')} subtitle={t('Kila kesi iliyolipwa: jumla = makato + pesa iliyolipwa, na pesa hiyo inaonekana M-Koba na kwenye Injini ya Ukweli.')}>
      {error && <Notice tone="overdue">{error}</Notice>}
      {!data && !error && <Skeleton className="h-24" />}
      {data && (
        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
            <Stat label={t('Kesi zilizokaguliwa')} value={data.cases} tone={data.ok ? 'good' : 'overdue'} hint={data.failing ? t('{0} hazilingani', { 0: data.failing }) : t('Zote zinalingana')} />
            <Stat label={t('Jumla ya mafao')} value={num(data.totals.gross)} />
            <Stat label={t('Makato (Kianzio + deni + mkopo)')} value={num(data.totals.joinFeeOffset + data.totals.arrearsOffset + (data.totals.loanArrearsOffset ?? 0))} />
            <Stat label={t('Pesa iliyolipwa')} value={num(data.totals.cashPaid)} />
          </div>
          {data.rows.filter((r) => !r.ok).map((r) => (
            <div key={r.id} className="rounded-xl border border-standing-overdue/40 px-3 py-2">
              <button type="button" className="figure text-sm font-semibold text-ink-950 underline" onClick={() => onOpen(r.id)}>
                {r.caseNumber}
              </button>
              <ul className="mt-1 space-y-0.5 text-xs">
                {r.checks.map((x) => (
                  <li key={x.code} className={`flex items-start gap-1.5 ${x.passed ? 'text-standing-good' : 'text-standing-overdue'}`}>
                    {x.passed ? <CheckCircle2 size={13} className="mt-0.5 shrink-0" /> : <XCircle size={13} className="mt-0.5 shrink-0" />}
                    {checkText(x)}
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}

type RelAction =
  | { kind: 'end'; r: MemberRelationRow }
  | { kind: 'decide'; r: MemberRelationRow; approve: boolean }
  | { kind: 'decideAdd'; r: MemberRelationRow; approve: boolean }
  | null;

function RelationsCard({ groupId, version, isLeader, onChanged }: { groupId: string; version: number; isLeader: boolean; onChanged: () => void }) {
  const toast = useToast();
  const { data, error } = useApi(() => benefitsApi.relations(groupId), groupId, [version]);
  const [adding, setAdding] = useState(false);
  const [action, setAction] = useState<RelAction>(null);
  const live = (data ?? []).filter((r) => r.status !== 'Ended' && r.status !== 'Rejected');
  const ended = (data ?? []).filter((r) => r.status === 'Ended' || r.status === 'Rejected');

  async function run(fn: () => Promise<unknown>, ok: string) {
    try {
      await fn();
      toast(ok, 'good');
      setAction(null);
      onChanged();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    }
  }

  return (
    <Card
      title={t('Uhusiano wa wanachama (mgongano wa maslahi)')}
      subtitle={t('Mwanachama aliyesajiliwa kuwa ndugu wa mnufaika hawezi kuhakiki cheti, kupiga kura, kuanzisha wala kuthibitisha malipo ya kesi yake. Kusajili au kuondoa uhusiano kunahitaji kiongozi wa pili.')}
      action={
        isLeader ? (
          <Button size="sm" icon={Plus} onClick={() => setAdding(true)}>
            {t('Sajili uhusiano')}
          </Button>
        ) : undefined
      }
      bodyClassName="p-0"
    >
      {error && <div className="p-4"><Notice tone="overdue">{error}</Notice></div>}
      {!data && !error && <div className="p-4"><Skeleton className="h-20" /></div>}
      {data && live.length === 0 && (
        <div className="p-4">
          <EmptyState icon={Users} title={t('Hakuna uhusiano uliosajiliwa')} />
        </div>
      )}
      {live.length > 0 && (
        <ul className="divide-y divide-ink-100">
          {live.map((r) => (
            <li key={r.id} className="flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3 sm:px-5">
              <Link2 size={15} className="text-ink-500" />
              <div className="min-w-0 flex-1">
                <div className="font-medium text-ink-950">
                  {r.memberAName} ↔ {r.memberBName}
                </div>
                <div className="text-xs text-ink-600">
                  {relationLabel(r.type)} · {t('amesajili {0}, {1}', { 0: r.createdBy, 1: realDateTime(r.createdAt) })}
                  {r.note ? ` — ${r.note}` : ''}
                </div>
                {r.status === 'Pending' && (
                  <div className="text-xs text-standing-warn">{t('Uhusiano huu bado haujaanza kutumika — kiongozi mwingine (si aliyesajili wala mhusika) aidhinishe.')}</div>
                )}
                {r.status === 'EndPending' && (
                  <div className="text-xs text-standing-warn">
                    {t('Ombi la kuondoa: {0}', { 0: r.endRequestedBy })} — {r.endReason}
                  </div>
                )}
              </div>
              {(r.status === 'EndPending' || r.status === 'Pending') && <StatusBadge label={t('Inasubiri idhini')} tone="warn" />}
              {r.canDecideAdd && (
                <div className="flex gap-1.5">
                  <Button size="sm" variant="good" onClick={() => setAction({ kind: 'decideAdd', r, approve: true })}>
                    {t('Idhinisha')}
                  </Button>
                  <Button size="sm" variant="danger" onClick={() => setAction({ kind: 'decideAdd', r, approve: false })}>
                    {t('Kataa')}
                  </Button>
                </div>
              )}
              {r.canCancelAdd && (
                <Button size="sm" variant="secondary" onClick={() => setAction({ kind: 'decideAdd', r, approve: false })}>
                  {t('Ghairi')}
                </Button>
              )}
              {r.canRequestEnd && (
                <Button size="sm" variant="secondary" onClick={() => setAction({ kind: 'end', r })}>
                  {t('Omba kuondoa')}
                </Button>
              )}
              {r.canDecideEnd && (
                <div className="flex gap-1.5">
                  <Button size="sm" variant="good" onClick={() => setAction({ kind: 'decide', r, approve: true })}>
                    {t('Idhinisha')}
                  </Button>
                  <Button size="sm" variant="danger" onClick={() => setAction({ kind: 'decide', r, approve: false })}>
                    {t('Kataa')}
                  </Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      {ended.length > 0 && (
        <details className="border-t border-ink-100 px-4 py-3 text-xs text-ink-600 sm:px-5">
          <summary className="cursor-pointer font-medium">{t('Uhusiano ulioondolewa ({0})', { 0: ended.length })}</summary>
          <ul className="mt-2 space-y-1">
            {ended.map((r) => (
              <li key={r.id}>
                {r.memberAName} ↔ {r.memberBName} · {relationLabel(r.type)} ·{' '}
                {r.status === 'Rejected'
                  ? t('haukuidhinishwa{0}', { 0: r.decisionNote ? ` — ${r.decisionNote}` : '' })
                  : t('iliondolewa na {0}, {1}', { 0: r.endedBy, 1: realDateTime(r.endedAt) })}
              </li>
            ))}
          </ul>
        </details>
      )}

      <ReasonModal
        open={action?.kind === 'decideAdd'}
        title={
          action?.kind === 'decideAdd' && action.approve
            ? t('Idhinisha uhusiano')
            : action?.kind === 'decideAdd' && action.r.canCancelAdd
              ? t('Ghairi usajili wa uhusiano')
              : t('Kataa uhusiano')
        }
        summary={action ? `${action.r.memberAName} ↔ ${action.r.memberBName} · ${relationLabel(action.r.type)}` : undefined}
        requireReason={action?.kind === 'decideAdd' && !action.approve && !action.r.canCancelAdd}
        variant={action?.kind === 'decideAdd' && action.approve ? 'good' : 'danger'}
        confirmLabel={action?.kind === 'decideAdd' && action.approve ? t('Idhinisha') : action?.kind === 'decideAdd' && action.r.canCancelAdd ? t('Ghairi') : t('Kataa')}
        onClose={() => setAction(null)}
        onConfirm={(note) =>
          action?.kind === 'decideAdd' ? run(() => benefitsApi.decideRelation(groupId, action.r.id, action.approve, note || undefined), t('Uamuzi umehifadhiwa.')) : Promise.resolve()
        }
      />
      <AddRelationModal open={adding} groupId={groupId} onClose={() => setAdding(false)} onDone={() => { setAdding(false); onChanged(); }} />
      <ReasonModal
        open={action?.kind === 'end'}
        title={t('Omba kuondoa uhusiano')}
        summary={action ? `${action.r.memberAName} ↔ ${action.r.memberBName}` : undefined}
        requireReason
        variant="danger"
        confirmLabel={t('Tuma ombi')}
        hint={t('Kiongozi mwingine lazima aidhinishe.')}
        onClose={() => setAction(null)}
        onConfirm={(reason) => (action ? run(() => benefitsApi.endRelation(groupId, action.r.id, reason), t('Ombi limetumwa.')) : Promise.resolve())}
      />
      <ReasonModal
        open={action?.kind === 'decide'}
        title={action?.kind === 'decide' && action.approve ? t('Idhinisha kuondoa uhusiano') : t('Kataa kuondoa uhusiano')}
        summary={action ? `${action.r.memberAName} ↔ ${action.r.memberBName}` : undefined}
        requireReason={action?.kind === 'decide' && !action.approve}
        variant={action?.kind === 'decide' && action.approve ? 'good' : 'danger'}
        confirmLabel={action?.kind === 'decide' && action.approve ? t('Idhinisha') : t('Kataa')}
        onClose={() => setAction(null)}
        onConfirm={(note) =>
          action?.kind === 'decide' ? run(() => benefitsApi.decideEndRelation(groupId, action.r.id, action.approve, note || undefined), t('Uamuzi umehifadhiwa.')) : Promise.resolve()
        }
      />
    </Card>
  );
}

function AddRelationModal({ open, groupId, onClose, onDone }: { open: boolean; groupId: string; onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const members = useApi(open ? () => getTruthMembers(groupId) : null, groupId, [open]);
  const [a, setA] = useState<string | null>(null);
  const [b, setB] = useState<string | null>(null);
  const [type, setType] = useState<RelationType>('Spouse');
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const list = members.data ?? [];
  return (
    <Modal open={open} onClose={onClose} title={t('Sajili uhusiano wa wanachama')}>
      <div className="space-y-4">
        <Field label={t('Mwanachama wa kwanza')}>
          <MemberPicker members={list} value={a} onChange={setA} />
        </Field>
        <Field label={t('Uhusiano')}>
          <Select value={type} onChange={(e) => setType(e.target.value as RelationType)}>
            {(Object.keys(RELATION_TYPE) as RelationType[]).map((r) => (
              <option key={r} value={r}>
                {relationLabel(r)}
              </option>
            ))}
          </Select>
        </Field>
        <Field label={t('Mwanachama wa pili')}>
          <MemberPicker members={list.filter((m) => m.memberId !== a)} value={b} onChange={setB} />
        </Field>
        <Field label={t('Maelezo (hiari)')}>
          <Textarea rows={2} value={note} onChange={(e) => setNote(e.target.value)} />
        </Field>
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            {t('Funga')}
          </Button>
          <Button
            loading={busy}
            disabled={!a || !b || a === b}
            onClick={async () => {
              setBusy(true);
              try {
                await benefitsApi.addRelation(groupId, { memberAId: a!, memberBId: b!, type: RELATION_TYPE[type], note: note.trim() || undefined });
                toast(t('Uhusiano umesajiliwa — unasubiri idhini ya kiongozi wa pili.'), 'good');
                setA(null);
                setB(null);
                setNote('');
                onDone();
              } catch (e) {
                toast(apiErrorMessage(e), 'overdue');
              } finally {
                setBusy(false);
              }
            }}
          >
            {t('Sajili')}
          </Button>
        </div>
      </div>
    </Modal>
  );
}
