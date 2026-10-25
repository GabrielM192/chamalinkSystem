import { useState } from 'react';
import { Navigate } from 'react-router-dom';
import { CheckCircle2, Download, FileText, RefreshCw, Search, XCircle } from 'lucide-react';
import { useMyGroups } from '../../hooks/useMyGroups';
import { apiErrorMessage } from '../../api/truth';
import {
  confirmReceipt,
  downloadInboxReportPdf,
  downloadReceiptPdf,
  listInbox,
  rejectReceipt,
  syncInbox,
  type InboxResponse,
  type InboxRow,
  type ReceiptStatus,
} from '../../api/payments';
import { Button, Card, EmptyState, Loading, Notice, Segmented, Stat } from '../../components/ui';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { LEADER_ROLES, num, realDateTime } from '../../lib/truth';
import { ReasonModal } from '../../components/benefits/common';
import { saveBlob } from '../../lib/benefits';
import { t } from '../../i18n';

type Filter = 'all' | ReceiptStatus;

/**
 * /hazina/malipo — SANDUKU LA MALIPO (viongozi).
 *
 * Mistari ya statement ya M-Koba inaingia hapa. Kila mstari unaonyesha:
 *   • kama mfumo unamjua mwenye pesa (kodi ya malipo au namba), na
 *   • kiasi kinahitaji kiongozi wa PILI (sera ya kikundi).
 *
 * Kanuni: ukibonyeza "Thibitisha", pesa inampa mwanachama — deni la zamani
 * kwanza. Hakuna kuandika salio kwa mkono.
 */
