import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { motion } from 'framer-motion';
import {
  Users,
  UserPlus,
  Inbox,
  KeyRound,
  Search,
  Copy,
  RefreshCw,
  Hash,
  Link2,
  ShieldCheck,
  Smartphone,
  UserCog,
  Check,
} from 'lucide-react';
import { Tabs, type Tab } from '../../components/Tabs';
import { Button, Card, EmptyState, Field, Input, Loading, Modal, Notice, Select, Skeleton, Textarea } from '../../components/ui';
import { useMyGroups } from '../../hooks/useMyGroups';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { groupRoleLabel } from '../../lib/status';
import { LEADER_ROLES, matches, wallDate } from '../../lib/truth';
import { apiErrorMessage } from '../../api/truth';
import {
  addMemberRecord,
  changeRole,
  decideRequest,
  getGroupProfile,
  getMembersAdmin,
  getPendingRequests,
  issueMemberNumbers,
  regenerateJoinCode,
  setShortCode,
  type GroupProfile,
  type IssueResult,
  type MemberRow,
  type PendingRequest,
} from '../../api/membership';
import { PageHeader } from '../truth/TruthProvider';

import { t, tk } from '../../i18n';
const ROLE_OPTIONS = ['Member', 'Secretary', 'Treasurer', 'MemberRepresentative', 'Chairperson'] as const;

function localPhone(p: string) {
  return p.startsWith('255') ? '0' + p.slice(3) : p;
}

/**
 * /wanachama/:tab — viongozi: orodha ya wanachama (rekodi za fedha na akaunti zao), maombi ya kujiunga,
 * kodi ya kujiunga na namba rasmi za uanachama. Ruhusa halisi zinakaguliwa na seva.
 */
export function MembersPage() {
  const { tab = 'orodha' } = useParams();
  const navigate = useNavigate();
  const { activeGroup, activeGroupId } = useMyGroups();
  const [version, setVersion] = useState(0);
  const bump = () => setVersion((v) => v + 1);

  const isLeader = !!activeGroup && LEADER_ROLES.has(activeGroup.role);
  const isChair = activeGroup?.role === 'Chairperson';
  const profile = useApi(isLeader && activeGroupId ? () => getGroupProfile(activeGroupId) : null, activeGroupId, [version]);
  const members = useApi(isLeader && activeGroupId ? () => getMembersAdmin(activeGroupId) : null, activeGroupId, [version]);
  const requests = useApi(isLeader && activeGroupId ? () => getPendingRequests(activeGroupId) : null, activeGroupId, [version]);

  if (!activeGroup || !activeGroupId) return <Loading />;
  if (!isLeader) return <Notice tone="warn">{t("Ukurasa huu ni wa viongozi wa kikundi.")}</Notice>;

  const TABS: Tab[] = [
    { id: 'orodha', label: tk('Wanachama'), icon: Users },
    { id: 'maombi', label: tk('Maombi ya kujiunga'), icon: Inbox, badge: requests.data?.length ?? 0 },
    { id: 'kodi', label: tk('Kodi na namba'), icon: KeyRound },
  ];
  const active = TABS.some((x) => x.id === tab) ? tab : 'orodha';

  return (
    <div className="space-y-5">
      <PageHeader
        eyebrow={t("Wanachama")}
        icon={Users}
        title={activeGroup.groupName}
        description={t("Mwanachama ni rekodi ya fedha (michango yake inatoka M-Koba). Akaunti ni ya kuingia kwenye mfumo — mwanachama anaweza kuwa hana akaunti bado.")}
      />
      <Tabs tabs={TABS} activeTab={active} onTabChange={(id) => navigate(`/wanachama/${id}`)} scrollable />

      {active === 'orodha' && (
        <MembersTab groupId={activeGroupId} rows={members.data} error={members.error} isChair={isChair} profile={profile.data} onChanged={bump} />
      )}
      {active === 'maombi' && (
        <RequestsTab groupId={activeGroupId} rows={requests.data} error={requests.error} members={members.data ?? []} onChanged={bump} />
      )}
      {active === 'kodi' && <CodesTab groupId={activeGroupId} profile={profile.data} error={profile.error} isChair={isChair} onChanged={bump} />}
    </div>
  );
}

