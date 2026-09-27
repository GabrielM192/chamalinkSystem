import { useEffect, useState } from 'react';
import { Check, Clock, FileWarning, Link2, X } from 'lucide-react';
import { apiErrorMessage, decideGap, listGaps, resolveGap, type GapItem } from '../../api/truth';
import { MemberPicker } from '../../components/truth/MemberPicker';
import { GapChain } from '../../components/truth/GapChain';
import { StatusBadge } from '../../components/StatusBadge';
import { Button, DecisionModal, EmptyState, Field, Modal, Notice, Skeleton, Textarea } from '../../components/ui';
import { useToast } from '../../lib/toast';
import { num, requestStatus, tsh, wallDateTime } from '../../lib/truth';
import { useTruth } from './TruthContext';

import { t } from '../../i18n';
/**
 * Mapengo: balance ya M-Koba inaonyesha pesa iliingia, lakini mstari wake haupo
 * kwenye PDF. Viongozi wanamtambua mwenyewe (kwa ushahidi, mf. SMS ya M-Pesa),
 * kiongozi mwingine anathibitisha — kisha pesa inahesabiwa kwake.
 */
export function GapsTab() {
  const { groupId, version, refresh } = useTruth();
  const [gaps, setGaps] = useState<GapItem[] | null>(null);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    listGaps(groupId)
      .then((g) => !cancelled && setGaps(g))
      .catch((e) => !cancelled && setErr(apiErrorMessage(e)));
    return () => {
      cancelled = true;
    };
  }, [groupId, version]);

  if (err) return <Notice tone="overdue">{err}</Notice>;
  if (!gaps) return <Skeleton className="h-32" />;

  const open = gaps.filter((g) => g.status === 'Unresolved' || g.status === 'Rejected').reduce((s, g) => s + Math.max(0, g.amount), 0);

  return (
    <div className="space-y-4">
      <Notice tone="neutral" title={t("Pengo ni nini?")}>
        {t("Kila muamala wa M-Koba una balance. Balance ya muamala uliotangulia + kiasi cha muamala unaofuata inapaswa kuwa sawa na balance ya muamala unaofuata. Ikiruka, pesa iliingia (au ilitoka)")}{' '}<b>{t("kati")}</b>{' '}{t("ya miamala hiyo miwili lakini mstari wake haupo kwenye PDF. Miamala ya pande zote mbili yenyewe ni sahihi na imeshahesabiwa. Pesa ya pengo haihesabiwi kwa mtu yeyote mpaka mwenyewe atambuliwe kwa ushahidi na viongozi wawili.")}
      </Notice>
      {gaps.length === 0 ? (
        <EmptyState icon={FileWarning} title={t("Hakuna mapengo")}>
          {t("Mnyororo wa balance wa PDF zote umekamilika.")}
        </EmptyState>
      ) : (
        <>
          {open > 0 && (
            <div className="text-sm text-ink-700">
              {t("Pesa isiyo na mwenyewe kutoka mapengo:")}{' '}<b className="figure text-standing-overdue">{tsh(open)}</b>
            </div>
          )}
          <div className="grid gap-3 md:grid-cols-2">
            {gaps.map((g) => (
              <GapCard key={g.id} gap={g} onChanged={refresh} />
            ))}
          </div>
        </>
      )}
    </div>
  );
}

