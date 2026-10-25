import { useEffect, useState, type ReactNode } from 'react';
import { invalidateLoansVisibility } from '../../hooks/useLoansVisibility';
import { AlertOctagon, Save, Trash2 } from 'lucide-react';
import {
  apiErrorMessage,
  decideSettingsProposal,
  getSettingsProposals,
  previewReset,
  listResetRequests,
  requestReset,
  approveReset,
  rejectReset,
  type ResetRequestItem,
  saveTruthSettings,
  type ResetReport,
  type SettingsProposal,
  type TruthSettings,
} from '../../api/truth';
import { StatusBadge } from '../../components/StatusBadge';
import { ReasonModal } from '../../components/benefits/common';
import { useApi } from '../../lib/useApi';
import { Button, Card, Field, Input, MoneyInput, Notice, Segmented } from '../../components/ui';
import { useToast } from '../../lib/toast';
import { isoDateOnly, monthName, num, realDateTime, wallDate } from '../../lib/truth';
import { useTruth } from './TruthContext';

import { t, tk } from '../../i18n';
/** Kanuni za michango za kikundi (hakuna hardcode) + kufuta data za majaribio. */
export function SettingsTab() {
  const { settings, canReset, groupId, version } = useTruth();
  const [pv, setPv] = useState(0);
  const proposals = useApi(settings ? () => getSettingsProposals(groupId) : null, groupId, [version, pv]);
  const pending = proposals.data?.find((p) => p.status === 'Pending') ?? null;
  return (
    <div className="space-y-6">
      {pending && <PendingProposal p={pending} settings={settings} onChanged={() => setPv((v) => v + 1)} />}
      {/* key: fomu inaanzishwa upya kwa thamani za seva kila zinapobadilika */}
      <SettingsForm key={JSON.stringify(settings)} settings={settings} locked={!!pending} onProposed={() => setPv((v) => v + 1)} />
      {proposals.data && proposals.data.some((p) => p.status !== 'Pending') && <ProposalHistory list={proposals.data.filter((p) => p.status !== 'Pending')} />}
      {canReset && (
        <DevTools>
          <DangerZone />
        </DevTools>
      )}
    </div>
  );
}

