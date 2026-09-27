import { useEffect, useState } from 'react';
import { Check, Clock, HandCoins, UserCheck, X } from 'lucide-react';
import {
  apiErrorMessage,
  decideManualPayment,
  evidenceLabel,
  previewPayment,
  type ManualPaymentItem,
  type PaymentPreview,
} from '../../api/truth';
import { StatusBadge } from '../../components/StatusBadge';
import { Button, DecisionModal, EmptyState, Loading, Notice } from '../../components/ui';
import { useToast } from '../../lib/toast';
import { PaymentPreviewPanel } from '../../components/truth/PaymentPreviewPanel';
import { num, realDateTime, requestStatus, wallDate } from '../../lib/truth';
import { useTruth } from './TruthContext';

import { t } from '../../i18n';
/** Orodha ya maombi ya malipo ya mkononi, pamoja na idhini/kukataa (maker-checker). */
export function ManualRequestsList({
  items,
  onChanged,
  emptyText = t("Hakuna maombi bado."),
}: {
  items: ManualPaymentItem[];
  onChanged: () => void;
  emptyText?: string;
}) {
  const [decide, setDecide] = useState<{ item: ManualPaymentItem; approve: boolean } | null>(null);
  const { groupId, openMember } = useTruth();
  const toast = useToast();

  if (items.length === 0) return <EmptyState icon={HandCoins} title={emptyText} />;

  return (
    <>
      <ul className="space-y-2.5">
        {items.map((it) => {
          const st = requestStatus(it.status);
          return (
            <li key={it.id} className="rounded-2xl border border-ink-100 bg-paper-raised p-3 sm:p-4">
              <div className="flex flex-wrap items-start gap-3">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <button
                      onClick={() => it.memberId && openMember(it.memberId)}
                      className="truncate font-semibold text-ink-950 hover:underline"
                    >
                      {it.memberName}
                    </button>
                    <span
                      className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${
                        it.kind === 'JoinFee' ? 'bg-gold-100 text-gold-700' : 'bg-ink-50 text-ink-700'
                      }`}
                    >
                      {it.kind === 'JoinFee' ? t("Kianzio") : t("Mchango / deni")}
                    </span>
                    <StatusBadge label={st.label} tone={st.tone} />
                  </div>
                  <div className="mt-1 text-xs text-ink-600">
                    {t("Malipo ya")}{' '}{wallDate(it.occurredAt)}{' '}{t("· aliingiza")}{' '}<b className="text-ink-800">{it.createdByName}</b>{' '}
                    {realDateTime(it.createdAt)}
                  </div>
                  {it.reason && <p className="mt-1.5 text-sm text-ink-800">“{it.reason}”</p>}
                  <p className="mt-1 text-xs text-ink-700">
                    {t("Ushahidi:")}{' '}<b>{evidenceLabel(it.evidenceType)}</b>
                    {it.evidenceReference ? ` — ${it.evidenceReference}` : ''}
                  </p>
                  {it.status !== 'Pending' && it.approvedByName && (
                    <p className="mt-1 text-xs text-ink-600">
                      {it.status === 'Approved' ? t("Aliidhinisha") : t("Alikataa")}: <b>{it.approvedByName}</b> {realDateTime(it.approvedAt)}
                      {it.approvalNote ? ` — “${it.approvalNote}”` : ''}
                    </p>
                  )}
                </div>
                <div className="figure text-right text-lg font-semibold text-ink-950">{num(it.amount)}</div>
              </div>

              {it.status === 'Pending' && (
                <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-ink-100 pt-3">
                  {it.canDecide ? (
                    <>
                      <Button size="sm" variant="good" icon={Check} onClick={() => setDecide({ item: it, approve: true })}>
                        {t("Kagua & idhinisha")}
                      </Button>
                      <Button size="sm" variant="secondary" icon={X} onClick={() => setDecide({ item: it, approve: false })}>
                        {t("Kataa")}
                      </Button>
                    </>
                  ) : (
                    <span className="inline-flex items-center gap-1.5 text-xs text-ink-600">
                      <Clock size={13} />
                      {t("Uliingiza wewe — kiongozi")}{' '}<b>{t("mwingine")}</b>{' '}{t("ndiye ataidhinisha (maker-checker).")}
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
        summary={decide && <DecisionSummary item={decide.item} approve={decide.approve} />}
        onConfirm={async (note) => {
          if (!decide) return;
          try {
            await decideManualPayment(groupId, decide.item.id, decide.approve, note || undefined);
            toast(decide.approve ? t("Imeidhinishwa: {0} kwa {1}", { 0: num(decide.item.amount), 1: decide.item.memberName }) : t("Ombi limekataliwa"), 'good');
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

/** Mkaguzi (checker) naye anaona matokeo KABLA ya kuidhinisha. */
function DecisionSummary({ item, approve }: { item: ManualPaymentItem; approve: boolean }) {
  const { groupId } = useTruth();
  const [pv, setPv] = useState<PaymentPreview | null>(null);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (!approve || !item.memberId) return;
    let cancelled = false;
    previewPayment(groupId, {
      memberId: item.memberId,
      amount: item.amount,
      occurredAt: item.occurredAt.slice(0, 19), // wall-clock EAT
      isJoinFee: item.kind === 'JoinFee',
    })
      .then((p) => !cancelled && setPv(p))
      .catch((e) => !cancelled && setErr(apiErrorMessage(e)));
    return () => {
      cancelled = true;
    };
  }, [approve, item, groupId]);

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2">
        <UserCheck size={16} className="text-ink-600" />
        <span>
          <b>{item.memberName}</b> · {item.kind === 'JoinFee' ? t("Kianzio") : t("Mchango / deni")} ·{' '}
          <span className="figure font-semibold">{num(item.amount)}</span> · {wallDate(item.occurredAt)}
        </span>
      </div>
      {item.reason && <div className="text-xs text-ink-700">{t("Maelezo: “")}{item.reason}”</div>}
      <div className={`text-xs ${item.evidenceType ? 'text-ink-700' : 'text-standing-warn'}`}>
        {t("Ushahidi:")}{' '}<b>{evidenceLabel(item.evidenceType)}</b>
        {item.evidenceReference ? ` — ${item.evidenceReference}` : t(" (rekodi ya zamani bila ushahidi uliopangwa — hakikisha kabla ya kuidhinisha)")}
      </div>
      {approve && (
        <div className="rounded-lg bg-paper-raised p-3">
          <div className="mb-2 text-xs font-semibold text-ink-900">{t("Matokeo ukiidhinisha")}</div>
          {err && <Notice tone="overdue">{err}</Notice>}
          {!pv && !err && <Loading label={t("Inahesabu...")} />}
          {pv && <PaymentPreviewPanel preview={pv} amount={item.amount} />}
        </div>
      )}
    </div>
  );
}
