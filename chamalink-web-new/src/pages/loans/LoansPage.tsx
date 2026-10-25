import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Banknote, CheckCircle2, HandCoins, Landmark, Plus, ScrollText, Send, ThumbsDown, ThumbsUp, XCircle } from 'lucide-react';
import {
  loansApi,
  type ApplyInput,
  type LoanApplicationDetail,
  type LoanApplicationStatus,
  type LoanAssessment,
  type LoanCheck,
  type LoanPolicy,
  type LoanRow,
} from '../../api/loans';
import { apiErrorMessage, getTruthMembers } from '../../api/truth';
import { Tabs, type Tab } from '../../components/Tabs';
import { StatusBadge } from '../../components/StatusBadge';
import { Button, Card, Drawer, EmptyState, Field, Input, Loading, Modal, MoneyInput, Notice, Skeleton, Stat, Textarea } from '../../components/ui';
import { ReasonModal } from '../../components/benefits/common';
import { MemberPicker } from '../../components/truth/MemberPicker';
import { useMyGroups } from '../../hooks/useMyGroups';
import { anyRoleLabel } from '../../lib/benefits';
import { checkText, locText } from '../../lib/loc';
import type { StatusTone } from '../../lib/status';
import { useToast } from '../../lib/toast';
import { useApi } from '../../lib/useApi';
import { LEADER_ROLES, num, realDateTime, tsh, wallDate } from '../../lib/truth';
import { PageHeader } from '../truth/TruthProvider';
import { t, tk } from '../../i18n';

/**
 * /mikopo — SASISHO15 (AWAMU L).
 *  - Mwanachama: anaomba mkopo wake (sifa zinaonyeshwa KABLA ya kutuma), anaona maombi yake na mikopo yote iliyotolewa (uwazi).
 *  - Viongozi: kura (si mkopaji wala ndugu yake), Mtunza-hazina anaanzisha utoaji, kiongozi MWINGINE anathibitisha
 *    kwa rejea ya utoaji wa M-Koba. Kuandika mkopo "umeshindikana" kunahitaji viongozi wawili.
 * Sheria zote (riba, kikomo, miezi, kura) zinatoka kwenye Mipangilio; seva ndiyo inayokagua kila ruhusa.
 * URL: ?tab=maombi|mikopo  &  ?ombi={id}
 */

const APP_STATUS: Record<LoanApplicationStatus, { label: string; tone: StatusTone }> = {
  Voting: { label: 'Kura zinaendelea', tone: 'warn' },
  Approved: { label: 'Imeidhinishwa — inasubiri utoaji', tone: 'warn' },
  DisbursementInitiated: { label: 'Utoaji umeanzishwa — inasubiri uthibitisho', tone: 'warn' },
  Disbursed: { label: 'Mkopo umetolewa', tone: 'good' },
  Rejected: { label: 'Limekataliwa', tone: 'overdue' },
  Cancelled: { label: 'Limeghairiwa', tone: 'neutral' },
  Expired: { label: 'Idhini imeisha muda', tone: 'neutral' },
};

function appStatus(s: string) {
  const x = APP_STATUS[s as LoanApplicationStatus];
  return x ? { label: t(x.label), tone: x.tone } : { label: s, tone: 'neutral' as StatusTone };
}

function loanStatus(s: string, pendingDefault: boolean): { label: string; tone: StatusTone } {
  if (s === 'Active' && pendingDefault) return { label: t('Hai — ombi la "umeshindikana" linasubiri'), tone: 'warn' };
  if (s === 'Active') return { label: t('Hai'), tone: 'warn' };
  if (s === 'Repaid' || s === 'Completed' || s === 'Closed') return { label: t('Umelipwa wote'), tone: 'good' };
  if (s === 'Defaulted') return { label: t('Umeshindikana'), tone: 'overdue' };
  return { label: s, tone: 'neutral' };
}