function SettingsForm({ settings, locked, onProposed }: { settings: TruthSettings | null; locked: boolean; onProposed: () => void }) {
  const { groupId, refresh, groupType } = useTruth();
  const toast = useToast();

  const [start, setStart] = useState(settings ? isoDateOnly(settings.contributionStartDate).slice(0, 7) : '');
  // Hakuna thamani za kubuni: kikundi kipya kinaanza na sehemu tupu, viongozi wanaweka sheria zao zote.
  const [monthly, setMonthly] = useState<number | null>(settings?.monthlyContribution ?? null);
  const [fine, setFine] = useState<number | null>(settings?.lateFine ?? null);
  const [fineMode, setFineMode] = useState<'Fixed' | 'Percent'>(settings?.lateFineMode ?? 'Fixed');
  const [finePct, setFinePct] = useState<number | null>(settings?.lateFinePercent ?? null);
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
  // SASISHO15: sheria za mikopo (zinatumika na /mikopo — hakuna namba ya kubuni)
  const yn = (v: boolean | undefined | null): 'yes' | 'no' | '' => (v === undefined || v === null ? '' : v ? 'yes' : 'no');
  const [loansOn, setLoansOn] = useState<'yes' | 'no'>(settings?.loansEnabled ? 'yes' : 'no');
  const [lInterest, setLInterest] = useState<number | null>(settings?.loanInterestPercent ?? null);
  const [lMonths, setLMonths] = useState<number | null>(settings?.loanRepaymentMonths ?? null);
  const [lMax, setLMax] = useState<number | null>(settings?.loanMaxAmount ?? null);
  const [lMult, setLMult] = useState<number | null>(settings?.loanMaxMultipleOfContributions ?? null);
  const [lMinMonths, setLMinMonths] = useState<number | null>(settings?.loanMinMembershipMonths ?? null);
  const [lArrears, setLArrears] = useState<'yes' | 'no' | ''>(yn(settings?.loanAllowWithArrears ?? false));
  const [lMulti, setLMulti] = useState<'yes' | 'no' | ''>(yn(settings?.loanAllowMultipleActive ?? false));
  const [lReserve, setLReserve] = useState<number | null>(settings?.loanMinCashReserve ?? null);
  const [lApprovals, setLApprovals] = useState<number | null>(settings?.loanRequiredApprovals ?? null);
  const [lValid, setLValid] = useState<number | null>(settings?.loanApprovalValidDays ?? null);
  const [roleVotes, setRoleVotes] = useState<number | null>(settings?.roleChangeApprovals ?? null);
  const [cooling, setCooling] = useState<number | null>(settings?.leaderCoolingOffDays ?? null);
  const [cashAge, setCashAge] = useState<number | null>(settings?.cashStatementMaxAgeDays ?? null);
  // SASISHO18 — vikundi vinavyochangishana KILA TUKIO: pochi + kiwango cha chini (floor)
  const [evtFloor, setEvtFloor] = useState<number | null>(settings?.memberMinimumBalance ?? null);
  const [evtWindow, setEvtWindow] = useState<number | null>(settings?.eventTopUpWindowDays ?? null);
  const [evtFine, setEvtFine] = useState<number | null>(settings?.overdueEventFineAmount ?? null);
  const [evtCap, setEvtCap] = useState<number | null>(settings?.maxEventDeductionPerMemberPerMonth ?? null);
  const [evtMax, setEvtMax] = useState<number | null>(settings?.maxEventsPerMonth ?? null);
  const [evtBen, setEvtBen] = useState<'yes' | 'no'>(settings?.beneficiaryContributes ? 'yes' : 'no');
  // SASISHO19 — kanuni za J3: faini -> siku za neema -> hatari ya kuondolewa -> kurudi
  const [evtGrace, setEvtGrace] = useState<number | null>(settings?.eventFineGraceDays ?? null);
  const [evtApprovals, setEvtApprovals] = useState<number | null>(settings?.removalApprovalsRequired ?? null);
  const [evtMult, setEvtMult] = useState<number | null>(settings?.reinstatementFineMultiplier ?? null);
  const [evtJoinBack, setEvtJoinBack] = useState<'yes' | 'no'>(settings?.reinstatementRequiresJoiningFee ? 'yes' : 'no');
  // SASISHO21 — lango la kujiunga, uthibitisho wa mikono miwili, madai na SMS
  const [joinGate, setJoinGate] = useState<'yes' | 'no'>(settings?.joiningPaymentRequired ? 'yes' : 'no');
  const [joinPortion, setJoinPortion] = useState<number | null>(settings?.joiningSavingsPortion ?? 0);
  const [joinWindow, setJoinWindow] = useState<number | null>(settings?.joiningPaymentWindowDays ?? 0);
  const [twoMan, setTwoMan] = useState<number | null>(settings?.paymentSecondApprovalAmount ?? 0);
  const [claimsOn, setClaimsOn] = useState<'yes' | 'no'>(settings?.claimsEnabled ? 'yes' : 'no');
  const [smsOn, setSmsOn] = useState<'yes' | 'no'>(settings?.smsEnabled ? 'yes' : 'no');
  const [smsLow, setSmsLow] = useState<number | null>(settings?.smsLowCreditThreshold ?? 0);
  const [smsFrom, setSmsFrom] = useState<number | null>(settings?.smsQuietFromHour ?? 22);
  const [smsTo, setSmsTo] = useState<number | null>(settings?.smsQuietToHour ?? 7);
  const [smsPer, setSmsPer] = useState<number | null>(settings?.smsMaxPerMemberPerEvent ?? 3);
  const [smsChars, setSmsChars] = useState<number | null>(settings?.smsMaxCharsPerMessage ?? 160);
  // SASISHO21 — rufaa (viongozi 2+ wanaamua)
  const [appealsOn, setAppealsOn] = useState<'yes' | 'no'>(settings?.appealsEnabled === false ? 'no' : 'yes');
  const [appealWindow, setAppealWindow] = useState<number | null>(settings?.appealWindowDays ?? 0);
  const [appealApprovals, setAppealApprovals] = useState<number | null>(settings?.appealApprovalsRequired ?? 2);
  const [busy, setBusy] = useState(false);

  // SASISHO18: kikundi cha "kila tukio" hakina mchango wa mwezi — hakiruhusiwi kulazimishwa.
  const isEventGroup = groupType === 'EventBased';

  const loanMissing =
    loansOn === 'yes'
      ? ([
          !(lInterest !== null && lInterest >= 0 && lInterest <= 100) && t('riba ya mkopo (0–100%)'),
          !(lMonths !== null && lMonths >= 1 && lMonths <= 60) && t('miezi ya kurejesha (1–60)'),
          !(lApprovals !== null && lApprovals >= 2) && t('kura za viongozi (angalau 2)'),
          !(lValid !== null && lValid >= 1 && lValid <= 90) && t('siku za idhini (1–90)'),
          !((lMax ?? 0) > 0 || (!isEventGroup && (lMult ?? 0) > 0)) &&
            t(isEventGroup ? 'kikomo cha mkopo (kiasi)' : 'kikomo cha mkopo (kiasi au mara ya michango)'),
        ].filter(Boolean) as string[])
      : [];

  const missing = [
    ...loanMissing,
    !start && t('mwezi wa kwanza wa kumbukumbu'),
    // SASISHO21: kila aina ya kikundi inadai namba ZAKE — si za mwenzake.
    !isEventGroup && !(monthly && monthly > 0) && t('mchango wa mwezi'),
    !isEventGroup && fineMode === 'Fixed' && fine === null && t('faini (0 kama hakuna)'),
    !isEventGroup && fineMode === 'Percent' && !(finePct !== null && finePct > 0 && finePct <= 100) && t('asilimia ya faini (1–100)'),
    joinFee === null && t('Kianzio (0 kama hakuna)'),
    !isEventGroup && !(dueDay !== null && dueDay >= 1 && dueDay <= 28) && t('tarehe ya mwisho (1–28)'),
    !isEventGroup && !(grace !== null && grace >= 0 && grace <= 25) && t('siku za neema (0–25)'),
    !isEventGroup && partial === '' && t('faini kwa malipo pungufu'),
    !isEventGroup && autoSavings === '' && t('akiba kulipa mwezi usiolipwa'),
    isEventGroup && evtFloor === null && t('kiwango cha chini cha salio (floor)'),
    isEventGroup && evtWindow === null && t('siku za kujaza salio baada ya tukio'),
    isEventGroup && evtFine === null && t('faini ya deni linalochelewa'),
    roleVotes !== null && !(roleVotes >= 1 && roleVotes <= 10) && t('kura za kubadilisha cheo (1–10)'),
    cooling !== null && !(cooling >= 0 && cooling <= 90) && t('siku za kusubiri za kiongozi mpya (0–90)'),
    cashAge !== null && !(cashAge >= 1 && cashAge <= 120) && t('umri wa juu wa PDF ya M-Koba (1–120)'),
    // SASISHO18 — sera ya matukio
    isEventGroup && evtWindow !== null && !(evtWindow >= 0 && evtWindow <= 365) && t('siku za kujaza salio (0–365)'),
    isEventGroup && evtMax !== null && !(evtMax >= 0 && evtMax <= 100) && t('idadi ya matukio kwa mwezi (0–100)'),
    isEventGroup && (evtFloor ?? 0) < 0 && t('kiwango cha chini (si hasi)'),
    isEventGroup && (evtFine ?? 0) < 0 && t('faini ya deni la tukio (si hasi)'),
    isEventGroup && (evtCap ?? 0) < 0 && t('kikomo cha makato kwa mwezi (si hasi)'),
    // SASISHO19
    isEventGroup && evtGrace !== null && !(evtGrace >= 0 && evtGrace <= 90) && t('siku za neema baada ya faini (0–90)'),
    isEventGroup && evtApprovals !== null && !(evtApprovals >= 0 && evtApprovals <= 20) && t('viongozi wanaoidhinisha kuondolewa (0–20)'),
    isEventGroup && evtMult !== null && !(evtMult >= 0 && evtMult <= 10) && t('kizidishi cha faini ya kurudi (0–10)'),
    isEventGroup && evtMult !== null && evtMult > 0 && evtMult < 1 && t('kizidishi cha faini ya kurudi (0 au kuanzia 1)'),
    // SASISHO21
    joinGate === 'yes' && (joinFee ?? 0) <= 0 && t('ada ya kujiunga (lango la malipo linahitaji ada)'),
    joinGate === 'yes' && joinPortion !== null && ((joinFee ?? 0) <= 0 || joinPortion < 0 || joinPortion > (joinFee ?? 0)) && t('sehemu ya akiba isizidi ada ya kujiunga'),
    joinGate === 'yes' && joinWindow !== null && !(joinWindow >= 1 && joinWindow <= 60) && t('siku za kulipa ada ya kujiunga (1–60)'),
    twoMan !== null && twoMan < 0 && t('kiasi cha mkono wa pili (si hasi)'),
    // Saa za utulivu si lazima: zote mbili tupu = hakuna kizuizi cha usiku (0 → 24).
    smsOn === 'yes' && smsFrom !== null && !(smsFrom >= 0 && smsFrom <= 23) && t('saa ya kuanza utulivu (0–23)'),
    smsOn === 'yes' && smsTo !== null && !(smsTo >= 1 && smsTo <= 24) && t('saa ya kuisha utulivu (1–24)'),
    smsOn === 'yes' && smsFrom !== null && smsTo !== null && smsFrom >= smsTo && t('saa za utulivu (kuanza kabla ya kuisha)'),
    smsOn === 'yes' && smsPer !== null && !(smsPer >= 0 && smsPer <= 20) && t('SMS kwa mwanachama kwa tukio (0–20)'),
    smsOn === 'yes' && smsChars !== null && !(smsChars >= 0 && smsChars <= 480) && t('herufi kwa SMS moja (0–480)'),
    smsOn === 'yes' && smsLow !== null && smsLow < 0 && t('kiwango cha onyo la salio (si hasi)'),
    appealsOn === 'yes' && !(appealApprovals !== null && appealApprovals >= 2 && appealApprovals <= 20) && t('viongozi wa rufaa (2–20)'),
    appealsOn === 'yes' && appealWindow !== null && !(appealWindow >= 0 && appealWindow <= 365) && t('siku za kuwasilisha rufaa (0–365)'),
  ].filter(Boolean) as string[];
  const valid = missing.length === 0;

  /** Kila kitu kinachokosekana kinaelekeza kwenye sehemu yake — kiongozi hafufui fomu mwenyewe. */
  const MISSING_SECTION: Record<string, string> = {
    'mwezi wa kwanza wa kumbukumbu': 'sec-start',
    'mchango wa mwezi': 'sec-month',
    'faini (0 kama hakuna)': 'sec-month',
    'asilimia ya faini (1–100)': 'sec-month',
    'tarehe ya mwisho (1–28)': 'sec-month',
    'siku za neema (0–25)': 'sec-month',
    'faini kwa malipo pungufu': 'sec-month',
    'akiba kulipa mwezi usiolipwa': 'sec-month',
    'kiwango cha chini cha salio (floor)': 'sec-events',
    'siku za kujaza salio baada ya tukio': 'sec-events',
    'faini ya deni linalochelewa': 'sec-events',
    'Kianzio (0 kama hakuna)': 'sec-common',
    'kiasi cha mkono wa pili (si hasi)': 'sec-common',
    'ada ya kujiunga (lango la malipo linahitaji ada)': 'sec-join',
    'sehemu ya akiba isizidi ada ya kujiunga': 'sec-join',
    'siku za kulipa ada ya kujiunga (1–60)': 'sec-join',
    'saa ya kuanza utulivu (0–23)': 'sec-sms',
    'saa ya kuisha utulivu (1–24)': 'sec-sms',
    'saa za utulivu (kuanza kabla ya kuisha)': 'sec-sms',
    'SMS kwa mwanachama kwa tukio (0–20)': 'sec-sms',
    'herufi kwa SMS moja (0–480)': 'sec-sms',
    'kiwango cha onyo la salio (si hasi)': 'sec-sms',
    'viongozi wa rufaa (2–20)': 'sec-appeals',
    'siku za kuwasilisha rufaa (0–365)': 'sec-appeals',
    'riba ya mkopo (0–100%)': 'sec-loans',
    'miezi ya kurejesha (1–60)': 'sec-loans',
    'kura za viongozi (angalau 2)': 'sec-loans',
    'siku za idhini (1–90)': 'sec-loans',
    'kikomo cha mkopo (kiasi)': 'sec-loans',
    'kikomo cha mkopo (kiasi au mara ya michango)': 'sec-loans',
    'kura za kubadilisha cheo (1–10)': 'sec-gov',
    'siku za kusubiri za kiongozi mpya (0–90)': 'sec-gov',
    'umri wa juu wa PDF ya M-Koba (1–120)': 'sec-gov',
  };

  function jumpToMissing() {
    const id = MISSING_SECTION[missing[0] ?? ''];
    const el = id ? document.getElementById(id) : null;
    if (!el) return;
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    el.classList.add('ring-2', 'ring-standing-warn');
    window.setTimeout(() => el.classList.remove('ring-2', 'ring-standing-warn'), 1800);
  }
  const [sy, sm] = start ? start.split('-').map(Number) : [0, 0];
  const exampleMonth = sm || 7;
  const effectiveFine = fineMode === 'Percent' ? Math.round(((monthly ?? 0) * (finePct ?? 0)) / 100) : fine ?? 0;

  async function save() {
    if (!valid) {
      const extra = missing.length > 3 ? ` (+${missing.length - 3})` : '';
      toast(t('Bado:') + ' ' + missing.slice(0, 3).join(', ') + extra, 'overdue');
      jumpToMissing();
      return;
    }
    setBusy(true);
    try {
      const saveResult = saveTruthSettings(groupId, {
        contributionStartDate: `${start}-01`,
        joiningFee: joinFee ?? undefined,
        // Mwisho wa siku husika (23:59:59) — malipo ya siku hiyo yanakubalika
        manualEntryCutoff: cutoff ? `${cutoff}T23:59:59` : undefined,
        // SASISHO21: kikundi cha "kila tukio" hakitumi sheria za mwezi — hazitumwi kabisa.
        ...(isEventGroup
          ? {}
          : {
              monthlyContribution: monthly ?? undefined,
              lateFineMode: fineMode,
              // Asilimia: faini = mchango × asilimia (Katiba 13: 50%). Kiasi kamili kinahifadhiwa kwa rekodi/ripoti za zamani.
              lateFine: fineMode === 'Fixed' ? fine ?? undefined : effectiveFine,
              lateFinePercent: fineMode === 'Percent' ? finePct ?? undefined : undefined,
              dueDateDay: dueDay ?? undefined,
              gracePeriodDays: grace ?? undefined,
              fineOnPartialPayment: partial === '' ? undefined : partial === 'yes',
              autoDeductSavings: autoSavings === '' ? undefined : autoSavings === 'yes',
              foundingMembersCutoff: founders ? `${founders}T23:59:59` : undefined,
            }),
        loanLatePenaltyAmount: loanPenalty ?? 0,
        loansEnabled: loansOn === 'yes',
        loanInterestPercent: lInterest ?? undefined,
        loanRepaymentMonths: lMonths ?? undefined,
        // Kikomo tupu => 0 = ondoa kikomo hicho (seva: 0 inafuta)
        loanMaxAmount: lMax ?? 0,
        loanMaxMultipleOfContributions: lMult ?? 0,
        loanMinMembershipMonths: lMinMonths ?? undefined,
        loanAllowWithArrears: lArrears === '' ? undefined : lArrears === 'yes',
        loanAllowMultipleActive: lMulti === '' ? undefined : lMulti === 'yes',
        loanMinCashReserve: lReserve ?? undefined,
        loanRequiredApprovals: lApprovals ?? undefined,
        loanApprovalValidDays: lValid ?? undefined,
        roleChangeApprovals: roleVotes ?? undefined,
        leaderCoolingOffDays: cooling ?? undefined,
        cashStatementMaxAgeDays: cashAge ?? undefined,
        // SASISHO18 — matukio
        ...(isEventGroup
          ? {
              memberMinimumBalance: evtFloor ?? 0,
              eventTopUpWindowDays: evtWindow ?? 0,
              overdueEventFineAmount: evtFine ?? 0,
              maxEventDeductionPerMemberPerMonth: evtCap ?? 0,
              maxEventsPerMonth: evtMax ?? 0,
              beneficiaryContributes: evtBen === 'yes',
              // SASISHO19 — hatarini kuondolewa + kurudi
              eventFineGraceDays: evtGrace ?? 0,
              removalApprovalsRequired: evtApprovals ?? 0,
              reinstatementFineMultiplier: evtMult ?? 0,
              reinstatementRequiresJoiningFee: evtJoinBack === 'yes',
            }
          : {}),
        // SASISHO21 — lango la kujiunga, mikono miwili, madai, SMS
        joiningPaymentRequired: joinGate === 'yes',
        joiningSavingsPortion: joinPortion ?? 0,
        joiningPaymentWindowDays: joinWindow ?? 0,
        paymentSecondApprovalAmount: twoMan ?? 0,
        claimsEnabled: claimsOn === 'yes',
        smsEnabled: smsOn === 'yes',
        smsLowCreditThreshold: smsLow ?? 0,
        smsQuietFromHour: smsFrom ?? 0,
        smsQuietToHour: smsTo ?? 24,
        smsMaxPerMemberPerEvent: smsPer ?? 0,
        smsMaxCharsPerMessage: smsChars ?? 0,
        appealsEnabled: appealsOn === 'yes',
        appealWindowDays: appealWindow ?? 0,
        appealApprovalsRequired: appealApprovals ?? 2,
      });
      const r = await saveResult;
      toast(r.applied ? t("Kanuni zimehifadhiwa — taarifa zote zimehesabiwa upya.") : t("Pendekezo limehifadhiwa. Litaanza kutumika kiongozi WA PILI akiidhinisha."), 'good');
      if (r.applied) refresh();
      else onProposed();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card
      title={isEventGroup ? t("Kanuni za kikundi") : t("Kanuni za michango")}
      subtitle={
        isEventGroup
          ? t("Kikundi hiki kinachangisha KILA TUKIO. Weka pochi, kiwango cha chini na mipaka — kila namba inatoka kwenu.")
          : t("Zinatumika kwa kila mwanachama na kila mwezi — kubadilisha kunahesabu upya historia yote.")
      }
    >
        <div className="mb-5 flex flex-wrap items-center gap-2 text-xs">
          <span className="text-ink-600">{t("Aina ya kikundi")}:</span>
          <span className="inline-flex items-center rounded-full bg-ink-100 px-2.5 py-1 text-[11px] font-semibold text-ink-800">
            {isEventGroup ? t("Kila tukio") : t("Kila mwezi")}
          </span>
        </div>

        <div id="sec-start" className="grid scroll-mt-24 gap-4 sm:grid-cols-2">
          <Field
            label={isEventGroup ? t("Mwezi wa kwanza wa kumbukumbu") : t("Mwezi wa kwanza wa michango")}
            hint={isEventGroup ? t("Mwezi ambao kumbukumbu za kikundi zinaanza") : undefined}
          >
            <Input type="month" value={start} onChange={(e) => setStart(e.target.value)} />
          </Field>
        </div>

        {/* ====== KILA MWEZI: sheria za mchango wa mwezi ====== */}
        {!isEventGroup && (
          <div id="sec-month" className="mt-6 scroll-mt-24 border-t border-ink-100 pt-5">
            <h3 className="font-semibold text-ink-950">{t("Sheria za mwezi")}</h3>
            <p className="mt-0.5 text-xs text-ink-600">
              {t("Mchango, faini, tarehe ya mwisho na siku za neema — kila mwezi, kwa kila mwanachama.")}
            </p>
            <div className="mt-4 grid gap-4 sm:grid-cols-2">
              <Field label={t("Mchango wa mwezi")}>
                <MoneyInput value={monthly} onChange={setMonthly} />
              </Field>
              <Field label={t("Faini ya kuchelewa (kwa mwezi)")} hint={fineMode === 'Percent' && monthly ? t("= {0} kwa mchango wa {1}", { 0: num(effectiveFine), 1: num(monthly) }) : undefined}>
                <div className="space-y-2">
                  <Segmented<'Fixed' | 'Percent'>
                    value={fineMode}
                    onChange={setFineMode}
                    options={[
                      { value: 'Fixed', label: t("Kiasi kamili") },
                      { value: 'Percent', label: t("Asilimia ya mchango") },
                    ]}
                  />
                  {fineMode === 'Fixed' ? (
                    <MoneyInput value={fine} onChange={setFine} />
                  ) : (
                    <div className="relative">
                      <Input type="number" inputMode="decimal" min={1} max={100} value={finePct ?? ''} onChange={(e) => setFinePct(e.target.value === '' ? null : Number(e.target.value))} className="pr-8" />
                      <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-sm text-ink-600">%</span>
                    </div>
                  )}
                </div>
              </Field>
              <Field label={t("Tarehe ya mwisho (ya mwezi unaofuata)")} hint={t("Mf. 5 = mchango wa Julai mwisho 5 Agosti saa 23:59")}>
                <Input type="number" inputMode="numeric" min={1} max={28} value={dueDay ?? ''} onChange={(e) => setDueDay(e.target.value === '' ? null : Number(e.target.value))} />
              </Field>
              <Field label={t("Siku za neema za kikundi")} hint={t("0 = faini kuanzia siku inayofuata tarehe ya mwisho")}>
                <Input type="number" inputMode="numeric" min={0} max={25} value={grace ?? ''} onChange={(e) => setGrace(e.target.value === '' ? null : Number(e.target.value))} />
              </Field>
              <Field label={t("Waanzilishi: waliojiunga hadi")} hint={t("Hawa wanaanza michango mwezi wa kwanza wa kikundi")}>
                <Input type="date" value={founders} onChange={(e) => setFounders(e.target.value)} />
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
          </div>
        )}

        {/* SASISHO18 — vikundi vinavyochangishana KILA TUKIO: pochi + kiwango cha chini */}
        {isEventGroup && (
          <div id="sec-events" className="mt-6 scroll-mt-24 border-t border-ink-100 pt-5">
            <h3 className="font-semibold text-ink-950">{t("Pochi na viwango vya matukio")}</h3>
            <p className="mt-0.5 text-xs text-ink-600">
              {t(
                "Kila mwanachama ana salio (pochi) ndani ya kikundi. Tukio linapoidhinishwa, mchango unakatwa kwenye salio — lakini salio halishuki chini ya kiwango ulichoweka hapa. Kinachobaki ni deni linalokamilishwa na amana zijazo."
              )}
            </p>
            <div className="mt-4 grid gap-4 sm:grid-cols-2">
              <Field
                label={t("Kiwango cha chini cha salio (floor)")}
                hint={t("Mf. 50,000: mwanachama lazima awe na angalau kiasi hiki; kinachozidi hicho pekee kinakatwa")}
              >
                <MoneyInput value={evtFloor} onChange={setEvtFloor} />
              </Field>
              <Field
                label={t("Siku za kujaza salio baada ya tukio")}
                hint={t("0 = hakuna muda maalum; deni linabaki wazi")}
              >
                <Input
                  type="number"
                  inputMode="numeric"
                  min={0}
                  max={365}
                  value={evtWindow ?? ''}
                  onChange={(e) => setEvtWindow(e.target.value === '' ? null : Number(e.target.value))}
                />
              </Field>
              <Field
                label={t("Faini ya deni linalochelewa")}
                hint={t("Inatozwa deni linapopita muda bila kujazwa. 0 = hakuna faini")}
              >
                <MoneyInput value={evtFine} onChange={setEvtFine} />
              </Field>
              <Field
                label={t("Kikomo cha makato kwa mwanachama kwa mwezi")}
                hint={t("Hulinda mwanachama matukio mengi mfululizo. 0 = hakuna kikomo")}
              >
                <MoneyInput value={evtCap} onChange={setEvtCap} />
              </Field>
              <Field
                label={t("Idadi ya juu ya matukio kwa mwezi")}
                hint={t("0 = hakuna kikomo")}
              >
                <Input
                  type="number"
                  inputMode="numeric"
                  min={0}
                  max={100}
                  value={evtMax ?? ''}
                  onChange={(e) => setEvtMax(e.target.value === '' ? null : Number(e.target.value))}
                />
              </Field>
              <Field
                label={t("Mnufaika anachangia tukio lake?")}
                hint={t("Hapana = mnufaika haakatwi kwenye tukio lake mwenyewe")}
              >
                <Segmented<'yes' | 'no'>
                  value={evtBen}
                  onChange={setEvtBen}
                  options={[
                    { value: 'no', label: t("Hapana") },
                    { value: 'yes', label: t("Ndiyo") },
                  ]}
                />
              </Field>

              {/* SASISHO19 — hatarini kuondolewa + kurudi (Kanuni za J3) */}
              <Field
                label={t("Siku za neema baada ya faini")}
                hint={t("Deni likibaki baada ya siku hizi, mwanachama anaingia orodha ya \"hatarini kuondolewa\" inayoonekana kwa wote")}
              >
                <Input
                  type="number"
                  inputMode="numeric"
                  min={0}
                  max={90}
                  value={evtGrace ?? ''}
                  onChange={(e) => setEvtGrace(e.target.value === '' ? null : Number(e.target.value))}
                />
              </Field>
              <Field
                label={t("Viongozi wanaoidhinisha kuondolewa")}
                hint={t("Kanuni: ZAIDI ya wawili (angalau 3). 0 = tumia sera ya mafao — kamwe chini ya 3")}
              >
                <Input
                  type="number"
                  inputMode="numeric"
                  min={0}
                  max={20}
                  value={evtApprovals ?? ''}
                  onChange={(e) => setEvtApprovals(e.target.value === '' ? null : Number(e.target.value))}
                />
              </Field>
              <Field
                label={t("Faini ya kurudi (mara ngapi ya faini)")}
                hint={t("Mf. 2 = anayerudi analipa faini mara mbili. 0 = hakuna ada ya kurudi")}
              >
                <Input
                  type="number"
                  inputMode="decimal"
                  min={0}
                  max={10}
                  step="0.5"
                  value={evtMult ?? ''}
                  onChange={(e) => setEvtMult(e.target.value === '' ? null : Number(e.target.value))}
                />
              </Field>
              <Field
                label={t("Kurudi kunahitaji ada ya kujiunga?")}
                hint={t("Ndiyo = anayerudi analipa tena ada ya kujiunga kabla ya kurejeshwa")}
              >
                <Segmented<'yes' | 'no'>
                  value={evtJoinBack}
                  onChange={setEvtJoinBack}
                  options={[
                    { value: 'no', label: t("Hapana") },
                    { value: 'yes', label: t("Ndiyo") },
                  ]}
                />
              </Field>
            </div>
            {evtFloor !== null && evtFloor > 0 && (
              <div className="mt-4 rounded-xl bg-ink-50 px-4 py-3 text-sm text-ink-800">
                <b>{t("Mfano:")}</b>{' '}
                {t(
                  "salio la mwanachama linakatwa hadi kufikia kiwango cha chini cha TSH {0}; kinachobaki baada ya hapo huwa deni, na amana ijayo inalilipa kwanza.",
                  { 0: num(evtFloor) }
                )}
              </div>
            )}
          </div>
        )}

        {/* SASISHO21 — LANGO LA KUJIUNGA, MIKONO MIWILI, MADAI NA SMS */}
        <div id="sec-common" className="mt-6 scroll-mt-24 border-t border-ink-100 pt-5">
          <h3 className="font-semibold text-ink-950">{t("Ada ya kujiunga na kumbukumbu")}</h3>
          <p className="mt-0.5 text-xs text-ink-600">
            {t("Zinatumika kwa kikundi cha kila mwezi na kile cha kila tukio — hazitegemei aina ya kikundi.")}
          </p>
          <div className="mt-4 grid gap-4 sm:grid-cols-2">
              <Field label={t("Kianzio (join fee)")}>
                <MoneyInput value={joinFee} onChange={setJoinFee} />
              </Field>
              <Field label={t("Mwisho wa kumbukumbu za mkononi")} hint={t("Baada ya tarehe hii M-Koba pekee ndiyo ukweli")}>
                <Input type="date" value={cutoff} onChange={(e) => setCutoff(e.target.value)} />
              </Field>
              <Field label={t("Adhabu ya rejesho la mkopo lililochelewa")}>
                <MoneyInput value={loanPenalty} onChange={setLoanPenalty} />
              </Field>
          </div>
        </div>


        <div id="sec-join" className="mt-6 scroll-mt-24 border-t border-ink-100 pt-5">
          <h3 className="font-semibold text-ink-950">{t("Kujiunga na malipo")}</h3>
          <p className="mt-0.5 text-xs text-ink-600">
            {t("Kikundi kinachokusanya ada ya kujiunga KABLA ya usajili hakimfanyi mtu mwanachama hadi malipo yathibitishwe. Ombi lisilolipwa linaisha — hakuna deni.")}
          </p>
          <div className="mt-4 grid gap-4 sm:grid-cols-2">
            <Field
              label={t("Lango la kujiunga (lipa kwanza)")}
              hint={t("Ndiyo = muombaji anapata kodi ya malipo; usajili unakamilika baada ya uthibitisho")}
            >
              <Segmented<'yes' | 'no'>
                value={joinGate}
                onChange={setJoinGate}
                options={[
                  { value: 'no', label: t("Hapana") },
                  { value: 'yes', label: t("Ndiyo") },
                ]}
              />
            </Field>
            {joinGate === 'yes' && (
              <>
                <Field label={t("Sehemu ya ada inayoingia akiba")} hint={t("Kiasi kilichobaki kinaenda kwenye kianzio/faini za kikundi")}>
                  <MoneyInput value={joinPortion} onChange={setJoinPortion} />
                </Field>
                <Field label={t("Siku za kulipa ada")} hint={t("Muombaji ana siku hizi kumaliza malipo; baadaye ombi linafungwa")}>
                  <Input
                    type="number"
                    inputMode="numeric"
                    min={1}
                    max={60}
                    value={joinWindow ?? ''}
                    onChange={(e) => setJoinWindow(e.target.value === '' ? null : Number(e.target.value))}
                  />
                </Field>
              </>
            )}
            <Field
              label={t("Kiasi kinachohitaji kiongozi wa pili")}
              hint={t("Malipo yaliyothibitishwa kwa mkono yanayofikia kiasi hiki yanahitaji kiongozi MWINGINE. 0 = hakuna")}
            >
              <MoneyInput value={twoMan} onChange={setTwoMan} />
            </Field>
            <Field label={t("Madai ya malipo yanakubaliwa?")} hint={t("Ndiyo = mwanachama anaweza kusema \"nimelipa\"; hazibadilishi deni hadi hazina ithibitishe")}>
              <Segmented<'yes' | 'no'>
                value={claimsOn}
                onChange={setClaimsOn}
                options={[
                  { value: 'no', label: t("Hapana") },
                  { value: 'yes', label: t("Ndiyo") },
                ]}
              />
            </Field>
          </div>
        </div>

        <div id="sec-sms" className="mt-6 scroll-mt-24 border-t border-ink-100 pt-5">
          <h3 className="font-semibold text-ink-950">{t("SMS za kikundi")}</h3>
          <p className="mt-0.5 text-xs text-ink-600">
            {t("SMS ni za no-reply: tangazo, kumbusho, faini na uthibitisho. Salio ni credits zilizonunuliwa — SI bajeti ya mwezi.")}
          </p>
          <div className="mt-4 grid gap-4 sm:grid-cols-2">
            <Field label={t("SMS zimewashwa?")}>
              <Segmented<'yes' | 'no'>
                value={smsOn}
                onChange={setSmsOn}
                options={[
                  { value: 'no', label: t("Hapana") },
                  { value: 'yes', label: t("Ndiyo") },
                ]}
              />
            </Field>
            {smsOn === 'yes' && (
              <>
                <Field label={t("Onyo la salio (credits)")} hint={t("Salio likifika hapa, viongozi wanajulishwa ili wanunue zaidi")}>
                  <Input
                    type="number"
                    inputMode="numeric"
                    min={0}
                    value={smsLow ?? ''}
                    onChange={(e) => setSmsLow(e.target.value === '' ? null : Number(e.target.value))}
                  />
                </Field>
                <Field label={t("Saa za utulivu (kuanzia)")} hint={t("SMS haitui usiku — inasubiri hadi saa hii")}>
                  <Input
                    type="number"
                    inputMode="numeric"
                    min={0}
                    max={23}
                    value={smsFrom ?? ''}
                    onChange={(e) => setSmsFrom(e.target.value === '' ? null : Number(e.target.value))}
                  />
                </Field>
                <Field label={t("Saa za utulivu (hadi)")} hint={t("Saa 24 = hakuna kizuizi cha usiku")}>
                  <Input
                    type="number"
                    inputMode="numeric"
                    min={1}
                    max={24}
                    value={smsTo ?? ''}
                    onChange={(e) => setSmsTo(e.target.value === '' ? null : Number(e.target.value))}
                  />
                </Field>
                <Field label={t("SMS kwa mwanachama kwa tukio")} hint={t("Kinga ya ujumbe mwingi kwa tukio moja (0 = hakuna kikomo)")}>
                  <Input
                    type="number"
                    inputMode="numeric"
                    min={0}
                    max={20}
                    value={smsPer ?? ''}
                    onChange={(e) => setSmsPer(e.target.value === '' ? null : Number(e.target.value))}
                  />
                </Field>
                <Field label={t("Herufi kwa SMS moja")} hint={t("0 = 160 (kawaida ya GSM-7). Ujumbe mrefu unagawanywa na kuhesabiwa SMS zaidi")}>
                  <Input
                    type="number"
                    inputMode="numeric"
                    min={0}
                    max={480}
                    value={smsChars ?? ''}
                    onChange={(e) => setSmsChars(e.target.value === '' ? null : Number(e.target.value))}
                  />
                </Field>
              </>
            )}
          </div>
        </div>

        <div id="sec-appeals" className="mt-6 scroll-mt-24 border-t border-ink-100 pt-5">
          <h3 className="font-semibold text-ink-950">{t("Rufaa")}</h3>
          <p className="mt-0.5 text-xs text-ink-600">
            {t("Mwanachama anapinga uamuzi unaomgusa. Rufaa haiwezi kuamuliwa na kiongozi mmoja — kamwe.")}
          </p>
          <div className="mt-4 grid gap-4 sm:grid-cols-2">
            <Field label={t("Rufaa zinaruhusiwa?")}>
              <Segmented<'yes' | 'no'>
                value={appealsOn}
                onChange={setAppealsOn}
                options={[
                  { value: 'no', label: t("Hapana") },
                  { value: 'yes', label: t("Ndiyo") },
                ]}
              />
            </Field>
            {appealsOn === 'yes' && (
              <>
                <Field label={t("Viongozi wanaohitajika kuamua")} hint={t("Kamwe chini ya 2. Aliyeomba hapigani kura yake.")}>
                  <Input
                    type="number"
                    inputMode="numeric"
                    min={2}
                    max={20}
                    value={appealApprovals ?? ''}
                    onChange={(e) => setAppealApprovals(e.target.value === '' ? null : Number(e.target.value))}
                  />
                </Field>
                <Field label={t("Siku za kuwasilisha rufaa")} hint={t("Tangu uamuzi. 0 = hakuna kikomo cha muda")}>
                  <Input
                    type="number"
                    inputMode="numeric"
                    min={0}
                    max={365}
                    value={appealWindow ?? ''}
                    onChange={(e) => setAppealWindow(e.target.value === '' ? null : Number(e.target.value))}
                  />
                </Field>
              </>
            )}
          </div>
        </div>

        {/* ====== AINA ZOTE MBILI: ada ya kujiunga + kumbukumbu ====== */}
        <div id="sec-loans" className="mt-6 scroll-mt-24 border-t border-ink-100 pt-5">
          <h3 className="font-semibold text-ink-950">{t("Sheria za mikopo")}</h3>
          <p className="mt-0.5 text-xs text-ink-600">
            {t("Zinatumika kwenye ukurasa wa Mikopo: sifa za mkopaji, riba, muda na kura. Mkopo unatolewa tu baada ya kura za viongozi na uthibitisho wa M-Koba.")}
          </p>
          <div className="mt-4 grid gap-4 sm:grid-cols-2">
            <Field label={t("Mikopo imefunguliwa?")}>
              <Segmented<'yes' | 'no'>
                value={loansOn}
                onChange={setLoansOn}
                options={[
                  { value: 'no', label: t("Hapana") },
                  { value: 'yes', label: t("Ndiyo") },
                ]}
              />
            </Field>
            {loansOn === 'yes' && (
              <>
                <Field label={t("Riba ya mkopo (%)")} hint={t("Ya kiasi chote, mara moja. 0 = hakuna riba")}>
                  <Input type="number" inputMode="numeric" min={0} max={100} value={lInterest ?? ''} onChange={(e) => setLInterest(e.target.value === '' ? null : Number(e.target.value))} />
                </Field>
                <Field label={t("Miezi ya kurejesha")}>
                  <Input type="number" inputMode="numeric" min={1} max={60} value={lMonths ?? ''} onChange={(e) => setLMonths(e.target.value === '' ? null : Number(e.target.value))} />
                </Field>
                <Field label={t("Kikomo cha mkopo mmoja")} hint={t("Acha tupu = hakuna kikomo cha kiasi")}>
                  <MoneyInput value={lMax} onChange={setLMax} />
                </Field>
                {!isEventGroup && (
                  <Field label={t("Kikomo kwa michango (mara ngapi)")} hint={t("Mf. 3 = hadi mara 3 ya michango aliyolipa. Tupu = hakuna")}>
                    <Input type="number" inputMode="decimal" min={0} step="0.5" value={lMult ?? ''} onChange={(e) => setLMult(e.target.value === '' ? null : Number(e.target.value))} />
                  </Field>
                )}
                <Field label={t("Miezi ya uanachama kabla ya kukopa")}>
                  <Input type="number" inputMode="numeric" min={0} value={lMinMonths ?? ''} onChange={(e) => setLMinMonths(e.target.value === '' ? null : Number(e.target.value))} />
                </Field>
                <Field label={t("Akiba ya lazima kwenye M-Koba")} hint={t("Mkopo hautolewi kama salio litashuka chini ya kiasi hiki")}>
                  <MoneyInput value={lReserve} onChange={setLReserve} />
                </Field>
                <Field label={t("Kura za viongozi zinazohitajika")} hint={t("Angalau 2 — mkopaji na ndugu zake hawapigi kura")}>
                  <Input type="number" inputMode="numeric" min={2} value={lApprovals ?? ''} onChange={(e) => setLApprovals(e.target.value === '' ? null : Number(e.target.value))} />
                </Field>
                <Field label={t("Idhini inadumu (siku)")} hint={t("Mkopo usipotolewa ndani ya siku hizi, ombi linaisha")}>
                  <Input type="number" inputMode="numeric" min={1} max={90} value={lValid ?? ''} onChange={(e) => setLValid(e.target.value === '' ? null : Number(e.target.value))} />
                </Field>
                <Field label={t("Anaweza kukopa akiwa na deni la michango?")}>
                  <Segmented<'yes' | 'no'>
                    value={lArrears as 'yes' | 'no'}
                    onChange={setLArrears}
                    options={[
                      { value: 'no', label: t("Hapana") },
                      { value: 'yes', label: t("Ndiyo") },
                    ]}
                  />
                </Field>
                <Field label={t("Zaidi ya mkopo mmoja kwa wakati mmoja?")}>
                  <Segmented<'yes' | 'no'>
                    value={lMulti as 'yes' | 'no'}
                    onChange={setLMulti}
                    options={[
                      { value: 'no', label: t("Hapana") },
                      { value: 'yes', label: t("Ndiyo") },
                    ]}
                  />
                </Field>
              </>
            )}
          </div>
        </div>

        <div id="sec-gov" className="mt-6 scroll-mt-24 border-t border-ink-100 pt-5">
          <h3 className="font-semibold text-ink-950">{t("Utawala na usalama")}</h3>
          <p className="mt-0.5 text-xs text-ink-600">
            {t("Vyeo vinabadilishwa kwa kura tu (si kiongozi mmoja). Fedha za mafao/mikopo zinatolewa tu kama PDF ya M-Koba ni ya karibuni.")}
          </p>
          <div className="mt-4 grid gap-4 sm:grid-cols-2">
            <Field label={t("Kura za kubadilisha cheo")} hint={t("Viongozi wangapi (mbali na mwombaji na mhusika) waidhinishe. Tupu = 1")}>
              <Input type="number" inputMode="numeric" min={1} max={10} value={roleVotes ?? ''} onChange={(e) => setRoleVotes(e.target.value === '' ? null : Number(e.target.value))} />
            </Field>
            <Field label={t("Siku za kusubiri za kiongozi mpya")} hint={t("Kiongozi mpya anapata mamlaka baada ya siku hizi. Tupu = 0")}>
              <Input type="number" inputMode="numeric" min={0} max={90} value={cooling ?? ''} onChange={(e) => setCooling(e.target.value === '' ? null : Number(e.target.value))} />
            </Field>
            <Field label={t("Umri wa juu wa PDF ya M-Koba (siku)")} hint={t("PDF ikiwa ya zamani kuliko hii, malipo ya mafao na mikopo yanazuiwa. Tupu = yanazuiwa daima")}>
              <Input type="number" inputMode="numeric" min={1} max={120} value={cashAge ?? ''} onChange={(e) => setCashAge(e.target.value === '' ? null : Number(e.target.value))} />
            </Field>
          </div>
        </div>

        {!settings && (
          <p className="mt-4 rounded-xl border border-ink-100 bg-paper-raised px-4 py-3 text-sm text-ink-700">
            {t("Weka kanuni")}{' '}<b>{t("zote")}</b>{' '}{t("za kikundi chako. Mfumo hauweki kiasi chochote wenyewe — kila namba inatoka kwenu viongozi.")}
          </p>
        )}
        {!valid && (start || monthly !== null) && (
          <p className="mt-3 text-xs text-ink-600">{t("Bado:")}{' '}{missing.join(', ')}.</p>
        )}
        {!isEventGroup && valid && dueDay !== null && grace !== null && (
          <div className="mt-5 rounded-xl bg-ink-50 px-4 py-3 text-sm text-ink-800">
            <b>{t("Mfano:")}</b>{' '}{t("Mchango wa")}{' '}{monthName(exampleMonth, sy || undefined)} ({(monthly ?? 0).toLocaleString('en-US')}{t(") unatakiwa kufika kabla ya")}{' '}<b>{t("tarehe")}{' '}{dueDay + grace} {monthName(exampleMonth === 12 ? 1 : exampleMonth + 1)}{' '}{t("saa 23:59")}</b>{t(". Usipofika — deni")}{' '}
            {(monthly ?? 0).toLocaleString('en-US')}{' '}{t("+ faini")}{' '}{effectiveFine.toLocaleString('en-US')}{t(". Malipo yanalipa deni la zamani kwanza.")}
          </div>
        )}
        {/* SASISHO21: kanuni ya seva ni ya mchango wa MWEZI — kikundi cha matukio haioni. */}
        {!isEventGroup && settings?.kanuni && <p className="mt-2 text-xs text-ink-600">{t("Seva:")}{' '}{deadlineRuleText(settings)}</p>}

        {settings && (
          <p className="mt-4 text-xs text-ink-600">
            {locked
              ? t("Kuna pendekezo linalosubiri juu — liamuliwe (au lighairiwe) kabla ya kupendekeza jingine.")
              : t("Mabadiliko yatakuwa PENDEKEZO: yanaanza kutumika kiongozi wa pili akiidhinisha (yanabadilisha deni la kila mwanachama).")}
          </p>
        )}
        {/* SASISHO21: kitufe cha kuhifadhi kinabaki mbele ya macho wakati wa kusogeza — fomu ni ndefu. */}
        <div className="sticky bottom-0 z-10 -mx-4 -mb-4 mt-6 border-t border-ink-100 bg-paper-raised/95 px-4 py-3 backdrop-blur sm:-mx-5 sm:-mb-5 sm:px-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="min-w-0 text-xs text-ink-600">
              {valid ? (
                t("Kila kitu kiko tayari — bonyeza kitufe upande wa kulia.")
              ) : (
                <>
                  {t("Bado:") + ' ' + missing.slice(0, 2).join(', ') + (missing.length > 2 ? '…' : '')}{' '}
                  <button type="button" onClick={jumpToMissing} className="font-semibold text-gold-700 underline underline-offset-2">
                    {t("Nionyeshe")}
                  </button>
                </>
              )}
            </p>
            <Button icon={Save} onClick={save} disabled={locked} loading={busy}>
              {settings ? t("Pendekeza mabadiliko") : t("Hifadhi kanuni")}
            </Button>
          </div>
        </div>
    </Card>
  );
}

/**
 * Kifuniko cha zana za maendeleo: eneo la hatari (kufuta data ya majaribio) halionekani
 * kwa kiongozi wa kawaida — lazima afungue kwa mkono.
 */
function DevTools({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  return (
    <section className="rounded-2xl border border-ink-100 bg-paper-raised">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        className="flex w-full items-center justify-between gap-2 px-4 py-3 text-left text-xs text-ink-600 transition-colors hover:text-ink-900"
      >
        <span>{t("Zana za maendeleo (kufuta data ya majaribio)")}</span>
        <span className="font-mono text-sm">{open ? '−' : '+'}</span>
      </button>
      {open && <div className="border-t border-ink-100 p-4">{children}</div>}
    </section>
  );
}

/**
 * SASISHO17 (P0-6): kufuta data ya fedha = kiongozi A anaomba (baada ya kuona kitakachofutwa) → kiongozi B anaidhinisha
 * ndani ya muda wa ombi. Server pia lazima iruhusu (msimamizi wa server) — vinginevyo ombi linakataliwa na kurekodiwa.
 */
function DangerZone() {
  const { groupId, groupName, refresh } = useTruth();
  const toast = useToast();
  const [dry, setDry] = useState<ResetReport | null>(null);
  const [keepLoans, setKeepLoans] = useState(false);
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const [requests, setRequests] = useState<ResetRequestItem[]>([]);

  const [reload, setReload] = useState(0);

  useEffect(() => {
    let cancelled = false;
    listResetRequests(groupId)
      .then((l) => !cancelled && setRequests(l))
      .catch(() => !cancelled && setRequests([]));
    return () => {
      cancelled = true;
    };
  }, [groupId, reload]);

  const pending = requests.find((r) => r.status === 'Pending');

  async function check() {
    setBusy(true);
    try {
      setDry(await previewReset(groupId, keepLoans));
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  async function run(action: () => Promise<unknown>, ok: string) {
    setBusy(true);
    try {
      await action();
      toast(ok, 'good');
      setConfirm('');
      setDry(null);
      setReload((v) => v + 1);
      refresh();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    } finally {
      setBusy(false);
    }
  }

  const total = dry ? Object.values(dry.rows).reduce((s, n) => s + n, 0) : 0;
  const confirmOk = confirm.trim() === groupName.trim();
  const confirmField = (
    <Field label={<>{t("Andika jina kamili la kikundi kuthibitisha:")}{' '}<b>{groupName}</b></>}>
      <Input value={confirm} onChange={(e) => setConfirm(e.target.value)} placeholder={groupName} />
    </Field>
  );

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
      <p className="mt-2 text-xs text-ink-600">{t("Usalama: kiongozi mmoja anaomba, kiongozi MWINGINE anaidhinisha. Wanachama wote wanaarifiwa, na kumbukumbu za ukaguzi hazifutwi.")}</p>

      {pending ? (
        <div className="mt-4 space-y-3">
          <Notice tone="overdue">
            {t("Kuna ombi la kufuta data linalosubiri (limeombwa {0}, linaisha {1}).", { 0: realDateTime(pending.requestedAt), 1: realDateTime(pending.expiresAt) })}{' '}
            {pending.keepLoans ? t("Mikopo haitafutwa.") : ''}
          </Notice>
          {pending.canApprove && confirmField}
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" loading={busy} onClick={() => run(() => rejectReset(groupId, pending.id), pending.mine ? t("Ombi limeghairiwa.") : t("Ombi limekataliwa."))}>
              {pending.mine ? t("Ghairi ombi") : t("Kataa ombi")}
            </Button>
            {pending.canApprove && (
              <Button variant="danger" icon={Trash2} disabled={!confirmOk} loading={busy}
                onClick={() => run(() => approveReset(groupId, pending.id, confirm), t("Data za fedha za kikundi zimefutwa. Ingiza PDF upya."))}>
                {t("Idhinisha kufuta")}
              </Button>
            )}
          </div>
          {pending.mine && <p className="text-xs text-ink-600">{t("Uliomba wewe — kiongozi mwingine (Mwenyekiti au Mtunza-hazina) lazima aidhinishe.")}</p>}
        </div>
      ) : (
        <>
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
                  {confirmField}
                  <Button variant="danger" icon={Trash2} disabled={!confirmOk} loading={busy}
                    onClick={() => run(() => requestReset(groupId, keepLoans, confirm), t("Ombi limetumwa. Kiongozi mwingine lazima aliidhinishe."))}>
                    {t("Omba kufuta rekodi")}{' '}{total.toLocaleString('en-US')}
                  </Button>
                </>
              )}
            </div>
          )}
        </>
      )}
    </Card>
  );
}

