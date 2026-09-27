import { Navigate, useNavigate } from 'react-router-dom';
import { Scale } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { useMyGroups } from '../hooks/useMyGroups';
import { getMyTruth } from '../api/truth';
import { useApi } from '../lib/useApi';
import { MemberStatement } from '../components/truth/MemberStatement';
import { Button, Loading, Notice } from '../components/ui';
import { groupRoleLabel } from '../lib/status';
import { LEADER_ROLES } from '../lib/truth';

import { t } from '../i18n';
/**
 * "Taarifa Yangu" — kila mwanachama anaona taarifa yake ya ukweli:
 * kila mwezi, kila faini, Kianzio, na kila shilingi aliyolipa ilikokwenda.
 * Namba zilezile ambazo viongozi wanaona (chanzo kimoja: Injini ya Ukweli).
 */
export function MyStatementPage() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const { activeGroup, activeGroupId, loading: groupsLoading, error: groupsError } = useMyGroups();
  const { data, error } = useApi(activeGroupId ? () => getMyTruth(activeGroupId) : null, activeGroupId);

  if (groupsLoading) return <Loading label={t("Inapakia makundi yako...")} />;
  if (groupsError) return <Notice tone="overdue">{groupsError}</Notice>;
  // Hana kikundi bado (amejisajili sasa hivi, au ombi lake linasubiri) → hatua ya kuanzisha/kujiunga
  if (!activeGroupId || !activeGroup) return <Navigate to="/karibu" replace />;

  const isLeader = LEADER_ROLES.has(activeGroup.role);

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-gold-700">
            <Scale size={14} />{' '}{t("Taarifa Yangu")}
          </div>
          <h1 className="mt-1 text-2xl font-semibold tracking-tight text-ink-950">{data?.fullName ?? user?.fullName}</h1>
          <p className="mt-1 text-sm text-ink-600">
            {activeGroup.groupName} · {groupRoleLabel(activeGroup.role)}
            {data ? ` · ${data.memberNumber}` : ''}
          </p>
        </div>
        {isLeader && (
          <Button variant="secondary" onClick={() => navigate('/ukweli')}>
            {t("Ukweli wa Kikundi")}
          </Button>
        )}
      </header>
      {error && <Notice tone="overdue">{error}</Notice>}
      {!data && !error && <Loading label={t("Injini inahesabu taarifa yako...")} />}
      {data && <MemberStatement data={data} showWarnings={isLeader} />}
    </div>
  );
}
