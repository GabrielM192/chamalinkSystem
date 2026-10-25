import { useState } from 'react';
import { Navigate } from 'react-router-dom';
import { BadgeCheck, HandCoins, Send } from 'lucide-react';
import { useMyGroups } from '../../hooks/useMyGroups';
import { apiErrorMessage } from '../../api/truth';
import { decideClaim, listClaims, submitClaim, type ClaimRow } from '../../api/payments';
import { Button, Card, EmptyState, Field, Input, Loading, Notice, Stat } from '../../components/ui';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { LEADER_ROLES, num, realDateTime } from '../../lib/truth';
import { t } from '../../i18n';

/**
 * /madai — MADAI YA MALIPO.
 *
 * Mwanachama: "nimelipa" + namba ya muamala. Dai halibadilishi deni;
 * hazina inalinganisha na statement ya M-Koba kisha inathibitisha au inakataa.
 *
 * Kiongozi: anaona madai yote ya kikundi na anayaamua.
 *
 * Hii ni njia ya mwanachama kuwa na sauti bila kumpa mtu uwezo wa kuandika
 * salio kwa mkono.
 */
export function ClaimsPage() {
  const { activeGroup, activeGroupId, loading, error } = useMyGroups();
  const [version, setVersion] = useState(0);
  const [busy, setBusy] = useState<string | null>(null);
  const [amount, setAmount] = useState('');
  const [reference, setReference] = useState('');
  const [paymentCode, setPaymentCode] = useState('');
  const [note, setNote] = useState('');
  const toast = useToast();

  const claims = useApi<ClaimRow[]>(activeGroupId ? () => listClaims(activeGroupId) : null, [activeGroupId], [version]);

  if (loading) return <Loading label={t('Inapakia...')} />;
  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!activeGroupId || !activeGroup) return <Navigate to="/karibu" replace />;

  const isLeader = LEADER_ROLES.has(activeGroup.role);
  const rows = claims.data ?? [];
  const mine = rows.filter((r) => r.mine);
  const pending = rows.filter((r) => r.status === 'Pending');

  async function send() {
    const n = Number(amount);
    if (!Number.isFinite(n) || n <= 0) {
      toast(t('Weka kiasi ulicholipa.'), 'warn');
      return;
    }
    if (!reference.trim()) {
      toast(t('Weka namba ya muamala (kumbukumbu).'), 'warn');
      return;
    }
    setBusy('new');
    try {
      const r = await submitClaim(activeGroupId!, {
        amount: n,
        reference: reference.trim(),
        paymentCode: paymentCode.trim() || null,
        note: note.trim() || null,
      });
      toast(r.ujumbe, 'good');
      setAmount('');
      setReference('');
      setPaymentCode('');
      setNote('');
      setVersion((v) => v + 1);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(null);
    }
  }

  async function decide(r: ClaimRow, approve: boolean) {
    setBusy(r.id);
    try {
      await decideClaim(activeGroupId!, r.id, approve, approve ? null : t('Hakuna mstari wa M-Koba unaolingana.'), null);
      toast(approve ? t('Dai limethibitishwa.') : t('Dai limekataliwa.'), approve ? 'good' : 'warn');
      setVersion((v) => v + 1);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(null);
    }
  }

  /** Hali kwa Kiswahili — backend ina ENUM, UI inatafsiri. */
  function statusLabel(status: ClaimRow['status']) {
    switch (status) {
      case 'Verified':
        return t('Imethibitishwa');
      case 'Rejected':
        return t('Imekataliwa');
      case 'Withdrawn':
        return t('Imefutwa');
      default:
        return t('Inasubiri');
    }
  }

  function tone(status: ClaimRow['status']) {
    if (status === 'Verified') return 'bg-standing-good/10 text-standing-good';
    if (status === 'Rejected') return 'bg-standing-overdue/10 text-standing-overdue';
    if (status === 'Withdrawn') return 'bg-ink-100 text-ink-600';
    return 'bg-standing-warn/10 text-standing-warn';
  }

  return (
    <div className="space-y-5">
      <header>
        <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-gold-700">
          <HandCoins size={14} /> {t('Malipo')}
        </div>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight text-ink-950">{t('Madai ya Malipo')}</h1>
        <p className="mt-1 max-w-3xl text-sm text-ink-600">
          {t('Umelipa lakini pesa haijaonekana? Dai hapa kwa namba ya muamala. Dai halibadilishi deni — hazina inalinganisha na taarifa ya M-Koba kisha inathibitisha.')}
        </p>
      </header>

      <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
        <Stat label={t('Madai yako')} value={num(mine.length)} />
        <Stat label={t('Yanasubiri')} value={num(mine.filter((r) => r.status === 'Pending').length)} tone="warn" />
        <Stat
          label={t('Yaliyothibitishwa')}
          value={num(mine.filter((r) => r.status === 'Verified').length)}
          tone="good"
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card title={t('Dai malipo mapya')} subtitle={t('Namba ya muamala ndiyo ushahidi.')}>
          <div className="space-y-3">
            <Field label={t('Kiasi ulicholipa')}>
              <Input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="numeric" placeholder="0" />
            </Field>
            <Field label={t('Namba ya muamala')} hint={t('Kutoka SMS ya benki au M-Koba')}>
              <Input value={reference} onChange={(e) => setReference(e.target.value)} placeholder="M-KOBA ref" />
            </Field>
            <Field label={t('Kodi ya malipo (hiari)')}>
              <Input
                value={paymentCode}
                onChange={(e) => setPaymentCode(e.target.value.toUpperCase())}
                className="figure uppercase tracking-wider"
                placeholder="J3-E07-0147"
              />
            </Field>
            <Field label={t('Maelezo (hiari)')}>
              <Input value={note} onChange={(e) => setNote(e.target.value)} />
            </Field>
            <Button onClick={send} disabled={busy === 'new'}>
              <Send size={14} /> {t('Tuma dai')}
            </Button>
          </div>
        </Card>

        <Card
          className="lg:col-span-2"
          title={isLeader ? t('Madai yote ya kikundi') : t('Madai yako')}
          subtitle={
            isLeader
              ? t('Linganisha na mstari wa taarifa ya M-Koba kabla ya kuthibitisha. Uthibitisho hauzalishi pesa mpya.')
              : t('Hali ya kila dai na uamuzi wa hazina.')
          }
        >
          {claims.loading && <Loading label={t('Inapakia madai...')} />}
          {claims.error && <Notice tone="overdue">{claims.error}</Notice>}
          {!claims.loading && rows.length === 0 && (
            <EmptyState icon={HandCoins} title={t('Hakuna dai')}>
              {t('Dai linaonekana hapa mara mwanachama anaposema ameliacha malipo.')}
            </EmptyState>
          )}

          {rows.length > 0 && (
            <ul className="divide-y divide-ink-100">
              {rows.map((r) => (
                <li key={r.id} className="py-3">
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2 text-xs text-ink-600">
                        {isLeader && <span className="font-mono">{r.memberNumber ?? '—'}</span>}
                        {isLeader && <span>·</span>}
                        <span className="font-mono">{r.reference}</span>
                        {r.paymentCode && <span className="font-mono text-gold-700">{r.paymentCode}</span>}
                        <span>·</span>
                        <span>{realDateTime(r.createdAt)}</span>
                      </div>
                      <div className="figure mt-0.5 text-base font-semibold text-ink-950">TSh {num(r.amount)}</div>
                      {r.note && <p className="mt-0.5 text-xs text-ink-600">{r.note}</p>}
                    </div>
                    <div className="flex items-center gap-2">
                      <span className={`inline-flex rounded-full px-2 py-0.5 text-[11px] font-semibold ${tone(r.status)}`}>
                        {statusLabel(r.status)}
                      </span>
                      {isLeader && r.status === 'Pending' && (
                        <div className="flex gap-1">
                          <Button size="sm" variant="good" onClick={() => decide(r, true)} disabled={busy === r.id}>
                            <BadgeCheck size={13} /> {t('Thibitisha')}
                          </Button>
                          <Button size="sm" variant="ghost" onClick={() => decide(r, false)} disabled={busy === r.id}>
                            {t('Kataa')}
                          </Button>
                        </div>
                      )}
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          )}
          {isLeader && pending.length > 0 && (
            <p className="mt-3 text-xs text-ink-600">
              {t('Dai lililothibitishwa linahitaji mstari halisi wa taarifa ya M-Koba. Kama mstari haupo, kataa — usianzishe pesa.')}
            </p>
          )}
        </Card>
      </div>
    </div>
  );
}