// ---------------------------------------------------------------- Mapendekezo (maker-checker)
const FIELD_LABELS: Record<string, string> = {
  contributionStartDate: tk('Mwezi wa kwanza wa michango'),
  dueDateDay: tk('Tarehe ya mwisho'),
  gracePeriodDays: tk('Siku za neema'),
  fineOnPartialPayment: tk('Faini kwa malipo pungufu'),
  autoDeductSavings: tk('Akiba ilipe mwezi usiolipwa'),
  manualEntryCutoff: tk('Mwisho wa kumbukumbu za mkononi'),
  foundingMembersCutoff: tk('Waanzilishi: waliojiunga hadi'),
  monthlyContribution: tk('Mchango wa mwezi'),
  lateFine: tk('Faini ya kuchelewa'),
  lateFineMode: tk('Aina ya faini'),
  lateFinePercent: tk('Asilimia ya faini'),
  joiningFee: tk('Kianzio'),
  roleChangeApprovals: tk('Kura za kubadilisha cheo'),
  leaderCoolingOffDays: tk('Siku za kusubiri za kiongozi mpya'),
  cashStatementMaxAgeDays: tk('Umri wa juu wa PDF ya M-Koba (siku)'),
  loanLatePenaltyAmount: tk('Adhabu ya rejesho la mkopo'),
  loansEnabled: tk('Mikopo imefunguliwa?'),
  loanInterestPercent: tk('Riba ya mkopo (%)'),
  loanRepaymentMonths: tk('Miezi ya kurejesha'),
  loanMaxAmount: tk('Kikomo cha mkopo mmoja'),
  loanMaxMultipleOfContributions: tk('Kikomo kwa michango (mara ngapi)'),
  loanMinMembershipMonths: tk('Miezi ya uanachama kabla ya kukopa'),
  loanAllowWithArrears: tk('Anaweza kukopa akiwa na deni la michango?'),
  loanAllowMultipleActive: tk('Zaidi ya mkopo mmoja kwa wakati mmoja?'),
  loanMinCashReserve: tk('Akiba ya lazima kwenye M-Koba'),
  loanRequiredApprovals: tk('Kura za viongozi zinazohitajika'),
  loanApprovalValidDays: tk('Idhini inadumu (siku)'),
};