export function LoansPage() {
  const [params, setParams] = useSearchParams();
  const { activeGroup, activeGroupId } = useMyGroups();
  const [version, setVersion] = useState(0);
  const refresh = () => setVersion((v) => v + 1);
  const [applying, setApplying] = useState(false);

  const isLeader = !!activeGroup && LEADER_ROLES.has(activeGroup.role);
  const policy = useApi(activeGroupId ? () => loansApi.policy(activeGroupId) : null, activeGroupId, [version]);

  if (!activeGroup || !activeGroupId) return <Loading />;

  const TABS: Tab[] = [
    { id: 'maombi', label: tk('Maombi'), icon: ScrollText },
    { id: 'mikopo', label: tk('Mikopo iliyotolewa'), icon: Banknote },
  ];
  const tabParam = params.get('tab') ?? 'maombi';
  const tab = TABS.some((x) => x.id === tabParam) ? tabParam : 'maombi';
  const appId = params.get('ombi');
  const setTab = (id: string) =>
    setParams((p) => {
      const n = new URLSearchParams(p);
      n.set('tab', id);
      return n;
    });
  const openApp = (id: string | null) =>
    setParams((p) => {
      const n = new URLSearchParams(p);
      if (id) n.set('ombi', id);
      else n.delete('ombi');
      return n;
    });

  const p = policy.data;
  return (
    <div className="space-y-5">
      <PageHeader
        eyebrow={t('Mikopo')}
        icon={HandCoins}
        title={activeGroup.groupName}
        description={t(
          'Mkopo unatolewa kwa hatua: ombi (sifa zinakaguliwa kwa sheria za kikundi) → kura za viongozi → Mtunza-hazina anaanzisha utoaji → kiongozi mwingine anathibitisha kwa rejea ya M-Koba. Marejesho yanalipwa kupitia M-Koba na yanakatwa kwanza kwenye kila malipo ya mkopaji.',
        )}
        aside={
          p?.enabled ? (
            <Button icon={Plus} onClick={() => setApplying(true)}>
              {t('Omba mkopo')}
            </Button>
          ) : undefined
        }
      />

      {policy.error && <Notice tone="overdue">{policy.error}</Notice>}
      {p && !p.enabled && (p.visible ?? true) && (
        // SASISHO16: mikopo mipya imesimamishwa, lakini kuna mikopo/maombi yaliyopo — yanaendelea kuhudumiwa
        <Notice tone="warn" title={t('Mikopo mipya haipokelewi kwa sasa')}>
          {t('Mikopo iliyopo inaendelea kuhudumiwa: marejesho kupitia M-Koba na taarifa zake hazibadiliki.')}
          {isLeader && p.loansEnabled && p.missing.length > 0 && (
            <div className="mt-1">{t('Viongozi wakamilishe sera ya mikopo kwenye Mipangilio: {0}', { 0: p.missing.map((m) => t(m)).join('; ') })}</div>
          )}
        </Notice>
      )}
      {p?.enabled && <PolicySummary p={p} />}

      {p && !p.visible && !p.enabled ? (
        // SASISHO16: kikundi kisicho na huduma ya mikopo — ujumbe safi, bila majina ya mipangilio ya ndani
        <EmptyState icon={HandCoins} title={t('Huduma ya mikopo haijawezeshwa kwenye kikundi hiki.')}>
          {isLeader
            ? p.loansEnabled && p.missing.length > 0
              ? t('Viongozi wakamilishe sera ya mikopo kwenye Mipangilio: {0}', { 0: p.missing.map((m) => t(m)).join('; ') })
              : t('Viongozi wanaweza kuiwezesha kwenye Mipangilio → Sera ya mikopo.')
            : t('Viongozi wa kikundi wakiiwezesha, utaweza kuomba mkopo hapa.')}
        </EmptyState>
      ) : (
        <>
          <Tabs tabs={TABS} activeTab={tab} onTabChange={setTab} />
          {tab === 'maombi' && <ApplicationsList groupId={activeGroupId} version={version} onOpen={openApp} />}
          {tab === 'mikopo' && <LoansList groupId={activeGroupId} version={version} onChanged={refresh} />}
        </>
      )}

      <ApplyModal
        open={applying}
        groupId={activeGroupId}
        isLeader={isLeader}
        onClose={() => setApplying(false)}
        onDone={(id) => {
          setApplying(false);
          refresh();
          openApp(id);
        }}
      />
      <ApplicationDrawer groupId={activeGroupId} id={appId} version={version} onClose={() => openApp(null)} onChanged={refresh} />
    </div>
  );
}

