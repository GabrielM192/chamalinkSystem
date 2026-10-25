import { HandCoins, BadgePercent } from 'lucide-react';
import { Drawer, Loading, Notice, Button } from '../../components/ui';
import { MemberStatement } from '../../components/truth/MemberStatement';
import { getMemberTruth } from '../../api/truth';
import { useApi } from '../../lib/useApi';
import { useTruth } from './TruthContext';

import { t } from '../../i18n';
export function MemberDrawer({ memberId, onClose }: { memberId: string | null; onClose: () => void }) {
  const { goTab, version } = useTruth();
  const { data, error } = useApi(memberId ? () => getMemberTruth(memberId) : null, memberId, [version]);

  return (
    <Drawer
      open={!!memberId}
      onClose={onClose}
      wide
      title={data?.fullName ?? t("Taarifa ya mwanachama")}
      subtitle={data ? `${data.memberNumber}` : undefined}
    >
      {error && <Notice tone="overdue">{error}</Notice>}
      {!data && !error && <Loading />}
      {data && (
        <div className="space-y-5">
          <div className="flex flex-wrap gap-2">
            <Button
              icon={HandCoins}
              onClick={() => {
                onClose();
                goTab('malipo', { member: data.memberId });
              }}
            >
              {t("Ingiza malipo ya mkononi")}
            </Button>
            <Button
              variant="secondary"
              icon={BadgePercent}
              onClick={() => {
                onClose();
                goTab('msamaha', { member: data.memberId });
              }}
            >
              {t("Msamaha / Neema")}
            </Button>
          </div>
          {/* SASISHO16: kikokotoo cha msiba/sherehe kimeondolewa — hesabu ya mafao iko kwenye kesi rasmi pekee (Mafao) */}
          <MemberStatement data={data} />
        </div>
      )}
    </Drawer>
  );
}
