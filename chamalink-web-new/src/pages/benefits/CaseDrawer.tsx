import { useState, type ReactNode } from 'react';
import { AlertTriangle, Ban, Banknote, CheckCircle2, Eye, FileUp, Lock, RefreshCw, Send, ShieldCheck, ThumbsDown, ThumbsUp, Upload, Wallet, XCircle } from 'lucide-react';
import { benefitsApi, EVIDENCE_TYPE, type CaseDetail, type CashPosition, type Evidence, type EvidenceType } from '../../api/benefits';
import { apiErrorMessage } from '../../api/truth';
import { StatusBadge } from '../../components/StatusBadge';
import { Button, Drawer, Field, Input, Notice, Select, Skeleton, Textarea } from '../../components/ui';
import { ReasonModal } from '../../components/benefits/common';
import { caseStatus, categoryLabel, channelLabel, closedReasonText, evidenceLabel, headcountRuleText, ledgerKindLabel, relationshipLabel, saveBlob } from '../../lib/benefits';
import { checkText, locMsg, locText } from '../../lib/loc';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { num, realDateTime, tsh, wallDate, wallDateTime } from '../../lib/truth';
import { t, tn } from '../../i18n';

/** Kesi moja: hesabu iliyogandishwa, vyeti, kura na uthibitisho wa malipo kwa M-Koba. */
export function CaseDrawer({
  groupId,
  caseId,
  version,
  onClose,
  onChanged,
}: {
  groupId: string;
  caseId: string | null;
  version: number;
  onClose: () => void;
  onChanged: () => void;
}) {
  const { data, error } = useApi(caseId ? () => benefitsApi.case(groupId, caseId) : null, caseId, [version]);
  return (
    <Drawer
      open={!!caseId}
      onClose={onClose}
      wide
      title={data ? `${data.caseNumber} · ${categoryLabel(data.category)}` : t('Kesi')}
      subtitle={data ? `${data.beneficiaryName ?? '—'} · ${relationshipLabel(data.relationship)}: ${data.subjectName}` : undefined}
    >
      {error && <Notice tone="overdue">{error}</Notice>}
      {!data && !error && <Skeleton className="h-64" />}
      {data && <CaseBody key={data.id} c={data} groupId={groupId} onChanged={onChanged} />}
    </Drawer>
  );
}

type Pending =
  | { kind: 'vote'; approve: boolean }
  | { kind: 'verify'; evidence: Evidence; approve: boolean }
  | { kind: 'historical'; approve: boolean }
  | { kind: 'cancel' }
  | null;

