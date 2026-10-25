import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import type { LucideIcon } from 'lucide-react';
import { useMyGroups } from '../../hooks/useMyGroups';
import { Button, Loading, Notice } from '../../components/ui';
import {
  getTruthMembers,
  getTruthSettings,
  listAdjustments,
  listGaps,
  listManualPayments,
  type MemberLite,
  type TruthSettings,
} from '../../api/truth';
import { LEADER_ROLES, RESET_ROLES } from '../../lib/truth';
import { useApi } from '../../lib/useApi';
import { TruthContext, type TruthCtx } from './TruthContext';
import { MemberDrawer } from './MemberDrawer';

import { t } from '../../i18n';
/**
 * Msingi mmoja wa kurasa za viongozi: /ukweli, /ripoti na /mipangilio.
 *
 * Unapakia kikundi, wanachama, kanuni na idadi ya vinavyosubiri idhini MARA MOJA
 * na kuvishirikisha kupitia TruthContext — kwa hiyo kurasa zote zinaonyesha
 * namba zile zile (chanzo kimoja: Injini ya Ukweli) na paneli ile ile ya mwanachama.
 */
export function TruthProvider({ children }: { children: (ctx: TruthCtx & { booted: boolean; bootError: string | null }) => ReactNode }) {
  const navigate = useNavigate();
  const { activeGroup, activeGroupId, loading: groupsLoading, error: groupsError } = useMyGroups();

  const [version, setVersion] = useState(0);
  const [drawer, setDrawer] = useState<{ groupId: string; memberId: string } | null>(null);

  const isLeader = !!activeGroup && LEADER_ROLES.has(activeGroup.role);
  const gid = isLeader ? activeGroupId : null;

  // scope = kikundi: kikibadilishwa, data ya kikundi kilichopita haionyeshwi hata sekunde moja
  const boot = useApi(gid ? () => Promise.all([getTruthMembers(gid), getTruthSettings(gid)]) : null, gid, [version]);
  // Idadi ya vinavyosubiri MIMI niamue (maker-checker: si nilivyoviingiza mwenyewe)
  const pending = useApi(
    gid
      ? () =>
          Promise.all([listManualPayments(gid), listAdjustments(gid), listGaps(gid)]).then(
            ([mp, adj, gaps]) =>
              mp.filter((x) => x.canDecide).length + adj.filter((x) => x.canDecide).length + gaps.filter((x) => x.canDecide).length
          )
      : null,
    gid,
    [version]
  );

  const members: MemberLite[] = useMemo(() => boot.data?.[0] ?? [], [boot.data]);
  const settings: TruthSettings | null = boot.data?.[1] ?? null;
  const booted = boot.data !== undefined || !!boot.error;
  const bootError = boot.error ?? null;
  const pendingCount = pending.data ?? 0;
  const drawerMember = drawer && drawer.groupId === activeGroupId ? drawer.memberId : null;

  const openMember = useCallback(
    (memberId: string) => activeGroupId && setDrawer({ groupId: activeGroupId, memberId }),
    [activeGroupId]
  );
  const refresh = useCallback(() => setVersion((v) => v + 1), []);
  const goTab = useCallback(
    (t: string, params?: Record<string, string>) => {
      const qs = params ? `?${new URLSearchParams(params).toString()}` : '';
      navigate(t === 'kanuni' ? '/mipangilio' : `/ukweli/${t}${qs}`);
      window.scrollTo({ top: 0, behavior: 'smooth' });
    },
    [navigate]
  );

  const ctx: TruthCtx | null = useMemo(
    () =>
      activeGroupId && activeGroup
        ? {
            groupId: activeGroupId,
            groupName: activeGroup.groupName,
            role: activeGroup.role,
            groupType: activeGroup.groupType,
            canReset: RESET_ROLES.has(activeGroup.role),
            members,
            settings,
            version,
            refresh,
            goTab,
            openMember,
            pendingCount,
          }
        : null,
    [activeGroupId, activeGroup, members, settings, version, refresh, goTab, openMember, pendingCount]
  );

  if (groupsLoading) return <Loading label={t("Inapakia makundi yako...")} />;
  if (groupsError) return <Notice tone="overdue">{groupsError}</Notice>;
  if (!ctx) return <Notice>{t("Bado hujajiunga na kikundi chochote.")}</Notice>;
  if (!isLeader)
    return (
      <Notice
        tone="warn"
        title={t("Ukurasa huu ni wa viongozi")}
        action={
          <Button size="sm" variant="secondary" onClick={() => navigate('/')}>
            {t("Taarifa Yangu")}
          </Button>
        }
      >
        {t("Ukiwa mwanachama wa kawaida, taarifa yako ya ukweli iko kwenye “Taarifa Yangu”.")}
      </Notice>
    );

  return (
    <TruthContext.Provider value={ctx}>
      {children({ ...ctx, booted, bootError })}
      <MemberDrawer memberId={drawerMember} onClose={() => setDrawer(null)} />
    </TruthContext.Provider>
  );
}

/** Tangazo la pamoja: kanuni hazijawekwa → peleka kwenye Mipangilio. */
export function SetupNotice() {
  const navigate = useNavigate();
  return (
    <Notice
      tone="warn"
      title={t("Kanuni za michango hazijawekwa")}
      action={
        <Button size="sm" onClick={() => navigate('/mipangilio')}>
          {t("Weka sasa")}
        </Button>
      }
    >
      {t("Bila kanuni (mchango wa mwezi, faini, tarehe ya mwisho, kianzio) injini haiwezi kuhesabu.")}
    </Notice>
  );
}

/** Kichwa cha ukurasa, kinachofanana kwenye kurasa zote za viongozi. */
export function PageHeader({
  eyebrow,
  icon: Icon,
  title,
  description,
  aside,
}: {
  eyebrow: string;
  icon: LucideIcon;
  title: string;
  description?: ReactNode;
  aside?: ReactNode;
}) {
  return (
    <header className="flex flex-wrap items-end justify-between gap-3">
      <div className="min-w-0">
        <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-gold-700">
          <Icon size={14} /> {eyebrow}
        </div>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight text-ink-950">{title}</h1>
        {description && <p className="mt-1 max-w-2xl text-sm text-ink-600">{description}</p>}
      </div>
      {aside}
    </header>
  );
}
