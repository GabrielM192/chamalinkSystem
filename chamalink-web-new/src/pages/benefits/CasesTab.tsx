import { useMemo, useState } from 'react';
import { CheckCircle2, ChevronRight, History, HandHeart, Plus, XCircle } from 'lucide-react';
import {
  benefitsApi,
  CATEGORY,
  PAYMENT_CHANNEL,
  RELATIONSHIP,
  type Assessment,
  type CaseInput,
  type CaseRow,
  type Category,
  type Relationship,
} from '../../api/benefits';
import { apiErrorMessage, getTruthMembers } from '../../api/truth';
import { StatusBadge } from '../../components/StatusBadge';
import { MemberPicker } from '../../components/truth/MemberPicker';
import { Button, EmptyState, Field, Input, Modal, MoneyInput, Notice, Segmented, Skeleton, Textarea } from '../../components/ui';
import { caseStatus, categoryLabel, evidenceLabel, headcountRuleText, relationshipLabel, todayIso } from '../../lib/benefits';
import { checkText } from '../../lib/loc';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { num, tsh, wallDate } from '../../lib/truth';
import { t } from '../../i18n';

type Filter = 'open' | 'closed' | 'all';
const CLOSED = new Set(['Paid', 'Rejected', 'Cancelled']);

export function CasesTab({
  groupId,
  isLeader,
  version,
  canOpen,
  onOpen,
  onChanged,
}: {
  groupId: string;
  isLeader: boolean;
  version: number;
  canOpen: boolean;
  onOpen: (id: string) => void;
  onChanged: () => void;
}) {
  const { data, error } = useApi(() => benefitsApi.cases(groupId), groupId, [version]);
  const [filter, setFilter] = useState<Filter>('open');
  const [opening, setOpening] = useState(false);
  const [historical, setHistorical] = useState(false);

  const shown = useMemo(
    () => (data ?? []).filter((c) => filter === 'all' || (filter === 'open' ? !CLOSED.has(c.status) : CLOSED.has(c.status))),
    [data, filter],
  );
  const counts = {
    open: (data ?? []).filter((c) => !CLOSED.has(c.status)).length,
    closed: (data ?? []).filter((c) => CLOSED.has(c.status)).length,
    all: data?.length ?? 0,
  };

  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!data) return <Skeleton className="h-48" />;

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="-mx-4 flex gap-1.5 overflow-x-auto px-4 sm:mx-0 sm:px-0">
          {(
            [
              ['open', t('Zinaendelea')],
              ['closed', t('Zimefungwa')],
              ['all', t('Zote')],
            ] as [Filter, string][]
          ).map(([f, l]) => (
            <button
              key={f}
              onClick={() => setFilter(f)}
              className={`shrink-0 rounded-full px-3 py-1.5 text-xs font-medium ${
                filter === f ? 'bg-ink-900 text-ink-50' : 'bg-paper-raised text-ink-700 ring-1 ring-ink-100'
              }`}
            >
              {l} <span className="opacity-70">{counts[f]}</span>
            </button>
          ))}
        </div>
        {isLeader && (
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" icon={History} onClick={() => setHistorical(true)}>
              {t('Kesi ya zamani')}
            </Button>
            <Button icon={Plus} onClick={() => setOpening(true)} disabled={!canOpen}>
              {t('Fungua kesi')}
            </Button>
          </div>
        )}
      </div>

      {shown.length === 0 ? (
        <EmptyState icon={HandHeart} title={filter === 'open' ? t('Hakuna kesi inayoendelea') : t('Hakuna kesi')}>
          {isLeader ? t('Msiba au sherehe ikitokea, fungua kesi hapa. Kesi za zamani (zilizolipwa kabla ya mfumo) zirekodiwe kwa "Kesi ya zamani".') : undefined}
        </EmptyState>
      ) : (
        <ul className="divide-y divide-ink-100 overflow-hidden rounded-2xl border border-ink-100 bg-paper-raised">
          {shown.map((c) => (
            <CaseItem key={c.id} c={c} onOpen={() => onOpen(c.id)} />
          ))}
        </ul>
      )}

      <OpenCaseModal open={opening} groupId={groupId} onClose={() => setOpening(false)} onDone={(id) => { setOpening(false); onChanged(); onOpen(id); }} />
      <HistoricalCaseModal
        preset={historical ? {} : null}
        groupId={groupId}
        onClose={() => setHistorical(false)}
        onDone={(id) => { setHistorical(false); onChanged(); onOpen(id); }}
      />
    </div>
  );
}