const CLEAR_ON_ZERO = new Set(['loanMaxAmount', 'loanMaxMultipleOfContributions']);

function fmt(v: unknown): string {
  if (v === null || v === undefined || v === '') return '—';
  if (typeof v === 'boolean') return v ? t("Ndiyo") : t("Hapana");
  if (typeof v === 'number') return num(v);
  if (typeof v === 'string' && /^\d{4}-\d{2}-\d{2}/.test(v)) return wallDate(v);
  if (v === 'Fixed') return t("Kiasi kamili");
  if (v === 'Percent') return t("Asilimia ya mchango");
  return String(v);
}

/** Mabadiliko halisi tu (sehemu zisizobadilika hazionyeshwi). */
function diff(p: SettingsProposal, s: TruthSettings | null) {
  const cur = (s ?? {}) as unknown as Record<string, unknown>;
  return Object.entries(p.changes)
    .filter(([k, v]) => v !== null && v !== undefined && k in FIELD_LABELS)
    // Kikomo cha mkopo = 0 maana yake "ondoa kikomo" (sawa na tupu)
    .map(([k, v]) => ({ k, label: t(FIELD_LABELS[k]), from: cur[k], to: CLEAR_ON_ZERO.has(k) && v === 0 ? '' : v }))
    .filter((x) => fmt(x.from) !== fmt(x.to));
}

