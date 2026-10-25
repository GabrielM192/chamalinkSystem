import { useState, type ReactNode } from 'react';
import { useLoansVisibility } from '../../hooks/useLoansVisibility';
import { Plus, ScrollText, Send, X } from 'lucide-react';
import {
  ANY_ROLE,
  benefitsApi,
  HEADCOUNT_AS_OF,
  RELATION_TYPE,
  type AnyRole,
  type HeadcountAsOf,
  type RelationType,
  CATEGORY,
  EVIDENCE_TYPE,
  FUNDING_SOURCE,
  GROUP_ROLE,
  HEADCOUNT_RULE,
  RELATIONSHIP,
  type Category,
  type EvidenceType,
  type FundingSource,
  type Governance,
  type GovernanceInput,
  type HeadcountRule,
  type LeaderRole,
  type PolicyView,
  type Rate,
  type Relationship,
} from '../../api/benefits';
import { apiErrorMessage, getTruthMembers } from '../../api/truth';
import { MemberPicker } from '../../components/truth/MemberPicker';
import { useApi } from '../../lib/useApi';
import { StatusBadge } from '../../components/StatusBadge';
import { Button, Card, EmptyState, Field, Input, Modal, MoneyInput, Notice, Segmented, Skeleton, Textarea } from '../../components/ui';
import { ReasonModal } from '../../components/benefits/common';
import { anyRoleLabel, categoryLabel, evidenceLabel, headcountAsOfLabel, headcountLabel, relationLabel, relationshipLabel, roleLabel, todayIso } from '../../lib/benefits';
import { useToast } from '../../lib/toast';
import { num, realDateTime, wallDate } from '../../lib/truth';
import { t } from '../../i18n';
import { locText } from '../../lib/loc';

type YesNo = 'yes' | 'no' | '';
const yn = (b: boolean | undefined): YesNo => (b === undefined ? '' : b ? 'yes' : 'no');

type Decision = { kind: 'gov' | 'rate'; id: string; approve: boolean; summary: string } | null;

