import { CheckCircle2, HelpCircle } from 'lucide-react';
import type { GapContext, GapNeighbor } from '../../api/truth';
import { num, wallDateTime } from '../../lib/truth';

import { t } from '../../i18n';
/**
 * Mnyororo wa balance kuzunguka pengo — kwa kikundi chochote:
 *   [muamala sahihi wa kabla] → [❓ pesa isiyo na mstari] → [muamala sahihi wa baada]
 * Miamala ya mipakani imeshahesabiwa kwa wenyewe; pengo ni KATI yake.
 */
export function GapChain({ gap, amount, onOpenMember }: { gap: GapContext; amount: number; onOpenMember?: (id: string) => void }) {
  const out = amount < 0;
  return (
    <ol className="space-y-1.5 text-xs">
      <Row label={t("Baada ya")} n={gap.previous} onOpenMember={onOpenMember} />
      <li
        className={`flex items-center justify-between gap-2 rounded-lg border border-dashed px-2.5 py-2 ${
          out ? 'border-standing-overdue bg-standing-overdue-bg' : 'border-standing-warn bg-standing-warn-bg'
        }`}
      >
        <span className="inline-flex items-center gap-1.5 font-medium text-ink-900">
          <HelpCircle size={14} className={out ? 'text-standing-overdue' : 'text-standing-warn'} />
          {out ? t("Pesa ilitoka — hakuna mstari") : t("Pesa iliingia — hakuna mstari")}
        </span>
        <span className="figure font-semibold text-ink-950">
          {out ? '−' : '+'}
          {num(Math.abs(amount))}
        </span>
      </li>
      <Row label={t("Kabla ya")} n={gap.next} onOpenMember={onOpenMember} />
      {gap.balanceAfterPrevious != null && gap.expectedBalanceBeforeNext != null && (
        <li className="px-1 pt-1 text-ink-600">
          {t("Balance:")}{' '}<span className="figure">{num(gap.balanceAfterPrevious)}</span> →{' '}
          <span className="figure font-medium text-ink-900">{num(gap.expectedBalanceBeforeNext)}</span>{' '}{t("(tofauti")}{' '}
          <span className="figure">{num(gap.expectedBalanceBeforeNext - gap.balanceAfterPrevious)}</span>)
        </li>
      )}
    </ol>
  );
}

function Row({ label, n, onOpenMember }: { label: string; n: GapNeighbor | null; onOpenMember?: (id: string) => void }) {
  if (!n)
    return (
      <li className="rounded-lg bg-ink-50 px-2.5 py-2 text-ink-600">
        {label}: <span className="italic">{t("mwanzo wa taarifa")}</span>
      </li>
    );
  const who = n.memberName ?? (n.payerName || n.phone);
  return (
    <li className="rounded-lg bg-ink-50 px-2.5 py-2">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="text-[11px] uppercase tracking-wide text-ink-500">{label}</div>
          <div className="flex items-center gap-1.5 font-medium text-ink-900">
            <CheckCircle2 size={13} className="shrink-0 text-standing-good" />
            {n.memberId && onOpenMember ? (
              <button className="truncate hover:underline" onClick={() => onOpenMember(n.memberId!)}>
                {who}
              </button>
            ) : (
              <span className="truncate">{who}</span>
            )}
          </div>
          <div className="figure truncate text-ink-600">
            {n.reference} · {wallDateTime(n.timestamp)}
          </div>
        </div>
        <div className="shrink-0 text-right">
          <div className="figure">
            {n.isWithdrawal ? '−' : '+'}
            {num(n.amount)}
          </div>
          <div className="figure text-ink-600">{t("bal")}{' '}{num(n.balance)}</div>
        </div>
      </div>
    </li>
  );
}