function PolicySummary({ p }: { p: LoanPolicy }) {
  const limits: string[] = [];
  if (p.maxAmount != null) limits.push(t('hadi {0}', { 0: tsh(p.maxAmount) }));
  if (p.maxMultipleOfContributions != null) limits.push(t('hadi mara {0} ya michango uliyolipa', { 0: String(p.maxMultipleOfContributions) }));
  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
      <Stat label={t('Riba')} value={`${p.interestPercent ?? 0}%`} hint={t('ya kiasi chote (mara moja)')} />
      <Stat label={t('Muda wa kurejesha')} value={t('miezi {0}', { 0: String(p.repaymentMonths ?? 0) })} />
      <Stat label={t('Kikomo')} value={limits[0] ?? '—'} hint={limits[1]} />
      <Stat label={t('Kura za viongozi')} value={num(p.requiredApprovals)} hint={t('idhini inadumu siku {0}', { 0: String(p.approvalValidDays ?? 0) })} />
    </div>
  );
}

function Checks({ checks }: { checks: LoanCheck[] }) {
  return (
    <ul className="space-y-1 text-sm">
      {checks.map((c) => (
        <li key={c.code} className={`flex items-start gap-1.5 ${c.passed ? 'text-standing-good' : 'text-standing-overdue'}`}>
          {c.passed ? <CheckCircle2 size={14} className="mt-0.5 shrink-0" /> : <XCircle size={14} className="mt-0.5 shrink-0" />}
          <span>{checkText(c)}</span>
        </li>
      ))}
    </ul>
  );
}

function Terms({ a }: { a: { amount: number; interestPercent: number; interestAmount: number; totalRepayable: number; repaymentMonths: number; monthlyInstallment: number } }) {
  return (
    <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
      <dt className="text-ink-600">{t('Kiasi')}</dt>
      <dd className="figure text-right font-semibold text-ink-950">{tsh(a.amount)}</dd>
      <dt className="text-ink-600">{t('Riba ({0}%)', { 0: String(a.interestPercent) })}</dt>
      <dd className="figure text-right text-ink-950">{tsh(a.interestAmount)}</dd>
      <dt className="text-ink-600">{t('Jumla ya kurejesha')}</dt>
      <dd className="figure text-right font-semibold text-ink-950">{tsh(a.totalRepayable)}</dd>
      <dt className="text-ink-600">{t('Kwa mwezi (miezi {0})', { 0: String(a.repaymentMonths) })}</dt>
      <dd className="figure text-right text-ink-950">{tsh(a.monthlyInstallment)}</dd>
    </dl>
  );
}

// ---------------------------------------------------------------- Kuomba
function ApplyModal({
  open,
  groupId,
  isLeader,
  onClose,
  onDone,
}: {
  open: boolean;
  groupId: string;
  isLeader: boolean;
  onClose: () => void;
  onDone: (id: string) => void;
}) {
  return (
    <Modal open={open} onClose={onClose} title={t('Omba mkopo')}>
      {/* Mwili unapachikwa upya kila dirisha linapofunguliwa => fomu huanza tupu */}
      {open && <ApplyBody groupId={groupId} isLeader={isLeader} onClose={onClose} onDone={onDone} />}
    </Modal>
  );
}