export function PolicyTab({
  groupId,
  isLeader,
  data,
  error,
  onChanged,
}: {
  groupId: string;
  isLeader: boolean;
  data?: PolicyView;
  error?: string;
  onChanged: () => void;
}) {
  const toast = useToast();
  const [proposing, setProposing] = useState(false);
  const [rateFor, setRateFor] = useState<{ category: Category; relationship: Relationship; current?: Rate } | null>(null);
  const [decision, setDecision] = useState<Decision>(null);

  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!data) return <Skeleton className="h-64" />;

  async function cancel(kind: 'gov' | 'rate', id: string) {
    try {
      if (kind === 'gov') await benefitsApi.cancelGovernance(groupId, id);
      else await benefitsApi.cancelRate(groupId, id);
      toast(t('Pendekezo limeghairiwa.'), 'good');
      onChanged();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    }
  }

  const cats = Object.keys(CATEGORY) as Category[];
  const rels = Object.keys(RELATIONSHIP) as Relationship[];

  return (
    <div className="space-y-6">
      {/* ---------------- Sera ---------------- */}
      <Card
        title={t('Sera ya idhini na hesabu')}
        subtitle={t('Inaamua nani anapiga kura, kura ngapi zinahitajika, nani anahesabiwa kwenye idadi, na makato. Mabadiliko yanahitaji viongozi wawili.')}
        action={
          isLeader && data.pending.length === 0 ? (
            <Button size="sm" icon={Plus} onClick={() => setProposing(true)}>
              {data.active ? t('Pendekeza mabadiliko') : t('Pendekeza sera')}
            </Button>
          ) : undefined
        }
      >
        {data.iAmAuditor && !data.iAmLeader && (
          <div className="mb-3">
            <Notice>{t('Umeteuliwa kuwa mkaguzi: unaona kila kitu (daftari, uwazi, hatari, ukaguzi wa mnyororo) lakini huwezi kupiga kura wala kulipa.')}</Notice>
          </div>
        )}
        {data.active && (data.active.capacityWarnings?.length ?? 0) > 0 && (
          <div className="mb-3">
            <Notice tone="warn" title={t('Tahadhari: viongozi wanaweza kutotosha kwa kesi fulani')}>
              <ul className="list-disc space-y-1 pl-4">
                {data.active.capacityWarnings!.map((w, i) => (
                  <li key={i}>{locText(w.message, w.tpl, w.args)}</li>
                ))}
              </ul>
            </Notice>
          </div>
        )}
        {data.active ? <GovernanceSummary g={data.active} /> : <EmptyState icon={ScrollText} title={t('Hakuna sera iliyoidhinishwa bado')} />}

        {data.upcoming.map((g) => (
          <div key={g.id} className="mt-4 rounded-xl border border-ink-200 bg-ink-50/60 p-4">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <div className="text-sm font-semibold text-ink-900">
                {t('Sera toleo {0} itaanza {1}', { 0: g.version, 1: wallDate(g.effectiveFrom) })}
              </div>
              <StatusBadge label={t('Imeidhinishwa — bado haijaanza')} tone="neutral" />
            </div>
            <GovernanceSummary g={g} compareTo={data.active ?? undefined} />
          </div>
        ))}

        {data.pending.map((g) => (
          <div key={g.id} className="mt-4 rounded-xl border border-standing-warn/30 bg-standing-warn-bg/40 p-4">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <div className="text-sm font-semibold text-ink-900">
                {t('Pendekezo la {0}', { 0: g.requestedBy })} · {realDateTime(g.requestedAt)}
              </div>
              <StatusBadge label={t('Linasubiri idhini')} tone="warn" />
            </div>
            <p className="mb-2 text-xs text-ink-700">{t('Itaanza kutumika: {0}', { 0: wallDate(g.effectiveFrom) })}</p>
            <GovernanceSummary g={g} compareTo={data.active ?? undefined} />
            {g.note && <p className="mt-2 text-sm text-ink-700">“{g.note}”</p>}
            <DecisionButtons
              canDecide={isLeader && g.canDecide}
              canCancel={g.canCancel}
              onApprove={() => setDecision({ kind: 'gov', id: g.id, approve: true, summary: t('Sera mpya ya mafao') })}
              onReject={() => setDecision({ kind: 'gov', id: g.id, approve: false, summary: t('Sera mpya ya mafao') })}
              onCancel={() => cancel('gov', g.id)}
            />
          </div>
        ))}
      </Card>

      {/* ---------------- Viwango ---------------- */}
      <Card title={t('Viwango vya mafao')} subtitle={t('Kiasi kwa KILA mwanachama anayehesabiwa (mf. msiba 20,000 × wanachama 32). Kiwango kipya kinaanza tarehe yake — kesi za zamani hazibadiliki.')} bodyClassName="p-0">
        <div className="divide-y divide-ink-100">
          {cats.map((cat) =>
            rels.map((rel) => {
              const list = data.rates.filter((r) => r.category === cat && r.relationship === rel);
              const current = list.find((r) => r.isCurrent);
              const pending = list.filter((r) => r.status === 'Pending');
              const upcoming = list.filter((r) => r.status === 'Approved' && !r.isCurrent && r.effectiveFrom.slice(0, 10) > todayIso());
              const expired = current ? undefined : list.find((r) => r.status === 'Approved' && r.isExpired);
              return (
                <div key={`${cat}-${rel}`} className="px-4 py-3 sm:px-5">
                  <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
                    <div className="min-w-0 flex-1">
                      <div className="font-medium text-ink-950">
                        {categoryLabel(cat)} · {relationshipLabel(rel)}
                      </div>
                      <div className="text-xs text-ink-600">
                        {current
                          ? current.isDisabled
                            ? t('Hakuna mafao kwa uhusiano huu')
                            : `${current.acceptedEvidence.map(evidenceLabel).join(', ')}${current.maxClaimsPerMember ? ` · ${t('madai {0} kwa mwanachama', { 0: current.maxClaimsPerMember })}` : ''} · ${t('tangu {0}', { 0: wallDate(current.effectiveFrom) })}${current.effectiveTo ? ` · ${t('mpaka {0}', { 0: wallDate(current.effectiveTo) })}` : ''}`
                          : expired
                            ? t('Kiwango kiliisha {0} — weka kipya', { 0: wallDate(expired.effectiveTo) })
                            : t('Hakijawekwa — kesi haiwezi kufunguliwa')}
                      </div>
                      {upcoming.map((u) => (
                        <div key={u.id} className="text-xs text-ink-700">
                          {t('Kuanzia {0}: {1}', { 0: wallDate(u.effectiveFrom), 1: u.isDisabled ? t('hakuna mafao') : num(u.perMemberAmount) })}
                        </div>
                      ))}
                    </div>
                    <div className="figure text-lg font-semibold text-ink-950">{current && !current.isDisabled ? num(current.perMemberAmount) : '—'}</div>
                    {isLeader && pending.length === 0 && (
                      <Button size="sm" variant="secondary" onClick={() => setRateFor({ category: cat, relationship: rel, current })}>
                        {current ? t('Badilisha') : t('Weka')}
                      </Button>
                    )}
                  </div>
                  {pending.map((p) => (
                    <div key={p.id} className="mt-2 rounded-lg border border-standing-warn/30 bg-standing-warn-bg/40 px-3 py-2 text-sm">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="font-medium">
                          {p.isDisabled ? t('Pendekezo: zima mafao haya') : t('Pendekezo: {0} kwa mwanachama', { 0: num(p.perMemberAmount) })}
                        </span>
                        <span className="text-xs text-ink-600">
                          {t('kuanzia {0}', { 0: wallDate(p.effectiveFrom) })}
                          {p.effectiveTo ? ` ${t('mpaka {0}', { 0: wallDate(p.effectiveTo) })}` : ''} · {p.requestedBy}
                        </span>
                      </div>
                      {!p.isDisabled && <div className="text-xs text-ink-600">{p.acceptedEvidence.map(evidenceLabel).join(', ')}</div>}
                      {p.note && <div className="text-xs text-ink-700">“{p.note}”</div>}
                      <DecisionButtons
                        canDecide={isLeader && p.canDecide}
                        canCancel={p.canCancel}
                        onApprove={() => setDecision({ kind: 'rate', id: p.id, approve: true, summary: `${categoryLabel(cat)} · ${relationshipLabel(rel)}: ${num(p.perMemberAmount)}` })}
                        onReject={() => setDecision({ kind: 'rate', id: p.id, approve: false, summary: `${categoryLabel(cat)} · ${relationshipLabel(rel)}` })}
                        onCancel={() => cancel('rate', p.id)}
                      />
                    </div>
                  ))}
                </div>
              );
            }),
          )}
        </div>
      </Card>

      {data.history.length > 0 && (
        <details className="rounded-2xl border border-ink-100 bg-paper-raised px-4 py-3 text-sm">
          <summary className="cursor-pointer font-medium text-ink-800">{t('Historia ya sera ({0})', { 0: data.history.length })}</summary>
          <ul className="mt-3 space-y-1.5 text-xs text-ink-700">
            {data.history.map((g) => (
              <li key={g.id}>
                <b>{g.status === 'Approved' ? t('Iliidhinishwa') : t('Ilikataliwa/ilighairiwa')}</b> {g.version ? `· ${t('toleo {0}', { 0: g.version })}` : ''} {realDateTime(g.decidedAt)} · {t('ilipendekezwa na {0}', { 0: g.requestedBy })}
                {g.decidedBy ? `, ${t('uamuzi: {0}', { 0: g.decidedBy })}` : ''} — {t('kura {0}', { 0: g.requiredApprovals })}, {g.description}
              </li>
            ))}
          </ul>
        </details>
      )}

      <GovernanceModal open={proposing} base={data.upcoming[data.upcoming.length - 1] ?? data.active} groupId={groupId} onClose={() => setProposing(false)} onDone={() => { setProposing(false); onChanged(); }} />
      <RateModal target={rateFor} groupId={groupId} onClose={() => setRateFor(null)} onDone={() => { setRateFor(null); onChanged(); }} />
      <ReasonModal
        open={!!decision}
        title={decision?.approve ? t('Idhinisha pendekezo') : t('Kataa pendekezo')}
        summary={decision?.summary}
        requireReason={!!decision && !decision.approve}
        variant={decision?.approve ? 'good' : 'danger'}
        confirmLabel={decision?.approve ? t('Idhinisha') : t('Kataa')}
        onClose={() => setDecision(null)}
        onConfirm={async (note) => {
          if (!decision) return;
          try {
            if (decision.kind === 'gov') await benefitsApi.decideGovernance(groupId, decision.id, decision.approve, note || undefined);
            else await benefitsApi.decideRate(groupId, decision.id, decision.approve, note || undefined);
            toast(decision.approve ? t('Imeidhinishwa na inaanza kutumika.') : t('Pendekezo limekataliwa.'), 'good');
            setDecision(null);
            onChanged();
          } catch (e) {
            toast(apiErrorMessage(e), 'overdue');
          }
        }}
      />
    </div>
  );
}

