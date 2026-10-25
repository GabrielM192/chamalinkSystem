import { useEffect, useState } from 'react';
import { BadgePercent, ClipboardCheck, FileWarning, HandCoins, Trash2, UploadCloud } from 'lucide-react';
import { Link } from 'react-router-dom';
import {
  apiErrorMessage,
  listAdjustments,
  listGaps,
  listManualPayments,
  listMkobaImports,
  listResetRequests,
  type AdjustmentItem,
  type ResetRequestItem,
  type StagedImportSummary,
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
  const [data, setData] = useState<{
    mp: ManualPaymentItem[];
    adj: AdjustmentItem[];
    gaps: GapItem[];
    imports: StagedImportSummary[];
    resets: ResetRequestItem[];
  } | null>(null);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      listManualPayments(groupId),
      listAdjustments(groupId),
      listGaps(groupId),
      listMkobaImports(groupId).catch(() => [] as StagedImportSummary[]),
      listResetRequests(groupId).catch(() => [] as ResetRequestItem[]),
    ])
      .then(([mp, adj, gaps, imports, resets]) =>
        !cancelled &&
        setData({
          mp: mp.filter((x) => x.status === 'Pending'),
          adj: adj.filter((x) => x.status === 'Pending'),
          gaps: gaps.filter((x) => x.status === 'Pending'),
          imports: imports.filter((x) => x.status === 'Pending'),
          resets: resets.filter((x) => x.status === 'Pending'),
        })
      )
      .catch((e) => !cancelled && setErr(apiErrorMessage(e)));
    return () => {
      cancelled = true;
    };
  }, [groupId, version]);

  if (err) return <Notice tone="overdue">{err}</Notice>;
  if (!data) return <Loading />;

  const total = data.mp.length + data.adj.length + data.gaps.length + data.imports.length + data.resets.length;
  if (total === 0)
    return (
      <EmptyState icon={ClipboardCheck} title={t("Hakuna kinachosubiri idhini")}>
        {t("Maombi mapya ya malipo ya mkononi, msamaha/neema, mapengo ya PDF, rasimu za PDF ya M-Koba na maombi ya kufuta data yataonekana hapa.")}
      </EmptyState>
    );

  const mine =
    [...data.mp, ...data.adj, ...data.gaps].filter((x) => !x.canDecide).length +
    data.imports.filter((x) => x.mine).length +
    data.resets.filter((x) => x.mine).length;

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

      {data.imports.length > 0 && (
        <Section icon={UploadCloud} title={t("Rasimu ya PDF ya M-Koba")} count={data.imports.length}>
          {data.imports.map((i) => (
            <Notice key={i.id} tone={i.canApprove ? 'warn' : 'neutral'}>
              {t("Miamala mipya {0}, salio la mwisho {1}.", { 0: i.newRows, 1: (i.lastBalance ?? 0).toLocaleString() })}{' '}
              {i.mine ? t("Uliipakia wewe — inahitaji kiongozi mwingine.") : t("Ipitie na uiidhinishe au uikatae.")}{' '}
              <Link className="font-semibold underline" to="/ukweli/import">{t("Fungua")}</Link>
            </Notice>
          ))}
        </Section>
      )}
      {data.resets.length > 0 && (
        <Section icon={Trash2} title={t("Ombi la kufuta data ya fedha")} count={data.resets.length}>
          {data.resets.map((r) => (
            <Notice key={r.id} tone="overdue">
              {r.mine ? t("Uliomba wewe — inahitaji kiongozi mwingine.") : t("Kiongozi ameomba kufuta data ya fedha ya kikundi. Kama hukubaliani, likatae.")}{' '}
              <Link className="font-semibold underline" to="/mipangilio">{t("Fungua")}</Link>
            </Notice>
          ))}
        </Section>
      )}
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
