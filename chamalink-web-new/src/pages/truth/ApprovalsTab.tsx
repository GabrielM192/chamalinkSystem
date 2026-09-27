import { useEffect, useState } from 'react';
import { BadgePercent, ClipboardCheck, FileWarning, HandCoins } from 'lucide-react';
import {
  apiErrorMessage,
  listAdjustments,
  listGaps,
  listManualPayments,
  type AdjustmentItem,
  type GapItem,
  type ManualPaymentItem,
} from '../../api/truth';
import { EmptyState, Loading, Notice } from '../../components/ui';
import { ManualRequestsList } from './ManualRequests';
import { AdjustmentList } from './AdjustmentsTab';
import { GapCard } from './GapsTab';
import { useTruth } from './TruthContext';

import { t } from '../../i18n';
/**
 * Kikasha cha idhini: kila kinachosubiri uamuzi mahali pamoja.
 * Maker-checker: vilivyoingizwa na wewe vinaonyeshwa lakini huwezi kuviidhinisha.
 */
export function ApprovalsTab() {
  const { groupId, version, refresh } = useTruth();
  const [data, setData] = useState<{ mp: ManualPaymentItem[]; adj: AdjustmentItem[]; gaps: GapItem[] } | null>(null);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    Promise.all([listManualPayments(groupId), listAdjustments(groupId), listGaps(groupId)])
      .then(([mp, adj, gaps]) =>
        !cancelled &&
        setData({
          mp: mp.filter((x) => x.status === 'Pending'),
          adj: adj.filter((x) => x.status === 'Pending'),
          gaps: gaps.filter((x) => x.status === 'Pending'),
        })
      )
      .catch((e) => !cancelled && setErr(apiErrorMessage(e)));
    return () => {
      cancelled = true;
    };
  }, [groupId, version]);

  if (err) return <Notice tone="overdue">{err}</Notice>;
  if (!data) return <Loading />;

  const total = data.mp.length + data.adj.length + data.gaps.length;
  if (total === 0)
    return (
      <EmptyState icon={ClipboardCheck} title={t("Hakuna kinachosubiri idhini")}>
        {t("Maombi mapya ya malipo ya mkononi, msamaha/neema na mapengo ya PDF yataonekana hapa.")}
      </EmptyState>
    );

  const mine = [...data.mp, ...data.adj, ...data.gaps].filter((x) => !x.canDecide).length;

  return (
    <div className="space-y-6">
      <Notice tone="neutral">
        {t("Vinavyosubiri:")}{' '}<b>{total}</b>
        {mine > 0 && (
          <>
            {' '}
            · <b>{mine}</b>{' '}{t("uliviingiza wewe — vinahitaji kiongozi mwingine.")}
          </>
        )}
      </Notice>

      {data.mp.length > 0 && (
        <Section icon={HandCoins} title={t("Malipo ya mkononi")} count={data.mp.length}>
          <ManualRequestsList items={data.mp} onChanged={refresh} />
        </Section>
      )}
      {data.adj.length > 0 && (
        <Section icon={BadgePercent} title={t("Msamaha wa faini & neema")} count={data.adj.length}>
          <AdjustmentList items={data.adj} onChanged={refresh} />
        </Section>
      )}
      {data.gaps.length > 0 && (
        <Section icon={FileWarning} title={t("Mapengo ya PDF")} count={data.gaps.length}>
          <div className="space-y-2.5">
            {data.gaps.map((g) => (
              <GapCard key={g.id} gap={g} onChanged={refresh} />
            ))}
          </div>
        </Section>
      )}
    </div>
  );
}

function Section({ icon: Icon, title, count, children }: { icon: typeof HandCoins; title: string; count: number; children: React.ReactNode }) {
  return (
    <section className="space-y-3">
      <h2 className="flex items-center gap-2 font-semibold text-ink-950">
        <Icon size={17} /> {title}
        <span className="rounded-full bg-ink-100 px-2 text-xs font-semibold text-ink-700">{count}</span>
      </h2>
      {children}
    </section>
  );
}