function DecisionButtons({
  canDecide,
  canCancel,
  onApprove,
  onReject,
  onCancel,
}: {
  canDecide: boolean;
  canCancel: boolean;
  onApprove: () => void;
  onReject: () => void;
  onCancel: () => void;
}) {
  if (!canDecide && !canCancel) return <p className="mt-2 text-xs text-ink-600">{t('Linasubiri kiongozi mwingine (si aliyependekeza).')}</p>;
  return (
    <div className="mt-3 flex flex-wrap gap-2">
      {canDecide && (
        <>
          <Button size="sm" variant="good" onClick={onApprove}>
            {t('Idhinisha')}
          </Button>
          <Button size="sm" variant="danger" onClick={onReject}>
            {t('Kataa')}
          </Button>
        </>
      )}
      {canCancel && (
        <Button size="sm" variant="secondary" onClick={onCancel}>
          {t('Ghairi pendekezo langu')}
        </Button>
      )}
    </div>
  );
}

function Row({ k, v, changed }: { k: ReactNode; v: ReactNode; changed?: boolean }) {
  return (
    <>
      <dt className="text-ink-600">{k}</dt>
      <dd className={`text-right font-medium ${changed ? 'text-standing-warn' : 'text-ink-900'}`}>{v}</dd>
    </>
  );
}

function GovernanceSummary({ g, compareTo }: { g: Governance; compareTo?: Governance }) {
  const ch = (k: keyof Governance) => !!compareTo && compareTo[k] !== g[k];
  const yes = (b: boolean) => (b ? t('Ndiyo') : t('Hapana'));
  const off = t('Imezimwa');
  const roles = (r: LeaderRole[] | undefined) => (r && r.length ? r.map(roleLabel).join(', ') : '—');
  const chList = (k: 'paymentInitiatorRoles' | 'paymentConfirmerRoles' | 'approverRoles' | 'evidenceVerifierRoles' | 'conflictRelationTypes') =>
    !!compareTo && (compareTo[k] ?? []).join() !== (g[k] ?? []).join();
  const audIds = (x?: Governance) => (x?.auditors ?? []).map((a) => a.id).join();
  return (
    <dl className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-1.5 text-sm sm:grid-cols-[1fr_auto_1fr_auto]">
      <Row k={t('Kura za NDIYO zinazohitajika')} v={g.requiredApprovals} changed={ch('requiredApprovals')} />
      <Row k={t('Kati yake, viongozi angalau')} v={g.minLeaderApprovals} changed={ch('minLeaderApprovals')} />
      <Row k={t('Wanaopiga kura ya kuidhinisha')} v={(g.approverRoles ?? []).map(anyRoleLabel).join(', ') || '—'} changed={chList('approverRoles')} />
      <Row k={t('Kura za HAPANA zinazokataa')} v={g.rejectionsToReject} changed={ch('rejectionsToReject')} />
      <Row k={t('Siku za kupiga kura')} v={g.votingDays} changed={ch('votingDays')} />
      <Row
        k={t('Wanaohesabiwa kwenye idadi')}
        v={`${headcountLabel(g.headcountRule)}${g.headcountRule === 'MembersPaidWithinDays' ? ` (${g.headcountPaidWithinDays})` : ''}`}
        changed={ch('headcountRule') || ch('headcountPaidWithinDays')}
      />
      <Row k={t('Mnufaika anaondolewa kwenye idadi')} v={yes(g.excludeBeneficiaryFromHeadcount)} changed={ch('excludeBeneficiaryFromHeadcount')} />
      <Row k={t('Idadi inahesabiwa tarehe ya')} v={headcountAsOfLabel(g.headcountAsOf)} changed={ch('headcountAsOf')} />
      <Row k={t('Kata Kianzio kilichobaki')} v={yes(g.deductJoinFee)} changed={ch('deductJoinFee')} />
      <Row k={t('Kata deni la michango')} v={yes(g.deductArrears)} changed={ch('deductArrears')} />
      {(g.deductLoanArrears || ch('deductLoanArrears')) && <Row k={t('Kata deni la mkopo')} v={yes(g.deductLoanArrears)} changed={ch('deductLoanArrears')} />}
      <Row k={t('Mnufaika asiwe na deni')} v={yes(g.requireNoArrearsForEligibility)} changed={ch('requireNoArrearsForEligibility')} />
      <Row k={t('Siku za chini za uanachama')} v={g.minMembershipDays} changed={ch('minMembershipDays')} />
      <Row k={t('Siku za kuwasilisha baada ya tukio')} v={g.claimWindowDays} changed={ch('claimWindowDays')} />
      <Row k={t('Siku za malipo kuonekana M-Koba')} v={g.paymentConfirmationDays} changed={ch('paymentConfirmationDays')} />
      <Row k={t('Viongozi wa kuidhinisha hali ya mwanachama')} v={g.statusChangeApprovals} changed={ch('statusChangeApprovals')} />
      <Row k={t('Asilimia ya kura kumfukuza mwanachama')} v={`${g.expulsionVotePercent}%`} changed={ch('expulsionVotePercent')} />
      <Row k={t('Toleo la sera')} v={g.version || '—'} />
      <Row k={t('Inaanza kutumika')} v={g.effectiveFrom ? wallDate(g.effectiveFrom) : '—'} changed={ch('effectiveFrom')} />
      <Row k={t('Wahakiki wa ushahidi')} v={roles(g.evidenceVerifierRoles)} changed={chList('evidenceVerifierRoles')} />
      <Row k={t('Mhusika mmoja kwa wanachama tofauti azuiwe')} v={yes(g.blockSameSubjectAcrossMembers)} changed={ch('blockSameSubjectAcrossMembers')} />
      <Row k={t('Mnufaika awe amemaliza Kianzio')} v={yes(g.requireJoinFeePaid)} changed={ch('requireJoinFeePaid')} />
      <Row k={t('Siku za kusubiri kati ya madai')} v={g.cooldownDays || off} changed={ch('cooldownDays')} />
      <Row
        k={t('Kikomo cha mafao kwa kipindi')}
        v={g.maxBenefitAmountPerPeriod ? t('{0} kwa siku {1}', { 0: num(g.maxBenefitAmountPerPeriod), 1: g.maxBenefitPeriodDays ?? '—' }) : off}
        changed={ch('maxBenefitAmountPerPeriod') || ch('maxBenefitPeriodDays')}
      />
      <Row k={t('Pesa ya M-Koba ihakikiwe kabla ya malipo')} v={yes(g.requireCashAvailability)} changed={ch('requireCashAvailability')} />
      <Row k={t('Pesa ihakikiwe pia wakati wa kuidhinisha')} v={yes(g.cashCheckAtApproval)} changed={ch('cashCheckAtApproval')} />
      <Row
        k={t('Malipo makubwa: aliyeidhinisha asianzishe (kuanzia)')}
        v={g.highValueThreshold ? num(g.highValueThreshold) : off}
        changed={ch('highValueThreshold')}
      />
      <Row k={t('Wanaoanzisha malipo')} v={roles(g.paymentInitiatorRoles)} changed={chList('paymentInitiatorRoles')} />
      <Row k={t('Wanaothibitisha malipo')} v={roles(g.paymentConfirmerRoles)} changed={chList('paymentConfirmerRoles')} />
      <Row k={t('Aliyepiga kura asianzishe malipo')} v={yes(g.approverCannotInitiatePayment)} changed={ch('approverCannotInitiatePayment')} />
      <Row k={t('Ndugu wa mnufaika hawashiriki uamuzi')} v={yes(g.excludeRelatedParties)} changed={ch('excludeRelatedParties')} />
      {g.excludeRelatedParties && (
        <Row k={t('Aina za uhusiano zinazozuia')} v={(g.conflictRelationTypes ?? []).map(relationLabel).join(', ') || '—'} changed={chList('conflictRelationTypes')} />
      )}
      <Row
        k={t('Wakaguzi (kusoma tu)')}
        v={(g.auditors ?? []).length ? g.auditors.map((a) => a.name).join(', ') : '—'}
        changed={!!compareTo && audIds(compareTo) !== audIds(g)}
      />
      <Row k={t('Tahadhari: madai mengi (idadi kwa mwaka)')} v={g.frequentClaimsFlagCount || off} changed={ch('frequentClaimsFlagCount')} />
      <Row k={t('Tahadhari: sera ilibadilika siku chache kabla')} v={g.recentPolicyChangeFlagDays || off} changed={ch('recentPolicyChangeFlagDays')} />
    </dl>
  );
}

