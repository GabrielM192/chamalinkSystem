import { useState } from 'react';
import { Navigate } from 'react-router-dom';
import { AlertTriangle, HandCoins, RefreshCw, Scale, ShieldAlert, UserMinus, Wallet } from 'lucide-react';
import { useAuth } from '../../auth/AuthContext';
import { useMyGroups } from '../../hooks/useMyGroups';
import { apiErrorMessage } from '../../api/truth';
import {
  getMemberWallet,
  getMyWallet,
  listAtRisk,
  listOutstanding,
  proposeRemoval,
  sweepWallet,
  type AtRiskRow,
  type EventObligation,
  type EventWallet,
} from '../../api/events';
import { Button, Card, EmptyState, Loading, Notice, Segmented, Stat } from '../../components/ui';
import { ReasonModal } from '../../components/benefits/common';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { num, realDateTime } from '../../lib/truth';
import { groupRoleLabel } from '../../lib/status';
import { LEADER_ROLES } from '../../lib/truth';

import { t } from '../../i18n';

type Tab = 'mine' | 'atRisk' | 'leaders';

/**
 * /pochi — POCHI ZA KIKUNDI CHA "KILA TUKIO".
 *
 * Kila mwanachama ana salio lake ndani ya kikundi. Tukio linapoidhinishwa,
 * mchango unakatwa kwenye salio — lakini HALISHUKI chini ya "kiwango funali".
 * Kinachobaki ni deni linalokamilishwa na amana zijazo (deni la zamani kwanza).
 *
 * Kanuni: namba zote zinatoka seva. Ukurasa haukokotoi kiasi cha fedha.
 */
export function WalletPage() {
  const { user } = useAuth();
  const { activeGroup, activeGroupId, loading, error } = useMyGroups();
  const [tab, setTab] = useState<Tab>('mine');

  if (loading) return <Loading label={t('Inapakia makundi yako...')} />;
  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!activeGroupId || !activeGroup) return <Navigate to="/karibu" replace />;

  const isLeader = LEADER_ROLES.has(activeGroup.role);
  const isEventGroup = activeGroup.groupType === 'EventBased';

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-gold-700">
            <Wallet size={14} /> {t('Pochi')}
          </div>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight text-ink-950">{t('Pochi Yangu')}</h1>
          <p className="mt-1 text-sm text-ink-600">
            {activeGroup.groupName} · {groupRoleLabel(activeGroup.role)}
          </p>
        </div>
      </header>

      {!isEventGroup && (
        <Notice tone="warn" title={t('Hiki ni kikundi cha mchango wa mwezi')}>
          {t(
            'Pochi zinatumika kwa vikundi vinavyochangishana kila tukio. Kwenye kikundi cha mwezi, taarifa yako iko kwenye "Taarifa Yangu".'
          )}
        </Notice>
      )}

      {/* SASISHO19: "Hatarini kuondolewa" inaonekana kwa KILA mwanachama — si siri ya viongozi. */}
      {isEventGroup && (
        <Segmented<Tab>
          value={tab}
          onChange={setTab}
          options={[
            { value: 'mine', label: t('Pochi yangu'), hint: t('Salio, deni na historia yako') },
            { value: 'atRisk', label: t('Hatarini kuondolewa'), hint: t('Orodha inaonekana kwa wote') },
            ...(isLeader
              ? [{ value: 'leaders' as Tab, label: t('Walio na deni'), hint: t('Wanachama wanaotakiwa kujaza') }]
              : []),
          ]}
        />
      )}

      {isEventGroup && tab === 'mine' && <MyWallet groupId={activeGroupId} />}
      {isEventGroup && tab === 'atRisk' && <AtRiskPanel groupId={activeGroupId} isLeader={isLeader} />}
      {isEventGroup && tab === 'leaders' && isLeader && <OutstandingList groupId={activeGroupId} />}

      {!isEventGroup && (
        <Card title={t('Namba zako')}>
          <MyWalletHeaderOnly />
        </Card>
      )}
    </div>
  );

  function MyWalletHeaderOnly() {
    return (
      <p className="text-sm text-ink-600">
        {t('Tafadhali fungua "Taarifa Yangu" kuona michango, faini na Kianzio chako.')}{' '}
        {user?.fullName ? `(${user.fullName})` : ''}
      </p>
    );
  }
}

