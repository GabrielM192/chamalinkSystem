import { Navigate } from 'react-router-dom';
import { CalendarHeart, HandCoins, Users } from 'lucide-react';
import { useMyGroups } from '../../hooks/useMyGroups';
import { listGroupEvents, type GroupEventsResponse } from '../../api/events';
import { Card, EmptyState, Loading, Notice, Stat } from '../../components/ui';
import { useApi } from '../../lib/useApi';
import { num } from '../../lib/truth';
import { t } from '../../i18n';

/**
 * /matukio — MATUKIO YA KIKUNDI (kila mwanachama).
 *
 * Mwanachama anaona matukio YOTE yaliyopitishwa (msiba, kufukuzwa kazi,
 * kifo cha mwanachama) pamoja na:
 *   • tarehe na aina ya tukio,
 *   • mnufaika kwa NAMBA YA MWANACHAMA (si jina la marehemu),
 *   • kiasi kinachotakiwa kwa kila mwanachama,
 *   • HALI YAKE MWENYEWE pekee kwa tukio hilo, na kodi yake ya malipo.
 *
 * Haoni deni la mtu mwingine yeyote. Hii ndiyo "uwazi usiwe adhabu ya umma".
 */
export function GroupEventsPage() {
  const { activeGroup, activeGroupId, loading, error } = useMyGroups();
  const events = useApi<GroupEventsResponse>(
    activeGroupId ? () => listGroupEvents(activeGroupId) : null,
    [activeGroupId],
  );

  if (loading) return <Loading label={t('Inapakia...')} />;
  if (error) return <Notice tone="overdue">{error}</Notice>;
  if (!activeGroupId || !activeGroup) return <Navigate to="/karibu" replace />;

  const data = events.data;
  const rows = data?.rows ?? [];

  function tone(status: string) {
    if (status === 'Imelipwa') return 'bg-standing-good/10 text-standing-good';
    if (status === 'Hujalipa') return 'bg-standing-overdue/10 text-standing-overdue';
    if (status === 'Imesamehewa') return 'bg-ink-100 text-ink-700';
    return 'bg-ink-100 text-ink-600';
  }

  function categoryLabel(c: string) {
    if (c === 'Msiba') return t('Msiba');
    if (c === 'KufukuzwaKazi') return t('Kufukuzwa kazi');
    if (c === 'KifoChaMwanachama') return t('Kifo cha mwanachama');
    return c;
  }

  return (
    <div className="space-y-5">
      <header>
        <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-gold-700">
          <CalendarHeart size={14} /> {t('Matukio')}
        </div>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight text-ink-950">{t('Matukio ya Kikundi')}</h1>
        <p className="mt-1 max-w-3xl text-sm text-ink-600">
          {t('Kila tukio lililopitishwa na kikundi, pamoja na hali yako mwenyewe na kodi yako ya malipo. Deni la mwanachama mwingine halionekani hapa.')}
        </p>
      </header>

      {data && (
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
          <Stat label={t('Matukio')} value={num(data.jumla.matukio)} />
          <Stat label={t('Yaliyolipwa na wewe')} value={num(data.jumla.yaliyolipwa)} tone="good" />
          <Stat
            label={t('Unayodaiwa')}
            value={`TSh ${num(data.jumla.nayodaiwa)}`}
            tone={data.jumla.nayodaiwa > 0 ? 'overdue' : 'good'}
          />
        </div>
      )}

      <Card title={t('Orodha ya matukio')} subtitle={t('Kiasi ni kile kilichotakiwa kwa kila mwanachama.')}>
        {events.loading && <Loading label={t('Inapakia matukio...')} />}
        {events.error && <Notice tone="overdue">{events.error}</Notice>}
        {!events.loading && rows.length === 0 && (
          <EmptyState icon={CalendarHeart} title={t('Hakuna tukio bado')}>
            {t('Matukio yataonekana hapa mara viongozi wanapoyaidhinisha.')}
          </EmptyState>
        )}

        {rows.length > 0 && (
          <div className="-mx-4 overflow-x-auto sm:-mx-5">
            <table className="min-w-full text-sm">
              <thead className="bg-ink-50 text-left text-[11px] uppercase tracking-wide text-ink-600">
                <tr>
                  <th className="px-4 py-2 font-semibold">{t('Tarehe')}</th>
                  <th className="px-2 py-2 font-semibold">{t('Tukio')}</th>
                  <th className="px-2 py-2 font-semibold">{t('Mnufaika')}</th>
                  <th className="px-2 py-2 text-right font-semibold">{t('Kiasi')}</th>
                  <th className="px-2 py-2 font-semibold">{t('Kodi yako')}</th>
                  <th className="px-2 py-2 font-semibold">{t('Hali yako')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-100">
                {rows.map((r) => (
                  <tr key={r.caseId}>
                    <td className="whitespace-nowrap px-4 py-2 text-xs text-ink-600">
                      {new Date(r.eventDate).toLocaleDateString('sw-TZ')}
                    </td>
                    <td className="px-2 py-2">
                      <div className="font-medium text-ink-900">{categoryLabel(r.category)}</div>
                      <div className="text-[11px] text-ink-600">{r.caseNumber ?? r.eventCode ?? ''}</div>
                    </td>
                    <td className="px-2 py-2 text-xs">
                      <span className="inline-flex items-center gap-1 text-ink-700">
                        <Users size={12} /> {r.beneficiaryMemberNumber ?? '—'}
                      </span>
                    </td>
                    <td className="figure whitespace-nowrap px-2 py-2 text-right font-semibold">
                      {num(r.perMemberAmount)}
                    </td>
                    <td className="px-2 py-2 font-mono text-xs text-gold-700">{r.paymentCode ?? '—'}</td>
                    <td className="px-2 py-2">
                      <span className={`inline-flex rounded-full px-2 py-0.5 text-[11px] font-semibold ${tone(r.myStatus)}`}>
                        {r.myStatus}
                      </span>
                      {r.myOutstanding > 0 && (
                        <div className="mt-0.5 flex items-center gap-1 text-[11px] text-standing-overdue">
                          <HandCoins size={11} /> {num(r.myOutstanding)}
                        </div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  );
}
