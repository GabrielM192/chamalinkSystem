import { useState } from 'react';
import { Navigate } from 'react-router-dom';
import { MessageSquare, RefreshCw, Send } from 'lucide-react';
import { useMyGroups } from '../../hooks/useMyGroups';
import { apiErrorMessage } from '../../api/truth';
import { dispatchSms, getSmsOverview, listSms, topUpSms, type SmsOverview, type SmsRow } from '../../api/sms';
import { Button, Card, EmptyState, Field, Input, Loading, Notice, Segmented, Stat } from '../../components/ui';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { LEADER_ROLES, num, realDateTime } from '../../lib/truth';
import { t } from '../../i18n';

type Filter = 'all' | 'Queued' | 'Sent' | 'Failed' | 'Suppressed';

/**
 * /hazina/sms — SMS ZA NO-REPLY (viongozi).
 *
 * Mwanachama asiye na smartphone anafikiwa kwa SMS. Salio ni CREDITS
 * (hazina), si bajeti ya mwezi. Kila ujumbe una kumbukumbu: hali, jaribio,
 * sababu ya kuzuiwa, na jina la kiolezo.
 *
 * Kanuni: ujumbe unatoka kwenye violezo pekee — hakuna kuandika ujumbe wa
 * mkono wenye majina au kiasi cha watu wengine.
 */
