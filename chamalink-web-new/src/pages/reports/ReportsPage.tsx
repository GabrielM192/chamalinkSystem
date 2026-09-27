import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { BarChart3, CalendarRange, FileText, Copy, LayoutDashboard, Printer, Share2, Wallet, AlertTriangle, Upload } from 'lucide-react';
import { Tabs, type Tab } from '../../components/Tabs';
import { Button, Loading, Modal, Notice, Skeleton } from '../../components/ui';
import { getCashbook, getGroupTruth } from '../../api/truth';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { wallDate, wallDateTime } from '../../lib/truth';
import { PageHeader, SetupNotice, TruthProvider } from '../truth/TruthProvider';
import { useTruth } from '../truth/TruthContext';
import { aggregateMonths, staleMonths, whatsappSummary, type MonthAgg } from './reportData';
import { SummaryReport } from './SummaryReport';
import { MonthlyReport } from './MonthlyReport';
import { DebtsReport } from './DebtsReport';
import { CashReport } from './CashReport';
import { OfficialMonthReport } from './OfficialMonthReport';

import { t, tk, tn } from '../../i18n';
const TABS: Tab[] = [
  { id: 'muhtasari', label: tk('Muhtasari'), icon: LayoutDashboard },
  { id: 'taarifa-mwezi', label: tk('Taarifa ya Mwezi'), icon: FileText },
  { id: 'kila-mwezi', label: tk('Kila Mwezi'), icon: CalendarRange },
  { id: 'madeni', label: tk('Madeni & Kianzio'), icon: AlertTriangle },
  { id: 'fedha', label: tk('Fedha za M-Koba'), icon: Wallet },
];

/**
 * /ripoti/:tab — Ripoti za kikundi kutoka Injini ya Ukweli.
 * Chanzo kimoja: /api/truth/group (michango, faini, kianzio, madeni) +
 * /api/truth/cashbook (pesa iliyoingia/iliyotoka/salio kutoka M-Koba).
 * Ripoti za zamani (/api/Reports) zilisoma LedgerEntries zilizogawanywa vibaya — hazitumiki tena.
 */
export function ReportsPage() {
  return (
    <TruthProvider>
      {(ctx) => (
        <div className="space-y-5">
          <PageHeader
            eyebrow={t("Ripoti")}
            icon={BarChart3}
            title={ctx.groupName}
            description={t("Ripoti zote zinatoka kwenye injini ileile inayotoa “Taarifa Yangu” ya kila mwanachama — namba zinalingana kila mahali.")}
          />
          {ctx.bootError && <Notice tone="overdue">{ctx.bootError}</Notice>}
          {!ctx.booted ? <Loading /> : ctx.settings ? <ReportsBody /> : <SetupNotice />}
        </div>
      )}
    </TruthProvider>
  );
}

