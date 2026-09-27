import { useEffect, useMemo, useState } from 'react';
import { BadgePercent, Check, Clock, Send, X } from 'lucide-react';
import {
  apiErrorMessage,
  decideAdjustment,
  listAdjustments,
  requestAdjustment,
  type AdjustmentItem,
  type AdjustmentKind,
} from '../../api/truth';
import { MemberPicker } from '../../components/truth/MemberPicker';
import { StatusBadge } from '../../components/StatusBadge';
import { Button, Card, DecisionModal, EmptyState, Field, Input, Segmented, Select, Skeleton, Textarea } from '../../components/ui';
import { useToast } from '../../lib/toast';
import { monthName, realDateTime, requestStatus, todayEat } from '../../lib/truth';
import { useTruth } from './TruthContext';

import { t } from '../../i18n';
/** Msamaha wa faini (mwanachama mmoja) na siku za neema (mwanachama au kikundi kizima) — maker-checker. */
export function AdjustmentsTab({ initialMemberId }: { initialMemberId: string | null }) {
  const { groupId, members, settings, version, refresh } = useTruth();
  const toast = useToast();
  const [kind, setKind] = useState<AdjustmentKind>('FineWaiver');
  const [memberId, setMemberId] = useState<string | null>(
    initialMemberId && members.some((m) => m.memberId === initialMemberId) ? initialMemberId : null
  );
  const months = useMemo(() => monthOptions(settings?.contributionStartDate), [settings]);
  const [period, setPeriod] = useState(months[months.length - 1]?.value ?? '');
  const [days, setDays] = useState(3);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [items, setItems] = useState<AdjustmentItem[] | null>(null);

  useEffect(() => {
    let cancelled = false;
    listAdjustments(groupId)
      .then((d) => !cancelled && setItems(d))
      .catch((e) => !cancelled && toast(apiErrorMessage(e), 'overdue'));
    return () => {
      cancelled = true;
    };
  }, [groupId, version, toast]);

  const needsMember = kind === 'FineWaiver';
  const valid = (!needsMember || !!memberId) && !!period && reason.trim().length >= 5 && (kind === 'FineWaiver' || (days >= 1 && days <= 25));
  const [y, m] = period.split('-').map(Number);

  async function submit() {
    setBusy(true);
    try {
      await requestAdjustment(groupId, { memberId, kind, year: y, month: m, extraDays: kind === 'GraceExtension' ? days : 0, reason: reason.trim() });
      toast(t("Ombi limewasilishwa — linasubiri kiongozi mwingine."), 'good');
      setReason('');
      refresh();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="grid gap-5 lg:grid-cols-12">
      <Card title={t("Ombi jipya")} className="lg:col-span-5 lg:self-start">
        <div className="space-y-4">
          <Segmented<AdjustmentKind>
            value={kind}
            onChange={(k) => {
              setKind(k);
              if (k === 'FineWaiver' && memberId === null && initialMemberId) setMemberId(initialMemberId);
            }}
            options={[
              { value: 'FineWaiver', label: t("Msamaha wa faini"), hint: t("Faini ya mwezi mmoja") },
              { value: 'GraceExtension', label: t("Siku za neema"), hint: t("Kuongeza muda wa kulipa") },
            ]}
          />
          <Field label={t("Mwanachama")} hint={needsMember ? undefined : t("Acha “Wanachama wote” kwa kikundi kizima.")}>
            <MemberPicker members={members} value={memberId} onChange={setMemberId} allowAll={needsMember ? undefined : t('Wanachama wote')} />
          </Field>
          <div className={`grid gap-3 ${kind === 'GraceExtension' ? 'grid-cols-2' : 'grid-cols-1'}`}>
            <Field label={t("Mwezi wa mchango")}>
              <Select value={period} onChange={(e) => setPeriod(e.target.value)}>
                {months.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </Select>
            </Field>
            {kind === 'GraceExtension' && (
              <Field label={t("Siku za ziada")}>
                <Input type="number" min={1} max={25} value={days} onChange={(e) => setDays(Number(e.target.value))} />
              </Field>
            )}
          </div>
          {period && settings && (
            <p className="text-xs text-ink-600">
              {kind === 'FineWaiver'
                ? t("Faini ya {0} haitatozwa; mchango wenyewe ({1}) bado unadaiwa.", { 0: monthName(m, y), 1: settings.monthlyContribution.toLocaleString('en-US') })
                : t("Mwisho wa kulipa mchango wa {0} utasogezwa mbele siku {1} kutoka tarehe {2} {3}.", { 0: monthName(m, y), 1: days, 2: settings.dueDateDay + settings.gracePeriodDays, 3: monthName(m === 12 ? 1 : m + 1) })}
            </p>
          )}
          <Field label={t("Sababu (itaonekana kwenye ukaguzi)")}>
            <Textarea value={reason} onChange={(e) => setReason(e.target.value)} placeholder={t("Mf. Uamuzi wa kikao cha 12/04/2026")} />
          </Field>
          <Button onClick={submit} disabled={!valid} loading={busy} icon={Send} className="w-full sm:w-auto">
            {t("Wasilisha kwa idhini")}
          </Button>
        </div>
      </Card>

      <div className="space-y-3 lg:col-span-7">
        <h2 className="flex items-center gap-2 font-semibold text-ink-950">
          <BadgePercent size={17} />{' '}{t("Maombi yote")}
        </h2>
        {items === null ? <Skeleton className="h-24" /> : <AdjustmentList items={items} onChanged={refresh} />}
      </div>
    </div>
  );
}

export function AdjustmentList({ items, onChanged }: { items: AdjustmentItem[]; onChanged: () => void }) {
  const { groupId, openMember } = useTruth();
  const toast = useToast();
  const [decide, setDecide] = useState<{ item: AdjustmentItem; approve: boolean } | null>(null);

  if (items.length === 0) return <EmptyState icon={BadgePercent} title={t("Hakuna maombi ya msamaha au neema")} />;
  return (
    <>
      <ul className="space-y-2.5">
        {items.map((a) => {
          const st = requestStatus(a.status);
          return (
            <li key={a.id} className="rounded-2xl border border-ink-100 bg-paper-raised p-3 sm:p-4">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-semibold text-ink-950">{a.kind === 'FineWaiver' ? t("Msamaha wa faini") : t("Neema +siku {0}", { 0: a.extraDays })}</span>
                <span className="text-sm text-ink-700">· {monthName(a.month, a.year)}</span>
                <StatusBadge label={st.label} tone={st.tone} />
              </div>
              <div className="mt-1 text-sm">
                {a.groupMemberId ? (
                  <button onClick={() => openMember(a.groupMemberId!)} className="font-medium text-ink-900 hover:underline">
                    {a.memberName}
                  </button>
                ) : (
                  <span className="font-medium text-gold-700">{t("Wanachama wote")}</span>
                )}
              </div>
              <p className="mt-1 text-sm text-ink-800">“{a.reason}”</p>
              <p className="mt-1 text-xs text-ink-600">
                {t("Aliomba")}{' '}<b>{a.requestedByName}</b> {realDateTime(a.requestedAt)}
                {a.decidedByName && (
                  <>
                    {' '}
                    · {a.status === 'Approved' ? t('aliidhinisha') : t('alikataa')} <b>{a.decidedByName}</b> {realDateTime(a.decidedAt)}
                  </>
                )}
              </p>
              {a.status === 'Pending' && (
                <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-ink-100 pt-3">
                  {a.canDecide ? (
                    <>
                      <Button size="sm" variant="good" icon={Check} onClick={() => setDecide({ item: a, approve: true })}>
                        {t("Idhinisha")}
                      </Button>
                      <Button size="sm" variant="secondary" icon={X} onClick={() => setDecide({ item: a, approve: false })}>
                        {t("Kataa")}
                      </Button>
                    </>
                  ) : (
                    <span className="inline-flex items-center gap-1.5 text-xs text-ink-600">
                      <Clock size={13} />{' '}{t("Uliomba wewe — kiongozi mwingine ataamua.")}
                    </span>
                  )}
                </div>
              )}
            </li>
          );
        })}
      </ul>
      <DecisionModal
        open={!!decide}
        approve={decide?.approve ?? true}
        onClose={() => setDecide(null)}
        summary={
          decide && (
            <div>
              <b>{decide.item.kind === 'FineWaiver' ? t("Msamaha wa faini") : t("Neema ya siku {0}", { 0: decide.item.extraDays })}</b> — {decide.item.memberName},{' '}
              {monthName(decide.item.month, decide.item.year)}
              <div className="mt-1 text-xs">“{decide.item.reason}”</div>
            </div>
          )
        }
        onConfirm={async (note) => {
          if (!decide) return;
          try {
            await decideAdjustment(groupId, decide.item.id, decide.approve, note || undefined);
            toast(decide.approve ? t("Imeidhinishwa — taarifa zimehesabiwa upya.") : t("Imekataliwa."), 'good');
            setDecide(null);
            onChanged();
          } catch (e) {
            toast(apiErrorMessage(e), 'overdue');
          }
        }}
      />
    </>
  );
}

function monthOptions(start: string | undefined) {
  const out: { value: string; label: string }[] = [];
  const [ty, tm] = todayEat().split('-').map(Number);
  let y = start ? Number(start.slice(0, 4)) : ty;
  let m = start ? Number(start.slice(5, 7)) : tm;
  while (y < ty || (y === ty && m <= tm)) {
    out.push({ value: `${y}-${m}`, label: monthName(m, y) });
    m++;
    if (m > 12) {
      m = 1;
      y++;
    }
  }
  return out;
}