function CaseItem({ c, onOpen }: { c: CaseRow; onOpen: () => void }) {
  const st = caseStatus(c.status);
  return (
    <li>
      <button onClick={onOpen} className="flex w-full flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3 text-left hover:bg-ink-50">
        <div className="w-full min-w-0 sm:w-auto sm:flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="figure text-xs font-semibold text-ink-600">{c.caseNumber}</span>
            <span className="font-semibold text-ink-950">
              {categoryLabel(c.category)} · {c.beneficiaryName ?? '—'}
            </span>
          </div>
          <div className="mt-0.5 truncate text-xs text-ink-600">
            {relationshipLabel(c.relationship)}: {c.subjectName} · {wallDate(c.eventDate)}
            {c.isHistorical ? ` · ${t('ya zamani')}` : ''}
          </div>
        </div>
        <div className="figure text-sm text-ink-800">
          {c.headcount > 0 ? `${num(c.perMemberAmount)} × ${c.headcount} = ` : ''}
          <b>{num(c.netAmount)}</b>
        </div>
        {c.status === 'Voting' && (
          <span className="text-xs text-ink-600">
            {t('Ndiyo {0} · Hapana {1}', { 0: c.approvals, 1: c.rejections })}
          </span>
        )}
        <StatusBadge label={st.label} tone={st.tone} />
        <ChevronRight size={16} className="hidden text-ink-500 sm:block" />
      </button>
    </li>
  );
}

// ================================================================ Kufungua kesi (hakiki kwanza)
function CaseFields({
  groupId,
  value,
  onChange,
  selfOnly,
}: {
  groupId: string;
  value: CaseInput;
  onChange: (v: CaseInput) => void;
  /** Mwanachama anaomba kwa ajili yake mwenyewe — hakuna kuchagua mnufaika */
  selfOnly?: boolean;
}) {
  const members = useApi(selfOnly ? null : () => getTruthMembers(groupId), groupId);
  const cat = (Object.keys(CATEGORY) as Category[]).find((k) => CATEGORY[k] === value.category) ?? 'Msiba';
  const rel = (Object.keys(RELATIONSHIP) as Relationship[]).find((k) => RELATIONSHIP[k] === value.relationship) ?? 'Self';
  return (
    <div className="space-y-4">
      <Field label={t('Aina')}>
        <Segmented<Category>
          value={cat}
          onChange={(v) => onChange({ ...value, category: CATEGORY[v] })}
          options={[
            { value: 'Msiba', label: t('Msiba'), hint: t('Kifo') },
            { value: 'Sherehe', label: t('Sherehe'), hint: t('Ndoa / kuzaliwa') },
          ]}
        />
      </Field>
      {!selfOnly && (
        <Field label={t('Mwanachama anayepokea (mnufaika)')}>
          <MemberPicker members={members.data ?? []} value={value.beneficiaryMemberId || null} onChange={(id) => onChange({ ...value, beneficiaryMemberId: id ?? '' })} />
        </Field>
      )}
      <Field label={selfOnly ? t('Tukio linamhusu nani?') : t('Uhusiano wa mhusika na mwanachama')}>
        <Segmented<Relationship>
          stack
          value={rel}
          onChange={(v) => onChange({ ...value, relationship: RELATIONSHIP[v] })}
          options={(Object.keys(RELATIONSHIP) as Relationship[]).map((r) => ({ value: r, label: relationshipLabel(r) }))}
        />
      </Field>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={t('Jina la mhusika')} hint={t('Kama lilivyo kwenye cheti')}>
          <Input value={value.subjectName} onChange={(e) => onChange({ ...value, subjectName: e.target.value })} />
        </Field>
        <Field label={t('Tarehe ya tukio')}>
          <Input type="date" max={todayIso()} value={value.eventDate} onChange={(e) => onChange({ ...value, eventDate: e.target.value })} />
        </Field>
      </div>
      <Field label={t('Maelezo (hiari)')}>
        <Textarea value={value.description ?? ''} onChange={(e) => onChange({ ...value, description: e.target.value })} />
      </Field>
    </div>
  );
}

const EMPTY_CASE: CaseInput = { category: CATEGORY.Msiba, relationship: RELATIONSHIP.Self, beneficiaryMemberId: '', subjectName: '', eventDate: '' };

/**
 * Kufungua kesi. Kiongozi: kwa mwanachama yeyote. Mwanachama (selfMemberId): ombi lake mwenyewe —
 * baada ya kutuma anapakia cheti kwenye kesi yake, kiongozi mwingine anakihakiki, kisha kura.
 */