// ================================================================ Wanachama
function MembersTab({
  groupId,
  rows,
  error,
  isChair,
  profile,
  onChanged,
}: {
  groupId: string;
  rows?: MemberRow[];
  error?: string;
  isChair: boolean;
  profile?: GroupProfile;
  onChanged: () => void;
}) {
  const [q, setQ] = useState('');
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<MemberRow | null>(null);
  const list = useMemo(() => (rows ?? []).filter((m) => matches(q, m.fullName, m.memberNumber, ...m.phones)), [rows, q]);
  const withoutLogin = (rows ?? []).filter((m) => !m.hasLogin).length;
  const legacyNumbers = (rows ?? []).filter((m) => !m.numberIsOfficial).length;

  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!rows) return <Skeleton className="h-64" />;

  return (
    <div className="space-y-4">
      {isChair && legacyNumbers > 0 && (
        <IssueNumbersNotice groupId={groupId} count={legacyNumbers} shortCode={profile?.shortCode ?? null} onDone={onChanged} />
      )}

      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative sm:w-80">
          <Search size={15} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-500" />
          <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("Tafuta jina, namba au simu")} className="pl-9" />
        </div>
        <div className="flex items-center justify-between gap-3 sm:justify-end">
          <span className="text-xs text-ink-600">
            {rows.length}{' '}{t("wanachama ·")}{' '}{withoutLogin}{' '}{t("bila akaunti")}
          </span>
          <Button icon={UserPlus} onClick={() => setAdding(true)} className="shrink-0 whitespace-nowrap">
            <span>
              {t("Ongeza")}<span className="hidden sm:inline">{' '}{t("mwanachama")}</span>
            </span>
          </Button>
        </div>
      </div>

      {list.length === 0 ? (
        <EmptyState icon={Users} title={q ? t("Hakuna anayelingana na utafutaji") : t("Bado hakuna wanachama")}>
          {q ? t("Jaribu sehemu ya jina au tarakimu 4 za mwisho za simu.") : t("Ongeza mwanachama wa kwanza, au wape watu kodi ya kujiunga.")}
        </EmptyState>
      ) : (
        <Card bodyClassName="p-0">
          <ul className="divide-y divide-ink-100">
            {list.map((m) => (
              <li key={m.memberId} className="flex flex-col gap-2 px-4 py-3 sm:flex-row sm:items-center sm:gap-4 sm:px-5">
                <div className="flex min-w-0 flex-1 items-start gap-3">
                  <span
                    className={`figure mt-0.5 shrink-0 rounded-md px-2 py-0.5 text-xs ${
                      m.numberIsOfficial ? 'theme-chrome bg-ink-900 text-gold-500' : 'bg-ink-50 text-ink-600'
                    }`}
                    title={m.numberIsOfficial ? t("Namba rasmi (haibadiliki)") : t("Namba ya zamani — haijatolewa rasmi")}
                  >
                    {m.memberNumber || '—'}
                  </span>
                  <div className="min-w-0">
                    <div className="truncate font-medium text-ink-950">{m.fullName}</div>
                    <div className="mt-0.5 flex flex-wrap gap-x-3 gap-y-1 text-xs text-ink-600">
                      <span className={m.role === 'Member' ? '' : 'font-medium text-gold-700'}>{groupRoleLabel(m.role)}</span>
                      <span className="inline-flex items-center gap-1">
                        <Smartphone size={12} />
                        {m.phones.length ? m.phones.map(localPhone).join(', ') : <span className="text-standing-warn">{t("hana namba ya M-Koba")}</span>}
                      </span>
                    </div>
                  </div>
                </div>
                <div className="flex items-center justify-between gap-3 pl-[3.25rem] sm:pl-0">
                  <span
                    className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium ${
                      m.hasLogin ? 'bg-standing-good-bg text-standing-good' : 'bg-ink-50 text-ink-600'
                    }`}
                    title={m.email ?? t("Rekodi ya fedha tu — mtu huyu bado hajajisajili na kuunganishwa")}
                  >
                    {m.hasLogin ? <ShieldCheck size={12} /> : null}
                    {m.hasLogin ? t("Ana akaunti") : t("Hana akaunti")}
                  </span>
                  {isChair && (
                    <Button size="sm" variant="ghost" icon={UserCog} onClick={() => setEditing(m)}>
                      {t("Nafasi")}
                    </Button>
                  )}
                </div>
              </li>
            ))}
          </ul>
        </Card>
      )}

      <AddMemberModal open={adding} onClose={() => setAdding(false)} groupId={groupId} onDone={onChanged} />
      <RoleModal member={editing} onClose={() => setEditing(null)} groupId={groupId} onDone={onChanged} />
    </div>
  );
}

function IssueNumbersNotice({ groupId, count, shortCode, onDone }: { groupId: string; count: number; shortCode: string | null; onDone: () => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<IssueResult | null>(null);
  const year = new Date().getFullYear();

  async function run() {
    setBusy(true);
    try {
      const r = await issueMemberNumbers(groupId);
      setResult(r);
      toast(t("Namba {0} zimetolewa.", { 0: r.issued }), 'good');
      onDone();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <Notice
        tone="warn"
        title={t("Wanachama {0} bado hawana namba rasmi", { 0: count })}
        action={
          shortCode ? (
            <Button size="sm" icon={Hash} onClick={() => setConfirm(true)}>
              {t("Toa namba")}
            </Button>
          ) : (
            <Button size="sm" variant="secondary" onClick={() => navigate('/wanachama/kodi')}>
              {t("Weka kifupi kwanza")}
            </Button>
          )
        }
      >
        {t("Muundo:")}{' '}<span className="figure">{(shortCode ?? 'UMK') + `-${year}-014`}</span>{t(". Namba za zamani za tarakimu (mf. 14 ya Excel) zinahifadhiwa.")}
      </Notice>
      <Modal
        open={confirm}
        onClose={() => {
          setConfirm(false);
          setResult(null);
        }}
        title={t("Toa namba rasmi za uanachama")}
        footer={
          result ? (
            <Button onClick={() => (setConfirm(false), setResult(null))}>{t("Sawa")}</Button>
          ) : (
            <>
              <Button variant="secondary" onClick={() => setConfirm(false)} disabled={busy}>
                {t("Ghairi")}
              </Button>
              <Button onClick={run} loading={busy}>
                {t("Toa namba")}{' '}{count}
              </Button>
            </>
          )
        }
      >
        {result ? (
          <div className="space-y-2">
            <p className="text-sm text-ink-700">{t("Namba hizi sasa ni za kudumu:")}</p>
            <ul className="max-h-72 divide-y divide-ink-100 overflow-y-auto rounded-xl border border-ink-100 text-sm">
              {result.changes.map((c) => (
                <li key={c.newNumber} className="flex items-center justify-between gap-3 px-3 py-2">
                  <span className="truncate">{c.fullName}</span>
                  <span className="figure shrink-0 text-xs">
                    <span className="text-ink-500">{c.oldNumber || '—'} → </span>
                    <span className="font-semibold text-ink-950">{c.newNumber}</span>
                  </span>
                </li>
              ))}
            </ul>
          </div>
        ) : (
          <div className="space-y-3 text-sm text-ink-700">
            <p>
              {t("Kila mwanachama asiye na namba rasmi atapewa namba ya muundo")}{' '}<span className="figure">{shortCode}-{year}{t("-NNN")}</span>.
            </p>
            <ul className="list-disc space-y-1 pl-5">
              <li>{t("Namba za zamani za tarakimu zinahifadhiwa: 14 →")}{' '}{shortCode}-{year}-014.</li>
              <li>{t("Wengine wanapewa namba zinazofuata.")}</li>
              <li>
                <strong>{t("Namba zikishatolewa hazibadiliki")}</strong>{t(", na kifupi")}{' '}<span className="figure">{shortCode}</span>{' '}{t("kinafungwa.")}
              </li>
            </ul>
          </div>
        )}
      </Modal>
    </>
  );
}

function AddMemberModal({ open, onClose, groupId, onDone }: { open: boolean; onClose: () => void; groupId: string; onDone: () => void }) {
  const toast = useToast();
  const [form, setForm] = useState({ fullName: '', phone: '', joinedAt: '' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    setBusy(true);
    setError(null);
    try {
      const row = await addMemberRecord(groupId, {
        fullName: form.fullName,
        phone: form.phone || undefined,
        joinedAt: form.joinedAt ? `${form.joinedAt}T00:00:00Z` : undefined,
      });
      toast(t("{0} ameongezwa ({1}).", { 0: row.fullName, 1: row.memberNumber || t("bila namba") }), 'good');
      setForm({ fullName: '', phone: '', joinedAt: '' });
      onDone();
      onClose();
    } catch (e) {
      setError(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={t("Ongeza mwanachama")}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t("Ghairi")}
          </Button>
          <Button onClick={save} loading={busy} disabled={form.fullName.trim().length < 2}>
            {t("Hifadhi")}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <p className="text-sm text-ink-600">
          {t("Kwa mwanachama ambaye hana akaunti (mf. hana simu janja). Akijisajili baadaye kwa namba hii, utaombwa kuthibitisha kumuunganisha na rekodi hii.")}
        </p>
        <Field label={t("Jina kamili")}>
          <Input value={form.fullName} onChange={(e) => setForm((f) => ({ ...f, fullName: e.target.value }))} placeholder={t("Kama lilivyo kwenye daftari")} />
        </Field>
        <Field label={t("Namba ya simu ya M-Koba")} hint={t("Muhimu: malipo yake ya M-Koba yanatambuliwa kwa namba hii.")}>
          <Input
            value={form.phone}
            onChange={(e) => setForm((f) => ({ ...f, phone: e.target.value }))}
            placeholder="0712 345 678"
            inputMode="tel"
          />
        </Field>
        <Field label={t("Tarehe ya kujiunga (hiari)")} hint={t("Waanzilishi: acha tupu — injini inatumia malipo ya kwanza ya M-Koba.")}>
          <Input type="date" value={form.joinedAt} onChange={(e) => setForm((f) => ({ ...f, joinedAt: e.target.value }))} />
        </Field>
        {error && <Notice tone="overdue">{error}</Notice>}
      </div>
    </Modal>
  );
}

function RoleModal({ member, onClose, groupId, onDone }: { member: MemberRow | null; onClose: () => void; groupId: string; onDone: () => void }) {
  const toast = useToast();
  const [role, setRole] = useState<string>('Member');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastId, setLastId] = useState<string | null>(null);
  // Anza na nafasi ya sasa kila mwanachama mpya anapochaguliwa
  if (member && member.memberId !== lastId) {
    setLastId(member.memberId);
    setRole(member.role);
    setError(null);
  }
  const handOver = role === 'Chairperson' && member?.role !== 'Chairperson';

  async function save() {
    if (!member) return;
    setBusy(true);
    setError(null);
    try {
      await changeRole(groupId, member.memberId, role);
      toast(handOver ? t("Uenyekiti umekabidhiwa.") : t("Nafasi imebadilishwa."), 'good');
      onDone();
      onClose();
      if (handOver) window.location.assign('/');
    } catch (e) {
      setError(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      open={!!member}
      onClose={onClose}
      title={t("Nafasi ya {0}", { 0: member?.fullName ?? '' })}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {t("Ghairi")}
          </Button>
          <Button variant={handOver ? 'danger' : 'primary'} onClick={save} loading={busy} disabled={role === member?.role}>
            {handOver ? t("Kabidhi uenyekiti") : t("Hifadhi")}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label={t("Nafasi")} hint={t("Katibu na Mtunza-hazina wanaweza kuwa zaidi ya mmoja (manaibu).")}>
          <Select value={role} onChange={(e) => setRole(e.target.value)}>
            {ROLE_OPTIONS.map((r) => (
              <option key={r} value={r}>
                {groupRoleLabel(r)}
              </option>
            ))}
          </Select>
        </Field>
        {!member?.hasLogin && role !== 'Member' && (
          <Notice tone="warn">{t("Nafasi ya uongozi inahitaji akaunti. Mtu huyu ajisajili kwanza, kisha umuunganishe kupitia ombi lake.")}</Notice>
        )}
        {handOver && (
          <Notice tone="overdue" title={t("Unakabidhi uenyekiti")}>
            {member?.fullName}{' '}{t("atakuwa Mwenyekiti, na wewe utakuwa Mwanachama wa kawaida. Hatua hii inaandikwa kwenye kumbukumbu za ukaguzi.")}
          </Notice>
        )}
        {error && <Notice tone="overdue">{error}</Notice>}
      </div>
    </Modal>
  );
}

// ================================================================ Maombi
function RequestsTab({
  groupId,
  rows,
  error,
  members,
  onChanged,
}: {
  groupId: string;
  rows?: PendingRequest[];
  error?: string;
  members: MemberRow[];
  onChanged: () => void;
}) {
  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!rows) return <Skeleton className="h-48" />;
  if (rows.length === 0)
    return (
      <EmptyState icon={Inbox} title={t("Hakuna maombi yanayosubiri")}>
        {t("Watu wakiomba kujiunga kwa kodi ya kikundi, maombi yao yataonekana hapa.")}
      </EmptyState>
    );
  const unlinked = members.filter((m) => !m.hasLogin);
  return (
    <div className="space-y-3">
      {rows.map((r) => (
        <RequestCard
          key={r.requestId}
          groupId={groupId}
          r={r}
          unlinked={unlinked}
          // Namba ya simu hii tayari ni ya mwanachama mwenye akaunti => huenda ni mtu anayejifanya mwingine
          phoneOwner={members.find((m) => m.hasLogin && m.phones.includes(r.phone)) ?? null}
          onDone={onChanged}
        />
      ))}
    </div>
  );
}

function RequestCard({
  groupId,
  r,
  unlinked,
  phoneOwner,
  onDone,
}: {
  groupId: string;
  r: PendingRequest;
  unlinked: MemberRow[];
  phoneOwner: MemberRow | null;
  onDone: () => void;
}) {
  const toast = useToast();
  // Pendekezo la simu linachaguliwa mwanzoni lakini kiongozi ndiye anathibitisha (au anachagua jingine / mpya)
  const [link, setLink] = useState<string>(r.suggested?.memberId ?? '');
  const [rejecting, setRejecting] = useState(false);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  const chosen = unlinked.find((m) => m.memberId === link) ?? (r.suggested?.memberId === link ? r.suggested : null);

  async function decide(approve: boolean) {
    setBusy(true);
    try {
      await decideRequest(groupId, r.requestId, approve, approve && link ? link : null, approve ? undefined : note);
      setDone(true);
      toast(approve ? t("{0} ameidhinishwa.", { 0: r.fullName }) : t("Ombi limekataliwa."), approve ? 'good' : 'neutral');
      setTimeout(onDone, 600);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
      setRejecting(false);
    }
  }

  return (
    <motion.div animate={done ? { opacity: 0.5 } : { opacity: 1 }}>
      <Card bodyClassName="p-4 sm:p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <div className="font-semibold text-ink-950">{r.fullName}</div>
            <div className="mt-0.5 flex flex-wrap gap-x-3 gap-y-1 text-xs text-ink-600">
              <span className="figure">{localPhone(r.phone)}</span>
              <span>{r.email}</span>
              <span>{t("Aliomba")}{' '}{wallDate(r.createdAt)}</span>
            </div>
          </div>
          <span className={`rounded-full px-2.5 py-1 text-xs font-medium ${r.requestedRole === 'Member' ? 'bg-ink-50 text-ink-700' : 'bg-gold-100 text-gold-700'}`}>
            {t("Anaomba:")}{' '}{groupRoleLabel(r.requestedRole)}
          </span>
        </div>

        {phoneOwner && (
          <div className="mt-3">
            <Notice tone="warn" title={t("Namba hii ya simu tayari ina akaunti")}>
              {t("Ni ya")}{' '}{phoneOwner.fullName} ({phoneOwner.memberNumber}{t("), ambaye tayari ameunganishwa. Hakikisha huyu si mtu anayejifanya kuwa yeye kabla ya kuidhinisha.")}
            </Notice>
          </div>
        )}
        {r.note && <p className="mt-3 rounded-lg bg-ink-50 px-3 py-2 text-sm text-ink-800">“{r.note}”</p>}

        <div className="mt-4 space-y-2">
          <div className="flex items-center gap-1.5 text-sm font-medium text-ink-900">
            <Link2 size={15} className="text-gold-700" />{' '}{t("Ni mwanachama aliyepo tayari?")}
          </div>
          {r.suggested && (
            <p className="text-xs text-standing-good">
              {t("Namba yake ya simu inalingana na")}{' '}<strong>{r.suggested.fullName}</strong>
              {r.suggested.memberNumber ? ` (${r.suggested.memberNumber})` : ''}{t(". Thibitisha kuwa ni mtu yule yule.")}
            </p>
          )}
          <Select value={link} onChange={(e) => setLink(e.target.value)} disabled={done}>
            <option value="">{t("Hapana — ni mwanachama mpya")}</option>
            {r.suggested && !unlinked.some((m) => m.memberId === r.suggested!.memberId) && (
              <option value={r.suggested.memberId}>
                {r.suggested.fullName} {r.suggested.memberNumber ? `· ${r.suggested.memberNumber}` : ''}{' '}{t("(pendekezo)")}
              </option>
            )}
            {unlinked.map((m) => (
              <option key={m.memberId} value={m.memberId}>
                {m.fullName}
                {m.memberNumber ? ` · ${m.memberNumber}` : ''}
                {m.memberId === r.suggested?.memberId ? t(" (namba ya simu inalingana)") : ''}
              </option>
            ))}
          </Select>
          <p className="text-xs text-ink-600">
            {chosen
              ? t("Akaunti itaunganishwa na rekodi ya {0}: historia yake yote ya michango inabaki, namba ile ile.", { 0: chosen.fullName })
              : t("Atatengenezewa rekodi mpya ya mwanachama na namba mpya.")}
          </p>
        </div>

        {!r.youCanDecide && (
          <div className="mt-3">
            <Notice tone="neutral">{t("Ombi la")}{' '}{groupRoleLabel(r.requestedRole)}{' '}{t("linaidhinishwa na Mwenyekiti pekee.")}</Notice>
          </div>
        )}

        {rejecting ? (
          <div className="mt-4 space-y-2 border-t border-ink-100 pt-3">
            <Field label={t("Sababu ya kukataa")} hint={t("Muombaji ataiona.")}>
              <Textarea value={note} onChange={(e) => setNote(e.target.value)} rows={2} placeholder={t("Mf. Hajulikani kwa kikundi")} />
            </Field>
            <div className="flex justify-end gap-2">
              <Button variant="secondary" onClick={() => setRejecting(false)} disabled={busy}>
                {t("Ghairi")}
              </Button>
              <Button variant="danger" onClick={() => decide(false)} loading={busy} disabled={note.trim().length < 3}>
                {t("Kataa ombi")}
              </Button>
            </div>
          </div>
        ) : (
          <div className="mt-4 flex flex-wrap justify-end gap-2 border-t border-ink-100 pt-3">
            <Button variant="secondary" onClick={() => setRejecting(true)} disabled={!r.youCanDecide || busy || done}>
              {t("Kataa")}
            </Button>
            <Button variant="good" icon={done ? Check : undefined} onClick={() => decide(true)} loading={busy} disabled={!r.youCanDecide || done}>
              {done ? t("Imeidhinishwa") : chosen ? t("Idhinisha na unganisha") : t("Idhinisha")}
            </Button>
          </div>
        )}
      </Card>
    </motion.div>
  );
}

// ================================================================ Kodi na namba
function CodesTab({
  groupId,
  profile,
  error,
  isChair,
  onChanged,
}: {
  groupId: string;
  profile?: GroupProfile;
  error?: string;
  isChair: boolean;
  onChanged: () => void;
}) {
  const toast = useToast();
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!profile) return <Skeleton className="h-48" />;
  const link = profile.joinCode ? `${window.location.origin}/jiunge/${profile.joinCode}` : '';

  async function act(key: string, fn: () => Promise<unknown>, ok: string) {
    setBusy(key);
    try {
      await fn();
      toast(ok, 'good');
      onChanged();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(null);
    }
  }
  async function copy(text: string) {
    try {
      await navigator.clipboard.writeText(text);
      toast(t("Imenakiliwa."), 'good');
    } catch {
      toast(t("Imeshindwa kunakili."), 'warn');
    }
  }

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <Card title={t("Kodi ya kujiunga")} subtitle={t("Watu wanaitumia kuomba kujiunga. Kila ombi linasubiri idhini ya viongozi.")}>
        {profile.joinCode ? (
          <div className="space-y-3">
            <div className="theme-chrome figure w-fit rounded-lg bg-ink-900 px-3 py-2 text-xl tracking-widest text-gold-500">{profile.joinCode}</div>
            <div className="flex flex-wrap gap-2">
              <Button size="sm" variant="secondary" icon={Copy} onClick={() => copy(profile.joinCode!)}>
                {t("Nakili kodi")}
              </Button>
              <Button size="sm" variant="secondary" icon={Copy} onClick={() => copy(link)}>
                {t("Nakili kiungo")}
              </Button>
            </div>
          </div>
        ) : (
          <p className="text-sm text-ink-600">{t("Kikundi hiki bado hakina kodi ya kujiunga.")}</p>
        )}
        {isChair && (
          <div className="mt-4 border-t border-ink-100 pt-3">
            <Button
              size="sm"
              variant={profile.joinCode ? 'ghost' : 'primary'}
              icon={RefreshCw}
              loading={busy === 'join'}
              onClick={() => act('join', () => regenerateJoinCode(groupId), profile.joinCode ? t("Kodi mpya imetengenezwa.") : t("Kodi imetengenezwa."))}
            >
              {profile.joinCode ? t("Badilisha kodi (ya zamani itaacha kufanya kazi)") : t("Tengeneza kodi")}
            </Button>
          </div>
        )}
      </Card>

      <Card title={t("Kifupi cha namba za uanachama")} subtitle={t("Mf. UMK → UMK-2026-001")}>
        {profile.shortCodeLocked ? (
          <div className="space-y-2">
            <div className="figure w-fit rounded-lg bg-ink-50 px-3 py-2 text-lg tracking-widest text-ink-900">{profile.shortCode}</div>
            <p className="text-xs text-ink-600">{t("Kimefungwa: namba rasmi zimeshatolewa, na hazibadiliki.")}</p>
          </div>
        ) : isChair ? (
          <div className="flex flex-col gap-2 sm:flex-row">
            <Input
              value={code || profile.shortCode || ''}
              onChange={(e) => setCode(e.target.value.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 5))}
              placeholder="UMK"
              className="figure uppercase tracking-wider sm:max-w-[10rem]"
            />
            <Button
              variant="secondary"
              loading={busy === 'short'}
              disabled={!code || code === profile.shortCode}
              onClick={() => act('short', () => setShortCode(groupId, code), t("Kifupi kimehifadhiwa."))}
            >
              {t("Hifadhi")}
            </Button>
          </div>
        ) : (
          <p className="text-sm text-ink-700">{profile.shortCode ?? t("Bado hakijawekwa (kazi ya Mwenyekiti).")}</p>
        )}
      </Card>
    </div>
  );
}