// ================================================================ Fomu ya sera
function GovernanceModal({ open, base, groupId, onClose, onDone }: { open: boolean; base: Governance | null; groupId: string; onClose: () => void; onDone: () => void }) {
  return (
    <Modal open={open} onClose={onClose} title={base ? t('Pendekeza mabadiliko ya sera') : t('Pendekeza sera ya mafao')}>
      {open && <GovernanceForm base={base} groupId={groupId} onClose={onClose} onDone={onDone} />}
    </Modal>
  );
}

function NumField({ label, hint, value, onChange, min, max }: { label: ReactNode; hint?: ReactNode; value: number | null; onChange: (v: number | null) => void; min?: number; max?: number }) {
  return (
    <Field label={label} hint={hint}>
      <Input type="number" inputMode="numeric" min={min} max={max} value={value ?? ''} onChange={(e) => onChange(e.target.value === '' ? null : Number(e.target.value))} />
    </Field>
  );
}

function RolesField({ label, value, onChange }: { label: ReactNode; value: LeaderRole[]; onChange: (v: LeaderRole[]) => void }) {
  return <ChecksField<LeaderRole> label={label} all={Object.keys(GROUP_ROLE) as LeaderRole[]} value={value} onChange={onChange} text={roleLabel} />;
}

function ChecksField<T extends string>({
  label,
  hint,
  all,
  value,
  onChange,
  text,
}: {
  label: ReactNode;
  hint?: ReactNode;
  all: T[];
  value: T[];
  onChange: (v: T[]) => void;
  text: (v: T) => string;
}) {
  return (
    <Field label={label} hint={hint}>
      <div className="flex flex-wrap gap-3 pt-1">
        {all.map((r) => (
          <label key={r} className="flex items-center gap-1.5 text-sm text-ink-800">
            <input
              type="checkbox"
              className="accent-ink-800"
              checked={value.includes(r)}
              onChange={(e) => onChange(e.target.checked ? all.filter((x) => x === r || value.includes(x)) : value.filter((x) => x !== r))}
            />
            {text(r)}
          </label>
        ))}
      </div>
    </Field>
  );
}

function YesNoField({ label, hint, value, onChange }: { label: ReactNode; hint?: ReactNode; value: YesNo; onChange: (v: YesNo) => void }) {
  return (
    <Field label={label} hint={hint}>
      <Segmented<'yes' | 'no'>
        value={value as 'yes' | 'no'}
        onChange={onChange}
        options={[
          { value: 'yes', label: t('Ndiyo') },
          { value: 'no', label: t('Hapana') },
        ]}
      />
    </Field>
  );
}