function ReportsBody() {
  const { tab = 'muhtasari' } = useParams();
  const navigate = useNavigate();
  const toast = useToast();
  const { groupId, groupName, settings, version, goTab } = useTruth();
  const [shareOpen, setShareOpen] = useState(false);

  const truth = useApi(() => getGroupTruth(groupId), groupId, [version]);
  const cash = useApi(() => getCashbook(groupId), groupId, [version]);
  const dataUntil = cash.data?.lastTransaction ?? null;
  const months = useMemo(() => (truth.data ? aggregateMonths(truth.data.members, dataUntil) : []), [truth.data, dataUntil]);

  const activeTab = TABS.some((x) => x.id === tab) ? tab : 'muhtasari';
  const go = (id: string) => navigate(`/ripoti/${id}`);
  const text = truth.data ? whatsappSummary(groupName, truth.data, cash.data ?? null, settings, months) : '';

  async function copy() {
    try {
      await navigator.clipboard.writeText(text);
      toast(t('Ujumbe umenakiliwa — bandika kwenye WhatsApp.'), 'good');
    } catch {
      toast(t('Imeshindwa kunakili. Chagua maandishi na unakili mwenyewe.'), 'warn');
    }
  }

  return (
    <>
      <div className="flex flex-col gap-3 print:hidden sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0 flex-1">
          <Tabs tabs={TABS} activeTab={activeTab} onTabChange={go} scrollable />
        </div>
        <div className="flex shrink-0 gap-2">
          <Button variant="secondary" size="sm" icon={Share2} disabled={!truth.data} onClick={() => setShareOpen(true)}>
            {t("WhatsApp")}
          </Button>
          <Button variant="secondary" size="sm" icon={Printer} disabled={!truth.data} onClick={() => window.print()}>
            {t("Chapisha / PDF")}
          </Button>
        </div>
      </div>

      {truth.error && <Notice tone="overdue">{truth.error}</Notice>}
      {cash.error && <Notice tone="warn">{t("Daftari la M-Koba halikupakia:")}{' '}{cash.error}</Notice>}

      {truth.data && <StaleNotice months={months} until={dataUntil} onImport={() => goTab('import')} />}

      {!truth.data ? (
        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-24" />
            ))}
          </div>
          <Skeleton className="h-64" />
          <Loading label={t("Injini inahesabu upya taarifa za wanachama wote...")} />
        </div>
      ) : (
        <div key={activeTab}>
          {activeTab === 'muhtasari' && <SummaryReport truth={truth.data} cash={cash.data ?? null} months={months} onGo={go} />}
          {activeTab === 'taarifa-mwezi' && <OfficialMonthReport groupId={groupId} months={months} version={version} />}
          {activeTab === 'kila-mwezi' && <MonthlyReport months={months} />}
          {activeTab === 'madeni' && <DebtsReport truth={truth.data} staleLabels={staleMonths(months).map((m) => m.month)} />}
          {activeTab === 'fedha' && <CashReport cash={cash.data ?? null} truth={truth.data} />}
          <p className="mt-6 text-center text-xs text-ink-600">
            {t("Imehesabiwa")}{' '}{wallDateTime(truth.data.asOf)}{' '}{t("· Chanzo: M-Koba + kumbukumbu za mkononi zilizoidhinishwa")}
          </p>
        </div>
      )}

      <Modal
        open={shareOpen}
        onClose={() => setShareOpen(false)}
        title={t("Shiriki kwenye WhatsApp")}
        footer={
          <>
            <Button variant="secondary" onClick={() => setShareOpen(false)}>
              {t("Funga")}
            </Button>
            <Button icon={Copy} onClick={copy}>
              {t("Nakili ujumbe")}
            </Button>
          </>
        }
      >
        <p className="mb-2 text-xs text-ink-600">{t("Muhtasari wa kikundi na orodha ya wanaodaiwa. Nakili, kisha ubandike kwenye kundi la WhatsApp.")}</p>
        <pre className="max-h-[50vh] overflow-auto whitespace-pre-wrap rounded-xl bg-ink-50 p-3 text-xs leading-relaxed text-ink-900">{text}</pre>
      </Modal>
    </>
  );
}

/** Miezi ambayo PDF haijaifunika kikamilifu — "hawajalipa" hapo huenda si kweli. */
function StaleNotice({ months, until, onImport }: { months: MonthAgg[]; until: string | null; onImport: () => void }) {
  const stale = staleMonths(months);
  if (!until || stale.length === 0) return null;
  return (
    <Notice
      tone="warn"
      title={t("Taarifa ya M-Koba iliyoingizwa ni hadi {0}", { 0: wallDate(until) })}
      action={
        <Button size="sm" variant="secondary" icon={Upload} onClick={onImport} className="print:hidden">
          {t("Ingiza PDF mpya")}
        </Button>
      }
    >
      {tn(
        stale.length,
        'Malipo ya baada ya tarehe hiyo bado hayajaonekana, kwa hiyo {months} inaonekana kana kwamba wengi hawajalipa. Mwezi huo hauhesabiwi kwenye kiwango cha ukusanyaji hadi PDF inayofuata iingizwe.',
        'Malipo ya baada ya tarehe hiyo bado hayajaonekana, kwa hiyo {months} zinaonekana kana kwamba wengi hawajalipa. Miezi hiyo haihesabiwi kwenye kiwango cha ukusanyaji hadi PDF inayofuata iingizwe.',
        { months: stale.map((m) => m.label).join(', ') },
      )}
    </Notice>
  );
}