export function OpenCaseModal({
  open,
  groupId,
  onClose,
  onDone,
  selfMemberId,
}: {
  open: boolean;
  groupId: string;
  onClose: () => void;
  onDone: (id: string) => void;
  selfMemberId?: string;
}) {
  return (
    <Modal open={open} onClose={onClose} title={selfMemberId ? t('Omba mafao (msiba au sherehe)') : t('Fungua kesi ya mafao')}>
      {open && <OpenCaseBody groupId={groupId} onClose={onClose} onDone={onDone} selfMemberId={selfMemberId} />}
    </Modal>
  );
}

function OpenCaseBody({
  groupId,
  onClose,
  onDone,
  selfMemberId,
}: {
  groupId: string;
  onClose: () => void;
  onDone: (id: string) => void;
  selfMemberId?: string;
}) {
  const toast = useToast();
  const [v, setV] = useState<CaseInput>(selfMemberId ? { ...EMPTY_CASE, beneficiaryMemberId: selfMemberId } : EMPTY_CASE);
  const [preview, setPreview] = useState<Assessment | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const ready = !!v.beneficiaryMemberId && v.subjectName.trim().length >= 2 && !!v.eventDate;
  const change = (n: CaseInput) => {
    setV(n);
    setPreview(null); // hesabu ya zamani haihusiani tena
  };

  async function check() {
    setBusy(true);
    setErr(null);
    try {
      setPreview(await benefitsApi.preview(groupId, v));
    } catch (e) {
      setErr(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  }
  async function submit() {
    setBusy(true);
    setErr(null);
    try {
      const r = await benefitsApi.open(groupId, v);
      toast(
        selfMemberId
          ? t('Ombi {0} limetumwa. Sasa pakia cheti chako hapa chini.', { 0: r.caseNumber })
          : t('Kesi {0} imefunguliwa. Pakia cheti sasa.', { 0: r.caseNumber }),
        'good',
      );
      onDone(r.id);
    } catch (e) {
      setErr(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-4">
      {selfMemberId && (
        <Notice tone="neutral">
          {t('Hatua: 1) jaza tukio  2) tuma ombi  3) pakia cheti (kifo, ndoa au kuzaliwa)  4) kiongozi anakihakiki  5) wanachama wanapiga kura  6) malipo kupitia M-Koba.')}
        </Notice>
      )}
      <CaseFields groupId={groupId} value={v} onChange={change} selfOnly={!!selfMemberId} />
      {preview && <AssessmentView a={preview} />}
      {err && <Notice tone="overdue">{err}</Notice>}
      <div className="flex flex-wrap justify-end gap-2 border-t border-ink-100 pt-3">
        <Button variant="secondary" onClick={onClose} disabled={busy}>
          {t('Ghairi')}
        </Button>
        {!preview ? (
          <Button onClick={check} disabled={!ready} loading={busy}>
            {t('Hakiki ustahiki na kiasi')}
          </Button>
        ) : (
          <Button variant="good" onClick={submit} disabled={!preview.eligible} loading={busy}>
            {selfMemberId ? t('Tuma ombi') : t('Fungua kesi')}
          </Button>
        )}
      </div>
    </div>
  );
}

export function AssessmentView({ a }: { a: Assessment }) {
  return (
    <div className="space-y-3 rounded-xl border border-ink-100 bg-paper px-4 py-3">
      <ul className="space-y-1.5 text-sm">
        {a.checks.map((c) => (
          <li key={c.code} className={`flex items-start gap-2 ${c.passed ? 'text-standing-good' : 'text-standing-overdue'}`}>
            {c.passed ? <CheckCircle2 size={15} className="mt-0.5 shrink-0" /> : <XCircle size={15} className="mt-0.5 shrink-0" />}
            <span>{checkText(c)}</span>
          </li>
        ))}
      </ul>
      <dl className="grid grid-cols-2 gap-x-4 gap-y-1 border-t border-ink-100 pt-3 text-sm">
        <dt className="text-ink-600">{t('Kiwango kwa mwanachama')}</dt>
        <dd className="figure text-right">{num(a.perMemberAmount)}</dd>
        <dt className="text-ink-600">{t('Wanaohesabiwa')}</dt>
        <dd className="figure text-right">{a.headcount}</dd>
        <dt className="text-ink-600">{t('Jumla')}</dt>
        <dd className="figure text-right font-semibold">{num(a.gross)}</dd>
        {a.joinFeeDeducted > 0 && (
          <>
            <dt className="text-ink-600">{t('Kianzio kinachokatwa')}</dt>
            <dd className="figure text-right text-standing-warn">−{num(a.joinFeeDeducted)}</dd>
          </>
        )}
        {a.arrearsDeducted > 0 && (
          <>
            <dt className="text-ink-600">{t('Deni la michango linalokatwa')}</dt>
            <dd className="figure text-right text-standing-warn">−{num(a.arrearsDeducted)}</dd>
          </>
        )}
        {a.loanArrearsDeducted > 0 && (
          <>
            <dt className="text-ink-600">{t('Deni la mkopo linalokatwa')}</dt>
            <dd className="figure text-right text-standing-warn">−{num(a.loanArrearsDeducted)}</dd>
          </>
        )}
        <dt className="font-semibold text-ink-900">{t('Atalipwa')}</dt>
        <dd className="figure text-right text-base font-semibold text-ink-950">{tsh(a.net)}</dd>
      </dl>
      <p className="text-xs text-ink-600">
        {t('Kanuni ya idadi:')} {headcountRuleText(a.headcountRule)}
        {a.headcountDate ? ` · ${t('Idadi imehesabiwa tarehe {0}', { 0: wallDate(a.headcountDate) })}` : ''}
      </p>
      {a.acceptedEvidence.length > 0 && (
        <p className="text-xs text-ink-600">
          {t('Vyeti vinavyokubalika:')} {a.acceptedEvidence.map(evidenceLabel).join(', ')}
        </p>
      )}
    </div>
  );
}

// ================================================================ Kesi ya zamani (iliyolipwa kabla ya mfumo)
export interface HistoricalPreset {
  mkobaReference?: string;
  amount?: number;
  date?: string;
  /** Taslimu ya zamani (nje ya M-Koba, kabla ya tarehe ya mwisho ya kumbukumbu za mkononi). */
  cash?: boolean;
}

export function HistoricalCaseModal({
  preset,
  groupId,
  onClose,
  onDone,
}: {
  preset: HistoricalPreset | null;
  groupId: string;
  onClose: () => void;
  onDone: (id: string) => void;
}) {
  return (
    <Modal open={!!preset} onClose={onClose} title={t('Rekodi kesi ya zamani')}>
      {preset && <HistoricalBody preset={preset} groupId={groupId} onClose={onClose} onDone={onDone} />}
    </Modal>
  );
}

function HistoricalBody({ preset, groupId, onClose, onDone }: { preset: HistoricalPreset; groupId: string; onClose: () => void; onDone: (id: string) => void }) {
  const toast = useToast();
  const [v, setV] = useState<CaseInput>({ ...EMPTY_CASE, eventDate: preset.date ?? '' });
  const [ref, setRef] = useState(preset.mkobaReference ?? '');
  const [rate, setRate] = useState<number | null>(null);
  const [headcount, setHeadcount] = useState<number | null>(null);
  const [jf, setJf] = useState<number | null>(0);
  const [arrears, setArrears] = useState<number | null>(0);
  const [loan, setLoan] = useState<number | null>(0);
  const cash = !!preset.cash;
  const [paidAt, setPaidAt] = useState('');
  const [recipient, setRecipient] = useState('');
  const [recipientPhone, setRecipientPhone] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const deductions = (jf ?? 0) + (arrears ?? 0) + (loan ?? 0);
  const gross = (rate ?? 0) * (headcount ?? 0);
  const net = gross - deductions;
  const mismatch = preset.amount !== undefined && gross > 0 && net !== preset.amount;
  const channelOk = cash ? !!paidAt && recipient.trim().length >= 2 && reason.trim().length >= 5 : ref.trim().length >= 6;
  const ready = !!v.beneficiaryMemberId && v.subjectName.trim().length >= 2 && !!v.eventDate && channelOk && !!rate && !!headcount && net >= 0;

  async function submit() {
    setBusy(true);
    setErr(null);
    try {
      const r = await benefitsApi.historical(groupId, {
        ...v,
        mkobaReference: cash ? '' : ref.trim().toUpperCase(),
        perMemberAmount: rate ?? 0,
        headcount: headcount ?? 0,
        joinFeeDeducted: jf ?? 0,
        arrearsDeducted: arrears ?? 0,
        loanArrearsDeducted: loan ?? 0,
        paymentChannel: cash ? PAYMENT_CHANNEL.Cash : PAYMENT_CHANNEL.Mkoba,
        cashPaidAt: cash ? paidAt : null,
        cashRecipientName: cash ? recipient.trim() : null,
        cashRecipientPhone: cash && recipientPhone.trim() ? recipientPhone.trim() : null,
        exceptionReason: cash ? reason.trim() : null,
      });
      toast(
        cash
          ? t('Kesi {0} imerekodiwa. Pakia risiti; kiongozi mwingine aihakiki na wa tatu athibitishe.', { 0: r.caseNumber })
          : t('Kesi {0} imerekodiwa. Kiongozi mwingine athibitishe.', { 0: r.caseNumber }),
        'good',
      );
      onDone(r.id);
    } catch (e) {
      setErr(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-4">
      {cash ? (
        <Notice tone="warn" title={t('Taslimu ya zamani — njia ya kipekee')}>
          {t('Kwa mafao yaliyolipwa kwa TASLIMU kabla ya tarehe ya mwisho ya kumbukumbu za mkononi. Hatua: wewe unarekodi na kupakia risiti → kiongozi wa pili anahakiki risiti → kiongozi wa tatu anathibitisha. Baada ya tarehe hiyo M-Koba ndiyo ukweli.')}
        </Notice>
      ) : (
        <Notice>
          {t('Kwa mafao yaliyokwisha kulipwa kupitia M-Koba kabla ya mfumo huu. Kiongozi WA PILI atathibitisha; cheti kinaweza kupakiwa baadaye.')}
        </Notice>
      )}
      <CaseFields groupId={groupId} value={v} onChange={setV} />
      {cash ? (
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('Tarehe pesa taslimu ilipotolewa')}>
            <Input type="date" value={paidAt} max={todayIso()} onChange={(e) => setPaidAt(e.target.value)} />
          </Field>
          <Field label={t('Aliyepokea pesa (jina)')}>
            <Input value={recipient} onChange={(e) => setRecipient(e.target.value)} maxLength={200} />
          </Field>
          <Field label={t('Simu ya aliyepokea (hiari)')}>
            <Input value={recipientPhone} onChange={(e) => setRecipientPhone(e.target.value)} inputMode="tel" className="figure" />
          </Field>
          <Field label={t('Kwa nini taslimu (nje ya M-Koba)?')}>
            <Textarea rows={2} value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} />
          </Field>
        </div>
      ) : (
        <Field label={t('Ref ya utoaji wa M-Koba')}>
          <Input value={ref} onChange={(e) => setRef(e.target.value)} className="figure uppercase" disabled={!!preset.mkobaReference} />
        </Field>
      )}
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label={t('Kiwango kwa mwanachama')}>
          <MoneyInput value={rate} onChange={setRate} />
        </Field>
        <Field label={t('Idadi ya wanachama waliohesabiwa')}>
          <Input type="number" inputMode="numeric" min={1} value={headcount ?? ''} onChange={(e) => setHeadcount(e.target.value === '' ? null : Number(e.target.value))} />
        </Field>
        <Field label={t('Kianzio kilichokatwa')}>
          <MoneyInput value={jf} onChange={setJf} />
        </Field>
        <Field label={t('Deni la michango lililokatwa')}>
          <MoneyInput value={arrears} onChange={setArrears} />
        </Field>
        <Field label={t('Deni la mkopo lililokatwa')}>
          <MoneyInput value={loan} onChange={setLoan} />
        </Field>
      </div>
      <div className={`rounded-xl px-4 py-3 text-sm ${mismatch ? 'bg-standing-overdue-bg text-standing-overdue' : 'bg-ink-50 text-ink-800'}`}>
        <span className="figure">
          {num(rate)} × {headcount ?? 0} = {num(gross)} − {num(deductions)} = <b>{num(net)}</b>
        </span>
        {preset.amount !== undefined && (
          <div className="mt-1 text-xs">
            {mismatch
              ? t('Hailingani na kiasi kilichotoka M-Koba ({0}). Seva itakataa.', { 0: num(preset.amount) })
              : t('Inalingana na M-Koba ({0}).', { 0: num(preset.amount) })}
          </div>
        )}
      </div>
      {err && <Notice tone="overdue">{err}</Notice>}
      <div className="flex flex-wrap justify-end gap-2 border-t border-ink-100 pt-3">
        <Button variant="secondary" onClick={onClose} disabled={busy}>
          {t('Ghairi')}
        </Button>
        <Button onClick={submit} disabled={!ready} loading={busy}>
          {t('Rekodi kwa uthibitisho')}
        </Button>
      </div>
    </div>
  );
}