// ---------------------------------------------------------------------------
function MyWallet({ groupId }: { groupId: string }) {
  const wallet = useApi(() => getMyWallet(groupId), groupId);

  if (wallet.loading && !wallet.data) return <Loading label={t('Inahesabu pochi yako...')} />;
  if (wallet.error) return <Notice tone="overdue">{wallet.error}</Notice>;
  if (!wallet.data) return null;

  return <WalletCard w={wallet.data} />;
}

// ---------------------------------------------------------------------------
export function WalletCard({ w, onSwept }: { w: EventWallet; onSwept?: () => void }) {
  const toast = useToast();
  const [busy, setBusy] = useState(false);

  const underFloor = w.balance < w.floor;
  const gap = Math.max(0, w.floor - w.balance);

  async function sweep() {
    setBusy(true);
    try {
      const r = await sweepWallet(w.groupId, w.memberId);
      toast(r.message, r.applied > 0 ? 'good' : 'neutral');
      onSwept?.();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Stat
          label={t('Salio langu')}
          value={`TSH ${num(w.balance)}`}
          tone={underFloor ? 'warn' : 'good'}
          hint={w.memberNumber}
        />
        <Stat label={t('Kiwango cha chini')} value={`TSH ${num(w.floor)}`} hint={t('Salio halishuki chini ya hapa')} />
        <Stat
          label={t('Kinachopatikana')}
          value={`TSH ${num(w.available)}`}
          hint={t('Kiasi kinachoweza kukatwa na tukio lijalo')}
        />
        <Stat
          label={t('Deni la matukio')}
          value={`TSH ${num(w.outstanding)}`}
          tone={w.outstanding > 0 ? 'overdue' : 'good'}
          hint={w.outstanding > 0 ? t('Lazima ujaze') : t('Hakuna deni')}
        />
      </div>

      {underFloor && (
        <Notice tone="warn" title={t('Uko chini ya kiwango cha chini')}>
          {t('Unatakiwa ujaze TSH {0} ili kurudi kwenye kiwango cha kikundi.', { 0: num(gap) })}
        </Notice>
      )}

      {w.outstanding > 0 && (
        <Notice
          tone="overdue"
          title={t('Una deni la matukio yaliyopita')}
          action={
            <Button size="sm" variant="secondary" icon={RefreshCw} loading={busy} onClick={sweep}>
              {t('Tumia salio kulipa')}
            </Button>
          }
        >
          {t(
            'Amana yoyote unayoweka inalipa deni la zamani kwanza. Ukishalipa deni, salio linaendelea kukua hadi kiwango cha chini.'
          )}
        </Notice>
      )}

      <Card
        title={t('Historia ya makato')}
        subtitle={t('Kila tukio lililokukata, kiasi, na kilichobaki kuwa deni.')}
        bodyClassName="p-0"
      >
        {w.obligations.length === 0 ? (
          <div className="p-6">
            <EmptyState icon={Scale} title={t('Hakuna makato bado')}>
              {t('Ukikuta tukio la kikundi (msiba, sherehe), mchango wako utaonekana hapa.')}
            </EmptyState>
          </div>
        ) : (
          <ObligationTable rows={w.obligations} />
        )}
      </Card>
    </div>
  );
}

// ---------------------------------------------------------------------------
function ObligationTable({ rows }: { rows: EventObligation[] }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[36rem] text-sm">
        <thead>
          <tr className="border-b border-ink-100 text-left text-xs uppercase tracking-wide text-ink-600">
            <th className="px-4 py-3">{t('Tukio')}</th>
            <th className="px-4 py-3">{t('Tarehe')}</th>
            <th className="px-4 py-3 text-right">{t('Ulitakiwa')}</th>
            <th className="px-4 py-3 text-right">{t('Ulikatwa')}</th>
            <th className="px-4 py-3 text-right">{t('Deni')}</th>
            <th className="px-4 py-3">{t('Hali')}</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((o) => (
            <tr key={o.obligationId} className="border-b border-ink-100 last:border-0">
              <td className="px-4 py-3 font-medium text-ink-950">{o.caseNumber ?? '—'}</td>
              <td className="px-4 py-3 text-ink-700">{realDateTime(o.createdAt)}</td>
              <td className="figure px-4 py-3 text-right text-ink-950">{num(o.requiredAmount)}</td>
              <td className="figure px-4 py-3 text-right text-ink-950">{num(o.deductedAmount)}</td>
              <td
                className={`figure px-4 py-3 text-right font-semibold ${
                  o.outstandingAmount > 0 ? 'text-standing-overdue' : 'text-standing-good'
                }`}
              >
                {num(o.outstandingAmount)}
              </td>
              <td className="px-4 py-3">
                <ObligationBadge o={o} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ObligationBadge({ o }: { o: EventObligation }) {
  const cls =
    o.status === 'Settled'
      ? 'bg-standing-good-bg text-standing-good'
      : o.status === 'Waived'
        ? 'bg-ink-50 text-ink-700'
        : o.overdue
          ? 'bg-standing-overdue-bg text-standing-overdue'
          : 'bg-standing-warn-bg text-standing-warn';

  const label =
    o.status === 'Settled'
      ? t('Imekamilika')
      : o.status === 'Waived'
        ? t('Imesamehewa')
        : o.status === 'Exempted'
          ? t('Hakuhusika')
          : o.overdue
            ? t('Imechelewa')
            : t('Inasubiri');

  return (
    <span className={`inline-flex whitespace-nowrap rounded-full px-2.5 py-1 text-xs font-semibold ${cls}`}>
      {label}
      {o.dueDate && o.status === 'Pending' && !o.overdue ? ` · ${realDateTime(o.dueDate)}` : ''}
    </span>
  );
}

// ---------------------------------------------------------------------------
/**
 * SASISHO19 — "HATARINI KUONDOLEWA".
 *
 * Orodha hii ni ya WANACHAMA WOTE: pesa ya kikundi ni ya wote, hivyo kila
 * mwanachama ana haki ya kuona nani hajalipa deni la tukio baada ya faini na
 * siku za neema. Kuondolewa kunaanzishwa na kiongozi na kunahitaji viongozi
 * ZAIDI ya wawili kuidhinisha (seva inasimamisha kanuni hiyo — haipungui kwa sera).
 */
function AtRiskPanel({ groupId, isLeader }: { groupId: string; isLeader: boolean }) {
  const toast = useToast();
  const [v, setV] = useState(0);
  const list = useApi(() => listAtRisk(groupId), groupId, [v]);
  const [target, setTarget] = useState<AtRiskRow | null>(null);

  if (list.loading && !list.data) return <Loading label={t('Inahesabu hatari ya kuondolewa...')} />;
  if (list.error) return <Notice tone="overdue">{list.error}</Notice>;

  const rows = list.data ?? [];
  const totalDebt = rows.reduce((s2, r) => s2 + r.outstanding, 0);
  const totalFine = rows.reduce((s2, r) => s2 + r.fineCharged, 0);

  async function remove(reason: string) {
    if (!target) return;
    try {
      const r = await proposeRemoval(groupId, target.memberId, reason);
      toast(t('Ombi la kuondolewa limeanzishwa. Linahitaji idhini {0} za viongozi.', { 0: r.requiredApprovals }), 'good');
      setTarget(null);
      setV((x) => x + 1);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    }
  }

  return (
    <div className="space-y-4">
      <Notice tone="warn" title={t('Orodha hii inaonekana kwa wanachama wote')}>
        {t(
          'Mwanachama anaingia hapa pale deni la tukio linapobaki baada ya faini na siku za neema za kikundi. Kuondolewa kunaanzishwa na kiongozi na kunahitaji ZAIDI ya viongozi wawili kuidhinisha — kiongozi mmoja hawezi kumtoa mwanachama.',
        )}
      </Notice>

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
        <Stat
          label={t('Walio hatarini')}
          value={num(rows.length)}
          tone={rows.length ? 'overdue' : 'good'}
          hint={t('Wana deni lililochelewa')}
        />
        <Stat label={t('Jumla ya deni')} value={`TSH ${num(totalDebt)}`} tone={totalDebt > 0 ? 'overdue' : 'good'} />
        <Stat label={t('Faini zilizotozwa')} value={`TSH ${num(totalFine)}`} tone={totalFine > 0 ? 'warn' : 'neutral'} />
      </div>

      {rows.length === 0 ? (
        <Card>
          <EmptyState icon={ShieldAlert} title={t('Hakuna mtu aliye hatarini kuondolewa')}>
            {t('Kila mwanachama amelipa deni la matukio ndani ya muda wa kikundi.')}
          </EmptyState>
        </Card>
      ) : (
        <Card
          title={t('Hatarini kuondolewa')}
          subtitle={t('Deni lililobaki, siku zilizopita, na ombi la kuondolewa kama lipo.')}
          bodyClassName="p-0"
        >
          <div className="overflow-x-auto">
            <table className="w-full min-w-[34rem] text-sm">
              <thead>
                <tr className="border-b border-ink-100 text-left text-xs uppercase tracking-wide text-ink-600">
                  <th className="px-4 py-3">{t('Mwanachama')}</th>
                  <th className="px-4 py-3 text-right">{t('Siku zilizopita')}</th>
                  <th className="px-4 py-3 text-right">{t('Deni')}</th>
                  <th className="px-4 py-3 text-right">{t('Pengo la kiwango')}</th>
                  <th className="px-4 py-3">{t('Hali')}</th>
                  {isLeader && <th className="px-4 py-3" />}
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.memberId} className="border-b border-ink-100 last:border-0">
                    <td className="px-4 py-3">
                      <div className="font-medium text-ink-950">{r.name ?? r.memberNumber ?? r.memberId}</div>
                      <div className="figure text-xs text-ink-600">{r.memberNumber ?? '—'}</div>
                    </td>
                    <td className="figure px-4 py-3 text-right text-standing-overdue">{num(r.daysOverdue)}</td>
                    <td className="figure px-4 py-3 text-right font-semibold text-standing-overdue">TSH {num(r.outstanding)}</td>
                    <td className="figure px-4 py-3 text-right text-ink-700">TSH {num(r.floorShortfall)}</td>
                    <td className="px-4 py-3 text-xs text-ink-700">
                      {r.pendingRemovalApprovals !== null ? (
                        <span className="inline-flex whitespace-nowrap rounded-full bg-standing-warn-bg px-2.5 py-1 font-semibold text-standing-warn">
                          {t('Inasubiri idhini ya viongozi {0}', { 0: num(r.pendingRemovalApprovals) })}
                        </span>
                      ) : (
                        <span className="text-ink-500">{t('Hakuna ombi bado')}</span>
                      )}
                    </td>
                    {isLeader && (
                      <td className="px-4 py-3 text-right">
                        {r.pendingRemovalApprovals === null && (
                          <Button size="sm" variant="ghost" icon={UserMinus} onClick={() => setTarget(r)}>
                            {t('Ondoa')}
                          </Button>
                        )}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      <div className="flex justify-end">
        <Button variant="ghost" size="sm" icon={AlertTriangle} onClick={() => setV((x) => x + 1)}>
          {t('Pakia upya')}
        </Button>
      </div>

      <ReasonModal
        open={!!target}
        title={t('Kuondoa mwanachama')}
        summary={
          target ? (
            <span>
              <b>{target.name ?? target.memberNumber}</b> — {t('deni la tukio')} TSH {num(target.outstanding)} ·{' '}
              {t('siku {0} zimepita', { 0: num(target.daysOverdue) })}
            </span>
          ) : null
        }
        label={t('Sababu ya kuondolewa')}
        hint={t('Mf. deni la tukio halijalipwa baada ya faini na muda wa neema. Kumbuka: viongozi ZAIDI ya wawili wanatakiwa kuidhinisha.')}
        requireReason
        confirmLabel={t('Anzisha ombi la kuondolewa')}
        variant="danger"
        onClose={() => setTarget(null)}
        onConfirm={remove}
      />
    </div>
  );
}

// ---------------------------------------------------------------------------
function OutstandingList({ groupId }: { groupId: string }) {
  const toast = useToast();
  const [v, setV] = useState(0);
  const list = useApi(() => listOutstanding(groupId), groupId, [v]);
  const [open, setOpen] = useState<string | null>(null);
  const detail = useApi(open ? () => getMemberWallet(groupId, open) : null, open, [v]);
  const [busy, setBusy] = useState(false);

  if (list.loading && !list.data) return <Loading label={t('Inahesabu madeni...')} />;
  if (list.error) return <Notice tone="overdue">{list.error}</Notice>;

  const rows = list.data ?? [];
  const total = rows.reduce((s, r) => s + r.outstanding, 0);

  async function sweep(memberId: string) {
    setBusy(true);
    try {
      const r = await sweepWallet(groupId, memberId);
      toast(r.message, r.applied > 0 ? 'good' : 'neutral');
      setV((x) => x + 1);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
        <Stat label={t('Wanachama wenye deni')} value={num(rows.length)} tone={rows.length ? 'warn' : 'good'} />
        <Stat label={t('Jumla ya deni')} value={`TSH ${num(total)}`} tone={total > 0 ? 'overdue' : 'good'} />
        <Stat
          label={t('Wastani kwa mwanachama')}
          value={`TSH ${num(rows.length ? total / rows.length : 0)}`}
        />
      </div>

      {rows.length === 0 ? (
        <Card>
          <EmptyState icon={HandCoins} title={t('Hakuna deni la matukio')}>
            {t('Kila mwanachama amekatwa kikamilifu kwenye matukio yote.')}
          </EmptyState>
        </Card>
      ) : (
        <>
          <Card title={t('Walio na deni')} bodyClassName="p-0" subtitle={t('Bofya mwanachama kuona maelezo na kutumia salio lake.')}>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[28rem] text-sm">
                <thead>
                  <tr className="border-b border-ink-100 text-left text-xs uppercase tracking-wide text-ink-600">
                    <th className="px-4 py-3">{t('Mwanachama')}</th>
                    <th className="px-4 py-3 text-right">{t('Salio')}</th>
                    <th className="px-4 py-3 text-right">{t('Deni')}</th>
                    <th className="px-4 py-3 text-right">{t('Matukio')}</th>
                    <th className="px-4 py-3" />
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr
                      key={r.memberId}
                      className={`border-b border-ink-100 last:border-0 ${open === r.memberId ? 'bg-ink-50' : ''}`}
                    >
                      <td className="px-4 py-3 font-medium text-ink-950">{r.memberNumber ?? r.memberId}</td>
                      <td className="figure px-4 py-3 text-right text-ink-700">{num(r.balance)}</td>
                      <td className="figure px-4 py-3 text-right font-semibold text-standing-overdue">{num(r.outstanding)}</td>
                      <td className="figure px-4 py-3 text-right text-ink-700">{num(r.events)}</td>
                      <td className="px-4 py-3 text-right">
                        <Button size="sm" variant="ghost" onClick={() => setOpen(open === r.memberId ? null : r.memberId)}>
                          {open === r.memberId ? t('Funga') : t('Fungua')}
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>

          {open && (
            <Card
              title={t('Maelezo ya deni')}
              action={
                <Button size="sm" variant="secondary" icon={RefreshCw} loading={busy} onClick={() => sweep(open)}>
                  {t('Tumia salio kulipa')}
                </Button>
              }
            >
              {detail.loading && !detail.data && <Loading />}
              {detail.error && <Notice tone="overdue">{detail.error}</Notice>}
              {detail.data && <WalletCard w={detail.data} onSwept={() => setV((x) => x + 1)} />}
            </Card>
          )}
        </>
      )}

      {rows.length > 0 && (
        <Notice tone="warn" title={t('Kumbuka')}>
          {t(
            'Deni linakamilishwa na amana mpya (deni la zamani kwanza). Ukitumia kitufe cha "Tumia salio kulipa", mfumo unatumia salio lililopo juu ya kiwango cha chini pekee — hakuna kuandika kiasi kwa mkono.'
          )}
        </Notice>
      )}

      <div className="flex justify-end">
        <Button variant="ghost" size="sm" icon={AlertTriangle} onClick={() => setV((x) => x + 1)}>
          {t('Pakia upya')}
        </Button>
      </div>
    </div>
  );
}
