import { useEffect, useMemo, useState } from 'react';
import { CalendarX, Check, Link2, Plus, Search, Smartphone, Undo2, X } from 'lucide-react';
import {
  addPhone,
  apiErrorMessage,
  decideEndPhone,
  decidePhone,
  getPhones,
  removePhone,
  requestEndPhone,
  type MemberLite,
  type PhoneOverviewItem,
} from '../../api/truth';
import { ReasonModal } from '../../components/benefits/common';
import { MemberPicker } from '../../components/truth/MemberPicker';
import { StatusBadge } from '../../components/StatusBadge';
import { Button, EmptyState, Field, Input, Modal, Notice, Skeleton, Stat } from '../../components/ui';
import { useToast } from '../../lib/toast';
import { matches, num, todayEat, wallDate } from '../../lib/truth';
import { useTruth } from './TruthContext';

import { t } from '../../i18n';
type Filter = 'Unmapped' | 'Pending' | 'Linked' | 'NotSeen' | 'Ended' | 'all';

type Act =
  | { kind: 'decide'; row: PhoneOverviewItem; approve: boolean }
  | { kind: 'end'; row: PhoneOverviewItem }
  | { kind: 'decideEnd'; row: PhoneOverviewItem; approve: boolean }
  | null;

/**
 * Mfumo unatambua mwanachama kwa NAMBA YA SIMU aliyolipia M-Koba (si jina).
 * Namba isiyounganishwa = pesa isiyo na mwenyewe.
 * Kuunganisha namba = kuamua pesa zake ni za nani, kwa hiyo kunahitaji kiongozi WA PILI.
 * Namba iliyoidhinishwa haifutwi: inafungwa kwa tarehe (malipo ya zamani yanabaki kwa mwenyewe).
 */
