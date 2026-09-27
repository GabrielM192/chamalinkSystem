import { useState, type FormEvent } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { motion } from 'framer-motion';
import { Check, Copy, KeyRound, PlusCircle, Clock3, XCircle, ArrowRight } from 'lucide-react';
import { useAuth } from '../../auth/AuthContext';
import { useMyGroups } from '../../hooks/useMyGroups';
import { apiErrorMessage } from '../../api/truth';
import {
  GROUP_TYPE_LABEL,
  cancelMyRequest,
  createGroup,
  getMyRequests,
  previewJoin,
  requestToJoin,
  type GroupCreated,
  type GroupTypeName,
  type JoinPreview,
  type MyRequest,
  type RequestableRole,
} from '../../api/membership';
import { Button, Card, Field, Input, Notice, Segmented, Textarea } from '../../components/ui';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { groupRoleLabel } from '../../lib/status';
import { wallDate } from '../../lib/truth';

import { t } from '../../i18n';
type Mode = 'join' | 'create';

/**
 * /karibu na /jiunge/:code — hatua ya kwanza baada ya kujisajili:
 *  - kujiunga na kikundi kwa kodi (ombi linasubiri idhini ya viongozi), au
 *  - kuanzisha kikundi kipya (unakuwa Mwenyekiti).
 */
export function WelcomePage() {
  const { code } = useParams();
  const { user } = useAuth();
  const { groups } = useMyGroups();
  const [mode, setMode] = useState<Mode>('join');
  const [version, setVersion] = useState(0);
  const mine = useApi(() => getMyRequests(), user?.userId, [version]);
  const pending = (mine.data ?? []).filter((r) => r.status === 'Pending');
  const decided = (mine.data ?? []).filter((r) => r.status === 'Rejected').slice(0, 3);

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight text-ink-950">{t("Karibu,")}{' '}{user?.fullName?.split(' ')[0]}</h1>
        <p className="mt-1 text-sm text-ink-600">
          {groups.length > 0
            ? t("Unaweza kujiunga na kikundi kingine au kuanzisha kipya. Makundi yako yako kwenye kitufe cha kikundi juu.")
            : t("Akaunti yako iko tayari. Hatua inayofuata: jiunge na kikundi chako, au anzisha kikundi kipya.")}
        </p>
      </header>

      {pending.length > 0 && <PendingList items={pending} onChanged={() => setVersion((v) => v + 1)} />}
      {decided.map((r) => (
        <Notice key={r.requestId} tone="overdue" title={t("Ombi la kujiunga na {0} limekataliwa", { 0: r.groupName })}>
          {r.decisionNote}
        </Notice>
      ))}

      <Segmented<Mode>
        value={mode}
        onChange={setMode}
        options={[
          { value: 'join', label: t("Nina kodi ya kujiunga"), hint: t("Mwenyekiti wa kikundi amekupa kodi") },
          { value: 'create', label: t("Anzisha kikundi kipya"), hint: t("Utakuwa Mwenyekiti wake") },
        ]}
      />

      {mode === 'join' ? <JoinCard initialCode={code ?? ''} onRequested={() => setVersion((v) => v + 1)} /> : <CreateCard />}
    </div>
  );
}

function PendingList({ items, onChanged }: { items: MyRequest[]; onChanged: () => void }) {
  const toast = useToast();
  const [busy, setBusy] = useState<string | null>(null);
  async function cancel(id: string) {
    setBusy(id);
    try {
      await cancelMyRequest(id);
      toast(t("Ombi limeondolewa."), 'good');
      onChanged();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(null);
    }
  }
  return (
    <Card title={t("Maombi yanayosubiri idhini")} bodyClassName="divide-y divide-ink-100">
      {items.map((r) => (
        <div key={r.requestId} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 sm:px-5">
          <div className="flex min-w-0 items-start gap-3">
            <Clock3 size={18} className="mt-0.5 shrink-0 text-standing-warn" />
            <div className="min-w-0">
              <div className="font-medium text-ink-950">{r.groupName}</div>
              <div className="text-xs text-ink-600">
                {t("Nafasi:")}{' '}{groupRoleLabel(r.requestedRole)}{' '}{t("· Uliomba")}{' '}{wallDate(r.createdAt)}
              </div>
            </div>
          </div>
          <Button size="sm" variant="ghost" icon={XCircle} loading={busy === r.requestId} onClick={() => cancel(r.requestId)}>
            {t("Ondoa ombi")}
          </Button>
        </div>
      ))}
      <p className="px-4 py-3 text-xs text-ink-600 sm:px-5">
        {t("Viongozi wakiidhinisha, kikundi kitaonekana hapa moja kwa moja ukipakia upya ukurasa.")}
      </p>
    </Card>
  );
}

