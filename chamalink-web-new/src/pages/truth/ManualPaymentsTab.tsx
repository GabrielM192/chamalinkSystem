import { useEffect, useMemo, useState } from 'react';
import { Eye, HandCoins, Info, Loader2, Send, ShieldCheck } from 'lucide-react';
import {
  apiErrorMessage,
  EVIDENCE_TYPES,
  listManualPayments,
  previewPayment,
  requestManualPayment,
  type EvidenceType,
  type ManualPaymentItem,
  type PaymentPreview,
} from '../../api/truth';
import { MemberPicker } from '../../components/truth/MemberPicker';
import { PaymentPreviewPanel } from '../../components/truth/PaymentPreviewPanel';
import { Button, Card, Field, Input, MoneyInput, Notice, Segmented, Select, Skeleton, Textarea } from '../../components/ui';
import { useToast } from '../../lib/toast';
import { isoDateOnly, num, todayEat, wallDate } from '../../lib/truth';
import { ManualRequestsList } from './ManualRequests';
import { useTruth } from './TruthContext';

import { t, tk } from '../../i18n';
type Kind = 'joinfee' | 'payment';

const REASON_CHIPS = [
  tk('Pesa taslimu kwa Mtunza Hazina'),
  tk('Kabla ya kikundi kuanza kutumia M-Koba'),
  tk('Imethibitishwa kwenye daftari la kikundi'),
  tk('Risiti Na. '),
];

/**
 * Malipo ya mkononi (historia ya kabla ya M-Koba). Mtiririko:
 *  1. Chagua mwanachama  2. Aina  3. Kiasi  4. Tarehe  5. Ushahidi
 *  -> hakikisho LA MOJA KWA MOJA kutoka seva (bila kuhifadhi)
 *  -> Wasilisha: linasubiri kiongozi MWINGINE aidhinishe.
 */