function ApplyBody({ groupId, isLeader, onClose, onDone }: { groupId: string; isLeader: boolean; onClose: () => void; onDone: (id: string) => void }) {
  const toast = useToast();
  const members = useApi(isLeader ? () => getTruthMembers(groupId) : null, groupId);
  const [forOther, setForOther] = useState(false);
  const [borrower, setBorrower] = useState<string | null>(null);
  const [amount, setAmount] = useState<number | null>(null);
  const [purpose, setPurpose] = useState('');
  const [preview, setPreview] = useState<{ key: string; data: LoanAssessment } | null>(null);
  const [busy, setBusy] = useState(false);

  const dto: ApplyInput = { amount: amount ?? 0, purpose: purpose.trim() || null, borrowerMemberId: forOther ? borrower : null };
  const key = JSON.stringify([dto.amount, dto.borrowerMemberId]);
  const current = preview?.key === key ? preview.data : null;
  const canCheck = (amount ?? 0) > 0 && (!forOther || !!borrower);

  async function check() {
    setBusy(true);
    try {
      setPreview({ key, data: await loansApi.preview(groupId, dto) });
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  async function submit() {
    setBusy(true);
    try {
      const r = await loansApi.apply(groupId, dto);
      toast(t('Ombi {0} limetumwa kwa viongozi.', { 0: r.applicationNumber }), 'good');
      onDone(r.id);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-4">
      {isLeader && (
        <label className="flex items-center gap-2 text-sm text-ink-900">
          <input type="checkbox" checked={forOther} onChange={(e) => setForOther(e.target.checked)} />
          {t('Naomba kwa niaba ya mwanachama mwingine')}
        </label>
      )}
      {forOther && (
        <Field label={t('Mkopaji')}>
          <MemberPicker members={members.data ?? []} value={borrower} onChange={setBorrower} />
        </Field>
      )}
      <Field label={t('Kiasi unachoomba')} hint={t('Riba, muda na kikomo vinatoka kwenye sheria za kikundi — huwezi kuvibadilisha hapa.')}>
        <MoneyInput value={amount} onChange={setAmount} />
      </Field>
      <Field label={t('Madhumuni (hiari)')}>
        <Textarea rows={2} value={purpose} onChange={(e) => setPurpose(e.target.value)} maxLength={500} />
      </Field>

      {current && (
        <div className="space-y-3 rounded-xl border border-ink-100 bg-paper px-4 py-3">
          <Terms a={current} />
          <Checks checks={current.checks} />
          {current.maxAllowed != null && <div className="text-xs text-ink-600">{t('Kiwango cha juu unachoweza kuomba sasa: {0}', { 0: tsh(current.maxAllowed) })}</div>}
          <Notice tone={current.eligible ? 'good' : 'overdue'}>
            {current.eligible
              ? t('Unakidhi masharti. Ombi litahitaji kura {0} za viongozi.', { 0: String(current.requiredApprovals) })
              : t('Hukidhi masharti yote hapo juu — ombi haliwezi kutumwa.')}
          </Notice>
        </div>
      )}

      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={onClose}>
          {t('Funga')}
        </Button>
        {!current ? (
          <Button onClick={check} loading={busy} disabled={!canCheck}>
            {t('Angalia sifa')}
          </Button>
        ) : (
          <Button icon={Send} onClick={submit} loading={busy} disabled={!current.eligible}>
            {t('Tuma ombi')}
          </Button>
        )}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------- Orodha ya maombi
function ApplicationsList({ groupId, version, onOpen }: { groupId: string; version: number; onOpen: (id: string) => void }) {
  const { data, error } = useApi(() => loansApi.list(groupId), groupId, [version]);
  return (
    <Card title={t('Maombi ya mikopo')} subtitle={t('Viongozi wanaona maombi yote; mwanachama anaona yake na mikopo iliyotolewa.')} bodyClassName="p-0">
      {error && <div className="p-4"><Notice tone="overdue">{error}</Notice></div>}
      {!data && !error && <div className="p-4"><Skeleton className="h-24" /></div>}
      {data && data.length === 0 && (
        <div className="p-4">
          <EmptyState icon={ScrollText} title={t('Hakuna maombi bado')} />
        </div>
      )}
      {data && data.length > 0 && (
        <ul className="divide-y divide-ink-100">
          {data.map((a) => {
            const st = appStatus(a.status);
            return (
              <li key={a.id}>
                <button type="button" onClick={() => onOpen(a.id)} className="flex w-full flex-wrap items-center gap-x-4 gap-y-1 px-4 py-3 text-left hover:bg-ink-50 sm:px-5">
                  <div className="min-w-0 flex-1">
                    <div className="font-medium text-ink-950">
                      <span className="figure">{a.applicationNumber}</span> · {a.borrower ?? '—'}
                    </div>
                    <div className="text-xs text-ink-600">
                      {realDateTime(a.createdAt)}
                      {a.status === 'Voting' && ` · ${t('kura {0}/{1}', { 0: String(a.approvals), 1: String(a.requiredApprovals) })}`}
                      {a.mkobaReference && ` · M-Koba ${a.mkobaReference}`}
                    </div>
                  </div>
                  <span className="figure font-semibold text-ink-950">{tsh(a.amount)}</span>
                  <StatusBadge label={st.label} tone={st.tone} />
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}

// ---------------------------------------------------------------- Ombi moja
type Pending = { kind: 'vote'; approve: boolean } | { kind: 'cancel' } | { kind: 'initiate' } | null;

function ApplicationDrawer({ groupId, id, version, onClose, onChanged }: { groupId: string; id: string | null; version: number; onClose: () => void; onChanged: () => void }) {
  const { data, error } = useApi(id ? () => loansApi.get(groupId, id) : null, [groupId, id], [version]);
  return (
    <Drawer open={!!id} onClose={onClose} title={data ? `${t('Ombi')} ${data.applicationNumber}` : t('Ombi la mkopo')} subtitle={data?.borrower ?? undefined}>
      {error && <Notice tone="overdue">{error}</Notice>}
      {!data && !error && <Skeleton className="h-40" />}
      {data && <ApplicationBody groupId={groupId} a={data} onChanged={onChanged} />}
    </Drawer>
  );
}

function ApplicationBody({ groupId, a, onChanged }: { groupId: string; a: LoanApplicationDetail; onChanged: () => void }) {
  const toast = useToast();
  const [pending, setPending] = useState<Pending>(null);
  const st = appStatus(a.status);

  async function run(fn: () => Promise<unknown>, ok: string) {
    try {
      await fn();
      toast(ok, 'good');
      setPending(null);
      onChanged();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    }
  }

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center gap-2">
        <StatusBadge label={st.label} tone={st.tone} />
        {a.status === 'Voting' && (
          <span className="text-xs text-ink-600">
            {t('Idhini {0} kati ya {1} zinazohitajika · wapiga kura wanaostahili {2}', { 0: String(a.approvals), 1: String(a.requiredApprovals), 2: String(a.eligibleVoters) })}
          </span>
        )}
      </div>

      <div className="rounded-xl border border-ink-100 bg-paper px-4 py-3">
        <Terms a={a} />
        {a.purpose && <div className="mt-2 text-sm text-ink-700">{t('Madhumuni')}: {a.purpose}</div>}
      </div>

      <Timeline a={a} />

      {a.eligibility && a.eligibility.length > 0 && (
        <div>
          <h3 className="mb-1 text-sm font-semibold text-ink-950">{t('Sifa wakati wa kuomba')}</h3>
          <Checks checks={a.eligibility} />
        </div>
      )}

      {a.votes.length > 0 && (
        <div>
          <h3 className="mb-1 text-sm font-semibold text-ink-950">{t('Kura')}</h3>
          <ul className="space-y-1 text-sm">
            {a.votes.map((v, i) => (
              <li key={i} className={v.approve ? 'text-standing-good' : 'text-standing-overdue'}>
                {v.approve ? '✓' : '✗'} {v.voter ?? '—'} ({anyRoleLabel(v.role)}) · {realDateTime(v.at)}
                {v.reason ? ` — ${v.reason}` : ''}
              </li>
            ))}
          </ul>
        </div>
      )}

      {a.closeReason && (
        <Notice tone="neutral" title={t('Imefungwa')}>
          {locText(a.closeReason)} {a.closedBy ? `— ${a.closedBy}` : ''}
        </Notice>
      )}

      {a.canVote && (
        <div className="flex flex-wrap gap-2">
          <Button variant="good" icon={ThumbsUp} onClick={() => setPending({ kind: 'vote', approve: true })}>
            {t('Idhinisha')}
          </Button>
          <Button variant="danger" icon={ThumbsDown} onClick={() => setPending({ kind: 'vote', approve: false })}>
            {t('Kataa')}
          </Button>
        </div>
      )}

      {a.status === 'Approved' && (
        <div className="space-y-2">
          {a.approvalExpiresAt && <div className="text-xs text-ink-600">{t('Idhini inaisha {0}', { 0: realDateTime(a.approvalExpiresAt) })}</div>}
          {a.canInitiate ? (
            <Button icon={Landmark} onClick={() => setPending({ kind: 'initiate' })}>
              {t('Anzisha utoaji')}
            </Button>
          ) : (
            a.initiateWhy && <Notice tone="neutral">{locText(a.initiateWhy)}</Notice>
          )}
        </div>
      )}

      {a.canConfirm && <ConfirmDisbursement groupId={groupId} a={a} onDone={onChanged} />}
      {a.status === 'DisbursementInitiated' && !a.canConfirm && (
        <Notice tone="neutral">{t('Kiongozi mwingine (si aliyeanzisha, si mkopaji wala ndugu yake) athibitishe kwa rejea ya utoaji wa M-Koba.')}</Notice>
      )}

      {a.canCancel && (
        <div className="border-t border-ink-100 pt-4">
          <Button variant="secondary" size="sm" onClick={() => setPending({ kind: 'cancel' })}>
            {t('Ghairi ombi')}
          </Button>
        </div>
      )}

      <ReasonModal
        open={pending?.kind === 'vote'}
        title={pending?.kind === 'vote' && pending.approve ? t('Idhinisha mkopo') : t('Kataa mkopo')}
        summary={`${a.applicationNumber} · ${a.borrower ?? ''} · ${tsh(a.amount)}`}
        requireReason={pending?.kind === 'vote' && !pending.approve}
        variant={pending?.kind === 'vote' && pending.approve ? 'good' : 'danger'}
        confirmLabel={pending?.kind === 'vote' && pending.approve ? t('Idhinisha') : t('Kataa')}
        onClose={() => setPending(null)}
        onConfirm={(r) => (pending?.kind === 'vote' ? run(() => loansApi.vote(groupId, a.id, pending.approve, r || undefined), t('Kura imehifadhiwa.')) : Promise.resolve())}
      />
      <ReasonModal
        open={pending?.kind === 'cancel'}
        title={t('Ghairi ombi la mkopo')}
        summary={`${a.applicationNumber} · ${tsh(a.amount)}`}
        requireReason
        variant="danger"
        confirmLabel={t('Ghairi ombi')}
        onClose={() => setPending(null)}
        onConfirm={(r) => run(() => loansApi.cancel(groupId, a.id, r), t('Ombi limeghairiwa.'))}
      />
      <Modal
        open={pending?.kind === 'initiate'}
        onClose={() => setPending(null)}
        title={t('Anzisha utoaji')}
        footer={
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setPending(null)}>
              {t('Funga')}
            </Button>
            <Button icon={Landmark} onClick={() => run(() => loansApi.initiate(groupId, a.id), t('Utoaji umeanzishwa — kiongozi mwingine athibitishe.'))}>
              {t('Anzisha')}
            </Button>
          </div>
        }
      >
        <p className="text-sm text-ink-900">
          {t('Toa {0} kwa {1} kupitia M-Koba, kisha kiongozi mwingine atathibitisha kwa rejea ya utoaji huo.', { 0: tsh(a.amount), 1: a.borrower ?? '' })}
        </p>
      </Modal>
    </div>
  );
}

function Timeline({ a }: { a: LoanApplicationDetail }) {
  const rows: [string, string | null, string | null][] = [
    [t('Limeombwa'), a.requestedBy, a.createdAt],
    [t('Limeidhinishwa'), null, a.approvedAt],
    [t('Utoaji umeanzishwa'), a.initiatedBy, a.disbursementInitiatedAt],
    [t('Umethibitishwa (M-Koba)'), a.confirmedBy, a.disbursementConfirmedAt],
  ];
  return (
    <ol className="space-y-1 text-sm">
      {rows
        .filter((r) => r[2])
        .map(([label, who, at]) => (
          <li key={label} className="flex flex-wrap gap-x-2">
            <span className="font-medium text-ink-950">{label}</span>
            <span className="text-ink-600">
              {realDateTime(at)}
              {who ? ` · ${who}` : ''}
            </span>
          </li>
        ))}
      {a.mkobaReference && (
        <li className="text-ink-700">
          {t('Rejea ya M-Koba')}: <span className="figure">{a.mkobaReference}</span>
          {a.recipientNote ? ` — ${a.recipientNote}` : ''}
        </li>
      )}
    </ol>
  );
}

function ConfirmDisbursement({ groupId, a, onDone }: { groupId: string; a: LoanApplicationDetail; onDone: () => void }) {
  const toast = useToast();
  const [ref, setRef] = useState(a.candidates[0]?.reference ?? '');
  const [other, setOther] = useState(false);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);

  async function submit() {
    setBusy(true);
    try {
      await loansApi.confirm(groupId, a.id, ref.trim(), other, note.trim() || undefined);
      toast(t('Mkopo umethibitishwa na kuandikwa.'), 'good');
      onDone();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card title={t('Thibitisha utoaji kwa M-Koba')} subtitle={t('Chagua utoaji wa M-Koba wa kiasi hiki (baada ya idhini) ambao haujatumika kwa jambo lingine.')}>
      <div className="space-y-3">
        {a.candidates.length > 0 ? (
          <ul className="space-y-1 text-sm">
            {a.candidates.map((c) => (
              <li key={c.reference}>
                <label className="flex items-start gap-2">
                  <input type="radio" name="mkref" checked={ref === c.reference} onChange={() => setRef(c.reference)} className="mt-1" />
                  <span>
                    <span className="figure font-medium text-ink-950">{c.reference}</span> · {wallDate(c.timestamp)} · {tsh(c.amount)}
                    {c.payerName ? ` · ${c.payerName}` : ''} {c.phone ? `(${c.phone})` : ''}
                  </span>
                </label>
              </li>
            ))}
          </ul>
        ) : (
          <Notice tone="warn">{t('Hakuna utoaji wa M-Koba wa kiasi hiki ulioingizwa bado. Ingiza PDF ya M-Koba yenye utoaji huu, au andika rejea hapa chini.')}</Notice>
        )}
        <Field label={t('Rejea ya M-Koba')}>
          <Input value={ref} onChange={(e) => setRef(e.target.value)} />
        </Field>
        <label className="flex items-start gap-2 text-sm text-ink-900">
          <input type="checkbox" checked={other} onChange={(e) => setOther(e.target.checked)} className="mt-1" />
          {t('Pesa ilitumwa kwa namba isiyo ya mkopaji (nimeeleza sababu hapa chini)')}
        </label>
        {other && (
          <Field label={t('Sababu')}>
            <Textarea rows={2} value={note} onChange={(e) => setNote(e.target.value)} />
          </Field>
        )}
        <div className="flex justify-end">
          <Button variant="good" icon={CheckCircle2} onClick={submit} loading={busy} disabled={!ref.trim() || (other && note.trim().length < 3)}>
            {t('Thibitisha')}
          </Button>
        </div>
      </div>
    </Card>
  );
}

// ---------------------------------------------------------------- Mikopo iliyotolewa
type LoanAction = { kind: 'request'; l: LoanRow } | { kind: 'decide'; l: LoanRow; approve: boolean } | null;

function LoansList({ groupId, version, onChanged }: { groupId: string; version: number; onChanged: () => void }) {
  const toast = useToast();
  const { data, error } = useApi(() => loansApi.loans(groupId), groupId, [version]);
  const [action, setAction] = useState<LoanAction>(null);

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
    <Card title={t('Mikopo iliyotolewa')} subtitle={t('Kiasi kilicholipwa na kilichobaki kinahesabiwa na Injini ya Ukweli kutoka malipo ya M-Koba.')} bodyClassName="p-0">
      {error && <div className="p-4"><Notice tone="overdue">{error}</Notice></div>}
      {!data && !error && <div className="p-4"><Skeleton className="h-24" /></div>}
      {data && data.length === 0 && (
        <div className="p-4">
          <EmptyState icon={Banknote} title={t('Hakuna mkopo uliotolewa bado')} />
        </div>
      )}
      {data && data.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[720px] text-sm">
            <thead className="border-b border-ink-100 text-left text-xs uppercase tracking-wide text-ink-600">
              <tr>
                <th className="px-4 py-2">{t('Mkopaji')}</th>
                <th className="px-4 py-2 text-right">{t('Mkopo')}</th>
                <th className="px-4 py-2 text-right">{t('Jumla ya kurejesha')}</th>
                <th className="px-4 py-2 text-right">{t('Amelipa')}</th>
                <th className="px-4 py-2 text-right">{t('Anadaiwa')}</th>
                <th className="px-4 py-2">{t('Mwisho')}</th>
                <th className="px-4 py-2">{t('Hali')}</th>
                <th className="px-4 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-100">
              {data.map((l) => {
                const st = loanStatus(l.status, !!l.defaultRequestedBy);
                return (
                  <tr key={l.id} className="align-top">
                    <td className="px-4 py-2">
                      <div className="font-medium text-ink-950">{l.borrower ?? '—'}</div>
                      <div className="text-xs text-ink-600">
                        {wallDate(l.disbursedAt)}
                        {l.disbursementReference ? ` · ${l.disbursementReference}` : ''}
                      </div>
                      {l.defaultRequestedBy && (
                        <div className="text-xs text-standing-warn">
                          {t('Ombi la "umeshindikana": {0}', { 0: l.defaultRequestedBy })} — {l.defaultReason}
                        </div>
                      )}
                    </td>
                    <td className="figure px-4 py-2 text-right">{num(l.principalAmount)}</td>
                    <td className="figure px-4 py-2 text-right">{num(l.totalRepayable)}</td>
                    <td className="figure px-4 py-2 text-right">{num(l.repaid)}</td>
                    <td className="figure px-4 py-2 text-right font-semibold">{num(l.outstanding)}</td>
                    <td className="px-4 py-2 text-ink-700">{l.dueDate ? wallDate(l.dueDate) : '—'}</td>
                    <td className="px-4 py-2">
                      <StatusBadge label={st.label} tone={st.tone} />
                    </td>
                    <td className="px-4 py-2">
                      <div className="flex flex-wrap justify-end gap-1.5">
                        {l.canRequestDefault && (
                          <Button size="sm" variant="secondary" onClick={() => setAction({ kind: 'request', l })}>
                            {t('Omba "umeshindikana"')}
                          </Button>
                        )}
                        {l.canDecideDefault && (
                          <>
                            <Button size="sm" variant="danger" onClick={() => setAction({ kind: 'decide', l, approve: true })}>
                              {t('Idhinisha')}
                            </Button>
                            <Button size="sm" variant="secondary" onClick={() => setAction({ kind: 'decide', l, approve: false })}>
                              {t('Kataa')}
                            </Button>
                          </>
                        )}
                        {l.canCancelDefault && (
                          <Button size="sm" variant="secondary" onClick={() => setAction({ kind: 'decide', l, approve: false })}>
                            {t('Ghairi ombi')}
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      <ReasonModal
        open={action?.kind === 'request'}
        title={t('Omba mkopo uandikwe "umeshindikana"')}
        summary={action ? `${action.l.borrower ?? ''} · ${tsh(action.l.outstanding)}` : undefined}
        requireReason
        variant="danger"
        confirmLabel={t('Tuma ombi')}
        hint={t('Kiongozi mwingine lazima aidhinishe. Deni halifutwi — mkopaji bado anadaiwa.')}
        onClose={() => setAction(null)}
        onConfirm={(r) => (action?.kind === 'request' ? run(() => loansApi.requestDefault(groupId, action.l.id, r), t('Ombi limetumwa.')) : Promise.resolve())}
      />
      <ReasonModal
        open={action?.kind === 'decide'}
        title={action?.kind === 'decide' && action.approve ? t('Idhinisha "umeshindikana"') : t('Kataa / ghairi ombi')}
        summary={action ? `${action.l.borrower ?? ''} · ${tsh(action.l.outstanding)}` : undefined}
        variant={action?.kind === 'decide' && action.approve ? 'danger' : 'primary'}
        confirmLabel={action?.kind === 'decide' && action.approve ? t('Idhinisha') : t('Hifadhi')}
        onClose={() => setAction(null)}
        onConfirm={(n) => (action?.kind === 'decide' ? run(() => loansApi.decideDefault(groupId, action.l.id, action.approve, n || undefined), t('Uamuzi umehifadhiwa.')) : Promise.resolve())}
      />
    </Card>
  );
}