function JoinCard({ initialCode, onRequested }: { initialCode: string; onRequested: () => void }) {
  const toast = useToast();
  const { reload } = useMyGroups();
  const navigate = useNavigate();
  const [code, setCode] = useState(initialCode);
  const [preview, setPreview] = useState<JoinPreview | null>(null);
  const [role, setRole] = useState<RequestableRole>('Member');
  const [note, setNote] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);

  async function lookUp(e?: FormEvent) {
    e?.preventDefault();
    setTouched(true);
    setError(null);
    setPreview(null);
    if (!code.trim()) return;
    setBusy(true);
    try {
      setPreview(await previewJoin(code));
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  // Kiungo /jiunge/KODI: kikundi kinatafutwa mara moja (mpaka mtumiaji abadilishe kodi au atafute mwenyewe)
  const [touched, setTouched] = useState(false);
  const auto = useApi(initialCode && !touched ? () => previewJoin(initialCode) : null, initialCode);
  const shown = preview ?? (!touched ? auto.data ?? null : null);
  const shownError = error ?? (!touched ? auto.error ?? null : null);

  async function send() {
    setBusy(true);
    setError(null);
    try {
      await requestToJoin(code, role, note || undefined);
      setSent(true);
      toast(t("Ombi limetumwa kwa viongozi."), 'good');
      onRequested();
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  if (sent && shown)
    return (
      <Card>
        <div className="flex items-start gap-3">
          <motion.span
            initial={{ scale: 0.6, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            className="grid size-9 shrink-0 place-items-center rounded-full bg-standing-good-bg text-standing-good"
          >
            <Check size={18} />
          </motion.span>
          <div>
            <div className="font-semibold text-ink-950">{t("Ombi lako limetumwa kwa")}{' '}{shown.groupName}</div>
            <p className="mt-1 text-sm text-ink-600">
              {role === 'Member'
                ? t("Kiongozi yeyote wa kikundi anaweza kukuidhinisha.")
                : t("Nafasi ya {0} inaidhinishwa na Mwenyekiti pekee.", { 0: groupRoleLabel(role) })}{' '}
              {t("Ukishaidhinishwa utaona taarifa yako ya michango — kama ulikuwa mwanachama tayari, viongozi watakuunganisha na kumbukumbu zako za zamani.")}
            </p>
          </div>
        </div>
      </Card>
    );

  return (
    <Card title={t("Jiunge na kikundi")} subtitle={t("Weka kodi uliyopewa na Mwenyekiti (mf. UMK-7X2KQ)")}>
      <form onSubmit={lookUp} className="flex flex-col gap-2 sm:flex-row">
        <Input
          aria-label={t("Kodi ya kujiunga")}
          value={code}
          onChange={(e) => {
            setCode(e.target.value.toUpperCase());
            setPreview(null);
            setTouched(true);
          }}
          placeholder={t("UMK-XXXXX")}
          className="figure uppercase tracking-wider"
          autoCapitalize="characters"
        />
        <Button type="submit" variant="secondary" icon={KeyRound} loading={busy && !shown} className="sm:w-40">
          {t("Tafuta")}
        </Button>
      </form>

      {shownError && (
        <div className="mt-3">
          <Notice tone="overdue">{shownError}</Notice>
        </div>
      )}

      {shown && (
        <div className="mt-5 space-y-4 border-t border-ink-100 pt-4">
          <div>
            <div className="text-lg font-semibold text-ink-950">{shown.groupName}</div>
            <div className="text-xs text-ink-600">{GROUP_TYPE_LABEL[shown.groupType] ? t(GROUP_TYPE_LABEL[shown.groupType].label) : shown.groupType}</div>
          </div>

          {shown.alreadyMember ? (
            <Notice
              tone="good"
              title={t("Tayari wewe ni mwanachama wa kikundi hiki")}
              action={
                <Button
                  size="sm"
                  onClick={() => {
                    reload(shown.groupId);
                    navigate('/');
                  }}
                >
                  {t("Fungua")}
                </Button>
              }
            />
          ) : shown.hasPendingRequest ? (
            <Notice tone="warn">{t("Tayari una ombi linalosubiri idhini ya viongozi wa kikundi hiki.")}</Notice>
          ) : (
            <>
              <Field label={t("Nafasi unayoomba")} hint={t("Nafasi za Katibu na Mtunza-hazina zinaidhinishwa na Mwenyekiti.")}>
                <Segmented<RequestableRole>
                  stack
                  value={role}
                  onChange={setRole}
                  options={[
                    { value: 'Member', label: t("Mwanachama") },
                    { value: 'Secretary', label: t("Katibu") },
                    { value: 'Treasurer', label: t("Mtunza-hazina") },
                  ]}
                />
              </Field>
              <Field label={t("Ujumbe kwa viongozi (hiari)")} hint={t("Mf. “Nilikuwa namba 14 kwenye daftari” — inawasaidia kukutambua.")}>
                <Textarea value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} rows={2} />
              </Field>
              <div className="flex justify-end">
                <Button onClick={send} loading={busy} icon={ArrowRight}>
                  {t("Tuma ombi")}
                </Button>
              </div>
            </>
          )}
        </div>
      )}
    </Card>
  );
}

function CreateCard() {
  const { reload } = useMyGroups();
  const navigate = useNavigate();
  const toast = useToast();
  const [name, setName] = useState('');
  const [type, setType] = useState<GroupTypeName>('MonthlySavings');
  const [shortCode, setShortCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<GroupCreated | null>(null);

  // Pendekezo la kifupi kutoka herufi za kwanza za jina (mtumiaji anaweza kubadilisha)
  const suggested = name
    .split(/\s+/)
    .filter(Boolean)
    .map((w) => w[0])
    .join('')
    .replace(/[^A-Za-z]/g, '')
    .slice(0, 4)
    .toUpperCase();

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const g = await createGroup({ name, type, shortCode: shortCode || suggested });
      setDone(g);
      reload(g.groupId);
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  async function copy(text: string) {
    try {
      await navigator.clipboard.writeText(text);
      toast(t("Imenakiliwa."), 'good');
    } catch {
      toast(t("Imeshindwa kunakili — andika kodi mwenyewe."), 'warn');
    }
  }

  if (done) {
    const link = `${window.location.origin}/jiunge/${done.joinCode}`;
    return (
      <Card>
        <div className="flex items-start gap-3">
          <motion.span
            initial={{ scale: 0.6, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            className="grid size-9 shrink-0 place-items-center rounded-full bg-standing-good-bg text-standing-good"
          >
            <Check size={18} />
          </motion.span>
          <div className="min-w-0 flex-1">
            <div className="font-semibold text-ink-950">{done.name}{' '}{t("kimeanzishwa")}</div>
            <p className="mt-1 text-sm text-ink-600">
              {t("Wewe ni Mwenyekiti, namba")}{' '}<span className="figure">{done.chairMemberNumber}</span>{t(". Wape viongozi na wanachama kodi hii ili waombe kujiunga:")}
            </p>
            <div className="mt-3 flex flex-wrap items-center gap-2">
              <span className="theme-chrome figure rounded-lg bg-ink-900 px-3 py-2 text-lg tracking-widest text-gold-500">{done.joinCode}</span>
              <Button size="sm" variant="secondary" icon={Copy} onClick={() => copy(done.joinCode)}>
                {t("Nakili kodi")}
              </Button>
              <Button size="sm" variant="secondary" icon={Copy} onClick={() => copy(link)}>
                {t("Nakili kiungo")}
              </Button>
            </div>
            <div className="mt-5 rounded-xl bg-ink-50 p-3 text-sm text-ink-700">
              <div className="font-medium text-ink-900">{t("Hatua inayofuata: kanuni za michango")}</div>
              {t("Mchango wa mwezi, faini, tarehe ya mwisho na kianzio. Bila hizo, mfumo hauwezi kuhesabu madeni.")}
            </div>
            <div className="mt-4 flex flex-wrap justify-end gap-2">
              <Button variant="secondary" onClick={() => navigate('/wanachama')}>
                {t("Wanachama")}
              </Button>
              <Button icon={ArrowRight} onClick={() => navigate('/mipangilio')}>
                {t("Weka kanuni")}
              </Button>
            </div>
          </div>
        </div>
      </Card>
    );
  }

  return (
    <Card title={t("Anzisha kikundi")} subtitle={t("Utakuwa Mwenyekiti. Viongozi wengine wataomba nafasi zao kwa kodi utakayopata.")}>
      <form onSubmit={submit} className="space-y-4">
        <Field label={t("Jina la kikundi")}>
          <Input value={name} onChange={(e) => setName(e.target.value)} required minLength={3} placeholder={t("Mf. Umoja Mkoba Ukonga")} />
        </Field>
        <Field label={t("Aina ya michango")}>
          <Segmented<GroupTypeName>
            stack
            value={type}
            onChange={setType}
            options={(Object.keys(GROUP_TYPE_LABEL) as GroupTypeName[]).map((k) => ({
              value: k,
              label: t(GROUP_TYPE_LABEL[k].label),
              hint: t(GROUP_TYPE_LABEL[k].hint),
            }))}
          />
        </Field>
        <Field
          label={t("Kifupi cha kikundi")}
          hint={
            <>
              {t("Kinatumika kwenye namba za wanachama:")}{' '}<span className="figure">{`${shortCode || suggested || 'UMK'}-${new Date().getFullYear()}-001`}</span>{t(". Herufi 2–5; hakibadiliki baada ya namba ya kwanza kutolewa.")}
            </>
          }
        >
          <Input
            value={shortCode}
            onChange={(e) => setShortCode(e.target.value.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 5))}
            placeholder={suggested || 'UMK'}
            className="figure uppercase tracking-wider sm:max-w-[10rem]"
          />
        </Field>
        {error && <Notice tone="overdue">{error}</Notice>}
        <div className="flex justify-end">
          <Button type="submit" icon={PlusCircle} loading={busy} disabled={name.trim().length < 3 || !(shortCode || suggested)}>
            {t("Anzisha kikundi")}
          </Button>
        </div>
      </form>
    </Card>
  );
}
