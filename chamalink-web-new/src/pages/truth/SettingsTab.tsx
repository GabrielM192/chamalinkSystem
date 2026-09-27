import { useState } from 'react';
import { AlertOctagon, Save, Trash2 } from 'lucide-react';
import { apiErrorMessage, resetGroupData, saveTruthSettings, type ResetReport, type TruthSettings } from '../../api/truth';
import { Button, Card, Field, Input, MoneyInput, Notice, Segmented } from '../../components/ui';
import { useToast } from '../../lib/toast';
import { isoDateOnly, monthName } from '../../lib/truth';
import { useTruth } from './TruthContext';

import { t } from '../../i18n';
/** Kanuni za michango za kikundi (hakuna hardcode) + kufuta data za majaribio. */
export function SettingsTab() {
  const { settings, canReset } = useTruth();
  return (
    <div className="space-y-6">
      {/* key: fomu inaanzishwa upya kwa thamani za seva kila zinapobadilika */}
      <SettingsForm key={JSON.stringify(settings)} settings={settings} />
      {canReset && <DangerZone />}
    </div>
  );
}

function SettingsForm({ settings }: { settings: TruthSettings | null }) {
  const { groupId, refresh } = useTruth();
  const toast = useToast();

  const [start, setStart] = useState(settings ? isoDateOnly(settings.contributionStartDate).slice(0, 7) : '');
  // Hakuna thamani za kubuni: kikundi kipya kinaanza na sehemu tupu, viongozi wanaweka sheria zao zote.
  const [monthly, setMonthly] = useState<number | null>(settings?.monthlyContribution ?? null);
  const [fine, setFine] = useState<number | null>(settings?.lateFine ?? null);
  const [joinFee, setJoinFee] = useState<number | null>(settings?.joiningFee ?? null);
  const [dueDay, setDueDay] = useState<number | null>(settings?.dueDateDay ?? null);
  const [grace, setGrace] = useState<number | null>(settings?.gracePeriodDays ?? null);
  const [partial, setPartial] = useState<'yes' | 'no' | ''>(
    settings ? (settings.fineOnPartialPayment ? 'yes' : 'no') : '',
  );
  const [autoSavings, setAutoSavings] = useState<'yes' | 'no' | ''>(
    settings ? (settings.autoDeductSavings ? 'yes' : 'no') : '',
  );
  const [cutoff, setCutoff] = useState(isoDateOnly(settings?.manualEntryCutoff));
  const [founders, setFounders] = useState(isoDateOnly(settings?.foundingMembersCutoff));
  const [loanPenalty, setLoanPenalty] = useState<number | null>(settings?.loanLatePenaltyAmount ?? 0);
  const [busy, setBusy] = useState(false);

  const missing = [
    !start && t('mwezi wa kwanza'),
    !(monthly && monthly > 0) && t('mchango wa mwezi'),
    fine === null && t('faini (0 kama hakuna)'),
    joinFee === null && t('Kianzio (0 kama hakuna)'),
    !(dueDay !== null && dueDay >= 1 && dueDay <= 28) && t('tarehe ya mwisho (1–28)'),
    !(grace !== null && grace >= 0 && grace <= 25) && t('siku za neema (0–25)'),
    partial === '' && t('faini kwa malipo pungufu'),
    autoSavings === '' && t('akiba kulipa mwezi usiolipwa'),
  ].filter(Boolean) as string[];
  const valid = missing.length === 0;
  const [sy, sm] = start ? start.split('-').map(Number) : [0, 0];
  const exampleMonth = sm || 7;

  async function save() {
    setBusy(true);
    try {
      await saveTruthSettings(groupId, {
        contributionStartDate: `${start}-01`,
        monthlyContribution: monthly ?? undefined,
        lateFine: fine ?? undefined,
        joiningFee: joinFee ?? undefined,
        dueDateDay: dueDay ?? undefined,
        gracePeriodDays: grace ?? undefined,
        fineOnPartialPayment: partial === '' ? undefined : partial === 'yes',
        autoDeductSavings: autoSavings === '' ? undefined : autoSavings === 'yes',
        // Mwisho wa siku husika (23:59:59) — malipo ya siku hiyo yanakubalika
        manualEntryCutoff: cutoff ? `${cutoff}T23:59:59` : undefined,
        foundingMembersCutoff: founders ? `${founders}T23:59:59` : undefined,
        loanLatePenaltyAmount: loanPenalty ?? 0,
      });
      toast(t("Kanuni zimehifadhiwa — taarifa zote zimehesabiwa upya."), 'good');
      refresh();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card title={t("Kanuni za michango")} subtitle={t("Zinatumika kwa kila mwanachama na kila mwezi — kubadilisha kunahesabu upya historia yote.")}>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t("Mwezi wa kwanza wa michango")}>
            <Input type="month" value={start} onChange={(e) => setStart(e.target.value)} />
          </Field>
          <Field label={t("Mchango wa mwezi")}>
            <MoneyInput value={monthly} onChange={setMonthly} />
          </Field>
          <Field label={t("Faini ya kuchelewa (kwa mwezi)")}>
            <MoneyInput value={fine} onChange={setFine} />
          </Field>
          <Field label={t("Kianzio (join fee)")}>
            <MoneyInput value={joinFee} onChange={setJoinFee} />
          </Field>
          <Field label={t("Tarehe ya mwisho (ya mwezi unaofuata)")} hint={t("Mf. 5 = mchango wa Julai mwisho 5 Agosti saa 23:59")}>
            <Input type="number" inputMode="numeric" min={1} max={28} value={dueDay ?? ''} onChange={(e) => setDueDay(e.target.value === '' ? null : Number(e.target.value))} />
          </Field>
          <Field label={t("Siku za neema za kikundi")} hint={t("0 = faini kuanzia siku inayofuata tarehe ya mwisho")}>
            <Input type="number" inputMode="numeric" min={0} max={25} value={grace ?? ''} onChange={(e) => setGrace(e.target.value === '' ? null : Number(e.target.value))} />
          </Field>
          <Field label={t("Mwisho wa kumbukumbu za mkononi")} hint={t("Baada ya tarehe hii M-Koba pekee ndiyo ukweli")}>
            <Input type="date" value={cutoff} onChange={(e) => setCutoff(e.target.value)} />
          </Field>
          <Field label={t("Waanzilishi: waliojiunga hadi")} hint={t("Hawa wanaanza michango mwezi wa kwanza wa kikundi")}>
            <Input type="date" value={founders} onChange={(e) => setFounders(e.target.value)} />
          </Field>
          <Field label={t("Adhabu ya rejesho la mkopo lililochelewa")}>
            <MoneyInput value={loanPenalty} onChange={setLoanPenalty} />
          </Field>
          <Field label={t("Faini hata kwa malipo pungufu?")} hint={t("Hapana = faini ni kwa mwezi ambao hakulipa chochote kufikia tarehe ya mwisho")}>
            <Segmented<'yes' | 'no'>
              value={partial as 'yes' | 'no'}
              onChange={setPartial}
              options={[
                { value: 'no', label: t("Hapana") },
                { value: 'yes', label: t("Ndiyo") },
              ]}
            />
          </Field>
          <Field
            label={t("Akiba ilipe mwezi usiolipwa?")}
            hint={t("Ndiyo = mwanachama akiwa na ziada na hakulipa kufikia tarehe ya mwisho, ziada inalipa mwezi huo (hakuna faini)")}
          >
            <Segmented<'yes' | 'no'>
              value={autoSavings as 'yes' | 'no'}
              onChange={setAutoSavings}
              options={[
                { value: 'yes', label: t("Ndiyo") },
                { value: 'no', label: t("Hapana") },
              ]}
            />
          </Field>
        </div>

        {!settings && (
          <p className="mt-4 rounded-xl border border-ink-100 bg-paper-raised px-4 py-3 text-sm text-ink-700">
            {t("Weka kanuni")}{' '}<b>{t("zote")}</b>{' '}{t("za kikundi chako. Mfumo hauweki kiasi chochote wenyewe — kila namba inatoka kwenu viongozi.")}
          </p>
        )}
        {!valid && (start || monthly !== null) && (
          <p className="mt-3 text-xs text-ink-600">{t("Bado:")}{' '}{missing.join(', ')}.</p>
        )}
        {valid && dueDay !== null && grace !== null && (
          <div className="mt-5 rounded-xl bg-ink-50 px-4 py-3 text-sm text-ink-800">
            <b>{t("Mfano:")}</b>{' '}{t("Mchango wa")}{' '}{monthName(exampleMonth, sy || undefined)} ({(monthly ?? 0).toLocaleString('en-US')}{t(") unatakiwa kufika kabla ya")}{' '}<b>{t("tarehe")}{' '}{dueDay + grace} {monthName(exampleMonth === 12 ? 1 : exampleMonth + 1)}{' '}{t("saa 23:59")}</b>{t(". Usipofika — deni")}{' '}
            {(monthly ?? 0).toLocaleString('en-US')}{' '}{t("+ faini")}{' '}{(fine ?? 0).toLocaleString('en-US')}{t(". Malipo yanalipa deni la zamani kwanza.")}
          </div>
        )}
        {settings?.kanuni && <p className="mt-2 text-xs text-ink-600">{t("Seva:")}{' '}{settings.kanuni}</p>}

        <div className="mt-5">
          <Button icon={Save} onClick={save} disabled={!valid} loading={busy}>
            {t("Hifadhi kanuni")}
          </Button>
        </div>
    </Card>
  );
}

function DangerZone() {
  const { groupId, groupName, refresh } = useTruth();
  const toast = useToast();
  const [dry, setDry] = useState<ResetReport | null>(null);
  const [keepLoans, setKeepLoans] = useState(false);
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);

  async function check() {
    setBusy(true);
    try {
      setDry(await resetGroupData(groupId, { dryRun: true, keepLoans }));
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  async function wipe() {
    setBusy(true);
    try {
      await resetGroupData(groupId, { dryRun: false, confirm, keepLoans });
      toast(t("Data za fedha za kikundi zimefutwa. Ingiza PDF upya."), 'good');
      setDry(null);
      setConfirm('');
      refresh();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  const total = dry ? Object.values(dry.rows).reduce((s, n) => s + n, 0) : 0;

  return (
    <Card
      title={
        <span className="inline-flex items-center gap-2 text-standing-overdue">
          <AlertOctagon size={16} />{' '}{t("Eneo la hatari: futa data za fedha za majaribio")}
        </span>
      }
      className="border-standing-overdue/30"
    >
      <p className="text-sm text-ink-700">
        {t("Inafuta miamala ya M-Koba, malipo ya mkononi, mapengo, msamaha na data za mfumo wa zamani kwa kikundi hiki")}{' '}<b>{t("pekee")}</b>{t(". Wanachama, namba za simu na kanuni hazifutwi. Angalia kwanza kitakachofutwa.")}
      </p>
      <label className="mt-3 flex items-center gap-2 text-sm">
        <input type="checkbox" checked={keepLoans} onChange={(e) => setKeepLoans(e.target.checked)} className="h-4 w-4 accent-ink-800" />
        {t("Usifute mikopo")}
      </label>
      <div className="mt-3">
        <Button variant="secondary" onClick={check} loading={busy && !dry}>
          {t("Angalia kitakachofutwa")}
        </Button>
      </div>
      {dry && (
        <div className="mt-4 space-y-3">
          <div className="grid grid-cols-2 gap-1.5 text-xs sm:grid-cols-3">
            {Object.entries(dry.rows)
              .filter(([, n]) => n > 0)
              .map(([k, n]) => (
                <div key={k} className="flex justify-between rounded-lg bg-ink-50 px-2.5 py-1.5">
                  <span className="truncate text-ink-700">{k}</span>
                  <span className="figure font-semibold">{n}</span>
                </div>
              ))}
          </div>
          {total === 0 ? (
            <Notice tone="good">{t("Hakuna data ya kufuta.")}</Notice>
          ) : (
            <>
              <Field label={<>{t("Andika jina kamili la kikundi kuthibitisha:")}{' '}<b>{groupName}</b></>}>
                <Input value={confirm} onChange={(e) => setConfirm(e.target.value)} placeholder={groupName} />
              </Field>
              <Button variant="danger" icon={Trash2} disabled={confirm.trim() !== groupName.trim()} loading={busy} onClick={wipe}>
                {t("Futa rekodi")}{' '}{total.toLocaleString('en-US')}{' '}{t("kabisa")}
              </Button>
            </>
          )}
        </div>
      )}
    </Card>
  );
}