function CaseBody({ c, groupId, onChanged }: { c: CaseDetail; groupId: string; onChanged: () => void }) {
  const toast = useToast();
  const [pending, setPending] = useState<Pending>(null);
  const st = caseStatus(c.status);

  async function run(fn: () => Promise<unknown>, ok: string) {
    try {
      const res = await fn();
      toast(ok, 'good');
      // SASISHO15 (F-L2): seva inaonya kama cheti kilichohakikiwa si aina inayokubalika kwa kesi hii
      const w = (res as { warning?: { message: string; tpl: string; args: string[] } | null } | null | undefined)?.warning;
      if (w) toast(locText(w.message, w.tpl, w.args), 'warn');
      setPending(null);
      onChanged();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    }
  }

  const required = c.required ?? 0;
  const pct = required > 0 ? Math.min(100, Math.round((c.approvals / required) * 100)) : 0;

  return (
    <div className="space-y-5 pb-6">
      <div className="flex flex-wrap items-center gap-2">
        <StatusBadge label={st.label} tone={st.tone} />
        {c.frozen && (
          <span className="inline-flex items-center gap-1 rounded-full bg-ink-50 px-2 py-0.5 text-[11px] font-medium text-ink-700" title={t('Hesabu imegandishwa — haibadiliki hata wanachama wakibadilika')}>
            <Lock size={11} /> {t('Imegandishwa {0}', { 0: realDateTime(c.snapshotAt) })}
          </span>
        )}
        {c.isHistorical && <span className="text-xs text-ink-600">{t('Kesi ya zamani')}</span>}
        {c.paymentChannel === 'Cash' && (
          <span className="rounded-full bg-standing-warn/15 px-2 py-0.5 text-[11px] font-medium text-ink-800">{channelLabel(c.paymentChannel)}</span>
        )}
        {c.iAmAuditor && (
          <span className="inline-flex items-center gap-1 rounded-full bg-ink-50 px-2 py-0.5 text-[11px] font-medium text-ink-700" title={t('Umeteuliwa kuwa mkaguzi kwenye sera — unasoma tu')}>
            <ShieldCheck size={11} /> {t('Mkaguzi')}
          </span>
        )}
        {c.policyVersion != null && (
          <span className="rounded-full bg-ink-50 px-2 py-0.5 text-[11px] font-medium text-ink-700" title={t('Toleo la sera lililotumika — halibadiliki')}>
            {t('Sera toleo {0}', { 0: c.policyVersion })}
          </span>
        )}
      </div>

      {c.iAmConflicted && (
        <Notice tone="warn" title={t('Mgongano wa maslahi')}>
          {t('Umesajiliwa kama ndugu/mhusika wa karibu wa mnufaika. Huwezi kuhakiki cheti, kupiga kura, kuanzisha wala kuthibitisha malipo ya kesi hii.')}
        </Notice>
      )}

      {c.closedReason && (
        <Notice tone={c.status === 'Rejected' ? 'overdue' : 'neutral'} title={c.status === 'Rejected' ? t('Sababu ya kukataliwa') : t('Sababu')}>
          {closedReasonText(c.closedReason)} {c.closedBy ? `— ${c.closedBy}` : ''}
        </Notice>
      )}

      {/* Hesabu */}
      <section className="rounded-2xl border border-ink-100 bg-paper-raised p-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-sm">
            <dt className="text-ink-600">{t('Tarehe ya tukio')}</dt>
            <dd className="text-right">{wallDate(c.eventDate)}</dd>
            <dt className="text-ink-600">{t('Kiwango kwa mwanachama')}</dt>
            <dd className="figure text-right">{num(c.perMemberAmount)}</dd>
            <dt className="text-ink-600">{t('Wanaohesabiwa')}</dt>
            <dd className="figure text-right">{c.headcount}</dd>
            <dt className="text-ink-600">{t('Jumla')}</dt>
            <dd className="figure text-right font-semibold">{num(c.grossAmount)}</dd>
            {c.joinFeeDeducted > 0 && (
              <>
                <dt className="text-ink-600">{t('Kianzio kilichokatwa')}</dt>
                <dd className="figure text-right text-standing-warn">−{num(c.joinFeeDeducted)}</dd>
              </>
            )}
            {c.arrearsDeducted > 0 && (
              <>
                <dt className="text-ink-600">{c.legacyArrears ? t('Deni lililokatwa (michango + mkopo)') : t('Deni la michango lililokatwa')}</dt>
                <dd className="figure text-right text-standing-warn">−{num(c.arrearsDeducted)}</dd>
              </>
            )}
            {c.loanArrearsDeducted > 0 && (
              <>
                <dt className="text-ink-600">{t('Deni la mkopo lililokatwa')}</dt>
                <dd className="figure text-right text-standing-warn">−{num(c.loanArrearsDeducted)}</dd>
              </>
            )}
            <dt className="font-semibold text-ink-900">{t('Kiasi halisi')}</dt>
            <dd className="figure text-right text-base font-semibold text-ink-950">{tsh(c.netAmount)}</dd>
          </dl>
          <div className="space-y-1 text-xs text-ink-600">
            {c.headcountRule && <p>{t('Kanuni ya idadi:')} {headcountRuleText(c.headcountRule)}</p>}
            {c.paymentChannel === 'Cash' && (
              <p className="text-ink-800">
                {t('Taslimu kwa {0}', { 0: c.cashRecipientName ?? '—' })}
                {c.cashRecipientPhone ? ` (${c.cashRecipientPhone})` : ''}
                {c.paidAt ? ` · ${wallDate(c.paidAt)}` : ''}
                {c.exceptionReason ? ` — ${c.exceptionReason}` : ''}
              </p>
            )}
            <p>{t('Imefunguliwa na {0}, {1}', { 0: c.createdBy, 1: realDateTime(c.createdAt) })}</p>
            {c.description && <p className="text-ink-800">{c.description}</p>}
            {c.mkobaReference && (
              <p className="font-medium text-standing-good">
                {t('M-Koba ref')} <span className="figure">{c.mkobaReference}</span> · {wallDateTime(c.paidAt)}
                {c.paidConfirmedBy ? ` · ${t('amethibitisha')} ${c.paidConfirmedBy}` : ''}
              </p>
            )}
          </div>
        </div>
        {c.countedMembers && c.countedMembers.length > 0 && (
          <details className="mt-3 text-sm">
            <summary className="cursor-pointer text-xs font-medium text-ink-700">{t('Orodha ya waliohesabiwa ({0})', { 0: c.countedMembers.length })}</summary>
            <p className="mt-2 text-xs leading-relaxed text-ink-700">{c.countedMembers.join(', ')}</p>
          </details>
        )}
      </section>

      {c.eligibility && c.eligibility.length > 0 && (
        <section>
          <h3 className="mb-2 text-sm font-semibold text-ink-900">{t('Ukaguzi wa ustahiki')}</h3>
          <ul className="space-y-1 text-sm">
            {c.eligibility.map((x) => (
              <li key={x.code} className={`flex items-start gap-2 ${x.passed ? 'text-standing-good' : 'text-standing-overdue'}`}>
                {x.passed ? <CheckCircle2 size={15} className="mt-0.5 shrink-0" /> : <XCircle size={15} className="mt-0.5 shrink-0" />}
                <span>{checkText(x)}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      {/* Vyeti */}
      <section className="space-y-3">
        <h3 className="text-sm font-semibold text-ink-900">{t('Ushahidi (vyeti)')}</h3>
        {c.acceptedEvidence.length > 0 && (
          <p className="text-xs text-ink-600">
            {t('Vinavyokubalika:')} {c.acceptedEvidence.map(evidenceLabel).join(', ')}
          </p>
        )}
        {c.evidence.length === 0 ? (
          <p className="text-sm text-ink-600">{t('Bado hakuna cheti kilichopakiwa.')}</p>
        ) : (
          <ul className="divide-y divide-ink-100 rounded-xl border border-ink-100 bg-paper-raised">
            {c.evidence.map((e) => (
              <EvidenceRow key={e.id} e={e} groupId={groupId} onVerify={(approve) => setPending({ kind: 'verify', evidence: e, approve })} />
            ))}
          </ul>
        )}
        {c.canUpload && (
          <UploadForm
            groupId={groupId}
            caseId={c.id}
            accepted={c.paymentChannel === 'Cash' ? ['PaymentReceipt', ...c.acceptedEvidence.filter((x) => x !== 'PaymentReceipt')] : c.acceptedEvidence}
            onDone={onChanged}
          />
        )}
      </section>

      {/* Kura */}
      {(c.status === 'Voting' || c.votes.length > 0) && (
        <section className="space-y-3">
          <h3 className="text-sm font-semibold text-ink-900">{t('Kura')}</h3>
          {c.status === 'Voting' && (
            <div>
              <div className="flex flex-wrap justify-between gap-2 text-xs text-ink-700">
                <span>
                  {t('Ndiyo {0} kati ya {1} zinazohitajika', { 0: c.approvals, 1: required })}
                  {c.minLeaders ? ` · ${t('viongozi angalau {0} (sasa {1})', { 0: c.minLeaders, 1: c.leaderApprovals })}` : ''}
                </span>
                <span>
                  {t('Hapana {0} (ikifika {1} inakataliwa)', { 0: c.rejections, 1: c.rejectionsToReject ?? '—' })} · {t('mwisho {0}', { 0: realDateTime(c.votingDeadline) })}
                </span>
              </div>
              <div className="mt-1.5 h-2 overflow-hidden rounded-full bg-ink-100">
                <div className="h-full rounded-full bg-standing-good transition-all" style={{ width: `${pct}%` }} />
              </div>
              <p className="mt-1 text-xs text-ink-600">{tn(c.eligibleVoters, 'Mpiga kura 1 anaruhusiwa', 'Wapiga kura {n} wanaruhusiwa')}</p>
              {c.cashForVoters && <CashBox cash={c.cashForVoters} title={t('Pesa ipo? (kabla ya kupiga kura)')} />}
            </div>
          )}
          {c.votes.length > 0 && (
            <ul className="space-y-1 text-sm">
              {c.votes.map((v, i) => (
                <li key={i} className="flex flex-wrap items-center gap-2">
                  {v.approve ? <ThumbsUp size={14} className="text-standing-good" /> : <ThumbsDown size={14} className="text-standing-overdue" />}
                  <b className="text-ink-900">{v.name}</b>
                  <span className="text-xs text-ink-600">{realDateTime(v.at)}</span>
                  {v.reason && <span className="text-xs text-ink-700">— {v.reason}</span>}
                </li>
              ))}
            </ul>
          )}
          {c.canVote && (
            <div className="flex flex-wrap gap-2">
              <Button variant="good" icon={ThumbsUp} onClick={() => setPending({ kind: 'vote', approve: true })}>
                {t('Kubali')}
              </Button>
              <Button variant="danger" icon={ThumbsDown} onClick={() => setPending({ kind: 'vote', approve: false })}>
                {t('Kataa')}
              </Button>
            </div>
          )}
        </section>
      )}

      {c.status === 'AwaitingFunds' && <AwaitingFunds c={c} groupId={groupId} onDone={onChanged} />}

      {(c.status === 'Approved' || c.status === 'PaymentInitiated') && <PaymentFlow c={c} groupId={groupId} onDone={onChanged} />}

      {c.ledger?.posted && <LedgerCard c={c} />}

      {c.status === 'HistoricalPendingConfirmation' && !c.canConfirmHistorical && c.confirmHistoricalBlockedReason && (
        <Notice tone="warn" title={t('Kesi ya zamani haijathibitishwa bado')}>
          {locMsg(null, c.confirmHistoricalBlockedReason)}
        </Notice>
      )}

      {c.canConfirmHistorical && (
        <section className="space-y-2 rounded-2xl border border-ink-100 bg-paper-raised p-4">
          <h3 className="text-sm font-semibold text-ink-900">{t('Thibitisha kesi ya zamani')}</h3>
          <p className="text-sm text-ink-700">
            {c.paymentChannel === 'Cash'
              ? t('Fungua risiti iliyohakikiwa na uhakiki kwamba {0} taslimu zililipwa kwa {1} kwa ajili ya mafao haya, kisha thibitisha.', {
                  0: num(c.netAmount),
                  1: c.cashRecipientName ?? '—',
                })
              : t('Hakiki kwamba utoaji {0} ({1}) ulikuwa kweli mafao haya, kisha thibitisha.', { 0: c.mkobaReference ?? '—', 1: num(c.netAmount) })}
          </p>
          <div className="flex flex-wrap gap-2">
            <Button variant="good" onClick={() => setPending({ kind: 'historical', approve: true })}>
              {t('Thibitisha')}
            </Button>
            <Button variant="danger" onClick={() => setPending({ kind: 'historical', approve: false })}>
              {t('Kataa')}
            </Button>
          </div>
        </section>
      )}

      {c.canCancel && (
        <div className="border-t border-ink-100 pt-4">
          <Button variant="ghost" icon={Ban} onClick={() => setPending({ kind: 'cancel' })}>
            {t('Ghairi kesi')}
          </Button>
        </div>
      )}

      <ReasonModal
        open={pending?.kind === 'vote'}
        title={pending?.kind === 'vote' && pending.approve ? t('Kubali kesi hii') : t('Kataa kesi hii')}
        summary={`${c.caseNumber}: ${categoryLabel(c.category)} · ${c.beneficiaryName} — ${tsh(c.netAmount)}`}
        requireReason={pending?.kind === 'vote' && !pending.approve}
        variant={pending?.kind === 'vote' && pending.approve ? 'good' : 'danger'}
        confirmLabel={t('Piga kura')}
        hint={t('Kura haibadilishwi baada ya kupigwa, na inaonekana kwa wanachama wote.')}
        onClose={() => setPending(null)}
        onConfirm={(r) => run(() => benefitsApi.vote(groupId, c.id, pending?.kind === 'vote' && pending.approve, r || undefined), t('Kura yako imehifadhiwa.'))}
      />
      <ReasonModal
        open={pending?.kind === 'verify'}
        title={pending?.kind === 'verify' && pending.approve ? t('Thibitisha cheti') : t('Kataa cheti')}
        summary={
          pending?.kind === 'verify'
            ? `${evidenceLabel(pending.evidence.documentType)} · ${pending.evidence.documentNumber} · ${pending.evidence.fileName}`
            : undefined
        }
        hint={t('Hakikisha umefungua faili na kuona jina, tarehe na namba ya cheti vinalingana na kesi.')}
        requireReason={pending?.kind === 'verify' && !pending.approve}
        variant={pending?.kind === 'verify' && pending.approve ? 'good' : 'danger'}
        confirmLabel={pending?.kind === 'verify' && pending.approve ? t('Thibitisha') : t('Kataa')}
        onClose={() => setPending(null)}
        onConfirm={(r) =>
          pending?.kind === 'verify'
            ? run(() => benefitsApi.verify(groupId, pending.evidence.id, pending.approve, r || undefined), pending.approve ? t('Cheti kimethibitishwa.') : t('Cheti kimekataliwa.'))
            : Promise.resolve()
        }
      />
      <ReasonModal
        open={pending?.kind === 'historical'}
        title={pending?.kind === 'historical' && pending.approve ? t('Thibitisha kesi ya zamani') : t('Kataa kesi ya zamani')}
        requireReason={pending?.kind === 'historical' && !pending.approve}
        variant={pending?.kind === 'historical' && pending.approve ? 'good' : 'danger'}
        confirmLabel={pending?.kind === 'historical' && pending.approve ? t('Thibitisha') : t('Kataa')}
        onClose={() => setPending(null)}
        onConfirm={(r) =>
          run(() => benefitsApi.decideHistorical(groupId, c.id, pending?.kind === 'historical' && pending.approve, r || undefined), t('Uamuzi umehifadhiwa.'))
        }
      />
      <ReasonModal
        open={pending?.kind === 'cancel'}
        title={t('Ghairi kesi')}
        summary={t('Kesi itafungwa na wanachama wote wataarifiwa. Haiwezi kurejeshwa.')}
        requireReason
        variant="danger"
        confirmLabel={t('Ghairi kesi')}
        onClose={() => setPending(null)}
        onConfirm={(r) => run(() => benefitsApi.cancelCase(groupId, c.id, r), t('Kesi imeghairiwa.'))}
      />
    </div>
  );
}

function EvidenceRow({ e, groupId, onVerify }: { e: Evidence; groupId: string; onVerify: (approve: boolean) => void }) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const tone = e.status === 'Verified' ? 'good' : e.status === 'Rejected' ? 'overdue' : 'warn';
  const label = e.status === 'Verified' ? t('Kimethibitishwa') : e.status === 'Rejected' ? t('Kimekataliwa') : t('Kinasubiri uhakiki');
  return (
    <li className="flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
      <div className="w-full min-w-0 sm:w-auto sm:flex-1">
        <div className="font-medium text-ink-950">
          {evidenceLabel(e.documentType)} · <span className="figure">{e.documentNumber}</span>
        </div>
        <div className="text-xs text-ink-600">
          {t('Amepakia {0}, {1}', { 0: e.uploadedBy, 1: realDateTime(e.uploadedAt) })}
          {e.verifiedBy ? ` · ${t('amehakiki')} ${e.verifiedBy}` : ''}
          {e.reviewNote ? ` — ${e.reviewNote}` : ''}
        </div>
        <div className="figure truncate text-[10px] text-ink-500" title={t('Alama ya kidijitali (SHA-256) — faili likibadilishwa inaonekana')}>
          SHA-256 {e.sha256.slice(0, 16)}…
        </div>
      </div>
      <StatusBadge label={label} tone={tone} />
      {e.canView && (
        <Button
          size="sm"
          variant="secondary"
          icon={Eye}
          loading={busy}
          onClick={async () => {
            setBusy(true);
            try {
              saveBlob(await benefitsApi.evidenceFile(groupId, e.id), e.fileName);
            } catch (err) {
              toast(apiErrorMessage(err), 'overdue');
            } finally {
              setBusy(false);
            }
          }}
        >
          {t('Fungua')}
        </Button>
      )}
      {e.canVerify && (
        <div className="flex gap-1.5">
          <Button size="sm" variant="good" onClick={() => onVerify(true)}>
            {t('Thibitisha')}
          </Button>
          <Button size="sm" variant="danger" onClick={() => onVerify(false)}>
            {t('Kataa')}
          </Button>
        </div>
      )}
    </li>
  );
}

function UploadForm({ groupId, caseId, accepted, onDone }: { groupId: string; caseId: string; accepted: EvidenceType[]; onDone: () => void }) {
  const toast = useToast();
  const options = (accepted.length ? accepted : (Object.keys(EVIDENCE_TYPE) as EvidenceType[]));
  const [type, setType] = useState<EvidenceType>(options[0]);
  const [docNo, setDocNo] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [inputKey, setInputKey] = useState(0);

  return (
    <div className="space-y-3 rounded-xl border border-dashed border-ink-100 bg-paper p-4">
      <div className="flex items-center gap-2 text-sm font-medium text-ink-900">
        <FileUp size={15} /> {t('Pakia cheti')}
      </div>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label={t('Aina ya hati')}>
          <Select value={type} onChange={(e) => setType(e.target.value as EvidenceType)}>
            {options.map((o) => (
              <option key={o} value={o}>
                {evidenceLabel(o)}
              </option>
            ))}
          </Select>
        </Field>
        <Field label={t('Namba ya cheti')} hint={t('Namba moja haiwezi kutumika kwenye kesi mbili')}>
          <Input value={docNo} onChange={(e) => setDocNo(e.target.value)} className="figure uppercase" />
        </Field>
      </div>
      <Field label={t('Faili (PDF, JPG au PNG)')}>
        <input
          key={inputKey}
          type="file"
          accept="application/pdf,image/jpeg,image/png"
          onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          className="block w-full text-sm text-ink-800 file:mr-3 file:rounded-lg file:border-0 file:bg-ink-900 file:px-3 file:py-2 file:text-sm file:font-medium file:text-ink-50"
        />
      </Field>
      <Button
        icon={Upload}
        loading={busy}
        disabled={!file || docNo.trim().length < 3}
        onClick={async () => {
          if (!file) return;
          setBusy(true);
          try {
            await benefitsApi.upload(groupId, caseId, file, EVIDENCE_TYPE[type], docNo.trim());
            toast(t('Cheti kimepakiwa. Kiongozi mwingine atakihakiki.'), 'good');
            setDocNo('');
            setFile(null);
            setInputKey((k) => k + 1);
            onDone();
          } catch (e) {
            toast(apiErrorMessage(e), 'overdue');
          } finally {
            setBusy(false);
          }
        }}
      >
        {t('Pakia')}
      </Button>
    </div>
  );
}

function Step({ n, title, done, active, children }: { n: number; title: string; done: boolean; active: boolean; children?: ReactNode }) {
  return (
    <li className="flex gap-3">
      <span
        className={`mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-semibold ${
          done ? 'bg-standing-good text-on-accent' : active ? 'bg-ink-900 text-ink-50' : 'bg-ink-100 text-ink-600'
        }`}
      >
        {done ? <CheckCircle2 size={14} /> : n}
      </span>
      <div className="min-w-0 flex-1 space-y-2 pb-3">
        <div className={`text-sm font-semibold ${active || done ? 'text-ink-900' : 'text-ink-600'}`}>{title}</div>
        {children}
      </div>
    </li>
  );
}

/** Malipo = hatua mbili za watu wawili tofauti: (1) kuanzisha baada ya kuhakiki pesa ipo, (2) kuthibitisha kwa rejea ya M-Koba. */
function PaymentFlow({ c, groupId, onDone }: { c: CaseDetail; groupId: string; onDone: () => void }) {
  const toast = useToast();
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const initiated = c.status === 'PaymentInitiated';
  const cash = c.cash;
  return (
    <section className="space-y-3 rounded-2xl border border-ink-100 bg-paper-raised p-4">
      <h3 className="flex items-center gap-2 text-sm font-semibold text-ink-900">
        <Banknote size={16} /> {t('Malipo')}
      </h3>
      <ol>
        <Step n={1} title={t('Imeidhinishwa kwa kura')} done active={false}>
          <p className="text-xs text-ink-600">{realDateTime(c.approvedAt)}</p>
        </Step>
        <Step n={2} title={t('Kuanzisha malipo (baada ya kuhakiki pesa ipo)')} done={initiated} active={!initiated}>
          <PayeeCard c={c} />
          {cash && <CashBox cash={cash} title={t('Pesa ya M-Koba (ukweli)')} />}
          {c.deductionDrift && (
            <Notice tone="warn" title={t('Deni la mnufaika limebadilika tangu kura zianze')}>
              {locMsg(c.deductionDrift.messageLoc, c.deductionDrift.message)}
            </Notice>
          )}
          {initiated ? (
            <p className="text-xs text-ink-700">
              {t('Imeanzishwa na {0}, {1}', { 0: c.paymentInitiatedBy, 1: realDateTime(c.paymentInitiatedAt) })}
              {c.paymentInitiatedToPhone ? ` · ${t('kwa namba')} ${c.paymentInitiatedToPhone}` : ''}
              {c.cashAvailableAtInitiation != null ? ` · ${t('pesa iliyopatikana')} ${num(c.cashAvailableAtInitiation)}` : ''}
            </p>
          ) : c.canInitiatePayment ? (
            <div className="space-y-2">
              <Field label={t('Maelezo (hiari; lazima kama mnufaika hana namba iliyoidhinishwa)')}>
                <Textarea rows={2} value={note} onChange={(e) => setNote(e.target.value)} />
              </Field>
              <Button
                icon={Send}
                loading={busy}
                onClick={async () => {
                  setBusy(true);
                  try {
                    await benefitsApi.initiatePayment(groupId, c.id, note.trim() || undefined);
                    toast(t('Malipo yameanzishwa. Mthibitishaji mwingine ataarifiwa.'), 'good');
                    onDone();
                  } catch (e) {
                    toast(apiErrorMessage(e), 'overdue');
                  } finally {
                    setBusy(false);
                  }
                }}
              >
                {t('Anzisha malipo ya {0}', { 0: num(c.netAmount) })}
              </Button>
            </div>
          ) : (
            c.initiateBlockedReason && <p className="text-xs text-ink-600">{locMsg(c.initiateBlockedReasonLoc, c.initiateBlockedReason)}</p>
          )}
        </Step>
        <Step n={3} title={t('Kuthibitisha kwa rejea ya M-Koba (mtu mwingine)')} done={false} active={initiated}>
          {initiated && c.canConfirmPayment && <ConfirmPayment c={c} groupId={groupId} onDone={onDone} />}
          {initiated && !c.canConfirmPayment && c.confirmBlockedReason && (
            <p className="text-xs text-ink-600">{locMsg(c.confirmBlockedReasonLoc, c.confirmBlockedReason)}</p>
          )}
        </Step>
      </ol>
    </section>
  );
}

/** B6: maandishi ya fedha ya kesi iliyolipwa + ulinganisho na M-Koba na Injini ya Ukweli. */
function LedgerCard({ c }: { c: CaseDetail }) {
  const l = c.ledger!;
  return (
    <section className="space-y-2 rounded-2xl border border-ink-100 bg-paper-raised p-4">
      <h3 className="flex items-center gap-2 text-sm font-semibold text-ink-900">
        {l.ok ? <CheckCircle2 size={16} className="text-standing-good" /> : <AlertTriangle size={16} className="text-standing-overdue" />}
        {t('Mnyororo wa fedha')}
      </h3>
      <table className="w-full text-sm">
        <tbody>
          {l.entries.map((e) => (
            <tr key={e.kind} className="border-t border-ink-100 first:border-t-0">
              <td className="py-1 text-ink-700">{ledgerKindLabel(e.kind)}</td>
              <td className="figure py-1 text-xs text-ink-500">{e.reference ?? '—'}</td>
              <td className="figure py-1 text-right">{num(e.amount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <ul className="space-y-0.5 text-xs">
        {l.checks.map((x) => (
          <li key={x.code} className={`flex items-start gap-1.5 ${x.passed ? 'text-standing-good' : 'text-standing-overdue'}`}>
            {x.passed ? <CheckCircle2 size={13} className="mt-0.5 shrink-0" /> : <XCircle size={13} className="mt-0.5 shrink-0" />}
            <span>{checkText(x)}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function ConfirmPayment({ c, groupId, onDone }: { c: CaseDetail; groupId: string; onDone: () => void }) {
  const toast = useToast();
  const [ref, setRef] = useState(c.paymentCandidates.length === 1 ? c.paymentCandidates[0].reference : '');
  const [other, setOther] = useState(false);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  return (
    <div className="space-y-3">
      <p className="text-sm text-ink-700">
        {t('Pesa ikishatoka M-Koba na PDF kuingizwa, chagua utoaji huo hapa. Kiasi lazima kilingane kabisa ({0}).', { 0: num(c.netAmount) })}
      </p>
      {c.paymentCandidates.length > 0 ? (
        <div className="space-y-1.5">
          {c.paymentCandidates.map((p) => (
            <label key={p.reference} className="flex cursor-pointer items-center gap-3 rounded-lg border border-ink-100 px-3 py-2 text-sm hover:bg-ink-50">
              <input type="radio" name="payref" checked={ref === p.reference} onChange={() => setRef(p.reference)} className="accent-ink-800" />
              <span className="figure font-semibold">{p.reference}</span>
              <span className="text-ink-600">{wallDateTime(p.timestamp)}</span>
              {p.phone && (
                <span className={`figure text-xs ${c.beneficiaryPhone && p.phone !== c.beneficiaryPhone ? 'text-standing-overdue' : 'text-ink-600'}`}>
                  {p.phone}
                </span>
              )}
              <span className="figure ml-auto">{num(p.amount)}</span>
            </label>
          ))}
        </div>
      ) : (
        <Notice tone="warn">{t('Hakuna utoaji wa M-Koba wenye kiasi hiki baada ya idhini bado. Ingiza PDF mpya ya M-Koba kwanza, au andika ref mwenyewe.')}</Notice>
      )}
      <Field label={t('Ref ya M-Koba')}>
        <Input value={ref} onChange={(e) => setRef(e.target.value)} className="figure uppercase" />
      </Field>
      <label className="flex items-start gap-2 text-xs text-ink-700">
        <input type="checkbox" checked={other} onChange={(e) => setOther(e.target.checked)} className="mt-0.5 accent-ink-800" />
        {t('Pesa ilienda kwa namba isiyo ya mnufaika (mf. familia ya marehemu) — nitaeleza. Itaonekana kwenye ukaguzi wa hatari.')}
      </label>
      {other && (
        <Field label={t('Maelezo')}>
          <Textarea rows={2} value={note} onChange={(e) => setNote(e.target.value)} />
        </Field>
      )}
      <Button
        variant="good"
        loading={busy}
        disabled={ref.trim().length < 6 || (other && note.trim().length < 5)}
        onClick={async () => {
          setBusy(true);
          try {
            await benefitsApi.confirmPayment(groupId, c.id, ref.trim().toUpperCase(), other, other ? note.trim() : undefined);
            toast(t('Malipo yamethibitishwa. Wanachama wote wamearifiwa.'), 'good');
            onDone();
          } catch (e) {
            toast(apiErrorMessage(e), 'overdue');
          } finally {
            setBusy(false);
          }
        }}
      >
        {t('Thibitisha malipo')}
      </Button>
    </div>
  );
}

/** Pesa ipo? Salio la mwisho la M-Koba − kesi nyingine ambazo hazijalipwa. */
function CashBox({ cash, title }: { cash: CashPosition; title: string }) {
  return (
    <div className={`mt-2 rounded-xl border px-3 py-2 text-xs ${cash.sufficient ? 'border-standing-good/40' : 'border-standing-overdue/50'}`}>
      <div className="flex items-center gap-1.5 font-semibold text-ink-900">
        <Wallet size={13} /> {title}
      </div>
      {cash.mkobaBalance == null ? (
        <p className="mt-1 text-standing-overdue">{t('Hakuna salio la M-Koba lililoingizwa — ingiza PDF ya karibuni kwanza.')}</p>
      ) : (
        <dl className="mt-1 grid grid-cols-2 gap-x-3 gap-y-0.5">
          <dt className="text-ink-600">{t('Salio la mwisho ({0})', { 0: wallDateTime(cash.asOf) })}</dt>
          <dd className="figure text-right">{num(cash.mkobaBalance)}</dd>
          <dt className="text-ink-600">{t('Kesi nyingine zinazosubiri malipo')}</dt>
          <dd className="figure text-right">−{num(cash.reserved)}</dd>
          <dt className="font-semibold text-ink-800">{t('Inapatikana')}</dt>
          <dd className="figure text-right font-semibold">{num(cash.available ?? 0)}</dd>
          <dt className="text-ink-600">{t('Inahitajika')}</dt>
          <dd className={`figure text-right font-semibold ${cash.sufficient ? 'text-standing-good' : 'text-standing-overdue'}`}>{num(cash.needed)}</dd>
        </dl>
      )}
      {cash.reservedCases.length > 0 && <p className="mt-1 text-[11px] text-ink-500">{cash.reservedCases.join(', ')}</p>}
    </div>
  );
}

/** Skrini ya mweka-malipo: NANI hasa analipwa (jina + namba ya mwanachama + simu iliyoidhinishwa). */
function PayeeCard({ c }: { c: CaseDetail }) {
  return (
    <div className="rounded-xl border-2 border-ink-900/70 bg-paper px-3 py-2">
      <div className="text-[11px] font-semibold uppercase tracking-wide text-ink-600">{t('Lipa')}</div>
      <div className="figure text-lg font-bold text-ink-950">{tsh(c.netAmount)}</div>
      <div className="text-sm text-ink-900">
        {t('kwa')} <b>{c.beneficiaryName ?? '—'}</b>
        {c.beneficiaryMemberNumber && (
          <span className="text-ink-700">
            {' · '}
            {t('Namba ya mwanachama')} <span className="figure">{c.beneficiaryMemberNumber}</span>
          </span>
        )}
      </div>
      <div className="text-sm">
        {c.beneficiaryPhone ? (
          <span className="text-ink-800">
            {t('Simu iliyoidhinishwa')}: <span className="figure font-semibold">{c.beneficiaryPhone}</span>
          </span>
        ) : (
          <span className="text-standing-overdue">{t('Mnufaika hana namba iliyoidhinishwa — eleza nani atapokea pesa.')}</span>
        )}
      </div>
    </div>
  );
}

/** Imeidhinishwa kwa kura lakini salio la M-Koba halikutosha: ingiza PDF mpya kisha "Kagua pesa tena". */
function AwaitingFunds({ c, groupId, onDone }: { c: CaseDetail; groupId: string; onDone: () => void }) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  return (
    <section className="space-y-3 rounded-2xl border border-standing-overdue/40 bg-paper-raised p-4">
      <h3 className="flex items-center gap-2 text-sm font-semibold text-ink-900">
        <Wallet size={16} /> {t('Inasubiri pesa')}
      </h3>
      <p className="text-sm text-ink-700">
        {t('Kura zimeidhinisha kesi hii ({0}), lakini salio la M-Koba halikutosha. Haiwezi kulipwa mpaka pesa itoshe.', { 0: realDateTime(c.approvedAt) })}
      </p>
      {c.cashForVoters && <CashBox cash={c.cashForVoters} title={t('Pesa ya M-Koba (ukweli)')} />}
      {c.canRecheckFunds ? (
        <div className="space-y-1">
          <Button
            icon={RefreshCw}
            loading={busy}
            onClick={async () => {
              setBusy(true);
              try {
                await benefitsApi.recheckFunds(groupId, c.id);
                toast(t('Pesa inatosha — kesi sasa inasubiri kuanzishwa malipo.'), 'good');
                onDone();
              } catch (e) {
                toast(apiErrorMessage(e), 'overdue');
              } finally {
                setBusy(false);
              }
            }}
          >
            {t('Kagua pesa tena')}
          </Button>
          <p className="text-xs text-ink-600">{t('Ingiza PDF ya karibuni ya M-Koba (Ukweli → Ingiza) kwanza, kisha bonyeza hapa.')}</p>
        </div>
      ) : (
        <p className="text-xs text-ink-600">{t('Viongozi wataikagua tena pesa ikiingia.')}</p>
      )}
    </section>
  );
}
