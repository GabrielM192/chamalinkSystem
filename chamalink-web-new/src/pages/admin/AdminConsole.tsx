import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react';
import { t } from '../../i18n';
import { apiErrorMessage } from '../../api/truth';
import { tsh } from '../../lib/truth';
import { Preferences } from '../../components/Preferences';
import { Section, Stat, Kv } from './ui';
import { healthTone, flagLabel, actionLabel, fmtDate, btnCls, btnGhostCls } from './labels';
import {
  platformAdmins,
  platformApprovals,
  platformAudit,
  platformCancel,
  platformDecide,
  platformGroup,
  platformGroups,
  platformIssueResetCode,
  platformMembers,
  platformNotice,
  platformPeople,
  platformRequestAdminChange,
  platformRequestBreakGlass,
  platformRequestMfaReset,
  platformRotateJoinCode,
  platformUnlock,
  type AdminRow,
  type ApprovalRow,
  type AuditRow,
  type GroupDetail,
  type GroupRow,
  type MemberRow,
  type PersonRow,
  type PlatformLevel,
  type PlatformMe,
} from '../../api/platform';

/**
 * Dashibodi ya msimamizi wa mfumo — sehemu nne:
 *   1. VIKUNDI   — vikundi vyote + afya; muhtasari; wanachama kwa break-glass; notisi; code ya kujiunga.
 *   2. MSAADA    — kutafuta mtu, kufungua akaunti, code ya mara moja ya neno la siri.
 *   3. WASIMAMIZI— orodha ya wasimamizi + maamuzi ya 4-eyes (kuidhinisha/kukataa).
 *   4. KUMBUKUMBU— kila kitendo, kikiwa na jina la msimamizi na sababu.
 *
 * Kanuni inayoonekana kwenye UI yote: HAKUNA kitufe cha kubadilisha kiasi, deni, au malipo.
 * Msimamizi anaona na anasaidia; pesa zinabaki kwa viongozi wa kikundi + Injini ya Ukweli.
 */
const TABS = ['groups', 'support', 'admins', 'audit'] as const;
type Tab = (typeof TABS)[number];

export function AdminConsole({ me, onSignOut, onGoHome }: { me: PlatformMe; onSignOut: () => void; onGoHome: () => void }) {
  const [tab, setTab] = useState<Tab>('groups');
  const [pending, setPending] = useState<ApprovalRow[]>([]);

  const loadPending = useCallback(() => {
    platformApprovals(true).then(setPending).catch(() => setPending([]));
  }, []);

  useEffect(() => {
    loadPending();
  }, [loadPending]);

  const isSuper = me.level === 'SuperAdmin';
  const isOversight = me.level === 'Oversight' || isSuper;

  return (
    <div className="min-h-screen bg-paper">
      <header className="chrome-panel theme-chrome text-ink-50">
        <div className="mx-auto max-w-6xl px-4 py-3 flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-baseline gap-3">
            <span className="font-semibold text-gold-500">ChamaLink</span>
            <span className="text-sm text-ink-100/80">{t('Msimamizi wa Mfumo')}</span>
          </div>
          <div className="flex items-center gap-3 text-sm">
            <span className="text-ink-100/90">{me.fullName}</span>
            <span className="rounded-full border border-gold-500/40 px-2 py-0.5 text-xs text-gold-500">{levelLabel(me.level)}</span>
            <Preferences />
            <button type="button" onClick={onGoHome} className="text-ink-100/80 hover:text-ink-50">{t('Kikundi')}</button>
            <button type="button" onClick={onSignOut} className="text-ink-100/80 hover:text-ink-50">{t('Toka')}</button>
          </div>
        </div>
        <div className="mx-auto max-w-6xl px-4 pb-2 flex gap-1 overflow-x-auto">
          {TABS.map((x) => (
            <button
              key={x}
              type="button"
              onClick={() => setTab(x)}
              className={`px-3 py-2 text-sm border-b-2 whitespace-nowrap ${
                tab === x ? 'border-gold-500 text-ink-50' : 'border-transparent text-ink-100/70 hover:text-ink-50'
              }`}
            >
              {tabLabel(x)}
              {x === 'admins' && pending.length > 0 && (
                <span className="ml-2 rounded-full bg-standing-warn px-1.5 py-0.5 text-[10px] font-semibold text-on-accent">
                  {pending.length}
                </span>
              )}
            </button>
          ))}
        </div>
      </header>

      <main className="mx-auto max-w-6xl px-4 py-6 space-y-6">
        {tab === 'groups' && <GroupsTab isSuper={isSuper} isOversight={isOversight} onChanged={loadPending} />}
        {tab === 'support' && <SupportTab />}
        {tab === 'admins' && <AdminsTab me={me} pending={pending} reload={loadPending} />}
        {tab === 'audit' && <AuditTab />}
      </main>
    </div>
  );
}