export function PhonesTab() {
  const { groupId, members, version, refresh } = useTruth();
  const toast = useToast();
  const [rows, setRows] = useState<PhoneOverviewItem[] | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>('Unmapped');
  const [q, setQ] = useState('');
  const [link, setLink] = useState<LinkTarget | null>(null);
  const [act, setAct] = useState<Act>(null);
  const [endDate, setEndDate] = useState(todayEat());

  useEffect(() => {
    let cancelled = false;
    getPhones(groupId)
      .then((r) => {
        if (cancelled) return;
        setRows(r);
        // Ukurasa ukifunguka bila tatizo, onyesha zilizounganishwa
        if (!r.some((x) => x.state === 'Unmapped'))
          setFilter((f) => (f === 'Unmapped' ? (r.some((x) => x.state === 'Pending' || x.pendingEnd) ? 'Pending' : 'Linked') : f));
      })
      .catch((e) => !cancelled && setErr(apiErrorMessage(e)));
    return () => {
      cancelled = true;
    };
  }, [groupId, version]);

  const counts = useMemo(
    () => ({
      Unmapped: rows?.filter((r) => r.state === 'Unmapped').length ?? 0,
      Pending: rows?.filter((r) => r.state === 'Pending' || !!r.pendingEnd).length ?? 0,
      Linked: rows?.filter((r) => r.state === 'Linked').length ?? 0,
      NotSeen: rows?.filter((r) => r.state === 'NotSeen').length ?? 0,
      Ended: rows?.filter((r) => r.state === 'Ended').length ?? 0,
      all: rows?.length ?? 0,
    }),
    [rows]
  );
  const unmappedMoney = rows?.filter((r) => r.state === 'Unmapped').reduce((s, r) => s + r.total, 0) ?? 0;
  const membersWithoutPhone = members.filter((m) => m.phones.length === 0);

  const inFilter = (r: PhoneOverviewItem) => filter === 'all' || r.state === filter || (filter === 'Pending' && !!r.pendingEnd);
  const shown = (rows ?? []).filter((r) => inFilter(r) && matches(q, r.phone, r.mkobaName, r.memberName));
  const myTurn = rows?.filter((r) => r.canApprove || r.pendingEnd?.canApprove).length ?? 0;

  async function run(fn: () => Promise<unknown>, ok: string) {
    try {
      await fn();
      toast(ok, 'good');
      setAct(null);
      refresh();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    }
  }

  if (err) return <Notice tone="overdue">{err}</Notice>;
  if (!rows) return <Skeleton className="h-40" />;

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
        <Stat label={t("Zimeunganishwa")} value={counts.Linked} tone="good" />
        <Stat label={t("Hazijaunganishwa")} value={counts.Unmapped} tone={counts.Unmapped ? 'overdue' : 'good'} hint={counts.Unmapped ? t("{0} bila mwenyewe", { 0: num(unmappedMoney) }) : undefined} />
        <Stat label={t("Hazijaonekana M-Koba")} value={counts.NotSeen} tone={counts.NotSeen ? 'warn' : 'neutral'} hint={t("Zimewekwa lakini hazijalipa bado")} />
        <Stat label={t("Wanachama bila simu")} value={membersWithoutPhone.length} tone={membersWithoutPhone.length ? 'warn' : 'good'} />
      </div>

      {myTurn > 0 && (
        <Notice tone="warn" title={t("Namba {0} zinasubiri idhini yako", { 0: myTurn })}>
          {t("Kiongozi mwingine ameomba kuunganisha au kufunga namba. Malipo ya namba hizo hayahesabiwi mpaka uamue.")}
        </Notice>
      )}

      {membersWithoutPhone.length > 0 && (
        <Notice tone="warn" title={t("Wanachama bila namba ya simu")}>
          {membersWithoutPhone.map((m) => m.fullName).join(', ')}{' '}{t("— malipo yao ya M-Koba hayawezi kuhesabiwa hadi namba ziwekwe.")}
        </Notice>
      )}

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <div className="relative flex-1">
          <Search size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-500" />
          <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("Tafuta namba au jina...")} className="pl-9" />
        </div>
        <Button icon={Plus} onClick={() => setLink({ phone: '', mkobaName: null })}>
          {t("Ongeza namba")}
        </Button>
      </div>
      <div className="-mx-4 flex gap-1.5 overflow-x-auto px-4 sm:mx-0 sm:px-0">
        {(
          [
            ['Unmapped', t('Hazijaunganishwa')],
            ['Pending', t('Zinasubiri idhini')],
            ['Linked', t('Zimeunganishwa')],
            ['NotSeen', t('Hazijaonekana')],
            ['Ended', t('Zimefungwa')],
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

      {shown.length === 0 ? (
        <EmptyState icon={Smartphone} title={filter === 'Unmapped' ? t("Namba zote za M-Koba zimeunganishwa 🎉") : t("Hakuna namba")} />
      ) : (
        <ul className="divide-y divide-ink-100 overflow-hidden rounded-2xl border border-ink-100 bg-paper-raised">
          {shown.map((r) => (
            <li key={r.phone} className="flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3">
              <div className="w-full min-w-0 sm:w-auto sm:flex-1">
                <div className="figure font-semibold text-ink-950">{r.phone}</div>
                <div className="truncate text-xs text-ink-600">
                  {t("M-Koba:")}{' '}{r.mkobaName ?? '—'}
                  {r.deposits > 0 && t(" · malipo {0} ({1})", { 0: r.deposits, 1: num(r.total) })}
                </div>
              </div>
              <div className="min-w-0 flex-1 text-sm sm:min-w-[8rem] sm:flex-none">
                {r.memberName ? <span className="font-medium text-ink-900">{r.memberName}</span> : <span className="text-ink-500">—</span>}
              </div>
              <StatusBadge label={stateLabel(r)} tone={stateTone(r)} />
              <RowActions
                r={r}
                onLink={() => setLink({ phone: r.phone, mkobaName: r.mkobaName, validFrom: r.state === 'Ended' && r.validTo ? nextDay(r.validTo) : undefined })}
                onAct={(a) => {
                  if (a.kind === 'end') setEndDate(todayEat());
                  setAct(a);
                }}
                onCancel={() => r.phoneId && run(() => removePhone(groupId, r.phoneId!), t("Ombi la namba {0} limeghairiwa.", { 0: r.phone }))}
              />
              {(r.validFrom || r.validTo || r.pendingEnd || r.state === 'Pending' || r.history.length > 1) && (
                <div className="w-full space-y-0.5 text-xs text-ink-600">
                  {(r.validFrom || r.validTo) && (
                    <div>
                      {t("Muda wa umiliki:")}{' '}{r.validFrom ? wallDate(r.validFrom) : t("tangu mwanzo")} → {r.validTo ? wallDate(r.validTo) : t("sasa")}
                    </div>
                  )}
                  {r.state === 'Pending' && <div>{t("Ameomba: {0}", { 0: r.requestedByName ?? '—' })}</div>}
                  {r.pendingEnd && (
                    <div className="text-standing-warn">
                      {t("Ombi la kufunga kuanzia {0} ({1}) — {2}", { 0: wallDate(r.pendingEnd.validTo), 1: r.pendingEnd.requestedByName ?? '—', 2: r.pendingEnd.reason ?? '' })}
                    </div>
                  )}
                  {r.history.length > 1 && (
                    <details>
                      <summary className="cursor-pointer">{t("Historia ya namba ({0})", { 0: r.history.length })}</summary>
                      <ul className="mt-1 space-y-0.5 pl-3">
                        {r.history.map((h) => (
                          <li key={h.id}>
                            {h.memberName ?? '—'} · {h.status} · {h.validFrom ? wallDate(h.validFrom) : '…'} → {h.validTo ? wallDate(h.validTo) : '…'}
                          </li>
                        ))}
                      </ul>
                    </details>
                  )}
                </div>
              )}
            </li>
          ))}
        </ul>
      )}

      <LinkPhoneModal
        target={link}
        members={members}
        onClose={() => setLink(null)}
        onDone={(msg, ok) => {
          toast(msg, ok ? 'good' : 'warn');
          setLink(null);
          refresh();
        }}
      />

      <ReasonModal
        open={act?.kind === 'decide'}
        title={act?.kind === 'decide' && act.approve ? t("Idhinisha kuunganisha namba") : t("Kataa kuunganisha namba")}
        summary={
          act?.kind === 'decide' ? (
            <>
              <b className="figure">{act.row.phone}</b> → <b>{act.row.memberName}</b>
              {act.row.mkobaName ? <> · {t("M-Koba:")} {act.row.mkobaName}</> : null}
              {act.row.deposits > 0 ? <> · {t("malipo {0} ({1})", { 0: act.row.deposits, 1: num(act.row.total) })}</> : null}
            </>
          ) : undefined
        }
        hint={act?.kind === 'decide' && act.approve ? t("Ukiidhinisha, malipo yote ya namba hii (ndani ya muda wake) yatahesabiwa kwa mwanachama huyu.") : undefined}
        requireReason={act?.kind === 'decide' && !act.approve}
        variant={act?.kind === 'decide' && act.approve ? 'good' : 'danger'}
        confirmLabel={act?.kind === 'decide' && act.approve ? t("Idhinisha") : t("Kataa")}
        onClose={() => setAct(null)}
        onConfirm={(note) =>
          act?.kind === 'decide' && act.row.phoneId
            ? run(() => decidePhone(groupId, act.row.phoneId!, act.approve, note || undefined), act.approve ? t("Namba imeidhinishwa.") : t("Ombi limekataliwa."))
            : Promise.resolve()
        }
      />
      <ReasonModal
        open={act?.kind === 'end'}
        title={t("Funga namba")}
        summary={
          act?.kind === 'end' ? (
            <>
              <b className="figure">{act.row.phone}</b> · {act.row.memberName}.{' '}
              {t("Malipo ya kabla ya tarehe hii yanabaki kwake; ya baada yake hayatahesabiwa kwa mtu yeyote mpaka namba iunganishwe upya. Kiongozi wa pili ataidhinisha.")}
            </>
          ) : undefined
        }
        label={t("Sababu")}
        hint={t("Mf. \"Fauzia si mwanachama; Abdulla analipa kwa namba yake mpya\"")}
        requireReason
        variant="danger"
        confirmLabel={t("Tuma ombi la kufunga")}
        onClose={() => setAct(null)}
        onConfirm={(reason) =>
          act?.kind === 'end' && act.row.phoneId
            ? run(() => requestEndPhone(groupId, act.row.phoneId!, endDate, reason), t("Ombi la kufunga namba limetumwa kwa kiongozi wa pili."))
            : Promise.resolve()
        }
      >
        <Field label={t("Tarehe ya mwisho ya kuhesabu malipo")}>
          <Input type="date" value={endDate} onChange={(e) => setEndDate(e.target.value)} />
        </Field>
      </ReasonModal>
      <ReasonModal
        open={act?.kind === 'decideEnd'}
        title={act?.kind === 'decideEnd' && act.approve ? t("Idhinisha kufunga namba") : t("Kataa kufunga namba")}
        summary={
          act?.kind === 'decideEnd' && act.row.pendingEnd ? (
            <>
              <b className="figure">{act.row.phone}</b> · {act.row.memberName} — {t("kuanzia {0}", { 0: wallDate(act.row.pendingEnd.validTo) })}. {act.row.pendingEnd.reason}
            </>
          ) : undefined
        }
        requireReason={act?.kind === 'decideEnd' && !act.approve}
        variant={act?.kind === 'decideEnd' && act.approve ? 'good' : 'danger'}
        confirmLabel={act?.kind === 'decideEnd' && act.approve ? t("Idhinisha") : t("Kataa")}
        onClose={() => setAct(null)}
        onConfirm={(note) =>
          act?.kind === 'decideEnd' && act.row.phoneId
            ? run(() => decideEndPhone(groupId, act.row.phoneId!, act.approve, note || undefined), act.approve ? t("Namba imefungwa.") : t("Ombi limekataliwa."))
            : Promise.resolve()
        }
      />
    </div>
  );
}

interface LinkTarget {
  phone: string;
  mkobaName: string | null;
  /** Namba iliyofungwa inaunganishwa upya kuanzia siku inayofuata */
  validFrom?: string;
}

function nextDay(iso: string): string {
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number);
  const dt = new Date(Date.UTC(y, m - 1, d + 1));
  return dt.toISOString().slice(0, 10);
}

