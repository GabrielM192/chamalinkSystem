import { useEffect, useState } from 'react';
import { HandCoins, BadgePercent, HeartHandshake } from 'lucide-react';
import { Drawer, Loading, Notice, Button, MoneyInput, Field } from '../../components/ui';
import { MemberStatement } from '../../components/truth/MemberStatement';
import { apiErrorMessage, getMemberTruth, getWelfareDeduction, type WelfareDeduction } from '../../api/truth';
import { useApi } from '../../lib/useApi';
import { num } from '../../lib/truth';
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
          <MemberStatement data={data} />
          <WelfareCalculator memberId={data.memberId} />
        </div>
      )}
    </Drawer>
  );
}

/** Msiba/Sherehe: kiasi halisi cha kumlipa baada ya kukata Kianzio (na deni, ukitaka). */
function WelfareCalculator({ memberId }: { memberId: string }) {
  const [amount, setAmount] = useState<number | null>(null);
  const [arrears, setArrears] = useState(false);
  const key = amount ? `${amount}|${arrears}` : null;
  const [st, setSt] = useState<{ key: string; res?: WelfareDeduction; err?: string } | null>(null);

  useEffect(() => {
    if (!key || !amount) return;
    const h = setTimeout(() => {
      getWelfareDeduction(memberId, amount, arrears)
        .then((res) => setSt({ key, res }))
        .catch((e) => setSt({ key, err: apiErrorMessage(e) }));
    }, 350);
    return () => clearTimeout(h);
  }, [memberId, key, amount, arrears]);

  const res = key && st?.key === key ? st.res : null;
  const err = key && st?.key === key ? st.err : null;

  return (
    <details className="group rounded-xl border border-ink-100 bg-paper-raised">
      <summary className="flex cursor-pointer list-none items-center gap-2 px-4 py-3 text-sm font-semibold text-ink-900">
        <HeartHandshake size={16} className="text-gold-700" />
        {t("Kikokotoo cha msiba / sherehe")}
        <span className="ml-auto text-xs font-normal text-ink-600 group-open:hidden">{t("Fungua")}</span>
      </summary>
      <div className="space-y-3 border-t border-ink-100 px-4 py-3">
        <Field label={t("Kiasi kilichoidhinishwa")} hint={t("Kianzio kinachodaiwa hukatwa kwanza kabla ya kumlipa mwanachama.")}>
          <MoneyInput value={amount} onChange={setAmount} />
        </Field>
        <label className="flex items-center gap-2 text-sm text-ink-800">
          <input type="checkbox" checked={arrears} onChange={(e) => setArrears(e.target.checked)} className="h-4 w-4 accent-ink-800" />
          {t("Kata pia deni la michango")}
        </label>
        {err && <Notice tone="overdue">{err}</Notice>}
        {res && (
          <dl className="figure grid grid-cols-2 gap-2 rounded-xl bg-ink-50 p-3 text-sm">
            <dt className="text-ink-600">{t("Kilichoidhinishwa")}</dt>
            <dd className="text-right">{num(res.approved)}</dd>
            <dt className="text-ink-600">{t("− Kianzio")}</dt>
            <dd className="text-right text-standing-warn">{num(res.joinFeeDeducted)}</dd>
            <dt className="text-ink-600">{t("− Deni la michango")}</dt>
            <dd className="text-right text-standing-warn">{num(res.arrearsDeducted)}</dd>
            <dt className="font-semibold text-ink-900">{t("Alipwe")}</dt>
            <dd className="text-right font-semibold text-standing-good">{num(res.netPayout)}</dd>
          </dl>
        )}
      </div>
    </details>
  );
}