function tabLabel(x: Tab): string {
  if (x === 'groups') return t('Vikundi');
  if (x === 'support') return t('Msaada');
  if (x === 'admins') return t('Wasimamizi');
  return t('Kumbukumbu');
}

function levelLabel(level: PlatformLevel): string {
  if (level === 'SuperAdmin') return t('Msimamizi Mkuu');
  if (level === 'Oversight') return t('Uangalizi');
  return t('Msaada');
}

// ===================================================================== VIKUNDI
function GroupsTab({ isSuper, isOversight, onChanged }: { isSuper: boolean; isOversight: boolean; onChanged: () => void }) {
  const [rows, setRows] = useState<GroupRow[]>([]);
  const [counts, setCounts] = useState({ kikundi: 0, hatari: 0, tahadhari: 0 });
  const [q, setQ] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);

  const load = useCallback(() => {
    platformGroups()
      .then((d) => {
        setError(null);
        setRows(d.rows);
        setCounts({ kikundi: d.kikundi, hatari: d.hatari, tahadhari: d.tahadhari });
      })
      .catch((e) => setError(apiErrorMessage(e)));
  }, []);

  useEffect(load, [load]);

  const filtered = useMemo(() => {
    const s = q.trim().toLowerCase();
    if (!s) return rows;
    return rows.filter((r) => r.name.toLowerCase().includes(s) || (r.shortCode ?? '').toLowerCase().includes(s));
  }, [rows, q]);

  return (
    <div className="space-y-6">
      <div className="grid gap-3 sm:grid-cols-3">
        <Stat label={t('Vikundi vyote')} value={String(counts.kikundi)} />
        <Stat label={t('Viko hatarini')} value={String(counts.hatari)} tone="overdue" />
        <Stat label={t('Vinahitaji tahadhari')} value={String(counts.tahadhari)} tone="warn" />
      </div>

      <Section
        title={t('Vikundi vilivyounganishwa')}
        actions={
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder={t('Tafuta jina au code…')}
            className="rounded-lg border border-ink-100 bg-paper px-3 py-1.5 text-sm w-56"
            aria-label={t('Tafuta kikundi')}
          />
        }
      >
        {error && <p role="alert" className="text-sm text-standing-overdue mb-3">{error}</p>}
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-left text-xs uppercase tracking-wide text-ink-600">
                <th className="py-2 pr-3">{t('Kikundi')}</th>
                <th className="py-2 pr-3">{t('Aina')}</th>
                <th className="py-2 pr-3 text-right">{t('Wanachama')}</th>
                <th className="py-2 pr-3 text-right">{t('Viongozi')}</th>
                <th className="py-2 pr-3">{t('Afya')}</th>
                <th className="py-2" />
              </tr>
            </thead>
            <tbody>
              {filtered.map((r) => (
                <tr key={r.id} className="border-b border-ink-100 last:border-0">
                  <td className="py-2 pr-3">
                    <div className="font-medium text-ink-900">{r.name}</div>
                    <div className="text-xs text-ink-600">{r.shortCode ?? '—'}</div>
                  </td>
                  <td className="py-2 pr-3 text-ink-700">{r.type === 'EventBased' ? t('Kila tukio') : t('Kila mwezi')}</td>
                  <td className="py-2 pr-3 text-right tabular-nums">{r.members}</td>
                  <td className="py-2 pr-3 text-right tabular-nums">{r.leaders}</td>
                  <td className="py-2 pr-3">
                    <span className={`inline-block rounded-full border px-2 py-0.5 text-xs ${healthTone(r.health).cls}`}>
                      {healthTone(r.health).label}
                    </span>
                    {r.flags.length > 0 && <div className="mt-1 text-xs text-ink-600">{r.flags.map(flagLabel).join(' · ')}</div>}
                  </td>
                  <td className="py-2 text-right">
                    <button type="button" onClick={() => setOpenId(openId === r.id ? null : r.id)} className="text-sm text-ink-700 underline">
                      {openId === r.id ? t('Funga') : t('Fungua')}
                    </button>
                  </td>
                </tr>
              ))}
              {filtered.length === 0 && (
                <tr>
                  <td colSpan={6} className="py-6 text-center text-ink-600">{t('Hakuna kikundi kinacholingana.')}</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </Section>

      {openId && <GroupPanel groupId={openId} isSuper={isSuper} isOversight={isOversight} onChanged={onChanged} onClose={() => setOpenId(null)} />}
    </div>
  );
}

function GroupPanel({
  groupId,
  isSuper,
  isOversight,
  onChanged,
  onClose,
}: {
  groupId: string;
  isSuper: boolean;
  isOversight: boolean;
  onChanged: () => void;
  onClose: () => void;
}) {
  const [detail, setDetail] = useState<GroupDetail | null>(null);
  const [members, setMembers] = useState<MemberRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [reason, setReason] = useState('');
  const [noticeTitle, setNoticeTitle] = useState('');
  const [noticeBody, setNoticeBody] = useState('');

  const load = useCallback(() => {
    platformGroup(groupId)
      .then((d) => {
        setError(null);
        setDetail(d);
      })
      .catch((e) => setError(apiErrorMessage(e)));
  }, [groupId]);

  useEffect(load, [load]);

  async function requestBreakGlass() {
    setError(null);
    setBusy(true);
    try {
      const r = await platformRequestBreakGlass(groupId, reason.trim());
      setInfo(r.ujumbe);
      setReason('');
      onChanged();
    } catch (e) {
      setError(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  async function showMembers() {
    setError(null);
    setBusy(true);
    try {
      const r = await platformMembers(groupId);
      setMembers(r.rows);
      load();
    } catch (e) {
      setError(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  async function sendNotice(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await platformNotice(groupId, noticeTitle.trim(), noticeBody.trim());
      setInfo(t('Notisi imetumwa kwa viongozi wote wa kikundi.'));
      setNoticeTitle('');
      setNoticeBody('');
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  async function rotate() {
    if (!reason.trim()) {
      setError(t('Eleza sababu ya kubadilisha code ya kujiunga.'));
      return;
    }
    setError(null);
    setBusy(true);
    try {
      const code = await platformRotateJoinCode(groupId, reason.trim());
      setInfo(t('Code mpya ya kujiunga: {0}', { 0: code }));
      setReason('');
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  if (!detail) {
    return (
      <Section title={t('Inapakia kikundi…')}>
        {error ? <p role="alert" className="text-sm text-standing-overdue">{error}</p> : <p className="text-sm text-ink-600">{t('Inapakia…')}</p>}
      </Section>
    );
  }

  const s = detail.summary;

  return (
    <Section
      title={s.name}
      actions={
        <button type="button" onClick={onClose} className="text-sm text-ink-600 hover:text-ink-900">{t('Funga')}</button>
      }
    >
      <div className="space-y-5">
        {error && <p role="alert" className="text-sm text-standing-overdue">{error}</p>}
        {info && <p role="status" className="text-sm text-standing-good">{info}</p>}

        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <Kv label={t('Wanachama hai')} value={`${s.activeMembers} / ${s.members}`} />
          <Kv label={t('Viongozi')} value={String(s.leaders)} />
          <Kv label={t('Miezi iliyofuatiliwa')} value={String(detail.monthsTracked)} />
          <Kv label={t('Jumla iliyokubaliwa')} value={tsh(detail.totalAccepted)} />
          <Kv label={t('Akiba')} value={tsh(detail.savings)} />
          <Kv label={t('Madeni')} value={tsh(detail.arrears)} />
          <Kv label={t('Mikopo inayodaiwa')} value={tsh(detail.loansOutstanding)} />
          <Kv label={t('Deni la matukio')} value={tsh(detail.eventDebt)} />
          <Kv label={t('Kianzio kinachodaiwa')} value={tsh(detail.joinFeeOutstanding)} />
        </div>

        {detail.issues.length > 0 && (
          <div className="space-y-2">
            <h3 className="text-xs uppercase tracking-wide text-ink-600">{t('Matatizo yaliyogundulika')}</h3>
            <ul className="space-y-1">
              {detail.issues.map((i, idx) => (
                <li key={`${i.code}-${idx}`} className="text-sm text-ink-800">
                  <span className={`mr-2 inline-block rounded-full border px-2 py-0.5 text-xs ${healthTone(i.severity === 'Critical' ? 'Critical' : 'Warning').cls}`}>
                    {flagLabel(i.code)}
                  </span>
                  {i.detail}
                </li>
              ))}
            </ul>
          </div>
        )}

        {/* Break-glass */}
        <div className="rounded-xl border border-ink-100 bg-paper p-4 space-y-3">
          <h3 className="text-sm font-semibold text-ink-900">{t('Taarifa za mwanachama mmoja mmoja')}</h3>
          {detail.breakGlassActive ? (
            <div className="space-y-3">
              <p className="text-sm text-standing-good">
                {t('Dirisha la break-glass linafanya kazi hadi {0}.', { 0: fmtDate(detail.breakGlassExpiresAt) })}
              </p>
              <button type="button" onClick={showMembers} disabled={busy} className={btnCls}>
                {busy ? t('Inapakia…') : t('Onyesha wanachama')}
              </button>
            </div>
          ) : (
            <div className="space-y-3">
              <p className="text-sm text-ink-700">
                {t('Kuona wanachama mmoja mmoja kunahitaji sababu + idhini ya msimamizi mwingine. Dirisha linaisha baada ya saa 24 na kikundi kinaona ombi lote.')}
              </p>
              <textarea
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                rows={2}
                placeholder={t('Sababu (itaonekana kwa kikundi)…')}
                className="w-full rounded-lg border border-ink-100 bg-paper-raised px-3 py-2 text-sm"
              />
              <button type="button" onClick={requestBreakGlass} disabled={busy || !isSuper || reason.trim().length < 12} className={btnCls}>
                {t('Omba break-glass')}
              </button>
              {!isSuper && <p className="text-xs text-ink-600">{t('Break-glass inaombwa na Msimamizi Mkuu pekee.')}</p>}
            </div>
          )}
          {members && (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-ink-100 text-left text-xs uppercase tracking-wide text-ink-600">
                    <th className="py-2 pr-3">{t('Namba')}</th>
                    <th className="py-2 pr-3">{t('Jina')}</th>
                    <th className="py-2 pr-3">{t('Nafasi')}</th>
                    <th className="py-2 pr-3 text-right">{t('Salio')}</th>
                    <th className="py-2 pr-3 text-right">{t('Deni')}</th>
                    <th className="py-2">{t('Hali')}</th>
                  </tr>
                </thead>
                <tbody>
                  {members.map((m) => (
                    <tr key={m.memberId} className="border-b border-ink-100 last:border-0">
                      <td className="py-2 pr-3 font-mono text-xs">{m.memberNumber ?? '—'}</td>
                      <td className="py-2 pr-3">{m.name ?? '—'}</td>
                      <td className="py-2 pr-3 text-ink-700">{m.role}</td>
                      <td className="py-2 pr-3 text-right tabular-nums">{tsh(m.balance)}</td>
                      <td className="py-2 pr-3 text-right tabular-nums">{tsh(m.outstanding + m.arrears)}</td>
                      <td className="py-2 text-ink-700">{m.active ? t('Hai') : t('Si hai')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {/* Notisi */}
        <form onSubmit={sendNotice} className="rounded-xl border border-ink-100 bg-paper p-4 space-y-3">
          <h3 className="text-sm font-semibold text-ink-900">{t('Notisi kwa viongozi')}</h3>
          <input
            value={noticeTitle}
            onChange={(e) => setNoticeTitle(e.target.value)}
            placeholder={t('Kichwa')}
            className="w-full rounded-lg border border-ink-100 bg-paper-raised px-3 py-2 text-sm"
          />
          <textarea
            value={noticeBody}
            onChange={(e) => setNoticeBody(e.target.value)}
            rows={2}
            placeholder={t('Ujumbe…')}
            className="w-full rounded-lg border border-ink-100 bg-paper-raised px-3 py-2 text-sm"
          />
          <button type="submit" disabled={busy || !noticeTitle.trim() || !noticeBody.trim()} className={btnCls}>
            {t('Tuma notisi')}
          </button>
        </form>

        {/* Code ya kujiunga */}
        <div className="rounded-xl border border-ink-100 bg-paper p-4 space-y-3">
          <h3 className="text-sm font-semibold text-ink-900">{t('Code ya kujiunga')}</h3>
          <p className="text-sm text-ink-700">
            {t('Badilisha code kama imesambaa kwa watu wasio wanachama. Viongozi watapewa code mpya.')}
          </p>
          <div className="flex flex-wrap gap-2">
            <input
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder={t('Sababu…')}
              className="flex-1 min-w-48 rounded-lg border border-ink-100 bg-paper-raised px-3 py-2 text-sm"
            />
            <button type="button" onClick={rotate} disabled={busy || !isOversight} className={btnCls}>
              {t('Badilisha code')}
            </button>
          </div>
        </div>
      </div>
    </Section>
  );
}

// ===================================================================== MSAADA
function SupportTab() {
  const [q, setQ] = useState('');
  const [rows, setRows] = useState<PersonRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [issued, setIssued] = useState<{ code: string; expiresAt: string; person: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [reason, setReason] = useState('');

  async function search(e?: FormEvent) {
    e?.preventDefault();
    setError(null);
    setInfo(null);
    setBusy(true);
    try {
      setRows(await platformPeople(q.trim()));
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  async function unlock(p: PersonRow) {
    setError(null);
    setBusy(true);
    try {
      const why = reason.trim() || t('Kiongozi amepiga simu: amefungiwa akaunti');
      await platformUnlock(p.userId, why);
      setInfo(t('Akaunti ya {0} imefunguliwa. Anaweza kuingia kwa neno lake la siri.', { 0: p.fullName }));
      await search();
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  async function resetCode(p: PersonRow) {
    setError(null);
    setBusy(true);
    try {
      const why = reason.trim() || t('Kiongozi amepiga simu: amesahau neno la siri');
      const r = await platformIssueResetCode(p.userId, p.groups[0]?.groupId ?? null, why);
      setIssued({ code: r.code, expiresAt: r.expiresAt, person: r.person });
      setInfo(null);
      setReason('');
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-6">
      <Section title={t('Tafuta mtu')}>
        <form onSubmit={search} className="flex flex-wrap gap-2">
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder={t('Jina, barua pepe au namba ya simu')}
            className="flex-1 min-w-56 rounded-lg border border-ink-100 bg-paper px-3 py-2 text-sm"
            aria-label={t('Tafuta mtu')}
          />
          <button type="submit" disabled={busy || q.trim().length < 3} className={btnCls}>
            {busy ? t('Inatafuta…') : t('Tafuta')}
          </button>
        </form>
        <input
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          placeholder={t('Sababu (inaandikwa kwenye kumbukumbu)…')}
          className="mt-2 w-full rounded-lg border border-ink-100 bg-paper px-3 py-2 text-sm"
        />
        <p className="mt-2 text-xs text-ink-600">
          {t('Msimamizi haoni wala haweki neno la siri la mtu. Anatoa code ya mara moja; mwenye akaunti anaweka neno jipya mwenyewe.')}
        </p>
        {error && <p role="alert" className="mt-3 text-sm text-standing-overdue">{error}</p>}
        {info && <p role="status" className="mt-3 text-sm text-standing-good">{info}</p>}

        {issued && (
          <div className="mt-4 rounded-xl border border-gold-500/40 bg-gold-100 p-4 space-y-2">
            <p className="text-sm font-semibold text-ink-900">{t('Code ya mara moja kwa {0}', { 0: issued.person })}</p>
            <p className="font-mono text-2xl tracking-widest text-ink-950" data-testid="admin-reset-code">{issued.code}</p>
            <p className="text-xs text-ink-700">
              {t('Inaisha {0}. Mpe kwa simu; asiishe kwa WhatsApp ya kikundi. Mtu huyu anaweka neno jipya kwenye ukurasa wa ChamaLink → “Nina code ya msaada”.', {
                0: fmtDate(issued.expiresAt),
              })}
            </p>
            <button type="button" onClick={() => setIssued(null)} className="text-xs text-ink-700 underline">{t('Nimeimaliza')}</button>
          </div>
        )}

        {rows.length > 0 && (
          <div className="mt-4 space-y-3">
            {rows.map((p) => (
              <div key={p.userId} className="rounded-xl border border-ink-100 bg-paper p-3 space-y-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <div className="font-medium text-ink-900">{p.fullName}</div>
                    <div className="text-xs text-ink-600">{p.email} · {p.phone}</div>
                    <div className="text-xs text-ink-600">
                      {p.groups.length === 0
                        ? t('Hana uanachama kwenye kikundi')
                        : p.groups.map((g) => `${g.groupName ?? '—'} (${g.role})`).join(' · ')}
                    </div>
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    {p.locked && (
                      <span className="rounded-full border border-standing-overdue/30 bg-standing-overdue-bg px-2 py-0.5 text-xs text-standing-overdue">
                        {t('Imefungwa')}
                      </span>
                    )}
                    {!p.hasAccount && (
                      <span className="rounded-full border border-ink-100 bg-ink-50 px-2 py-0.5 text-xs text-ink-700">{t('Hana akaunti')}</span>
                    )}
                    <button type="button" onClick={() => unlock(p)} disabled={busy || !p.locked} className={btnGhostCls}>
                      {t('Fungua akaunti')}
                    </button>
                    <button type="button" onClick={() => resetCode(p)} disabled={busy || !p.hasAccount} className={btnCls}>
                      {t('Toa code ya neno la siri')}
                    </button>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </Section>
    </div>
  );
}

// ===================================================================== WASIMAMIZI
function AdminsTab({ me, pending, reload }: { me: PlatformMe; pending: ApprovalRow[]; reload: () => void }) {
  const [admins, setAdmins] = useState<AdminRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [q, setQ] = useState('');
  const [found, setFound] = useState<PersonRow[]>([]);
  const [candidate, setCandidate] = useState<PersonRow | null>(null);
  const [level, setLevel] = useState<PlatformLevel>('Support');
  const [reason, setReason] = useState('');
  const [note, setNote] = useState('');

  const isSuper = me.level === 'SuperAdmin';

  const load = useCallback(() => {
    platformAdmins()
      .then((rows) => {
        setError(null);
        setAdmins(rows);
      })
      .catch((e) => setError(apiErrorMessage(e)));
  }, []);

  useEffect(() => {
    if (isSuper) load();
  }, [isSuper, load]);

  async function findPerson(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      setFound(await platformPeople(q.trim()));
    } catch (err) {
      setError(apiErrorMessage(err));
    }
  }

  async function grant() {
    if (!candidate) return;
    setError(null);
    setBusy(true);
    try {
      const r = await platformRequestAdminChange({
        action: 'GrantAdmin',
        targetUserId: candidate.userId,
        level,
        reason: reason.trim(),
      });
      setInfo(t('Ombi limetumwa. Msimamizi mwingine (Msimamizi Mkuu) anahitaji kuidhinisha — ombi namba {0}.', { 0: r.approvalId.slice(0, 8) }));
      setReason('');
      setCandidate(null);
      reload();
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  async function decide(a: ApprovalRow, approve: boolean) {
    setError(null);
    setBusy(true);
    try {
      await platformDecide(a.id, approve, note.trim());
      setNote('');
      reload();
      load();
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  async function cancel(a: ApprovalRow) {
    setError(null);
    setBusy(true);
    try {
      await platformCancel(a.id);
      reload();
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  async function resetMfa(a: AdminRow) {
    setError(null);
    setBusy(true);
    try {
      await platformRequestMfaReset(a.id, reason.trim() || t('Simu imepotea; MFA mpya inahitajika'));
      setInfo(t('Ombi la MFA mpya limetumwa kwa msimamizi mwingine.'));
      reload();
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  if (!isSuper) {
    return (
      <Section title={t('Wasimamizi')}>
        <p className="text-sm text-ink-700">
          {t('Orodha ya wasimamizi na mabadiliko yao yanaonekana kwa Msimamizi Mkuu pekee. Maamuzi ya 4-eyes yanapatikana kwenye kichupo cha Vikundi na Wasimamizi kwa ngazi yako.')}
        </p>
        <Approvals pending={pending} busy={busy} note={note} setNote={setNote} onDecide={decide} onCancel={cancel} error={error} />
      </Section>
    );
  }

  return (
    <div className="space-y-6">
      <Section title={t('Maamuzi yanayosubiri (4-eyes)')}>
        {error && <p role="alert" className="text-sm text-standing-overdue mb-3">{error}</p>}
        <Approvals pending={pending} busy={busy} note={note} setNote={setNote} onDecide={decide} onCancel={cancel} />
      </Section>

      <Section title={t('Wasimamizi wa mfumo')}>
        {info && <p role="status" className="text-sm text-standing-good mb-3">{info}</p>}
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-left text-xs uppercase tracking-wide text-ink-600">
                <th className="py-2 pr-3">{t('Jina')}</th>
                <th className="py-2 pr-3">{t('Ngazi')}</th>
                <th className="py-2 pr-3">{t('MFA')}</th>
                <th className="py-2 pr-3">{t('Hali')}</th>
                <th className="py-2" />
              </tr>
            </thead>
            <tbody>
              {admins.map((a) => (
                <tr key={a.id} className="border-b border-ink-100 last:border-0">
                  <td className="py-2 pr-3">
                    <div className="font-medium text-ink-900">{a.name ?? '—'}</div>
                    <div className="text-xs text-ink-600">{a.email ?? '—'}</div>
                  </td>
                  <td className="py-2 pr-3 text-ink-700">{levelLabel(a.level)}</td>
                  <td className="py-2 pr-3 text-ink-700">{a.mfaEnabled ? t('Imewashwa') : t('Haijawashwa')}</td>
                  <td className="py-2 pr-3 text-ink-700">{a.isActive ? t('Hai') : t('Ameondolewa')}</td>
                  <td className="py-2 text-right">
                    {a.isActive && a.userId !== me.userId && (
                      <button type="button" onClick={() => resetMfa(a)} disabled={busy} className={btnGhostCls}>
                        {t('MFA mpya')}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Section>

      <Section title={t('Ongeza msimamizi (4-eyes)')}>
        <form onSubmit={findPerson} className="flex flex-wrap gap-2">
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder={t('Barua pepe au namba ya simu')}
            className="flex-1 min-w-56 rounded-lg border border-ink-100 bg-paper px-3 py-2 text-sm"
            aria-label={t('Tafuta mtumiaji')}
          />
          <button type="submit" disabled={q.trim().length < 3} className={btnGhostCls}>{t('Tafuta')}</button>
        </form>

        {found.length > 0 && (
          <ul className="mt-3 space-y-1">
            {found.map((p) => (
              <li key={p.userId}>
                <button
                  type="button"
                  onClick={() => setCandidate(p)}
                  className={`w-full text-left rounded-lg px-3 py-2 text-sm ${candidate?.userId === p.userId ? 'bg-ink-50 text-ink-950' : 'hover:bg-ink-50 text-ink-800'}`}
                >
                  {p.fullName} · {p.email}
                </button>
              </li>
            ))}
          </ul>
        )}

        {candidate && (
          <div className="mt-4 space-y-3 rounded-xl border border-ink-100 bg-paper p-3">
            <p className="text-sm text-ink-900">{t('Kumpa {0} nafasi ya msimamizi:', { 0: candidate.fullName })}</p>
            <div className="flex flex-wrap items-center gap-2">
              {(['Support', 'Oversight', 'SuperAdmin'] as PlatformLevel[]).map((l) => (
                <button
                  key={l}
                  type="button"
                  onClick={() => setLevel(l)}
                  className={`rounded-lg border px-3 py-1.5 text-sm ${
                    level === l ? 'border-ink-900 bg-ink-900 text-ink-50' : 'border-ink-100 bg-paper-raised text-ink-800'
                  }`}
                >
                  {levelLabel(l)}
                </button>
              ))}
            </div>
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              rows={2}
              placeholder={t('Sababu ya ombi…')}
              className="w-full rounded-lg border border-ink-100 bg-paper-raised px-3 py-2 text-sm"
            />
            <button type="button" onClick={grant} disabled={busy || reason.trim().length < 5} className={btnCls}>
              {t('Tuma ombi')}
            </button>
            <p className="text-xs text-ink-600">
              {t('Msimamizi mmoja hawezi kuongeza mwingine peke yake; Msimamizi Mkuu wa pili anaidhinisha.')}
            </p>
          </div>
        )}
      </Section>
    </div>
  );
}

function Approvals({
  pending,
  busy,
  note,
  setNote,
  onDecide,
  onCancel,
  error,
}: {
  pending: ApprovalRow[];
  busy: boolean;
  note: string;
  setNote: (v: string) => void;
  onDecide: (a: ApprovalRow, approve: boolean) => void;
  onCancel: (a: ApprovalRow) => void;
  error?: string | null;
}) {
  if (pending.length === 0) return <p className="text-sm text-ink-600">{t('Hakuna maamuzi yanayosubiri.')}</p>;
  return (
    <div className="space-y-3">
      {error && <p role="alert" className="text-sm text-standing-overdue">{error}</p>}
      <input
        value={note}
        onChange={(e) => setNote(e.target.value)}
        placeholder={t('Maelezo ya uamuzi (lazima kwa kukataa)…')}
        className="w-full rounded-lg border border-ink-100 bg-paper px-3 py-2 text-sm"
      />
      {pending.map((a) => (
        <div key={a.id} className="rounded-xl border border-ink-100 bg-paper p-3 space-y-2">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <div className="font-medium text-ink-900">{actionLabel(a.action)}</div>
              <div className="text-xs text-ink-600">
                {a.groupName ? `${t('Kikundi')}: ${a.groupName} · ` : ''}
                {t('Imeombwa na {0}', { 0: a.requestedByName ?? '—' })} · {fmtDate(a.createdAt)}
              </div>
              <div className="text-sm text-ink-800 mt-1">{a.reason}</div>
            </div>
            <div className="flex flex-wrap gap-2">
              {a.canDecide ? (
                <>
                  <button type="button" onClick={() => onDecide(a, true)} disabled={busy} className={btnCls}>{t('Idhinisha')}</button>
                  <button type="button" onClick={() => onDecide(a, false)} disabled={busy || note.trim().length < 3} className={btnGhostCls}>{t('Kataa')}</button>
                </>
              ) : (
                <span className="text-xs text-ink-600">
                  {a.isMine ? t('Wewe ndiye uliyeomba — mwingine anaidhinisha.') : t('Hauwezi kuamua ombi hili.')}
                </span>
              )}
              {a.isMine && (
                <button type="button" onClick={() => onCancel(a)} disabled={busy} className="text-xs text-ink-600 underline">{t('Futa ombi')}</button>
              )}
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}

// ===================================================================== KUMBUKUMBU
function AuditTab() {
  const [rows, setRows] = useState<AuditRow[]>([]);
  const [action, setAction] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    platformAudit({ action: action.trim() || undefined, take: 200 })
      .then((r) => {
        setError(null);
        setRows(r);
      })
      .catch((e) => setError(apiErrorMessage(e)));
  }, [action]);

  return (
    <Section
      title={t('Kumbukumbu za msimamizi')}
      actions={
        <input
          value={action}
          onChange={(e) => setAction(e.target.value)}
          placeholder={t('Chuja kwa kitendo (mf. break_glass_approved)')}
          className="rounded-lg border border-ink-100 bg-paper px-3 py-1.5 text-sm w-72"
          aria-label={t('Chuja kumbukumbu')}
        />
      }
    >
      {error && <p role="alert" className="text-sm text-standing-overdue mb-3">{error}</p>}
      <p className="text-xs text-ink-600 mb-3">
        {t('Kumbukumbu hizi haziwezi kufutwa wala kubadilishwa — hata na msimamizi mkuu. Kila kitendo kinachoigusa kikundi kinaonekana pia kwa wanachama.')}
      </p>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-ink-100 text-left text-xs uppercase tracking-wide text-ink-600">
              <th className="py-2 pr-3">{t('Muda')}</th>
              <th className="py-2 pr-3">{t('Kitendo')}</th>
              <th className="py-2 pr-3">{t('Msimamizi')}</th>
              <th className="py-2 pr-3">{t('Kikundi')}</th>
              <th className="py-2">{t('Sababu / maelezo')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} className="border-b border-ink-100 last:border-0 align-top">
                <td className="py-2 pr-3 text-xs text-ink-600 whitespace-nowrap">{fmtDate(r.at)}</td>
                <td className="py-2 pr-3 font-mono text-xs">{r.action}</td>
                <td className="py-2 pr-3">{r.actorName ?? '—'}</td>
                <td className="py-2 pr-3">{r.groupName ?? '—'}</td>
                <td className="py-2 text-ink-700">
                  {r.reason ?? '—'}
                  {r.detailJson && <div className="text-xs text-ink-600 font-mono break-all">{r.detailJson}</div>}
                </td>
              </tr>
            ))}
            {rows.length === 0 && (
              <tr>
                <td colSpan={5} className="py-6 text-center text-ink-600">{t('Hakuna kumbukumbu.')}</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </Section>
  );
}