export function PaymentInboxPage() {
  const { activeGroup, activeGroupId, loading, error } = useMyGroups();
  const [filter, setFilter] = useState<Filter>('all');
  const [version, setVersion] = useState(0);
  const [busy, setBusy] = useState<string | null>(null);
  const [rejecting, setRejecting] = useState<InboxRow | null>(null);
  const toast = useToast();

  const inbox = useApi<InboxResponse>(
    activeGroupId ? () => listInbox(activeGroupId, filter) : null,
    [activeGroupId, filter],
    [version],
  );

  if (loading) return <Loading label={t('Inapakia...')} />;
  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!activeGroupId || !activeGroup) return <Navigate to="/karibu" replace />;
  if (!LEADER_ROLES.has(activeGroup.role)) return <Navigate to="/" replace />;

  async function sync() {
    setBusy('sync');
    try {
      const r = await syncInbox(activeGroupId!);
      toast(r.ujumbe, 'good');
      setVersion((v) => v + 1);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(null);
    }
  }

  async function reportPdf() {
    setBusy('report');
    try {
      const blob = await downloadInboxReportPdf(activeGroupId!);
      saveBlob(blob, `Ripoti-Ya-Malipo-${new Date().toISOString().slice(0, 10)}.pdf`);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(null);
    }
  }

  async function receiptPdf(row: InboxRow) {
    setBusy(`pdf-${row.receiptId}`);
    try {
      const blob = await downloadReceiptPdf(activeGroupId!, row.receiptId);
      saveBlob(blob, `Risiti-${row.reference ?? row.receiptId.slice(0, 8)}.pdf`);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(null);
    }
  }

  async function confirm(row: InboxRow) {
    if (!row.suggestedMemberId) return;
    setBusy(row.receiptId);
    try {
      const r = await confirmReceipt(activeGroupId!, row.receiptId, row.suggestedMemberId, null, false);
      toast(
        r.ujumbe ??
          r.message ??
          (r.owed !== undefined
            ? t('Malipo yamethibitishwa. Deni lililobaki: ') + num(r.owed)
            : t('Malipo yamethibitishwa.')),
        r.status === 'InasubiriMkonoWapili' ? 'warn' : 'good',
      );
      setVersion((v) => v + 1);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(null);
    }
  }

  async function reject(reason: string) {
    if (!rejecting) return;
    setBusy(rejecting.receiptId);
    try {
      await rejectReceipt(activeGroupId!, rejecting.receiptId, reason);
      toast(t('Risiti imekataliwa.'), 'good');
      setRejecting(null);
      setVersion((v) => v + 1);
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(null);
    }
  }

  const s = inbox.data?.summary;
  const rows = inbox.data?.rows ?? [];

  function badge(r: InboxRow) {
    switch (r.status) {
      case 'Confirmed':
        return { label: t('Imethibitishwa'), cls: 'bg-standing-good/10 text-standing-good' };
      case 'AutoMatched':
        return { label: t('Imehesabiwa tayari'), cls: 'bg-ink-100 text-ink-700' };
      case 'Suggested':
        return { label: t('Inapendekezwa'), cls: 'bg-standing-warn/10 text-standing-warn' };
      case 'Rejected':
        return { label: t('Imekataliwa'), cls: 'bg-standing-overdue/10 text-standing-overdue' };
      default:
        return { label: t('Haijatambuliwa'), cls: 'bg-standing-overdue/10 text-standing-overdue' };
    }
  }

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-gold-700">
            <Search size={14} /> {t('Hazina')}
          </div>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight text-ink-950">{t('Sanduku la Malipo')}</h1>
          <p className="mt-1 max-w-3xl text-sm text-ink-600">
            {t('Kila mstari wa taarifa ya M-Koba ni pesa ya mtu. Hapa tunamjua ni nani — kwa kodi ya malipo, kwa namba, au kwa uamuzi wa kiongozi.')}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={reportPdf} disabled={busy === 'report'}>
            <FileText size={14} /> {t('Ripoti (PDF)')}
          </Button>
          <Button variant="secondary" onClick={sync} disabled={busy === 'sync'}>
            <RefreshCw size={14} /> {t('Sawazisha')}
          </Button>
        </div>
      </header>

      {s && (
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
          <Stat label={t('Mistari')} value={num(s.total)} />
          <Stat label={t('Haijatambuliwa')} value={num(s.unmatched)} tone={s.unmatched > 0 ? 'warn' : 'good'} />
          <Stat label={t('Inapendekezwa')} value={num(s.suggested)} />
          <Stat label={t('Imethibitishwa')} value={num(s.confirmed)} tone="good" />
        </div>
      )}

      <Card
        title={t('Mistari ya malipo')}
        subtitle={t('Bonyeza "Thibitisha" pale mfumo unapojua mwenye pesa. Kiasi kikubwa kinahitaji kiongozi wa pili.')}
        action={
          <Segmented<Filter>
            value={filter}
            onChange={setFilter}
            options={[
              { value: 'all', label: t('Zote') },
              { value: 'Suggested', label: t('Inapendekezwa') },
              { value: 'Unmatched', label: t('Haijatambuliwa') },
              { value: 'Confirmed', label: t('Imethibitishwa') },
            ]}
          />
        }
      >
        {inbox.loading && <Loading label={t('Inapakia sanduku...')} />}
        {inbox.error && <Notice tone="overdue">{inbox.error}</Notice>}
        {!inbox.loading && rows.length === 0 && (
          <EmptyState icon={Search} title={t('Sanduku ni tupu')}>
            {t('Pakia taarifa ya M-Koba na kiongozi wa pili aidhinishe — mistari itaingia hapa yenyewe.')}
          </EmptyState>
        )}

        {rows.length > 0 && (
          <div className="-mx-4 overflow-x-auto sm:-mx-5">
            <table className="min-w-full text-sm">
              <thead className="bg-ink-50 text-left text-[11px] uppercase tracking-wide text-ink-600">
                <tr>
                  <th className="px-4 py-2 font-semibold">{t('Tarehe')}</th>
                  <th className="px-2 py-2 font-semibold">{t('Kumbukumbu')}</th>
                  <th className="px-2 py-2 font-semibold">{t('Maelezo')}</th>
                  <th className="px-2 py-2 text-right font-semibold">{t('Kiasi')}</th>
                  <th className="px-2 py-2 font-semibold">{t('Mwenye pesa')}</th>
                  <th className="px-2 py-2 font-semibold">{t('Hali')}</th>
                  <th className="px-4 py-2" />
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-100">
                {rows.map((r) => {
                  const b = badge(r);
                  const canConfirm = r.status === 'Suggested' && r.suggestedMemberId;
                  return (
                    <tr key={r.receiptId} className="align-top">
                      <td className="whitespace-nowrap px-4 py-2 text-xs text-ink-600">{realDateTime(r.occurredAt)}</td>
                      <td className="px-2 py-2">
                        <div className="font-mono text-xs">{r.reference ?? '—'}</div>
                        {r.paymentCodeSeen && (
                          <div className="mt-0.5 font-mono text-[11px] text-gold-700">{r.paymentCodeSeen}</div>
                        )}
                      </td>
                      <td className="max-w-[22rem] px-2 py-2 text-xs text-ink-600">
                        <div className="truncate">{r.description ?? '—'}</div>
                        <div className="truncate text-[11px] text-ink-500">
                          {r.payerName ?? ''} {r.phone ? `· ${r.phone}` : ''}
                        </div>
                      </td>
                      <td className="figure whitespace-nowrap px-2 py-2 text-right font-semibold">{num(r.amount)}</td>
                      <td className="px-2 py-2 text-xs">
                        {r.suggestedMemberNumber ? (
                          <>
                            <div className="font-medium text-ink-900">{r.suggestedMemberNumber}</div>
                            <div className="text-[11px] text-ink-600">{r.suggestedName ?? ''}</div>
                          </>
                        ) : (
                          <span className="text-ink-500">—</span>
                        )}
                        {r.needsSecondApprover && (
                          <div className="mt-0.5 text-[11px] font-medium text-standing-warn">
                            {t('Kiasi kikubwa — kiongozi wa pili')}
                          </div>
                        )}
                      </td>
                      <td className="px-2 py-2">
                        <span className={`inline-flex rounded-full px-2 py-0.5 text-[11px] font-semibold ${b.cls}`}>
                          {b.label}
                        </span>
                      </td>
                      <td className="whitespace-nowrap px-4 py-2 text-right">
                        <div className="flex justify-end gap-1">
                          {canConfirm && (
                            <>
                              <Button size="sm" variant="good" onClick={() => confirm(r)} disabled={busy === r.receiptId}>
                                <CheckCircle2 size={13} /> {t('Thibitisha')}
                              </Button>
                              <Button size="sm" variant="ghost" onClick={() => setRejecting(r)} disabled={busy === r.receiptId}>
                                <XCircle size={13} /> {t('Kataa')}
                              </Button>
                            </>
                          )}
                          {r.status === 'Confirmed' && (
                            <Button
                              size="sm"
                              variant="ghost"
                              onClick={() => receiptPdf(r)}
                              disabled={busy === `pdf-${r.receiptId}`}
                            >
                              <Download size={13} /> {t('Risiti')}
                            </Button>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <ReasonModal
        open={!!rejecting}
        title={t('Kataa risiti')}
        summary={rejecting ? `${rejecting.reference ?? ''} · ${num(rejecting.amount)}` : undefined}
        label={t('Sababu (itaonekana kwenye kumbukumbu)')}
        requireReason
        confirmLabel={t('Kataa')}
        variant="danger"
        onClose={() => setRejecting(null)}
        onConfirm={reject}
      />
    </div>
  );
}