export function ManualPaymentsTab({ initialMemberId }: { initialMemberId: string | null }) {
  const { groupId, members, settings, version, refresh } = useTruth();
  const toast = useToast();

  const [memberId, setMemberId] = useState<string | null>(
    initialMemberId && members.some((m) => m.memberId === initialMemberId) ? initialMemberId : null
  );
  const [kind, setKind] = useState<Kind>('joinfee');
  const [amount, setAmount] = useState<number | null>(null);
  const cutoff = isoDateOnly(settings?.manualEntryCutoff);
  const maxDate = cutoff && cutoff < todayEat() ? cutoff : todayEat();
  const [date, setDate] = useState<string>(maxDate);
  const [time, setTime] = useState('12:00');
  const [reason, setReason] = useState('');
  const [evidenceType, setEvidenceType] = useState<EvidenceType | ''>('');
  const [evidenceRef, setEvidenceRef] = useState('');

  const [pv, setPv] = useState<{ sig: string; memberId: string; data?: PaymentPreview; err?: string } | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const [items, setItems] = useState<ManualPaymentItem[] | null>(null);
  const [listFilter, setListFilter] = useState<'pending' | 'all'>('pending');

  const occurredAt = date ? `${date}T${time || '12:00'}:00` : '';

  // Hakikisho la moja kwa moja (debounced). Linaitwa hata kiasi kikiwa 0 ili kuonyesha hali ya sasa.
  const sig = memberId && occurredAt ? JSON.stringify([memberId, amount ?? 0, occurredAt, kind, version]) : null;
  useEffect(() => {
    if (!sig || !memberId) return;
    let cancelled = false;
    const h = setTimeout(() => {
      previewPayment(groupId, { memberId, amount: amount ?? 0, occurredAt, isJoinFee: kind === 'joinfee' })
        .then((data) => !cancelled && setPv({ sig, memberId, data }))
        .catch((e) => !cancelled && setPv({ sig, memberId, err: apiErrorMessage(e) }));
    }, 350);
    return () => {
      cancelled = true;
      clearTimeout(h);
    };
  }, [sig, groupId, memberId, amount, occurredAt, kind]);

  // Onyesha hakikisho la mwanachama HUYU tu (la zamani linabaki likififia wakati jipya linakuja)
  const sameMember = !!pv && pv.memberId === memberId;
  const preview = sameMember ? (pv.data ?? null) : null;
  const previewErr = sameMember && pv.sig === sig ? (pv.err ?? null) : null;
  const previewing = !!sig && pv?.sig !== sig;

  useEffect(() => {
    let cancelled = false;
    listManualPayments(groupId)
      .then((d) => !cancelled && setItems(d))
      .catch((e) => !cancelled && toast(apiErrorMessage(e), 'overdue'));
    return () => {
      cancelled = true;
    };
  }, [groupId, version, toast]);

  const member = members.find((m) => m.memberId === memberId) ?? null;
  const before = preview?.before;
  const quick = useMemo(() => {
    const s = settings;
    const base =
      kind === 'joinfee'
        ? // Vitufe vya haraka vinatokana na kanuni za kikundi (hakuna kiasi kilichoandikwa kwenye code)
          [before?.joinFeeOutstanding ?? 0, s?.monthlyContribution ?? 0, (s?.monthlyContribution ?? 0) * 2, s?.joiningFee ?? 0]
        : [s?.monthlyContribution ?? 0, (s?.monthlyContribution ?? 0) + (s?.lateFine ?? 0), before?.arrears ?? 0];
    return [...new Set(base.filter((x) => x > 0))].sort((a, b) => a - b);
  }, [kind, before, settings]);

  // Ukiwa amount tupu, "Kiasi lazima..." si kosa la kuonyesha kwa nguvu
  const problems = (preview?.problems ?? []).filter((p) => (amount ? true : !p.startsWith('Kiasi')));
  const reasonOk = reason.trim().length >= 5;
  const evidenceOk = evidenceType !== '' && evidenceRef.trim().length >= 3;
  const canSubmit = !!memberId && !!amount && !!preview?.valid && reasonOk && evidenceOk && !previewing && !submitting;
  const example = EVIDENCE_TYPES.find((e) => e.value === evidenceType)?.example;
  const evidenceExample = example ? t('Mf. {0}', { 0: t(example) }) : t('Chagua aina ya ushahidi kwanza');

  async function submit() {
    if (!memberId || !amount) return;
    setSubmitting(true);
    try {
      if (evidenceType === '') return;
      await requestManualPayment(groupId, {
        memberId,
        amount,
        occurredAt,
        isJoinFee: kind === 'joinfee',
        reason: reason.trim(),
        evidenceType,
        evidenceReference: evidenceRef.trim(),
      });
      toast(t("Ombi limewasilishwa: {0} kwa {1}. Linasubiri kiongozi mwingine.", { 0: num(amount), 1: member?.fullName }), 'good');
      setAmount(null);
      setReason('');
      setEvidenceType('');
      setEvidenceRef('');
      setListFilter('pending');
      refresh();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setSubmitting(false);
    }
  }

  if (!settings) return null;
  const shown = (items ?? []).filter((i) => listFilter === 'all' || i.status === 'Pending');

  return (
    <div className="space-y-6">
      <Notice tone="neutral" title={t("Ni kwa historia ya kabla ya M-Koba tu")}>
        {t("Tumia hapa kwa pesa iliyolipwa taslimu/nje ya M-Koba hadi")}{' '}
        <b>{settings.manualEntryCutoff ? wallDate(settings.manualEntryCutoff) : t("— (weka tarehe kwenye Kanuni)")}</b>{t(". Baada ya hapo M-Koba pekee ndiyo ukweli. Kila ombi linahitaji idhini ya kiongozi mwingine.")}
      </Notice>

      <div className="grid gap-5 lg:grid-cols-12">
        {/* ------------------------- FOMU ------------------------- */}
        <Card title={t("Ingiza malipo")} subtitle={t("Hatua 5 — hakikisho linaonekana upande wa pili")} className="lg:col-span-5 lg:self-start">
          <div className="space-y-5">
            <Step n={1} label={t("Mwanachama")}>
              <MemberPicker members={members} value={memberId} onChange={setMemberId} />
              {preview && memberId && (
                <div className="figure mt-2 flex flex-wrap gap-1.5 text-[11px]">
                  <Pill tone={preview.before.arrears > 0 ? 'overdue' : 'good'}>{t("Deni")}{' '}{num(preview.before.arrears)}</Pill>
                  <Pill tone={preview.before.joinFeeOutstanding > 0 ? 'warn' : 'good'}>
                    {t("Kianzio kinadaiwa")}{' '}{num(preview.before.joinFeeOutstanding)}
                  </Pill>
                  {preview.before.savings > 0 && <Pill tone="neutral">{t("Akiba")}{' '}{num(preview.before.savings)}</Pill>}
                  {preview.pendingRequestsForMember > 0 && (
                    <Pill tone="warn">{t("Maombi")}{' '}{preview.pendingRequestsForMember}{' '}{t("yanasubiri")}</Pill>
                  )}
                </div>
              )}
            </Step>

            <Step n={2} label={t("Aina ya malipo")}>
              <Segmented<Kind>
                value={kind}
                onChange={setKind}
                options={[
                  { value: 'joinfee', label: t("Kianzio"), hint: t("Inaenda Kianzio pekee") },
                  { value: 'payment', label: t("Mchango / deni / mkopo"), hint: t("Mkopo → deni la zamani → Kianzio → akiba") },
                ]}
              />
            </Step>

            <Step n={3} label={t("Kiasi")}>
              <MoneyInput value={amount} onChange={setAmount} quick={quick} />
            </Step>

            <Step n={4} label={t("Tarehe ya malipo")}>
              <div className="grid grid-cols-[1fr_7rem] gap-2">
                <Input type="date" value={date} max={maxDate} onChange={(e) => setDate(e.target.value)} />
                <Input type="time" value={time} onChange={(e) => setTime(e.target.value)} aria-label={t("Saa")} />
              </div>
              <p className="mt-1.5 text-xs text-ink-600">
                {t("Tarehe halisi pesa ilipopokelewa — inaamua kama mwezi ulilipwa kwa wakati (bila faini).")}
              </p>
            </Step>

            <Step n={5} label={t("Maelezo")}>
              <Textarea
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                placeholder={t("Mf. Kianzio taslimu kwa Mtunza Hazina")}
              />
              <div className="mt-2 flex flex-wrap gap-1.5">
                {REASON_CHIPS.map((key) => t(key)).map((c) => (
                  <button
                    key={c}
                    type="button"
                    onClick={() => setReason((r) => (r ? `${r.trim()}; ${c}` : c))}
                    className="rounded-full border border-ink-100 bg-paper-raised px-2.5 py-1 text-xs text-ink-700 hover:border-ink-500"
                  >
                    + {c.trim()}
                  </button>
                ))}
              </div>
            </Step>

            <Step n={6} label={t("Ushahidi (lazima)")}>
              <div className="grid gap-2 sm:grid-cols-[minmax(0,14rem)_minmax(0,1fr)]">
                <Select
                  aria-label={t("Aina ya ushahidi")}
                  value={evidenceType}
                  onChange={(e) => setEvidenceType(e.target.value as EvidenceType | '')}
                >
                  <option value="">{t("— Aina ya ushahidi —")}</option>
                  {EVIDENCE_TYPES.map((ev) => (
                    <option key={ev.value} value={ev.value}>
                      {t(ev.label)}
                    </option>
                  ))}
                </Select>
                <Input
                  aria-label={t("Ushahidi ulipo")}
                  value={evidenceRef}
                  maxLength={200}
                  onChange={(e) => setEvidenceRef(e.target.value)}
                  placeholder={evidenceExample}
                  disabled={evidenceType === ''}
                />
              </div>
              <p className="mt-1.5 text-xs text-ink-600">
                {t("Mkaguzi lazima aweze kuupata ushahidi huu. Kiongozi mwingine ataukagua kabla ya kuidhinisha.")}
              </p>
            </Step>
          </div>
        </Card>

        {/* ------------------------- HAKIKISHO ------------------------- */}
        <Card
          className="lg:col-span-7 lg:sticky lg:top-4 lg:self-start"
          title={
            <span className="inline-flex items-center gap-2">
              <Eye size={16} />{' '}{t("Hakikisho — nini kitatokea")}
            </span>
          }
          subtitle={t("Imehesabiwa na injini kwa historia yote ya mwanachama. Hakuna kilichohifadhiwa bado.")}
          action={previewing ? <Loader2 size={16} className="animate-spin text-ink-500" /> : undefined}
        >
          {!memberId ? (
            <Placeholder text={t("Chagua mwanachama ili kuona hali yake na jinsi malipo yatakavyogawanywa.")} />
          ) : previewErr ? (
            <Notice tone="overdue">{previewErr}</Notice>
          ) : !preview ? (
            <div className="space-y-3">
              <Skeleton className="h-4 w-1/3" />
              <Skeleton className="h-3" />
              <Skeleton className="h-24" />
            </div>
          ) : (
            <div className={`space-y-5 transition-opacity ${previewing ? 'opacity-60' : ''}`}>
              {problems.length > 0 && (
                <Notice tone="overdue" title={t("Haiwezi kuwasilishwa bado")}>
                  <ul className="list-disc space-y-0.5 pl-4">
                    {problems.map((p, i) => (
                      <li key={i}>{p}</li>
                    ))}
                  </ul>
                </Notice>
              )}
              {!amount ? (
                <Placeholder text={t("Weka kiasi ili kuona pesa itakwenda wapi.")} />
              ) : (
                <PaymentPreviewPanel preview={preview} amount={amount} />
              )}

              <div className="space-y-3 border-t border-ink-100 pt-4">
                <div className="flex items-start gap-2 text-xs text-ink-600">
                  <ShieldCheck size={15} className="mt-0.5 shrink-0 text-standing-good" />
                  <span>
                    {t("Ukiwasilisha, ombi")}{' '}<b>{t("halitabadilisha")}</b>{' '}{t("taarifa mpaka kiongozi mwingine aliidhinishe. Yeye pia ataona hakikisho hili.")}
                  </span>
                </div>
                {amount && preview.valid && (!reasonOk || !evidenceOk) ? (
                  <div className="flex items-center gap-2 text-xs text-standing-warn">
                    <Info size={14} />
                    {!reasonOk ? t("Andika maelezo (hatua ya 5)") : t("Weka aina na mahali pa ushahidi (hatua ya 6)")}{' '}{t("ili kuwasilisha.")}
                  </div>
                ) : null}
                <Button onClick={submit} disabled={!canSubmit} loading={submitting} icon={Send} className="w-full sm:w-auto">
                  {t("Wasilisha kwa idhini")}{amount ? ` · ${num(amount)}` : ''}
                </Button>
              </div>
            </div>
          )}
        </Card>
      </div>

      {/* ------------------------- MAOMBI ------------------------- */}
      <div className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="flex items-center gap-2 font-semibold text-ink-950">
            <HandCoins size={17} />{' '}{t("Maombi ya malipo ya mkononi")}
          </h2>
          <div className="flex gap-1.5">
            {(
              [
                ['pending', t('Yanasubiri ({0})', { 0: (items ?? []).filter((i) => i.status === 'Pending').length })],
                ['all', t('Yote ({0})', { 0: (items ?? []).length })],
              ] as const
            ).map(([f, l]) => (
              <button
                key={f}
                onClick={() => setListFilter(f)}
                className={`rounded-full px-3 py-1.5 text-xs font-medium ${
                  listFilter === f ? 'bg-ink-900 text-ink-50' : 'bg-paper-raised text-ink-700 ring-1 ring-ink-100'
                }`}
              >
                {l}
              </button>
            ))}
          </div>
        </div>
        {items === null ? (
          <Skeleton className="h-24" />
        ) : (
          <ManualRequestsList
            items={shown}
            onChanged={refresh}
            emptyText={listFilter === 'pending' ? t("Hakuna ombi linalosubiri") : t("Hakuna maombi bado")}
          />
        )}
      </div>
    </div>
  );
}

function Step({ n, label, children }: { n: number; label: string; children: React.ReactNode }) {
  return (
    <Field
      label={
        <span className="flex items-center gap-2">
          <span className="figure flex h-5 w-5 items-center justify-center rounded-full bg-ink-900 text-[11px] font-bold text-ink-50">{n}</span>
          {label}
        </span>
      }
    >
      {children}
    </Field>
  );
}

function Pill({ tone, children }: { tone: 'good' | 'warn' | 'overdue' | 'neutral'; children: React.ReactNode }) {
  const cls =
    tone === 'good'
      ? 'bg-standing-good-bg text-standing-good'
      : tone === 'warn'
        ? 'bg-standing-warn-bg text-standing-warn'
        : tone === 'overdue'
          ? 'bg-standing-overdue-bg text-standing-overdue'
          : 'bg-ink-50 text-ink-700';
  return <span className={`rounded-full px-2 py-0.5 font-medium ${cls}`}>{children}</span>;
}

function Placeholder({ text }: { text: string }) {
  return (
    <div className="flex flex-col items-center justify-center rounded-xl border border-dashed border-ink-100 px-6 py-12 text-center text-sm text-ink-600">
      <Eye size={22} className="mb-2 text-ink-500" />
      {text}
    </div>
  );
}