function GovernanceForm({ base, groupId, onClose, onDone }: { base: Governance | null; groupId: string; onClose: () => void; onDone: () => void }) {
  // SASISHO16: "kata deni la mkopo" inaonyeshwa tu kama kikundi kina huduma ya mikopo au mikopo iliyopo.
  // Ikifichwa: thamani iliyopo inabaki kama ilivyo (haibadilishwi kimya); kama haijawahi kuwekwa → "hapana".
  const loansVisible = useLoansVisibility(groupId);
  const showLoanDeduction = loansVisible !== false || base?.deductLoanArrears === true;
  const toast = useToast();
  // Hakuna thamani za kubuni: sera ya kwanza inaanza tupu — viongozi wanaweka kila namba (Katiba yao).
  const [required, setRequired] = useState<number | null>(base?.requiredApprovals ?? null);
  const [minLeaders, setMinLeaders] = useState<number | null>(base?.minLeaderApprovals ?? null);
  const [approvers, setApprovers] = useState<AnyRole[]>(base?.approverRoles ?? []);
  const [rejections, setRejections] = useState<number | null>(base?.rejectionsToReject ?? null);
  const [votingDays, setVotingDays] = useState<number | null>(base?.votingDays ?? null);
  const [rule, setRule] = useState<HeadcountRule | ''>(base?.headcountRule ?? '');
  const [paidWithin, setPaidWithin] = useState<number | null>(base?.headcountPaidWithinDays ?? null);
  const [exclBen, setExclBen] = useState<YesNo>(yn(base?.excludeBeneficiaryFromHeadcount));
  const [funding] = useState<FundingSource>(base?.fundingSource ?? 'GroupFund');
  const [dJf, setDJf] = useState<YesNo>(yn(base?.deductJoinFee));
  const [dArr, setDArr] = useState<YesNo>(yn(base?.deductArrears));
  const [noArr, setNoArr] = useState<YesNo>(yn(base?.requireNoArrearsForEligibility));
  const [minDays, setMinDays] = useState<number | null>(base?.minMembershipDays ?? null);
  const [claimDays, setClaimDays] = useState<number | null>(base?.claimWindowDays ?? null);
  const [payDays, setPayDays] = useState<number | null>(base?.paymentConfirmationDays ?? null);
  const [statusAppr, setStatusAppr] = useState<number | null>(base?.statusChangeApprovals ?? null);
  const [expelPct, setExpelPct] = useState<number | null>(base?.expulsionVotePercent ?? null);
  const [jfPaid, setJfPaid] = useState<YesNo>(yn(base?.requireJoinFeePaid));
  const [cooldown, setCooldown] = useState<number | null>(base ? (base.cooldownDays ?? 0) : null);
  const [maxAmt, setMaxAmt] = useState<number | null>(base ? (base.maxBenefitAmountPerPeriod ?? 0) : null);
  const [maxDays, setMaxDays] = useState<number | null>(base ? (base.maxBenefitPeriodDays ?? 0) : null);
  const [cashCheck, setCashCheck] = useState<YesNo>(yn(base?.requireCashAvailability));
  const [initiators, setInitiators] = useState<LeaderRole[]>(base?.paymentInitiatorRoles ?? []);
  const [confirmers, setConfirmers] = useState<LeaderRole[]>(base?.paymentConfirmerRoles ?? []);
  const [apprNoInit, setApprNoInit] = useState<YesNo>(yn(base?.approverCannotInitiatePayment));
  const [exclRel, setExclRel] = useState<YesNo>(yn(base?.excludeRelatedParties));
  const [freqFlag, setFreqFlag] = useState<number | null>(base ? (base.frequentClaimsFlagCount ?? 0) : null);
  const [policyFlag, setPolicyFlag] = useState<number | null>(base ? (base.recentPolicyChangeFlagDays ?? 0) : null);
  // SASISHO14
  const [asOf, setAsOf] = useState<HeadcountAsOf | ''>(base?.headcountAsOf ?? '');
  const [dLoan, setDLoan] = useState<YesNo>(yn(base?.deductLoanArrears));
  const [verifiers, setVerifiers] = useState<LeaderRole[]>(base?.evidenceVerifierRoles ?? []);
  const [relTypes, setRelTypes] = useState<RelationType[]>(base?.conflictRelationTypes ?? []);
  const [sameSubject, setSameSubject] = useState<YesNo>(yn(base?.blockSameSubjectAcrossMembers));
  const [cashAtApproval, setCashAtApproval] = useState<YesNo>(yn(base?.cashCheckAtApproval));
  const [highValue, setHighValue] = useState<number | null>(base ? (base.highValueThreshold ?? 0) : null);
  const [auditors, setAuditors] = useState<{ id: string; name: string }[]>(base?.auditors ?? []);
  const [from, setFrom] = useState(todayIso());
  const members = useApi(() => getTruthMembers(groupId), groupId);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const missing = [
    required === null, minLeaders === null, approvers.length === 0, rejections === null, votingDays === null, rule === '',
    rule === 'MembersPaidWithinDays' && paidWithin === null, exclBen === '', dJf === '', dArr === '', noArr === '',
    minDays === null, claimDays === null, payDays === null, statusAppr === null, expelPct === null,
    jfPaid === '', cooldown === null, maxAmt === null, !!maxAmt && !maxDays, cashCheck === '', initiators.length === 0, confirmers.length === 0,
    apprNoInit === '', exclRel === '', freqFlag === null, policyFlag === null,
    asOf === '', dLoan === '' && showLoanDeduction, verifiers.length === 0, exclRel === 'yes' && relTypes.length === 0, sameSubject === '', cashAtApproval === '', highValue === null, !from,
  ].some(Boolean);
  const cashClash = cashAtApproval === 'yes' && cashCheck !== 'yes';
  const leaderVoteClash = !!minLeaders && !approvers.some((r) => r !== 'Member');
  const jfClash = jfPaid === 'yes' && dJf === 'yes';
  // Mweka-malipo na mthibitishaji lazima wawe watu tofauti; nafasi moja pande zote mbili inaruhusiwa (manaibu) — seva inazuia mtu mmoja.

  async function submit() {
    setBusy(true);
    setErr(null);
    try {
      const dto: GovernanceInput = {
        requiredApprovals: required!,
        minLeaderApprovals: minLeaders!,
        allowMemberApprovers: approvers.includes('Member'),
        rejectionsToReject: rejections!,
        votingDays: votingDays!,
        headcountRule: HEADCOUNT_RULE[rule as HeadcountRule],
        headcountPaidWithinDays: rule === 'MembersPaidWithinDays' ? paidWithin : null,
        excludeBeneficiaryFromHeadcount: exclBen === 'yes',
        fundingSource: FUNDING_SOURCE[funding],
        deductJoinFee: dJf === 'yes',
        deductArrears: dArr === 'yes',
        requireNoArrearsForEligibility: noArr === 'yes',
        minMembershipDays: minDays!,
        claimWindowDays: claimDays!,
        paymentConfirmationDays: payDays!,
        statusChangeApprovals: statusAppr!,
        expulsionVotePercent: expelPct!,
        requireJoinFeePaid: jfPaid === 'yes',
        cooldownDays: cooldown ?? 0,
        maxBenefitAmountPerPeriod: maxAmt ?? 0,
        maxBenefitPeriodDays: maxAmt ? (maxDays ?? 0) : 0,
        requireCashAvailability: cashCheck === 'yes',
        paymentInitiatorRoles: initiators.map((r) => GROUP_ROLE[r]),
        paymentConfirmerRoles: confirmers.map((r) => GROUP_ROLE[r]),
        approverCannotInitiatePayment: apprNoInit === 'yes',
        excludeRelatedParties: exclRel === 'yes',
        frequentClaimsFlagCount: freqFlag ?? 0,
        recentPolicyChangeFlagDays: policyFlag ?? 0,
        cashCheckAtApproval: cashAtApproval === 'yes',
        conflictRelationTypes: relTypes.map((r) => RELATION_TYPE[r]),
        approverRoles: approvers.map((r) => ANY_ROLE[r]),
        evidenceVerifierRoles: verifiers.map((r) => GROUP_ROLE[r]),
        auditorMemberIds: auditors.map((a) => a.id),
        highValueThreshold: highValue ?? 0,
        blockSameSubjectAcrossMembers: sameSubject === 'yes',
        deductLoanArrears: showLoanDeduction ? dLoan === 'yes' : (base?.deductLoanArrears ?? false),
        effectiveFrom: from,
        headcountAsOf: HEADCOUNT_AS_OF[asOf as HeadcountAsOf],
        note: note.trim() || undefined,
      };
      await benefitsApi.proposeGovernance(groupId, dto);
      toast(t('Pendekezo limetumwa. Kiongozi mwingine ataidhinisha.'), 'good');
      onDone();
    } catch (e) {
      setErr(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-5">
      <Notice>{t('Weka namba kulingana na Katiba ya kikundi chenu (mf. utoaji wa pesa: ridhaa ya wanachama 5; kufukuza mwanachama: ¾ ya wanachama wote). Mfumo hauweki namba yoyote wenyewe.')}</Notice>
      <h4 className="text-xs font-semibold uppercase tracking-wide text-ink-600">{t('Kura')}</h4>
      <div className="grid gap-4 sm:grid-cols-2">
        <NumField label={t('Kura za NDIYO zinazohitajika')} hint={t('Angalau 2')} value={required} onChange={setRequired} min={2} />
        <NumField label={t('Kati yake, viongozi angalau')} hint={t('0 = yeyote anayeruhusiwa')} value={minLeaders} onChange={setMinLeaders} min={0} />
        <ChecksField<AnyRole>
          label={t('Nani anapiga kura ya kuidhinisha?')}
          hint={t('Mwanachama = wanachama wa kawaida pia wanapiga kura')}
          all={Object.keys(ANY_ROLE) as AnyRole[]}
          value={approvers}
          onChange={setApprovers}
          text={anyRoleLabel}
        />
        <NumField label={t('Kura za HAPANA zinazokataa kesi')} value={rejections} onChange={setRejections} min={1} />
        <NumField label={t('Siku za kupiga kura')} value={votingDays} onChange={setVotingDays} min={1} max={90} />
      </div>
      <h4 className="text-xs font-semibold uppercase tracking-wide text-ink-600">{t('Hesabu ya kiasi')}</h4>
      <Field label={t('Nani anahesabiwa kwenye idadi (kiwango × idadi)?')}>
        <Segmented<HeadcountRule>
          stack
          value={rule as HeadcountRule}
          onChange={setRule}
          options={(Object.keys(HEADCOUNT_RULE) as HeadcountRule[]).map((h) => ({ value: h, label: headcountLabel(h) }))}
        />
      </Field>
      <div className="grid gap-4 sm:grid-cols-2">
        {rule === 'MembersPaidWithinDays' && <NumField label={t('N = siku')} value={paidWithin} onChange={setPaidWithin} min={1} max={366} />}
        <Field label={t('Idadi inahesabiwa tarehe gani?')} hint={t('Wanachama waliokuwa hai siku hiyo ndio wanaohesabiwa')}>
          <Segmented<HeadcountAsOf>
            stack
            value={asOf as HeadcountAsOf}
            onChange={setAsOf}
            options={(Object.keys(HEADCOUNT_AS_OF) as HeadcountAsOf[]).map((h) => ({ value: h, label: headcountAsOfLabel(h) }))}
          />
        </Field>
        <YesNoField label={t('Mnufaika aondolewe kwenye idadi?')} value={exclBen} onChange={setExclBen} />
        <YesNoField label={t('Kata Kianzio kilichobaki kabla ya kulipa?')} value={dJf} onChange={setDJf} />
        <YesNoField label={t('Kata deni la michango kabla ya kulipa?')} value={dArr} onChange={setDArr} />
        {showLoanDeduction && <YesNoField label={t('Kata deni la mkopo kabla ya kulipa?')} value={dLoan} onChange={setDLoan} />}
      </div>
      <h4 className="text-xs font-semibold uppercase tracking-wide text-ink-600">{t('Ustahiki na muda')}</h4>
      <div className="grid gap-4 sm:grid-cols-2">
        <YesNoField label={t('Mnufaika asiwe na deni la michango?')} value={noArr} onChange={setNoArr} />
        <NumField label={t('Siku za chini za uanachama')} hint={t('0 = hakuna')} value={minDays} onChange={setMinDays} min={0} />
        <NumField label={t('Siku za kuwasilisha baada ya tukio')} value={claimDays} onChange={setClaimDays} min={1} />
        <NumField label={t('Siku za malipo kuonekana M-Koba')} hint={t('Zikipita bila malipo — tahadhari kwa wote')} value={payDays} onChange={setPayDays} min={1} max={90} />
      </div>
      <h4 className="text-xs font-semibold uppercase tracking-wide text-ink-600">{t('Hali ya wanachama')}</h4>
      <div className="grid gap-4 sm:grid-cols-2">
        <NumField label={t('Viongozi wa kuidhinisha kubadilisha hali')} hint={t('Mbali na aliyeomba')} value={statusAppr} onChange={setStatusAppr} min={1} />
        <NumField label={t('Asilimia ya kura kumfukuza (%)')} value={expelPct} onChange={setExpelPct} min={50} max={100} />
      </div>
      <h4 className="text-xs font-semibold uppercase tracking-wide text-ink-600">{t('Ulinzi dhidi ya madai ya faida')}</h4>
      <div className="grid gap-4 sm:grid-cols-2">
        <YesNoField label={t('Mnufaika awe amemaliza Kianzio?')} hint={t('Hapana = Kianzio kinakatwa kwenye mafao')} value={jfPaid} onChange={setJfPaid} />
        <NumField label={t('Siku za kusubiri kati ya madai ya mwanachama mmoja')} hint={t('0 = hakuna')} value={cooldown} onChange={setCooldown} min={0} />
        <Field label={t('Kikomo cha jumla ya mafao kwa mwanachama')} hint={t('0 = bila kikomo')}>
          <MoneyInput value={maxAmt} onChange={setMaxAmt} />
        </Field>
        {!!maxAmt && <NumField label={t('Kipindi cha kikomo (siku)')} value={maxDays} onChange={setMaxDays} min={1} />}
        <YesNoField label={t('Ndugu wa mnufaika wasihakiki, kupiga kura wala kulipa?')} hint={t('Uhusiano unasajiliwa na viongozi kwenye kichupo cha Ulinzi')} value={exclRel} onChange={setExclRel} />
        {exclRel === 'yes' && (
          <ChecksField<RelationType>
            label={t('Uhusiano upi unazuia?')}
            all={Object.keys(RELATION_TYPE) as RelationType[]}
            value={relTypes}
            onChange={setRelTypes}
            text={relationLabel}
          />
        )}
        <YesNoField
          label={t('Zuia mhusika mmoja kudaiwa na wanachama tofauti?')}
          hint={t('Mf. msiba wa mzazi mmoja unadaiwa na ndugu wawili — kesi ya pili inazuiwa')}
          value={sameSubject}
          onChange={setSameSubject}
        />
      </div>
      <h4 className="text-xs font-semibold uppercase tracking-wide text-ink-600">{t('Ukaguzi na uhakiki')}</h4>
      <div className="grid gap-4 sm:grid-cols-2">
        <RolesField label={t('Nani anahakiki ushahidi (vyeti)?')} value={verifiers} onChange={setVerifiers} />
        <Field label={t('Wakaguzi (wanaona kila kitu, hawapigi kura)')} hint={t('Hiari — mf. kamati ya ukaguzi')}>
          <div className="space-y-2">
            {members.data && (
              <MemberPicker
                members={members.data.filter((m) => !auditors.some((a) => a.id === m.memberId))}
                value={null}
                placeholder={t('Ongeza mkaguzi...')}
                onChange={(id) => {
                  const m = members.data?.find((x) => x.memberId === id);
                  if (m) setAuditors((l) => [...l, { id: m.memberId, name: m.fullName }]);
                }}
              />
            )}
            <div className="flex flex-wrap gap-1.5">
              {auditors.map((a) => (
                <span key={a.id} className="inline-flex items-center gap-1 rounded-full border border-ink-200 px-2 py-0.5 text-xs text-ink-800">
                  {a.name}
                  <button type="button" aria-label={t('Ondoa')} onClick={() => setAuditors((l) => l.filter((x) => x.id !== a.id))}>
                    <X size={12} />
                  </button>
                </span>
              ))}
            </div>
          </div>
        </Field>
      </div>
      <h4 className="text-xs font-semibold uppercase tracking-wide text-ink-600">{t('Malipo (watu wawili tofauti)')}</h4>
      <div className="grid gap-4 sm:grid-cols-2">
        <RolesField label={t('Nani anaanzisha malipo?')} value={initiators} onChange={setInitiators} />
        <RolesField label={t('Nani anathibitisha kwa rejea ya M-Koba?')} value={confirmers} onChange={setConfirmers} />
        <YesNoField label={t('Hakiki pesa ya M-Koba kabla ya kuanzisha malipo?')} value={cashCheck} onChange={setCashCheck} />
        <YesNoField label={t('Aliyepiga kura ya NDIYO asianzishe malipo?')} value={apprNoInit} onChange={setApprNoInit} />
        <YesNoField
          label={t('Hakiki pesa pia wakati wa kuidhinisha?')}
          hint={t('Pesa isipotosha, kesi inasubiri pesa (haiwezi kulipwa)')}
          value={cashAtApproval}
          onChange={setCashAtApproval}
        />
        <Field label={t('Malipo makubwa: kuanzia kiasi gani aliyeidhinisha asianzishe?')} hint={t('0 = imezimwa')}>
          <MoneyInput value={highValue} onChange={setHighValue} />
        </Field>
      </div>
      <h4 className="text-xs font-semibold uppercase tracking-wide text-ink-600">{t('Tahadhari za hatari')}</h4>
      <div className="grid gap-4 sm:grid-cols-2">
        <NumField label={t('Madai mangapi kwa mwaka ni mengi?')} hint={t('0 = hakuna tahadhari')} value={freqFlag} onChange={setFreqFlag} min={0} />
        <NumField label={t('Tahadhari kama sera ilibadilika siku N kabla ya dai')} hint={t('0 = hakuna tahadhari')} value={policyFlag} onChange={setPolicyFlag} min={0} />
      </div>
      <Field label={t('Sera inaanza kutumika')} hint={t('Leo au baadaye — sera za zamani hazibadilishwi; kesi zilizokwisha funguliwa zinabaki na sera yake')}>
        <Input type="date" value={from} min={base ? todayIso() : undefined} onChange={(e) => setFrom(e.target.value)} />
      </Field>
      <Field label={t('Maelezo (mf. kifungu cha Katiba / kikao)')}>
        <Textarea value={note} onChange={(e) => setNote(e.target.value)} />
      </Field>
      {cashClash && <Notice tone="warn">{t("'Hakiki pesa wakati wa kuidhinisha' inahitaji 'Hakiki pesa ya M-Koba kabla ya kuanzisha malipo' iwe Ndiyo.")}</Notice>}
      {leaderVoteClash && <Notice tone="warn">{t('Sera inataka kura za viongozi lakini hakuna nafasi ya kiongozi kwenye wapiga kura.')}</Notice>}
      {jfClash && <Notice tone="warn">{t("Chagua moja: 'Mnufaika awe amemaliza Kianzio' AU 'Kata Kianzio kilichobaki' — si vyote viwili.")}</Notice>}
      {err && <Notice tone="overdue">{err}</Notice>}
      <div className="flex flex-wrap justify-end gap-2 border-t border-ink-100 pt-3">
        <Button variant="secondary" onClick={onClose} disabled={busy}>
          {t('Ghairi')}
        </Button>
        <Button icon={Send} onClick={submit} disabled={missing || jfClash || cashClash || leaderVoteClash} loading={busy}>
          {t('Tuma pendekezo')}
        </Button>
      </div>
    </div>
  );
}

// ================================================================ Fomu ya kiwango
function RateModal({
  target,
  groupId,
  onClose,
  onDone,
}: {
  target: { category: Category; relationship: Relationship; current?: Rate } | null;
  groupId: string;
  onClose: () => void;
  onDone: () => void;
}) {
  return (
    <Modal open={!!target} onClose={onClose} title={target ? `${categoryLabel(target.category)} · ${relationshipLabel(target.relationship)}` : ''}>
      {target && <RateForm target={target} groupId={groupId} onClose={onClose} onDone={onDone} />}
    </Modal>
  );
}

function RateForm({
  target,
  groupId,
  onClose,
  onDone,
}: {
  target: { category: Category; relationship: Relationship; current?: Rate };
  groupId: string;
  onClose: () => void;
  onDone: () => void;
}) {
  const toast = useToast();
  const cur = target.current;
  const [disabled, setDisabled] = useState(cur?.isDisabled ?? false);
  const [amount, setAmount] = useState<number | null>(cur && !cur.isDisabled ? cur.perMemberAmount : null);
  const [evidence, setEvidence] = useState<EvidenceType[]>(cur?.acceptedEvidence ?? []);
  const [maxClaims, setMaxClaims] = useState<number | null>(cur?.maxClaimsPerMember ?? null);
  const [from, setFrom] = useState(todayIso());
  const [to, setTo] = useState('');
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const ready = !!from && (!to || to >= from) && (disabled || (!!amount && amount > 0 && evidence.length > 0));

  return (
    <div className="space-y-4">
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" checked={disabled} onChange={(e) => setDisabled(e.target.checked)} className="h-4 w-4 accent-ink-800" />
        {t('Hakuna mafao kwa uhusiano huu')}
      </label>
      {!disabled && (
        <>
          <Field label={t('Kiasi kwa kila mwanachama')} hint={t('Jumla = kiasi hiki × idadi ya wanachama wanaohesabiwa siku kesi inapoidhinishwa')}>
            <MoneyInput value={amount} onChange={setAmount} />
          </Field>
          <Field label={t('Ushahidi unaokubalika')}>
            <div className="flex flex-wrap gap-1.5">
              {(Object.keys(EVIDENCE_TYPE) as EvidenceType[]).map((e) => {
                const on = evidence.includes(e);
                return (
                  <button
                    key={e}
                    type="button"
                    onClick={() => setEvidence((l) => (on ? l.filter((x) => x !== e) : [...l, e]))}
                    className={`rounded-full border px-2.5 py-1 text-xs ${on ? 'border-ink-800 bg-ink-900 text-ink-50' : 'border-ink-100 bg-paper-raised text-ink-800 hover:border-ink-500'}`}
                  >
                    {evidenceLabel(e)}
                  </button>
                );
              })}
            </div>
          </Field>
          <Field label={t('Kikomo cha madai kwa mwanachama (hiari)')} hint={t('Mf. 1 kwa msiba wa mwanachama mwenyewe; acha wazi = bila kikomo')}>
            <Input type="number" inputMode="numeric" min={1} value={maxClaims ?? ''} onChange={(e) => setMaxClaims(e.target.value === '' ? null : Number(e.target.value))} />
          </Field>
        </>
      )}
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={t('Inaanza kutumika')}>
          <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </Field>
        <Field label={t('Inaisha (hiari)')} hint={t('Tupu = hakina mwisho')}>
          <Input type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} />
        </Field>
      </div>
      <Field label={t('Maelezo (hiari)')}>
        <Textarea value={note} onChange={(e) => setNote(e.target.value)} />
      </Field>
      {err && <Notice tone="overdue">{err}</Notice>}
      <div className="flex flex-wrap justify-end gap-2 border-t border-ink-100 pt-3">
        <Button variant="secondary" onClick={onClose} disabled={busy}>
          {t('Ghairi')}
        </Button>
        <Button
          icon={Send}
          disabled={!ready}
          loading={busy}
          onClick={async () => {
            setBusy(true);
            setErr(null);
            try {
              await benefitsApi.proposeRate(groupId, {
                category: CATEGORY[target.category],
                relationship: RELATIONSHIP[target.relationship],
                perMemberAmount: disabled ? 0 : amount ?? 0,
                acceptedEvidence: disabled ? [] : evidence.map((e) => EVIDENCE_TYPE[e]),
                maxClaimsPerMember: disabled ? null : maxClaims,
                isDisabled: disabled,
                effectiveFrom: from,
                effectiveTo: to || null,
                note: note.trim() || undefined,
              });
              toast(t('Pendekezo la kiwango limetumwa. Kiongozi mwingine ataidhinisha.'), 'good');
              onDone();
            } catch (e) {
              setErr(apiErrorMessage(e));
            } finally {
              setBusy(false);
            }
          }}
        >
          {t('Tuma pendekezo')}
        </Button>
      </div>
    </div>
  );
}