export function GapCard({ gap: g, onChanged }: { gap: GapItem; onChanged: () => void }) {
  const { groupId, members, openMember } = useTruth();
  const toast = useToast();
  const [assign, setAssign] = useState(false);
  const [memberId, setMemberId] = useState<string | null>(null);
  const [evidence, setEvidence] = useState('');
  const [busy, setBusy] = useState(false);
  const [decide, setDecide] = useState<boolean | null>(null);
  const st = requestStatus(g.status);
  const isOut = g.amount < 0;
  const suggestions = (g.possibleMemberIds ?? [])
    .map((id) => members.find((m) => m.memberId === id))
    .filter((m): m is NonNullable<typeof m> => !!m);

  return (
    <div className="rounded-2xl border border-ink-100 bg-paper-raised p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className={`figure text-xl font-semibold ${isOut ? 'text-standing-overdue' : 'text-ink-950'}`}>
            {isOut ? '−' : '+'}
            {num(Math.abs(g.amount))}
          </div>
          <div className="mt-0.5 text-xs text-ink-600">{isOut ? t("Pesa ilitoka bila mstari") : t("Deposit isiyo na mstari kwenye PDF")}</div>
        </div>
        <StatusBadge label={st.label} tone={st.tone} />
      </div>
      <p className="mt-2 text-sm font-medium text-ink-900">{g.title}</p>
      <div className="mt-3">
        <GapChain gap={g} amount={g.amount} onOpenMember={openMember} />
      </div>
      <p className="mt-3 text-xs leading-relaxed text-ink-700">{g.message}</p>
      <dl className="mt-2 space-y-1 text-xs">
        {g.assignedMemberName && (
          <div className="flex justify-between gap-2">
            <dt className="text-ink-600">{t("Mwenyewe")}</dt>
            <dd>
              <button className="font-medium hover:underline" onClick={() => g.assignedMemberId && openMember(g.assignedMemberId)}>
                {g.assignedMemberName}
              </button>
            </dd>
          </div>
        )}
        {g.evidence && <p className="pt-1 text-ink-800">“{g.evidence}”</p>}
        {g.requestedByName && (
          <p className="text-ink-600">
            {t("Aliomba")}{' '}{g.requestedByName}
            {g.approvedByName ? t(" · alithibitisha {0}", { 0: g.approvedByName }) : ''}
          </p>
        )}
      </dl>

      {!isOut && (g.status === 'Unresolved' || g.status === 'Rejected') && (
        <Button size="sm" className="mt-3" icon={Link2} onClick={() => setAssign(true)}>
          {t("Mtambue mwenyewe")}
        </Button>
      )}
      {g.status === 'Pending' && (
        <div className="mt-3 flex flex-wrap gap-2 border-t border-ink-100 pt-3">
          {g.canDecide ? (
            <>
              <Button size="sm" variant="good" icon={Check} onClick={() => setDecide(true)}>
                {t("Thibitisha")}
              </Button>
              <Button size="sm" variant="secondary" icon={X} onClick={() => setDecide(false)}>
                {t("Kataa")}
              </Button>
            </>
          ) : (
            <span className="inline-flex items-center gap-1.5 text-xs text-ink-600">
              <Clock size={13} />{' '}{t("Kiongozi mwingine athibitishe.")}
            </span>
          )}
        </div>
      )}

      <Modal
        open={assign}
        onClose={() => !busy && setAssign(false)}
        title={t("Pengo la {0} — ni la nani?", { 0: num(g.amount) })}
        footer={
          <>
            <Button variant="secondary" onClick={() => setAssign(false)} disabled={busy}>
              {t("Ghairi")}
            </Button>
            <Button
              loading={busy}
              disabled={!memberId || evidence.trim().length < 5}
              onClick={async () => {
                if (!memberId) return;
                setBusy(true);
                try {
                  await resolveGap(groupId, g.id, memberId, evidence.trim());
                  toast(t("Imewasilishwa — kiongozi mwingine athibitishe."), 'good');
                  setAssign(false);
                  onChanged();
                } catch (e) {
                  toast(apiErrorMessage(e), 'overdue');
                } finally {
                  setBusy(false);
                }
              }}
            >
              {t("Wasilisha")}
            </Button>
          </>
        }
      >
        <div className="space-y-4">
          <p className="text-sm text-ink-700">
            {t("Muda:")}{' '}<b>{wallDateTime(g.windowStart)}</b>{' '}{t("hadi")}{' '}<b>{wallDateTime(g.windowEnd)}</b>{t(". Mwanachama aonyeshe SMS ya muamala wa muda huo.")}
          </p>
          {suggestions.length > 0 && (
            <div className="space-y-1.5">
              <div className="text-xs text-ink-600">
                {t("Wenye miamala ya mipakani (pendekezo tu — thibitisha kwa ushahidi kabla ya kuchagua):")}
              </div>
              <div className="flex flex-wrap gap-2">
                {suggestions.map((m) => (
                  <button
                    key={m.memberId}
                    type="button"
                    onClick={() => setMemberId(m.memberId)}
                    className={`rounded-full px-3 py-1.5 text-xs font-medium ring-1 transition-colors ${
                      memberId === m.memberId ? 'bg-ink-900 text-ink-50 ring-ink-900' : 'bg-paper-raised text-ink-800 ring-ink-100 hover:bg-ink-50'
                    }`}
                  >
                    {m.fullName}
                  </button>
                ))}
              </div>
            </div>
          )}
          <Field label={t("Mwanachama")}>
            <MemberPicker members={members} value={memberId} onChange={setMemberId} />
          </Field>
          <Field label={t("Ushahidi")} hint={t("Mf. SMS ya M-Pesa/M-Koba: kumbukumbu, tarehe na saa ya muamala")}>
            <Textarea value={evidence} onChange={(e) => setEvidence(e.target.value)} />
          </Field>
        </div>
      </Modal>

      <DecisionModal
        open={decide !== null}
        approve={decide ?? true}
        onClose={() => setDecide(null)}
        summary={
          <div>
            {t("Pengo la")}{' '}<b className="figure">{num(g.amount)}</b> → <b>{g.assignedMemberName}</b>
            {g.evidence && <div className="mt-1 text-xs">{t("Ushahidi: “")}{g.evidence}”</div>}
          </div>
        }
        onConfirm={async (note) => {
          try {
            await decideGap(groupId, g.id, !!decide, note || undefined);
            toast(decide ? t("Imethibitishwa — pesa imehesabiwa kwa mwanachama.") : t("Imekataliwa."), 'good');
            setDecide(null);
            onChanged();
          } catch (e) {
            toast(apiErrorMessage(e), 'overdue');
          }
        }}
      />
    </div>
  );
}
