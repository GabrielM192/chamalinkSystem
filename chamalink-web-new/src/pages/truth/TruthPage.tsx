import { Navigate, useParams, useSearchParams } from 'react-router-dom';
import {
  LayoutDashboard,
  HandCoins,
  ClipboardCheck,
  Smartphone,
  FileWarning,
  BadgePercent,
  Upload,
  Scale,
  ShieldCheck,
} from 'lucide-react';
import { Tabs, type Tab } from '../../components/Tabs';
import { Loading, Notice } from '../../components/ui';
import { wallDate } from '../../lib/truth';
import { PageHeader, SetupNotice, TruthProvider } from './TruthProvider';
import { OverviewTab } from './OverviewTab';
import { ManualPaymentsTab } from './ManualPaymentsTab';
import { ApprovalsTab } from './ApprovalsTab';
import { PhonesTab } from './PhonesTab';
import { GapsTab } from './GapsTab';
import { AdjustmentsTab } from './AdjustmentsTab';
import { ImportTab } from './ImportTab';

import { t, tk } from '../../i18n';
const TABS: Tab[] = [
  { id: 'muhtasari', label: tk('Muhtasari'), icon: LayoutDashboard },
  { id: 'malipo', label: tk('Malipo ya Mkononi'), icon: HandCoins },
  { id: 'idhini', label: tk('Idhini'), icon: ClipboardCheck },
  { id: 'simu', label: tk('Namba za Simu'), icon: Smartphone },
  { id: 'mapengo', label: tk('Mapengo ya PDF'), icon: FileWarning },
  { id: 'msamaha', label: tk('Msamaha & Neema'), icon: BadgePercent },
  { id: 'import', label: tk('Import M-Koba'), icon: Upload },
];

/**
 * /ukweli/:tab — Kituo cha kazi cha viongozi cha Injini ya Ukweli.
 * Kanuni za michango sasa ziko /mipangilio (mahali pamoja); ripoti ziko /ripoti.
 * Hakuna ukurasa mwingine unaoandika mgawanyo wa malipo.
 */
export function TruthPage() {
  const { tab = 'muhtasari' } = useParams();
  const [search] = useSearchParams();

  // Viungo vya zamani vya /ukweli/kanuni vinaelekezwa kwenye Mipangilio
  if (tab === 'kanuni') return <Navigate to="/mipangilio" replace />;

  return (
    <TruthProvider>
      {(ctx) => {
        const tabs = TABS.map((x) => (x.id === 'idhini' ? { ...x, badge: ctx.pendingCount } : x));
        const activeTab = TABS.some((x) => x.id === tab) ? tab : 'muhtasari';
        const { settings } = ctx;
        return (
          <div className="space-y-5">
            <PageHeader
              eyebrow={t("Injini ya Ukweli")}
              icon={Scale}
              title={ctx.groupName}
              description={t("Taarifa zinahesabiwa upya kila mara kutoka M-Koba na kumbukumbu za mkononi zilizoidhinishwa — hakuna namba inayoandikwa kwa mkono.")}
              aside={
                settings && (
                  <div className="flex flex-wrap items-center gap-2 text-xs text-ink-600">
                    <span className="inline-flex items-center gap-1 rounded-full bg-paper-raised px-2.5 py-1 ring-1 ring-ink-100">
                      <ShieldCheck size={13} className="text-standing-good" />
                      {t("Mwisho wa kulipa: tarehe")}{' '}{settings.dueDateDay}{' '}{t("mwezi unaofuata")}
                      {settings.gracePeriodDays > 0 ? t(" + siku {0}", { 0: settings.gracePeriodDays }) : ''}
                    </span>
                    {settings.manualEntryCutoff && (
                      <span className="rounded-full bg-paper-raised px-2.5 py-1 ring-1 ring-ink-100">
                        {t("Manual hadi")}{' '}{wallDate(settings.manualEntryCutoff)}
                      </span>
                    )}
                  </div>
                )
              }
            />

            <Tabs tabs={tabs} activeTab={activeTab} onTabChange={(id) => ctx.goTab(id)} scrollable />

            {ctx.bootError && <Notice tone="overdue">{ctx.bootError}</Notice>}
            {ctx.booted && !settings && <SetupNotice />}

            {!ctx.booted ? (
              <Loading />
            ) : (
              <div key={`${activeTab}-${ctx.groupId}`}>
                {activeTab === 'muhtasari' && <OverviewTab />}
                {activeTab === 'malipo' && <ManualPaymentsTab initialMemberId={search.get('member')} />}
                {activeTab === 'idhini' && <ApprovalsTab />}
                {activeTab === 'simu' && <PhonesTab />}
                {activeTab === 'mapengo' && <GapsTab />}
                {activeTab === 'msamaha' && <AdjustmentsTab initialMemberId={search.get('member')} />}
                {activeTab === 'import' && <ImportTab />}
              </div>
            )}
          </div>
        );
      }}
    </TruthProvider>
  );
}