export function SmsPage() {
  const { activeGroup, activeGroupId, loading, error } = useMyGroups();
  const [filter, setFilter] = useState<Filter>('all');
  const [version, setVersion] = useState(0);
  const [credits, setCredits] = useState('100');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const toast = useToast();

  const overview = useApi<SmsOverview>(activeGroupId ? () => getSmsOverview(activeGroupId) : null, [activeGroupId], [version]);
  const messages = useApi<SmsRow[]>(
    activeGroupId ? () => listSms(activeGroupId, filter) : null,
    [activeGroupId, filter],
    [version],
  );

  if (loading) return <Loading label={t('Inapakia...')} />;
  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!activeGroupId || !activeGroup) return <Navigate to="/karibu" replace />;
  if (!LEADER_ROLES.has(activeGroup.role)) return <Navigate to="/" replace />;

  async function topUp() {
    const n = Number(credits);
    if (!Number.isFinite(n) || n <= 0) {
      toast(t('Weka idadi sahihi ya SMS.'), 'warn');
      return;
    }
    setBusy(true);
    try {
      const r = await topUpSms(activeGroupId!, n, reason || null);
      toast(t('Salio jipya la SMS: ') + num(r.credits), 'good');
      setReason('');
      setVersion((v) => v + 1);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  async function dispatch() {
    setBusy(true);
    try {
      const r = await dispatchSms(activeGroupId!);
      toast(`${t('Zimetumwa')}: ${num(r.sent)} · ${t('Zimeshindikana')}: ${num(r.failed)}`, r.failed > 0 ? 'warn' : 'good');
      setVersion((v) => v + 1);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  const o = overview.data;

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-gold-700">
            <MessageSquare size={14} /> {t('Hazina')}
          </div>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight text-ink-950">{t('SMS za Kikundi')}</h1>
          <p className="mt-1 max-w-3xl text-sm text-ink-600">
            {t('Ujumbe wa no-reply: tangazo la tukio, kumbusho, faini na uthibitisho wa malipo. Ujumbe unasubiri nje ya saa za utulivu; hauachwi.')}
          </p>
        </div>
        <Button variant="secondary" onClick={dispatch} disabled={busy}>
          <Send size={14} /> {t('Tuma zilizosubiri')}
        </Button>
      </header>

      {o && (
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
          <Stat
            label={t('Salio la SMS')}
            value={num(o.credits)}
            tone={o.lowThreshold > 0 && o.credits <= o.lowThreshold ? 'overdue' : o.credits > 0 ? 'good' : 'warn'}
            hint={o.enabled ? t('Zimewashwa') : t('Hazijawashwa')}
          />
          <Stat label={t('Zinasubiri')} value={num(o.queued)} />
          <Stat label={t('Zimetumwa')} value={num(o.totalSent)} />
          <Stat label={t('Zilizoshindikana')} value={num(o.failed)} tone={o.failed > 0 ? 'warn' : 'neutral'} />
        </div>
      )}

      {!o?.enabled && (
        <Notice tone="warn">
          {t('SMS hazijawashwa kwa kikundi hiki. Mipangilio → Kanuni za fedha → SMS.')}
        </Notice>
      )}

      <div className="grid gap-4 lg:grid-cols-3">
        <Card title={t('Ongeza salio')} subtitle={t('Credits zilizonunuliwa kwa mtoa-huduma. Si pesa ya mfumo.')}>
          <div className="space-y-3">
            <Field label={t('Idadi ya SMS')}>
              <Input value={credits} onChange={(e) => setCredits(e.target.value)} inputMode="numeric" />
            </Field>
            <Field label={t('Sababu (hiari)')}>
              <Input value={reason} onChange={(e) => setReason(e.target.value)} placeholder={t('Mfano: tuliponunua SMS 100')} />
            </Field>
            <Button onClick={topUp} disabled={busy}>
              <RefreshCw size={14} /> {t('Ongeza')}
            </Button>
          </div>
        </Card>

        <Card
          className="lg:col-span-2"
          title={t('Kumbukumbu ya ujumbe')}
          subtitle={t('Namba za simu zimefichwa. Kila ujumbe una hali na sababu.')}
          action={
            <Segmented<Filter>
              value={filter}
              onChange={setFilter}
              options={[
                { value: 'all', label: t('Zote') },
                { value: 'Queued', label: t('Zinasubiri') },
                { value: 'Failed', label: t('Zilizoshindikana') },
                { value: 'Suppressed', label: t('Zilizozuiwa') },
              ]}
            />
          }
        >
          {messages.loading && <Loading label={t('Inapakia...')} />}
          {messages.error && <Notice tone="overdue">{messages.error}</Notice>}
          {!messages.loading && (messages.data ?? []).length === 0 && (
            <EmptyState icon={MessageSquare} title={t('Hakuna ujumbe bado')}>
              {t('Ujumbe unatengenezwa na mfumo: tangazo la tukio, kumbusho la malipo, faini na uthibitisho.')}
            </EmptyState>
          )}

          {(messages.data ?? []).length > 0 && (
            <ul className="divide-y divide-ink-100">
              {(messages.data ?? []).map((m) => (
                <li key={m.id} className="py-3">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="min-w-0">
                      <div className="flex items-center gap-2 text-xs text-ink-600">
                        <span className="font-mono">{m.memberNumber ?? '—'}</span>
                        <span>·</span>
                        <span className="font-mono">{m.phone}</span>
                        <span>·</span>
                        <span>{m.templateKey}</span>
                      </div>
                      <p className="mt-1 text-sm text-ink-900">{m.body || m.suppressedReason || m.failureReason}</p>
                      <div className="mt-0.5 text-[11px] text-ink-500">
                        {realDateTime(m.createdAt)}
                        {m.sentAt ? ` · ${t('imetumwa')} ${realDateTime(m.sentAt)}` : ''}
                        {m.deliveredAt ? ` · ${t('imefika')}` : ''}
                        {m.attempts > 0 ? ` · ${t('majaribio')} ${m.attempts}` : ''}
                      </div>
                    </div>
                    <span
                      className={`inline-flex rounded-full px-2 py-0.5 text-[11px] font-semibold ${
                        m.status === 'Delivered'
                          ? 'bg-standing-good/10 text-standing-good'
                          : m.status === 'Sent'
                            ? 'bg-ink-100 text-ink-700'
                            : m.status === 'Failed'
                              ? 'bg-standing-overdue/10 text-standing-overdue'
                              : m.status === 'Suppressed'
                                ? 'bg-standing-warn/10 text-standing-warn'
                                : 'bg-ink-100 text-ink-600'
                      }`}
                    >
                      {m.status}
                    </span>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
    </div>
  );
}