function PendingProposal({ p, settings, onChanged }: { p: SettingsProposal; settings: TruthSettings | null; onChanged: () => void }) {
  const { groupId, refresh } = useTruth();
  const toast = useToast();
  const [decision, setDecision] = useState<'approve' | 'reject' | null>(null);
  const [busy, setBusy] = useState(false);
  const rows = diff(p, settings);

  async function decide(approve: boolean, note?: string) {
    try {
      await decideSettingsProposal(groupId, p.id, approve, note);
      invalidateLoansVisibility(groupId); // SASISHO16: menyu ya Mikopo isasishwe
      toast(approve ? t("Kanuni mpya zimeanza kutumika — taarifa zote zimehesabiwa upya.") : p.canCancel ? t("Pendekezo limeghairiwa.") : t("Pendekezo limekataliwa."), 'good');
      setDecision(null);
      onChanged();
      if (approve) refresh();
    } catch (e) {
      toast(apiErrorMessage(e), 'overdue');
    }
  }

  return (
    <Card
      title={t("Pendekezo la kubadilisha kanuni")}
      subtitle={t("Limeombwa na {0}, {1}", { 0: p.requestedBy ?? '—', 1: realDateTime(p.requestedAt) })}
      action={<StatusBadge label={t("Linasubiri idhini")} tone="warn" />}
      className="border-standing-warn/40"
    >
      {rows.length === 0 ? (
        <p className="text-sm text-ink-600">{t("Hakuna tofauti na kanuni za sasa.")}</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-[11px] uppercase tracking-wide text-ink-600">
              <tr>
                <th className="py-1.5 pr-3">{t("Kanuni")}</th>
                <th className="py-1.5 pr-3">{t("Sasa")}</th>
                <th className="py-1.5">{t("Inapendekezwa")}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-100">
              {rows.map((r) => (
                <tr key={r.k}>
                  <td className="py-1.5 pr-3 text-ink-800">{r.label}</td>
                  <td className="figure py-1.5 pr-3 text-ink-600">{fmt(r.from)}</td>
                  <td className="figure py-1.5 font-semibold text-standing-warn">{fmt(r.to)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {p.note && <p className="mt-2 text-sm text-ink-700">“{p.note}”</p>}
      <div className="mt-4 flex flex-wrap gap-2">
        {p.canDecide && (
          <>
            <Button variant="good" onClick={() => setDecision('approve')}>
              {t("Idhinisha")}
            </Button>
            <Button variant="danger" onClick={() => setDecision('reject')}>
              {t("Kataa")}
            </Button>
          </>
        )}
        {p.canCancel && (
          <Button
            variant="secondary"
            loading={busy}
            onClick={async () => {
              setBusy(true);
              await decide(false);
              setBusy(false);
            }}
          >
            {t("Ghairi pendekezo langu")}
          </Button>
        )}
        {!p.canDecide && !p.canCancel && <p className="text-xs text-ink-600">{t("Linasubiri kiongozi mwingine (si aliyependekeza).")}</p>}
      </div>
      <ReasonModal
        open={!!decision}
        title={decision === 'approve' ? t("Idhinisha kanuni mpya") : t("Kataa pendekezo")}
        summary={decision === 'approve' ? t("Deni la kila mwanachama litahesabiwa upya kwa kanuni hizi, kwa historia yote.") : undefined}
        requireReason={decision === 'reject'}
        variant={decision === 'approve' ? 'good' : 'danger'}
        confirmLabel={decision === 'approve' ? t("Idhinisha") : t("Kataa")}
        onClose={() => setDecision(null)}
        onConfirm={(note) => decide(decision === 'approve', note || undefined)}
      />
    </Card>
  );
}

function ProposalHistory({ list }: { list: SettingsProposal[] }) {
  return (
    <details className="rounded-2xl border border-ink-100 bg-paper-raised px-4 py-3 text-sm">
      <summary className="cursor-pointer font-medium text-ink-800">{t("Historia ya mabadiliko ya kanuni ({0})", { 0: list.length })}</summary>
      <ul className="mt-3 space-y-1.5 text-xs text-ink-700">
        {list.map((p) => (
          <li key={p.id}>
            <b>{p.status === 'Approved' ? t("Iliidhinishwa") : t("Ilikataliwa/ilighairiwa")}</b> {realDateTime(p.decidedAt)} ·{' '}
            {t("ilipendekezwa na {0}", { 0: p.requestedBy ?? '—' })}
            {p.decidedBy ? `, ${t("uamuzi: {0}", { 0: p.decidedBy })}` : ''}
            {p.decisionNote ? ` — ${p.decisionNote}` : ''}
          </li>
        ))}
      </ul>
    </details>
  );
}

/** S17: sentensi ya tarehe ya mwisho inajengwa kutoka sehemu za kanuni (si maandishi ghafi ya seva) ili itafsirike. */
function deadlineRuleText(s: { dueDateDay: number; gracePeriodDays: number; kanuni: string }): string {
  const time = /(\d{2}:\d{2}:\d{2})/.exec(s.kanuni)?.[1] ?? '23:59:59';
  return s.gracePeriodDays > 0
    ? t("Mchango wa mwezi M unatakiwa kufika tarehe {0} ya mwezi M+1 saa {1} + siku {2} za neema.", { 0: s.dueDateDay, 1: time, 2: s.gracePeriodDays })
    : t("Mchango wa mwezi M unatakiwa kufika tarehe {0} ya mwezi M+1 saa {1} (hakuna siku za neema — faini kuanzia tarehe {2}).", { 0: s.dueDateDay, 1: time, 2: s.dueDateDay + 1 });
}