function stateLabel(r: PhoneOverviewItem): string {
  switch (r.state) {
    case 'Linked':
      return t("Imeunganishwa");
    case 'Unmapped':
      return t("Haijaunganishwa");
    case 'Pending':
      return t("Inasubiri idhini");
    case 'Ended':
      return t("Imefungwa");
    default:
      return t("Haijaonekana");
  }
}

function stateTone(r: PhoneOverviewItem) {
  if (r.state === 'Linked') return r.pendingEnd ? ('warn' as const) : ('good' as const);
  if (r.state === 'Unmapped') return 'overdue' as const;
  if (r.state === 'Ended') return 'neutral' as const;
  return 'warn' as const;
}

function RowActions({
  r,
  onLink,
  onAct,
  onCancel,
}: {
  r: PhoneOverviewItem;
  onLink: () => void;
  onAct: (a: NonNullable<Act>) => void;
  onCancel: () => void;
}) {
  if (r.state === 'Unmapped' || r.state === 'Ended')
    return (
      <Button size="sm" icon={Link2} onClick={onLink}>
        {r.state === 'Ended' ? t("Unganisha upya") : t("Unganisha")}
      </Button>
    );
  if (r.state === 'Pending') {
    if (r.canApprove)
      return (
        <div className="flex gap-1.5">
          <Button size="sm" variant="good" icon={Check} onClick={() => onAct({ kind: 'decide', row: r, approve: true })}>
            {t("Idhinisha")}
          </Button>
          <Button size="sm" variant="danger" icon={X} onClick={() => onAct({ kind: 'decide', row: r, approve: false })}>
            {t("Kataa")}
          </Button>
        </div>
      );
    if (r.canCancel)
      return (
        <Button size="sm" variant="secondary" icon={Undo2} onClick={onCancel}>
          {t("Ghairi ombi")}
        </Button>
      );
    return <span className="text-xs text-ink-600">{t("Inasubiri kiongozi mwingine")}</span>;
  }
  // Imeidhinishwa (Linked / NotSeen)
  if (r.pendingEnd) {
    if (r.pendingEnd.canApprove)
      return (
        <div className="flex gap-1.5">
          <Button size="sm" variant="good" onClick={() => onAct({ kind: 'decideEnd', row: r, approve: true })}>
            {t("Idhinisha kufunga")}
          </Button>
          <Button size="sm" variant="danger" onClick={() => onAct({ kind: 'decideEnd', row: r, approve: false })}>
            {t("Kataa")}
          </Button>
        </div>
      );
    return <span className="text-xs text-ink-600">{t("Kufunga kunasubiri kiongozi mwingine")}</span>;
  }
  return (
    <Button size="sm" variant="ghost" icon={CalendarX} onClick={() => onAct({ kind: 'end', row: r })} aria-label={t("Funga {0}", { 0: r.phone })}>
      {t("Funga")}
    </Button>
  );
}

