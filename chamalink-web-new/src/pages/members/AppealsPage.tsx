import { useState } from 'react';
import { Navigate } from 'react-router-dom';
import { Gavel, Scale, Send } from 'lucide-react';
import { useMyGroups } from '../../hooks/useMyGroups';
import { apiErrorMessage } from '../../api/truth';
import { fileAppeal, listAppeals, voteAppeal, withdrawAppeal, type AppealKind, type AppealRow } from '../../api/appeals';
import { listClaims, type ClaimRow } from '../../api/payments';
import { governanceApi, type StatusChangeRequest } from '../../api/benefits';
import { Button, Card, EmptyState, Field, Loading, Notice, Textarea } from '../../components/ui';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { LEADER_ROLES, realDateTime } from '../../lib/truth';
import { t } from '../../i18n';

type StatusChangeRow = StatusChangeRequest;

const listStatusChanges = (groupId: string): Promise<StatusChangeRow[]> => governanceApi.list(groupId);

const STATUS_LABEL: Record<string, string> = {
  Pending: 'Inasubiri',
  Granted: 'Imekubaliwa',
  Dismissed: 'Imekataliwa',
  Withdrawn: 'Imefutwa',
};

export function AppealsPage() {
  const { activeGroup, activeGroupId, loading, error } = useMyGroups();
  const [version, setVersion] = useState(0);
  const [kind, setKind] = useState<AppealKind>('StatusChange');
  const [subjectId, setSubjectId] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  const toast = useToast();

  const appeals = useApi<AppealRow[]>(activeGroupId ? () => listAppeals(activeGroupId) : null, [activeGroupId], [version]);
  const claims = useApi<ClaimRow[]>(activeGroupId ? () => listClaims(activeGroupId) : null, [activeGroupId]);
  const changes = useApi<StatusChangeRow[]>(
    activeGroupId ? () => listStatusChanges(activeGroupId) : null,
    [activeGroupId],
  );

  if (loading) return <Loading label={t('Inapakia...')} />;
  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!activeGroupId || !activeGroup) return <Navigate to="/karibu" replace />;

  const isLeader = LEADER_ROLES.has(activeGroup.role);
  const rows = appeals.data ?? [];
  const mine = rows.filter((r) => r.mine);

  const myRejectedClaims = (claims.data ?? []).filter((c) => c.mine && c.status === 'Rejected');
  const myChanges = (changes.data ?? []).filter((c) => c.mine);

  const claimOptions = myRejectedClaims.map((c) => ({
    id: c.id,
    label: `${c.reference} · ${c.amount.toLocaleString('sw-TZ')}`,
  }));
  const changeOptions = myChanges.map((c) => ({
    id: c.id,
    label: `${c.fromStatus} → ${c.toStatus}${c.isExpulsion ? ' (kufukuzwa)' : ''} · ${realDateTime(c.requestedAt)}`,
  }));

  // Chaguo-msingi: aina yenye kitu halisi cha kupinga (usiweke mtu kwenye ukurasa tupu).
  const options = kind === 'Claim' ? claimOptions : kind === 'StatusChange' ? changeOptions : [];
  const autoKind: AppealKind = changeOptions.length ? 'StatusChange' : claimOptions.length ? 'Claim' : 'Other';
  const chosenKind: AppealKind = kind === 'Other' || options.length ? kind : autoKind;
  const shownOptions = chosenKind === 'Claim' ? claimOptions : chosenKind === 'StatusChange' ? changeOptions : [];

  function pickKind(k: AppealKind) {
    setKind(k);
    setSubjectId('');
  }

  async function submit() {
    if (reason.trim().length < 10) {
      toast(t('Eleza sababu kwa ufasaha (angalau herufi 10).'), 'warn');
      return;
    }
    if (chosenKind !== 'Other' && !subjectId) {
      toast(t('Chagua uamuzi unaopinga.'), 'warn');
      return;
    }
    setBusy('new');
    try {
      const r = await fileAppeal(activeGroupId!, { kind: chosenKind, subjectId: subjectId || null, reason: reason.trim() });
      toast(r.ujumbe, 'good');
      setReason('');
      setSubjectId('');
      setVersion((v) => v + 1);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(null);
    }
  }

  async function vote(r: AppealRow, approve: boolean) {
    setBusy(r.id);
    try {
      const res = await voteAppeal(activeGroupId!, r.id, approve, null);
      toast(res.ujumbe, res.status === 'Pending' ? 'warn' : approve ? 'good' : 'neutral');
      setVersion((v) => v + 1);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(null);
    }
  }

  async function withdraw(r: AppealRow) {
    setBusy(r.id);
    try {
      await withdrawAppeal(activeGroupId!, r.id);
      toast(t('Rufaa imefutwa.'), 'neutral');
      setVersion((v) => v + 1);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(null);
    }
  }

  function tone(s: AppealRow['status']) {
    if (s === 'Granted') return 'bg-standing-good/10 text-standing-good';
    if (s === 'Dismissed') return 'bg-standing-overdue/10 text-standing-overdue';
    if (s === 'Withdrawn') return 'bg-ink-100 text-ink-600';
    return 'bg-standing-warn/10 text-standing-warn';
  }

  return (
    <div className="space-y-5">
      <header>
        <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-gold-700">
          <Scale size={14} /> {t('Haki')}
        </div>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight text-ink-950">{t('Rufaa')}</h1>
        <p className="mt-1 max-w-3xl text-sm text-ink-600">
          {t('Unapinga uamuzi unaokugusa? Andika hapa. Viongozi wawili wanahitajika kuamua — mmoja hawezi. Rufaa haifuti deni lenyewe; inabadilisha uamuzi tu.')}
        </p>
      </header>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card title={t('Wasilisha rufaa')} subtitle={t('Chagua kile unachopinga, kisha eleza kwa uwazi.')}>
          <div className="space-y-3">
            <Field label={t('Aina ya rufaa')}>
              <select
                className="w-full rounded-lg border border-ink-100 bg-paper-raised px-3 py-2 text-sm text-ink-900"
                value={chosenKind}
                onChange={(e) => pickKind(e.target.value as AppealKind)}
              >
                <option value="StatusChange">{t('Uamuzi wa hali/kufukuzwa')}</option>
                <option value="Claim">{t('Dai la malipo lililokataliwa')}</option>
                <option value="Other">{t('Jambo lingine')}</option>
              </select>
            </Field>

            {chosenKind !== 'Other' && (
              <Field
                label={t('Uamuzi unaopinga')}
                hint={
                  shownOptions.length === 0
                    ? chosenKind === 'Claim'
                      ? t('Hakuna dai lako lililokataliwa.')
                      : t('Hakuna uamuzi wa hali uliofikiwa juu yako.')
                    : undefined
                }
              >
                {shownOptions.length === 0 ? (
                  <div className="rounded-lg border border-ink-100 bg-ink-50 px-3 py-2 text-xs text-ink-600">
                    {t('Hakuna cha kupinga kwa aina hii kwa sasa.')}
                  </div>
                ) : (
                  <select
                    className="w-full rounded-lg border border-ink-100 bg-paper-raised px-3 py-2 text-sm text-ink-900"
                    value={subjectId}
                    onChange={(e) => setSubjectId(e.target.value)}
                  >
                    <option value="">{t('— chagua —')}</option>
                    {shownOptions.map((o) => (
                      <option key={o.id} value={o.id}>
                        {o.label}
                      </option>
                    ))}
                  </select>
                )}
              </Field>
            )}

            <Field label={t('Sababu')} hint={t('Eleza kilichotokea na kifungu cha kanuni kama kipo.')}>
              <Textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={4} />
            </Field>

            <Button onClick={submit} disabled={busy === 'new'}>
              <Send size={14} /> {t('Tuma rufaa')}
            </Button>
            <p className="text-xs text-ink-600">
              {t('Rufaa haizuii malipo yoyote. Pesa inabadilishwa tu na hazina kwa rekodi yake.')}
            </p>
          </div>
        </Card>

        <Card
          className="lg:col-span-2"
          title={isLeader ? t('Rufaa zote za kikundi') : t('Rufaa zako')}
          subtitle={
            isLeader
              ? t('Kila kiongozi anapiga kura MOJA. Rufaa inaamuliwa pale viongozi wanaohitajika wanapokubaliana.')
              : t('Hali ya rufaa zako na uamuzi wa viongozi.')
          }
        >
          {appeals.loading && <Loading label={t('Inapakia rufaa...')} />}
          {appeals.error && <Notice tone="overdue">{appeals.error}</Notice>}
          {!appeals.loading && rows.length === 0 && (
            <EmptyState icon={Gavel} title={t('Hakuna rufaa')}>
              {t('Rufaa zinaonekana hapa mara mwanachama anapopinga uamuzi.')}
            </EmptyState>
          )}

          {rows.length > 0 && (
            <ul className="divide-y divide-ink-100">
              {rows.map((r) => (
                <li key={r.id} className="py-3">
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2 text-xs text-ink-600">
                        {isLeader && <span className="font-mono">{r.memberNumber}</span>}
                        {isLeader && <span>·</span>}
                        <span>{r.subjectSummary}</span>
                        <span>·</span>
                        <span>{realDateTime(r.filedAt)}</span>
                      </div>
                      <p className="mt-1 whitespace-pre-line text-sm text-ink-900">{r.reason}</p>
                      <div className="mt-1 text-[11px] text-ink-600">
                        {t('Kura')}: {r.approvals} / {r.requiredApprovals} · {t('Hapana')}: {r.rejections}
                        {r.decisionNote ? ` · ${r.decisionNote}` : ''}
                        {r.effectSummary ? ` · ${r.effectSummary}` : ''}
                      </div>
                    </div>
                    <div className="flex flex-wrap items-center gap-1.5">
                      <span className={`inline-flex rounded-full px-2 py-0.5 text-[11px] font-semibold ${tone(r.status)}`}>
                        {STATUS_LABEL[r.status] ?? r.status}
                      </span>
                      {r.canVote && (
                        <>
                          <Button size="sm" variant="good" onClick={() => vote(r, true)} disabled={busy === r.id}>
                            {t('Kubali')}
                          </Button>
                          <Button size="sm" variant="ghost" onClick={() => vote(r, false)} disabled={busy === r.id}>
                            {t('Kataa')}
                          </Button>
                        </>
                      )}
                      {r.canWithdraw && (
                        <Button size="sm" variant="ghost" onClick={() => withdraw(r)} disabled={busy === r.id}>
                          {t('Futa')}
                        </Button>
                      )}
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          )}
          {isLeader && (
            <p className="mt-3 text-xs text-ink-600">
              {t('Rufaa iliyokubaliwa inabadilisha uamuzi: ombi la hali linafutwa au hali inarudishwa, na dai lililokataliwa linafunguliwa tena. Hakuna shilingi inayoongezwa.')}
            </p>
          )}
        </Card>
      </div>

      {mine.length > 0 && !isLeader && (
        <p className="text-xs text-ink-600">
          {t('Unaweza kufuta rufaa yako mwenyewe kabla viongozi hawajaamua.')}
        </p>
      )}
    </div>
  );
}
