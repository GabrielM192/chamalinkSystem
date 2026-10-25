import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { BookOpen, Eye, FileSpreadsheet, HandHeart, Landmark, ScrollText, ShieldAlert, UserRound } from 'lucide-react';
import { Tabs, type Tab } from '../../components/Tabs';
import { Loading, Notice } from '../../components/ui';
import { useMyGroups } from '../../hooks/useMyGroups';
import { useApi } from '../../lib/useApi';
import { LEADER_ROLES } from '../../lib/truth';
import { benefitsApi } from '../../api/benefits';
import { PageHeader } from '../truth/TruthProvider';
import { alertText } from '../../lib/benefits';
import { CasesTab } from './CasesTab';
import { CaseDrawer } from './CaseDrawer';
import { PolicyTab } from './PolicyTab';
import { MkobaTab } from './MkobaTab';
import { LedgerTab } from './LedgerTab';
import { MineTab } from './MineTab';
import { TransparencyTab } from './TransparencyTab';
import { SecurityTab } from './SecurityTab';
import { t, tk } from '../../i18n';

/**
 * /mafao — msiba na sherehe.
 *  - Kila mwanachama: kesi zote (bila vyeti), kura zinazomsubiri, mafao aliyochangia/aliyopokea,
 *    na KILA utoaji wa M-Koba pamoja na maelezo yake (uwazi = kinga dhidi ya udanganyifu).
 *  - Viongozi: kufungua kesi, kuhakiki vyeti, kuthibitisha malipo, sera + viwango, daftari la mafao.
 * Ruhusa halisi zinakaguliwa na seva (BenefitService).
 * URL: ?tab=kesi|yangu|mkoba|uwazi|daftari|ulinzi|sera  &  ?kesi={id} kufungua kesi moja kwa moja (kutoka kwenye arifa).
 */
export function BenefitsPage() {
  const [params, setParams] = useSearchParams();
  const { activeGroup, activeGroupId } = useMyGroups();
  const [version, setVersion] = useState(0);
  const refresh = () => setVersion((v) => v + 1);

  const isLeader = !!activeGroup && LEADER_ROLES.has(activeGroup.role);
  const alerts = useApi(activeGroupId ? () => benefitsApi.alerts(activeGroupId) : null, activeGroupId, [version]);
  const policy = useApi(activeGroupId ? () => benefitsApi.policy(activeGroupId) : null, activeGroupId, [version]);

  if (!activeGroup || !activeGroupId) return <Loading />;

  const pendingPolicy = (policy.data?.pending.filter((g) => g.canDecide).length ?? 0) + (policy.data?.rates.filter((r) => r.canDecide).length ?? 0);
  const TABS: Tab[] = [
    { id: 'kesi', label: tk('Kesi'), icon: HandHeart },
    { id: 'yangu', label: tk('Yangu'), icon: UserRound },
    { id: 'mkoba', label: tk('Utoaji wa M-Koba'), icon: Landmark },
    { id: 'uwazi', label: tk('Uwazi'), icon: Eye },
    ...(isLeader || policy.data?.iAmAuditor
      ? [
          { id: 'daftari', label: tk('Daftari la mafao'), icon: FileSpreadsheet },
          { id: 'ulinzi', label: tk('Ulinzi na hatari'), icon: ShieldAlert },
        ]
      : []),
    { id: 'sera', label: tk('Sera na viwango'), icon: ScrollText, badge: isLeader ? pendingPolicy : 0 },
  ];
  const tabParam = params.get('tab') ?? 'kesi';
  const tab = TABS.some((x) => x.id === tabParam) ? tabParam : 'kesi';
  const caseId = params.get('kesi');

  const setTab = (id: string) => setParams((p) => {
    const n = new URLSearchParams(p);
    n.set('tab', id);
    return n;
  });
  const openCase = (id: string | null) => setParams((p) => {
    const n = new URLSearchParams(p);
    if (id) n.set('kesi', id);
    else n.delete('kesi');
    return n;
  });

  const noPolicy = policy.data && !policy.data.active;

  return (
    <div className="space-y-5">
      <PageHeader
        eyebrow={t('Mafao')}
        icon={BookOpen}
        title={activeGroup.groupName}
        description={t(
          'Msiba na sherehe: kiasi = kiwango kwa kila mwanachama × idadi ya wanachama wanaohesabiwa. Hakuna kiongozi mmoja anayeweza kutoa pesa — cheti kinahakikiwa, kura zinapigwa, na malipo yanathibitishwa kwa M-Koba.',
        )}
      />

      {(alerts.data ?? []).filter((a) => !(a.code === 'NO_GOVERNANCE' && noPolicy)).map((a) => (
        <Notice
          key={a.code}
          tone={a.level === 'danger' ? 'overdue' : a.level === 'warn' ? 'warn' : 'neutral'}
          action={
            a.link ? (
              <Link to={a.link} className="text-xs font-semibold underline">
                {t('Fungua')}
              </Link>
            ) : undefined
          }
        >
          {alertText(a)}
        </Notice>
      ))}
      {noPolicy && (
        <Notice tone="warn" title={t('Sera ya mafao haijawekwa')}>
          {isLeader
            ? t('Kiongozi mmoja apendekeze sera (idadi ya idhini, nani anahesabiwa, makato) na kiongozi mwingine aidhinishe. Kabla ya hapo hakuna kesi inayoweza kufunguliwa.')
            : t('Viongozi bado hawajaweka sera ya mafao ya kikundi.')}
        </Notice>
      )}

      <Tabs tabs={TABS} activeTab={tab} onTabChange={setTab} scrollable />

      {tab === 'kesi' && <CasesTab groupId={activeGroupId} isLeader={isLeader} version={version} onOpen={openCase} onChanged={refresh} canOpen={!noPolicy} />}
      {tab === 'yangu' && <MineTab groupId={activeGroupId} version={version} onOpen={openCase} onChanged={refresh} canOpen={!noPolicy} />}
      {tab === 'mkoba' && <MkobaTab groupId={activeGroupId} isLeader={isLeader} version={version} onOpen={openCase} onChanged={refresh} />}
      {tab === 'uwazi' && <TransparencyTab groupId={activeGroupId} version={version} onOpen={openCase} />}
      {tab === 'ulinzi' && (isLeader || policy.data?.iAmAuditor) && <SecurityTab groupId={activeGroupId} version={version} isLeader={isLeader} onOpen={openCase} onChanged={refresh} />}
      {tab === 'daftari' && (isLeader || policy.data?.iAmAuditor) && <LedgerTab groupId={activeGroupId} version={version} onOpen={openCase} />}
      {tab === 'sera' && (
        <PolicyTab groupId={activeGroupId} isLeader={isLeader} data={policy.data} error={policy.error} onChanged={refresh} />
      )}

      <CaseDrawer groupId={activeGroupId} caseId={caseId} version={version} onClose={() => openCase(null)} onChanged={refresh} />
    </div>
  );
}