function LinkPhoneModal({
  target,
  members,
  onClose,
  onDone,
}: {
  target: LinkTarget | null;
  members: MemberLite[];
  onClose: () => void;
  onDone: (msg: string, ok: boolean) => void;
}) {
  return (
    <Modal open={!!target} onClose={onClose} title={target?.phone ? t("Unganisha namba na mwanachama") : t("Ongeza namba ya simu")}>
      {/* Inapachikwa upya kila dirisha linapofunguliwa => fomu huanza safi */}
      {target && <LinkPhoneForm target={target} members={members} onClose={onClose} onDone={onDone} />}
    </Modal>
  );
}

function LinkPhoneForm({
  target,
  members,
  onClose,
  onDone,
}: {
  target: LinkTarget;
  members: MemberLite[];
  onClose: () => void;
  onDone: (msg: string, ok: boolean) => void;
}) {
  const { groupId } = useTruth();
  const [phone, setPhone] = useState(target.phone);
  const [memberId, setMemberId] = useState<string | null>(null);
  const [validFrom, setValidFrom] = useState(target.validFrom ?? '');
  const [validTo, setValidTo] = useState('');
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  // Pendekezo kwa jina la M-Koba (neno linalofanana) — kiongozi ndiye anayeamua.
  const suggestions = useMemo(() => {
    if (!target.mkobaName) return [];
    const words = target.mkobaName.toLowerCase().split(/\s+/).filter((w) => w.length > 2);
    return members
      .map((m) => ({ m, score: words.filter((w) => m.fullName.toLowerCase().includes(w)).length }))
      .filter((x) => x.score > 0)
      .sort((a, b) => b.score - a.score)
      .slice(0, 3)
      .map((x) => x.m);
  }, [target, members]);

  return (
    <div className="space-y-4">
      <Field label={t("Namba ya simu")} hint={t("Kama ilivyo kwenye M-Koba — 0712..., 712... au 255712... zote zinakubalika.")}>
        <Input value={phone} onChange={(e) => setPhone(e.target.value)} inputMode="tel" disabled={!!target.phone} className="figure" placeholder="0712 345 678" />
      </Field>
      {target.mkobaName && (
        <p className="text-sm text-ink-700">
          {t("Jina kwenye M-Koba:")}{' '}<b>{target.mkobaName}</b>
        </p>
      )}
      {suggestions.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          <span className="text-xs text-ink-600">{t("Mapendekezo:")}</span>
          {suggestions.map((m) => (
            <button
              key={m.memberId}
              type="button"
              onClick={() => setMemberId(m.memberId)}
              className={`rounded-full border px-2.5 py-1 text-xs ${
                memberId === m.memberId ? 'border-ink-800 bg-ink-800 text-ink-50' : 'border-ink-100 bg-paper-raised text-ink-800 hover:border-ink-500'
              }`}
            >
              {m.fullName}
            </button>
          ))}
        </div>
      )}
      <Field label={t("Mwanachama")}>
        <MemberPicker members={members} value={memberId} onChange={setMemberId} />
      </Field>
      <details open={!!target.validFrom} className="rounded-xl border border-ink-100 px-3 py-2">
        <summary className="cursor-pointer text-sm font-medium text-ink-800">{t("Muda wa umiliki (hiari)")}</summary>
        <p className="mt-1 text-xs text-ink-600">
          {t("Kwa namba iliyotumiwa na mtu huyu kwa kipindi fulani tu (mf. mke alilipia hadi Juni, kisha mwanachama akaanza kulipa kwa namba yake). Acha wazi = malipo yote ya namba hii.")}
        </p>
        <div className="mt-2 grid gap-3 sm:grid-cols-2">
          <Field label={t("Kuanzia")}>
            <Input type="date" value={validFrom} onChange={(e) => setValidFrom(e.target.value)} />
          </Field>
          <Field label={t("Hadi")}>
            <Input type="date" value={validTo} min={validFrom || undefined} onChange={(e) => setValidTo(e.target.value)} />
          </Field>
        </div>
      </details>
      <Field label={t("Maelezo / ushahidi (hiari)")}>
        <Input value={note} onChange={(e) => setNote(e.target.value)} placeholder={t("Mf. mwanachama amethibitisha kwenye kikao")} />
      </Field>
      <Notice tone="warn">
        {t("Hakiki kwa uhakika: pesa zote za namba hii zitahesabiwa kwa mwanachama utakayemchagua. Jina la M-Koba linaweza kuwa la mtu mwingine (mf. mke/mume analipia).")}{' '}
        {t("Kiongozi WA PILI ataidhinisha kabla malipo hayajahesabiwa.")}
      </Notice>
      {err && <Notice tone="overdue">{err}</Notice>}
      <div className="flex flex-wrap justify-end gap-2 border-t border-ink-100 pt-3">
        <Button variant="secondary" onClick={onClose} disabled={busy}>
          {t("Ghairi")}
        </Button>
        <Button
          loading={busy}
          disabled={!memberId || phone.replace(/\D/g, '').length < 9 || (!!validFrom && !!validTo && validTo < validFrom)}
          onClick={async () => {
            if (!memberId) return;
            setBusy(true);
            setErr(null);
            try {
              const r = await addPhone(groupId, memberId, phone, note.trim() || undefined, validFrom || null, validTo || null);
              onDone(r.ujumbe, r.matchedInMkoba);
            } catch (e) {
              setErr(apiErrorMessage(e));
            } finally {
              setBusy(false);
            }
          }}
        >
          {t("Tuma kwa idhini")}
        </Button>
      </div>
    </div>
  );
}
