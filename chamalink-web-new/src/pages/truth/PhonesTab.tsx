import { useEffect, useMemo, useState } from 'react';
import { Link2, Plus, Search, Smartphone, Trash2 } from 'lucide-react';
import { addPhone, apiErrorMessage, getPhones, removePhone, type MemberLite, type PhoneOverviewItem } from '../../api/truth';
import { MemberPicker } from '../../components/truth/MemberPicker';
import { StatusBadge } from '../../components/StatusBadge';
import { Button, EmptyState, Field, Input, Modal, Notice, Skeleton, Stat } from '../../components/ui';
import { useToast } from '../../lib/toast';
import { matches, num } from '../../lib/truth';
import { useTruth } from './TruthContext';

import { t } from '../../i18n';
type Filter = 'Unmapped' | 'Linked' | 'NotSeen' | 'all';

/**
 * Mfumo unatambua mwanachama kwa NAMBA YA SIMU aliyolipia M-Koba (si jina).
 * Namba isiyounganishwa = pesa isiyo na mwenyewe.
 */
export function PhonesTab() {
  const { groupId, members, version, refresh } = useTruth();
  const toast = useToast();
  const [rows, setRows] = useState<PhoneOverviewItem[] | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>('Unmapped');
  const [q, setQ] = useState('');
  const [link, setLink] = useState<{ phone: string; mkobaName: string | null } | null>(null);
  const [remove, setRemove] = useState<PhoneOverviewItem | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    getPhones(groupId)
      .then((r) => {
        if (cancelled) return;
        setRows(r);
        // Ukurasa ukifunguka bila tatizo, onyesha zilizounganishwa
        if (!r.some((x) => x.state === 'Unmapped')) setFilter((f) => (f === 'Unmapped' ? 'Linked' : f));
      })
      .catch((e) => !cancelled && setErr(apiErrorMessage(e)));
    return () => {
      cancelled = true;
    };
  }, [groupId, version]);

  const counts = useMemo(
    () => ({
      Unmapped: rows?.filter((r) => r.state === 'Unmapped').length ?? 0,
      Linked: rows?.filter((r) => r.state === 'Linked').length ?? 0,
      NotSeen: rows?.filter((r) => r.state === 'NotSeen').length ?? 0,
      all: rows?.length ?? 0,
    }),
    [rows]
  );
  const unmappedMoney = rows?.filter((r) => r.state === 'Unmapped').reduce((s, r) => s + r.total, 0) ?? 0;
  const membersWithoutPhone = members.filter((m) => m.phones.length === 0);

  const shown = (rows ?? []).filter((r) => (filter === 'all' || r.state === filter) && matches(q, r.phone, r.mkobaName, r.memberName));

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
            ['Linked', t('Zimeunganishwa')],
            ['NotSeen', t('Hazijaonekana')],
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
              <StatusBadge
                label={r.state === 'Linked' ? t("Imeunganishwa") : r.state === 'Unmapped' ? t("Haijaunganishwa") : t("Haijaonekana")}
                tone={r.state === 'Linked' ? 'good' : r.state === 'Unmapped' ? 'overdue' : 'warn'}
              />
              {r.state === 'Unmapped' ? (
                <Button size="sm" icon={Link2} onClick={() => setLink({ phone: r.phone, mkobaName: r.mkobaName })}>
                  {t("Unganisha")}
                </Button>
              ) : (
                <Button size="sm" variant="ghost" icon={Trash2} onClick={() => setRemove(r)} aria-label={t("Ondoa {0}", { 0: r.phone })}>
                  <span className="sm:hidden">{t("Ondoa")}</span>
                </Button>
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

      <Modal
        open={!!remove}
        onClose={() => !busy && setRemove(null)}
        title={t("Ondoa namba?")}
        footer={
          <>
            <Button variant="secondary" onClick={() => setRemove(null)} disabled={busy}>
              {t("Ghairi")}
            </Button>
            <Button
              variant="danger"
              loading={busy}
              onClick={async () => {
                if (!remove?.phoneId) return;
                setBusy(true);
                try {
                  await removePhone(groupId, remove.phoneId);
                  toast(t("Namba {0} imeondolewa.", { 0: remove.phone }), 'good');
                  setRemove(null);
                  refresh();
                } catch (e) {
                  toast(apiErrorMessage(e), 'overdue');
                } finally {
                  setBusy(false);
                }
              }}
            >
              {t("Ondoa")}
            </Button>
          </>
        }
      >
        <p className="text-sm text-ink-800">
          <b className="figure">{remove?.phone}</b>{' '}{t("itaondolewa kwa")}{' '}<b>{remove?.memberName}</b>.
          {remove && remove.deposits > 0 && (
            <>
              {' '}
              {t("Malipo yake")}{' '}<b>{remove.deposits}</b> ({num(remove.total)}) <b>{t("hayatahesabiwa tena")}</b>{' '}{t("kwa mwanachama huyu hadi namba iunganishwe upya.")}
            </>
          )}
        </p>
      </Modal>
    </div>
  );
}

function LinkPhoneModal({
  target,
  members,
  onClose,
  onDone,
}: {
  target: { phone: string; mkobaName: string | null } | null;
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
  target: { phone: string; mkobaName: string | null };
  members: MemberLite[];
  onClose: () => void;
  onDone: (msg: string, ok: boolean) => void;
}) {
  const { groupId } = useTruth();
  const [phone, setPhone] = useState(target.phone);
  const [memberId, setMemberId] = useState<string | null>(null);
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
      <Notice tone="warn">
        {t("Hakiki kwa uhakika: pesa zote za namba hii zitahesabiwa kwa mwanachama utakayemchagua. Jina la M-Koba linaweza kuwa la mtu mwingine (mf. mke/mume analipia).")}
      </Notice>
      {err && <Notice tone="overdue">{err}</Notice>}
      <div className="flex flex-wrap justify-end gap-2 border-t border-ink-100 pt-3">
        <Button variant="secondary" onClick={onClose} disabled={busy}>
          {t("Ghairi")}
        </Button>
        <Button
          loading={busy}
          disabled={!memberId || phone.replace(/\D/g, '').length < 9}
          onClick={async () => {
            if (!memberId) return;
            setBusy(true);
            setErr(null);
            try {
              const r = await addPhone(groupId, memberId, phone);
              onDone(r.ujumbe, r.matchedInMkoba);
            } catch (e) {
              setErr(apiErrorMessage(e));
            } finally {
              setBusy(false);
            }
          }}
        >
          {t("Unganisha")}
        </Button>
      </div>
    </div>
  );
}
