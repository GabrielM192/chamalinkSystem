import { isAxiosError } from 'axios';
import { apiClient } from './client';
import { t, tk } from '../i18n';
import { problemText } from '../lib/loc';

// =====================================================================
// Injini ya Ukweli (Truth Engine) — /api/truth
//
// Taarifa hazihifadhiwi: kila ombi linacheza upya data GHAFI (M-Koba +
// kumbukumbu za mkononi zilizoidhinishwa) kwa kanuni za kikundi. Kwa hiyo
// frontend haihesabu chochote cha fedha yenyewe — inaonyesha tu kile
// seva inachorudisha. Hii inazuia "ripoti mbili zinazopingana".
// =====================================================================

// ---------- Aina za data (zinalingana na TruthController) ----------

export type PeriodStatus =
  | 'NotYetOpen'
  | 'Open'
  | 'PaidOnTime'
  | 'PaidLate'
  | 'PartiallyPaid'
  | 'Unpaid';

export interface MonthLine {
  month: string; // "Jan 2026"
  deadline: string; // saa ya EAT (wall-clock)
  status: PeriodStatus;
  contributionDue: number;
  contributionPaid: number;
  fineDue: number;
  finePaid: number;
  fineWaived: boolean;
  extraGraceDays: number;
  loanDue: number;
  loanPaid: number;
  coveredBySavings: number;
  outstanding: number;
  excelCell: string;
  paidBy: string[];
}

export interface AllocationLine {
  reference: string;
  timestamp: string;
  target: string; // Contribution | Fine | JoinFee | Savings | LoanRepayment | LoanPenalty | FineWaived
  period: string | null;
  amount: number;
  explanation: string;
}

export interface MemberTotals {
  acceptedFromMkoba: number;
  acceptedManual: number;
  contributionPaid: number;
  fineCharged: number;
  finePaid: number;
  /** Idadi ya faini zilizosamehewa */
  finesWaived: number;
  /** Kiasi cha faini zilizosamehewa (SASISHO15) */
  finesWaivedAmount?: number;
  joinFeeDue: number;
  joinFeePaid: number;
  joinFeeOutstanding: number;
  loanRepaid: number;
  /** Adhabu za mkopo zilizolipwa */
  loanPenaltyPaid: number;
  /** Makato ya mafao (si pesa taslimu). SASISHO15: HAYAMO ndani ya acceptedManual tena. */
  welfareOffset: number;
  /** = acceptedFromMkoba + acceptedManual + welfareOffset */
  totalAccepted: number;
  loanOutstanding: number;
  loanOverdue: number;
  savings: number;
  arrears: number;
  currentDue: number;
  totalDebtIncludingJoinFee: number;
  monthsInArrears: string[];
}

export interface MemberTruth {
  memberId: string;
  memberNumber: string;
  fullName: string;
  phones: string[];
  asOf: string;
  invariantOk: boolean;
  totals: MemberTotals;
  monthlyStatement: MonthLine[];
  loans: {
    loanId: string;
    disbursedAt: string;
    totalRepayable: number;
    repaid: number;
    outstanding: number;
    penaltyCharged: number;
    penaltyPaid: number;
    overdueInstallments: number;
  }[];
  allocations: AllocationLine[] | null;
  /** Viongozi tu (seva inaacha tupu kwa mwanachama wa kawaida). */
  rejected: { reference: string; timestamp: string; amount: number; reason: string }[];
  /** SASISHO16: taarifa zenye msimbo, zinazotafsirika. Seva inachuja kwa anayeangalia (dataQuality = viongozi). */
  notices?: TruthNotice[];
  /** Urithi: maandishi ya Kiswahili ya notices. */
  warnings: string[];
}

export interface TruthNotice {
  code: string;
  kind: 'notice' | 'dataQuality';
  message: string;
  tpl: string;
  args: string[];
}

export interface GroupTruth {
  groupId: string;
  asOf: string | null;
  members: MemberTruth[];
  totals: {
    acceptedFromMkoba: number;
    acceptedManual: number;
    contributionPaid: number;
    finePaid: number;
    joinFeePaid: number;
    joinFeeOutstanding: number;
    loanRepaid: number;
    loanOutstanding: number;
    savings: number;
    arrears: number;
    unattributedMkobaMoney: number;
    /** Mapengo ya mnyororo wa balance ya PDF ambayo bado hayajathibitishwa */
    statementGapCount: number;
    statementGapAmount: number;
    statementGapOutflow: number;
  };
}

export interface MemberLite {
  memberId: string;
  fullName: string;
  memberNumber: string;
  role: string;
  phones: string[];
}

export interface MoneySummary {
  joinFeePaid: number;
  joinFeeOutstanding: number;
  arrears: number;
  savings: number;
  totalDebtIncludingJoinFee: number;
  loanOutstanding: number;
  monthsInArrears: string[];
}

export interface PaymentPreview {
  valid: boolean;
  problems: string[];
  before: MoneySummary;
  after: MoneySummary;
  changedMonths: {
    month: string;
    statusBefore: PeriodStatus | '-';
    statusAfter: PeriodStatus;
    outstandingBefore: number;
    outstandingAfter: number;
  }[];
  pendingRequestsForMember: number;
  newAllocations: { target: string; period: string | null; amount: number; explanation: string }[];
}

export type RequestStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';

export interface ManualPaymentItem {
  id: string;
  memberId: string | null;
  memberName: string;
  kind: 'JoinFee' | 'Payment';
  amount: number;
  occurredAt: string; // wall-clock EAT (imehifadhiwa na "Z" lakini si UTC halisi)
  createdAt: string; // UTC halisi
  status: RequestStatus;
  reason: string | null;
  evidenceType: EvidenceType | null; // null = rekodi ya kabla ushahidi haujawa lazima
  evidenceReference: string | null;
  createdByName: string;
  createdByMe: boolean;
  approvedByName: string | null;
  approvedAt: string | null;
  approvalNote: string | null;
  canDecide: boolean;
}

export type AdjustmentKind = 'FineWaiver' | 'GraceExtension';

export interface AdjustmentItem {
  id: string;
  groupMemberId: string | null;
  memberName: string;
  kind: AdjustmentKind;
  year: number;
  month: number;
  extraDays: number;
  reason: string;
  status: 'Pending' | 'Approved' | 'Rejected';
  requestedAt: string;
  requestedByName: string;
  decidedByName: string | null;
  decidedAt: string | null;
  canDecide: boolean;
}

/** Muamala wa mpakani wa pengo (sahihi, umeshahesabiwa) — pengo liko kabla/baada yake. */
export interface GapNeighbor {
  reference: string;
  timestamp: string; // wall-clock EAT
  payerName: string;
  phone: string;
  amount: number;
  isWithdrawal: boolean;
  balance: number | null;
  memberId: string | null;
  memberName: string | null;
}

/** Maelezo ya pengo yanayoshirikiwa na import + tabo ya Mapengo (yanatoka StatementGapDescriber). */
export interface GapContext {
  previous: GapNeighbor | null;
  next: GapNeighbor | null;
  balanceAfterPrevious: number | null;
  expectedBalanceBeforeNext: number | null;
  title: string;
}

export interface GapItem extends GapContext {
  message: string;
  possibleMemberIds: string[];
  id: string;
  afterReference: string | null;
  beforeReference: string;
  windowStart: string; // wall-clock EAT
  windowEnd: string;
  amount: number;
  status: 'Unresolved' | 'Pending' | 'Approved' | 'Rejected';
  assignedMemberId: string | null;
  assignedMemberName: string | null;
  evidence: string | null;
  requestedByName: string | null;
  approvedByName: string | null;
  canDecide: boolean;
}

export interface PhoneOverviewItem {
  phone: string;
  mkobaName: string | null;
  deposits: number;
  total: number;
  phoneId: string | null;
  memberId: string | null;
  memberName: string | null;
  state: 'Unmapped' | 'NotSeen' | 'Linked' | 'Pending' | 'Ended';
  hali: string;
  validFrom: string | null;
  validTo: string | null;
  requestedByName: string | null;
  /** Kiongozi wa pili (si aliyeomba, si mwenye namba) anaweza kuidhinisha/kukataa */
  canApprove: boolean;
  /** Aliyeomba anaweza kughairi ombi lake linalosubiri */
  canCancel: boolean;
  pendingEnd: { validTo: string; reason: string | null; requestedByName: string | null; canApprove: boolean } | null;
  history: { id: string; memberName: string | null; status: string; validFrom: string | null; validTo: string | null }[];
}

export interface AddPhoneResult {
  phoneId: string;
  phone: string;
  status: string;
  matchedInMkoba: boolean;
  mkobaNameSeen: string | null;
  depositsFound: number;
  depositsTotal: number;
  ujumbe: string;
}

export interface TruthSettings {
  monthlyContribution: number;
  lateFine: number;
  /** Asilimia ya mchango (mf. 50) — ikiwa null, faini ni kiasi kamili (lateFine) */
  lateFinePercent: number | null;
  lateFineMode: 'Fixed' | 'Percent';
  joiningFee: number;
  dueDateDay: number;
  gracePeriodDays: number;
  fineOnPartialPayment: boolean;
  autoDeductSavings: boolean;
  contributionStartDate: string;
  manualEntryCutoff: string | null;
  foundingMembersCutoff: string | null;
  loanLatePenaltyAmount: number;
  // SASISHO15 (AWAMU L): sheria za mikopo — null = haijawekwa
  loansEnabled: boolean;
  loanInterestPercent: number | null;
  loanRepaymentMonths: number | null;
  loanMaxAmount: number | null;
  loanMaxMultipleOfContributions: number | null;
  loanMinMembershipMonths: number | null;
  loanAllowWithArrears: boolean;
  loanAllowMultipleActive: boolean;
  loanMinCashReserve: number | null;
  loanRequiredApprovals: number | null;
  loanApprovalValidDays: number | null;
  // SASISHO17: utawala na usalama — null = chaguo-msingi la seva (kura 1, siku 0; umri wa PDF = haijawekwa => fedha zinazuiwa)
  roleChangeApprovals: number | null;
  leaderCoolingOffDays: number | null;
  cashStatementMaxAgeDays: number | null;
  // SASISHO18 — sera ya vikundi vinavyochangishana KILA TUKIO (pochi + floor)
  /** Kiwango cha chini (floor) cha salio la mwanachama. 0 = hakuna. */
  memberMinimumBalance: number;
  /** Siku za kujaza salio baada ya tukio. 0 = hakuna muda maalum. */
  eventTopUpWindowDays: number;
  /** Faini ya deni la tukio linapochelewa. 0 = hakuna faini. */
  overdueEventFineAmount: number;
  /** Kikomo cha makato ya matukio kwa mwanachama kwa mwezi. 0 = hakuna kikomo. */
  maxEventDeductionPerMemberPerMonth: number;
  /** Idadi ya juu ya matukio yanayoidhinishwa kwa mwezi. 0 = hakuna kikomo. */
  maxEventsPerMonth: number;
  /** true = mnufaika naye anachangia tukio lake mwenyewe. */
  beneficiaryContributes: boolean;
  // SASISHO19 — kanuni za J3: faini -> neema -> hatari ya kuondolewa -> kurudi
  /** Siku za neema BAADA ya faini kabla ya kuingia orodha ya "hatarini kuondolewa". */
  eventFineGraceDays: number;
  /** Idadi ya viongozi wanaoidhinisha kuondolewa (kamwe chini ya 3). */
  removalApprovalsRequired: number;
  /** Kizidishi cha faini kwa anayerudi (mf. 2 = faini x 2). 0 = hakuna. */
  reinstatementFineMultiplier: number;
  /** true = anayerudi analipa tena ada ya kujiunga. */
  reinstatementRequiresJoiningFee: boolean;
  // SASISHO21 — lango la kujiunga, mikono miwili, madai, SMS
  /** true = usajili unakamilika tu baada ya ada ya kujiunga kuthibitishwa. */
  joiningPaymentRequired: boolean;
  /** Sehemu ya ada ya kujiunga inayoingia akiba ya mwanachama. */
  joiningSavingsPortion: number;
  /** Siku alizonazo muombaji kumaliza malipo ya kujiunga. */
  joiningPaymentWindowDays: number;
  /** Malipo ya mkono yanayofikia kiasi hiki yanahitaji kiongozi wa pili. 0 = hakuna. */
  paymentSecondApprovalAmount: number;
  /** true = wanachama wanaweza kudai "nimelipa". */
  claimsEnabled: boolean;
  /** true = SMS za no-reply zimewashwa. */
  smsEnabled: boolean;
  /** Onyo la salio la credits. */
  smsLowCreditThreshold: number;
  /** Saa ya kuanza utulivu (0–23). */
  smsQuietFromHour: number;
  /** Saa ya kuisha utulivu (1–24; 24 = hakuna kizuizi). */
  smsQuietToHour: number;
  /** SMS kwa mwanachama kwa tukio (kinga ya ujumbe mwingi). */
  smsMaxPerMemberPerEvent: number;
  /** Herufi kwa SMS moja (0 = 160). */
  smsMaxCharsPerMessage: number;
  // SASISHO21 — rufaa
  /** true = wanachama wanaweza kuwasilisha rufaa. */
  appealsEnabled: boolean;
  /** Siku za kuwasilisha rufaa tangu uamuzi (0 = hakuna kikomo). */
  appealWindowDays: number;
  /** Viongozi wanaohitajika kuamua rufaa (kamwe chini ya 2). */
  appealApprovalsRequired: number;
  kanuni: string;
}

export type TruthSettingsUpdate = Partial<{
  roleChangeApprovals: number;
  leaderCoolingOffDays: number;
  cashStatementMaxAgeDays: number;
  contributionStartDate: string;
  dueDateDay: number;
  gracePeriodDays: number;
  fineOnPartialPayment: boolean;
  autoDeductSavings: boolean;
  manualEntryCutoff: string;
  foundingMembersCutoff: string;
  monthlyContribution: number;
  lateFine: number;
  lateFineMode: 'Fixed' | 'Percent';
  lateFinePercent: number;
  joiningFee: number;
  loanLatePenaltyAmount: number;
  /** Mikopo: sehemu isiyotumwa = haibadiliki. Kikomo (maxAmount / maxMultiple) = 0 => ondoa kikomo hicho. */
  loansEnabled: boolean;
  loanInterestPercent: number;
  loanRepaymentMonths: number;
  loanMaxAmount: number;
  loanMaxMultipleOfContributions: number;
  loanMinMembershipMonths: number;
  loanAllowWithArrears: boolean;
  loanAllowMultipleActive: boolean;
  loanMinCashReserve: number;
  loanRequiredApprovals: number;
  loanApprovalValidDays: number;
  // SASISHO18 — matukio (pochi + floor)
  memberMinimumBalance: number;
  eventTopUpWindowDays: number;
  overdueEventFineAmount: number;
  maxEventDeductionPerMemberPerMonth: number;
  maxEventsPerMonth: number;
  beneficiaryContributes: boolean;
  // SASISHO19 — hatarini kuondolewa + kurudi
  eventFineGraceDays: number;
  removalApprovalsRequired: number;
  reinstatementFineMultiplier: number;
  reinstatementRequiresJoiningFee: boolean;
  // SASISHO21
  joiningPaymentRequired: boolean;
  joiningSavingsPortion: number;
  joiningPaymentWindowDays: number;
  paymentSecondApprovalAmount: number;
  claimsEnabled: boolean;
  smsEnabled: boolean;
  smsLowCreditThreshold: number;
  smsQuietFromHour: number;
  smsQuietToHour: number;
  smsMaxPerMemberPerEvent: number;
  smsMaxCharsPerMessage: number;
  appealsEnabled: boolean;
  appealWindowDays: number;
  appealApprovalsRequired: number;
}>;

export interface SaveSettingsResult {
  /** true = imeanza kutumika sasa (mipangilio ya kwanza); false = pendekezo linasubiri kiongozi wa pili */
  applied: boolean;
  proposalId: string | null;
  ujumbe: string;
}

export interface SettingsProposal {
  id: string;
  status: 'Pending' | 'Approved' | 'Rejected';
  changes: Record<string, unknown>;
  note: string | null;
  requestedBy: string | null;
  requestedAt: string;
  decidedBy: string | null;
  decidedAt: string | null;
  decisionNote: string | null;
  canDecide: boolean;
  canCancel: boolean;
}

export interface ImportReport {
  fileHash: string;
  rowsInPdf: number;
  inserted: number;
  alreadyExisted: number;
  unreadableRows: string[];
  gaps: (GapContext & {
    id: string;
    afterReference: string | null;
    beforeReference: string;
    windowStart: string;
    windowEnd: string;
    amount: number;
    maelezo: string;
    resolved: boolean;
  })[];
  depositsInPdf: number;
  withdrawalsInPdf: number;
  declaredContributionTotal: number | null;
  declaredGroupBalance: number | null;
  lastBalanceInPdf: number | null;
  unknownPhones: { phone: string; mkobaName: string | null }[];
  warnings: string[];
  /** SASISHO17: kiolezo + vigeu (tafsiri ya EN) — sambamba na warnings */
  warningsLoc?: { tpl: string; args: string[] }[];
}

export interface CashMonth {
  month: string; // "Jan 2026" (tayari kwa Kiswahili)
  year: number;
  monthNumber: number;
  deposits: number;
  depositCount: number;
  withdrawals: number;
  withdrawalCount: number;
  closingBalance: number | null;
}

export interface CashWithdrawal {
  reference: string;
  timestamp: string; // wall-clock EAT
  amount: number;
  phone: string;
  payerName: string;
  description: string;
  memberId: string | null;
  memberName: string | null;
  balanceAfter: number | null;
}

/** Daftari la fedha la M-Koba — data ghafi, halihusiani na mgawanyo wa michango. */
export interface Cashbook {
  firstTransaction: string | null;
  lastTransaction: string | null;
  openingBalance: number | null;
  deposits: number;
  withdrawals: number;
  lastBalance: number | null;
  /** Salio la M-Koba linathibitisha pesa hii iliingia lakini mstari wake haupo kwenye PDF. */
  missingFromStatement: number | null;
  months: CashMonth[];
  withdrawalList: CashWithdrawal[];
}

export interface ResetReport {
  dryRun: boolean;
  rows: Record<string, number>;
}


// ---------- Ujumbe wa kosa unaosomeka ----------

/**
 * Seva inarudisha makosa ya biashara kama { message } (GlobalExceptionMiddleware)
 * au { error } (baadhi ya BadRequest) au ProblemDetails { title, errors }.
 * Kiongozi aone sababu HALISI, si "Imeshindwa".
 */
export function apiErrorMessage(err: unknown, fallback = t('Kuna tatizo. Jaribu tena.')): string {
  if (isAxiosError(err)) {
    if (!err.response) return t('Hakuna mawasiliano na seva. Hakikisha API inaendeshwa.');
    const status = err.response.status;
    const d = err.response.data as
      | { message?: string; error?: string; title?: string; detail?: string; errors?: Record<string, string[]>; correlationId?: string }
      | string
      | undefined;
    if (status === 429) return t('Maombi mengi mno kwa muda mfupi. Subiri dakika moja kisha jaribu tena.');
    // SASISHO16: hitilafu za seva (5xx) HAZIONYESHI maandishi yoyote ya seva/proxy (yanaweza kuwa na maelezo ya
    // ndani au ukurasa wa HTML). Mtumiaji anapata ujumbe wa jumla + namba ya kumbukumbu ya kumpa msimamizi.
    if (status >= 500) {
      const ref = d && typeof d === 'object' && typeof d.correlationId === 'string' ? d.correlationId.slice(-12) : '';
      return ref
        ? t('Hitilafu upande wa seva. Jaribu tena baadaye. Kumbukumbu: {0}', { 0: ref })
        : t('Hitilafu upande wa seva. Jaribu tena baadaye.');
    }
    // Maandishi ghafi yanakubaliwa tu kama ni ujumbe mfupi (si HTML/stack trace)
    if (typeof d === 'string') {
      const s = d.trim();
      if (s && s.length <= 300 && !/[<>]/.test(s) && !/\bat\s+[\w.]+\(/.test(s)) return s;
    } else if (d && typeof d === 'object') {
      // SASISHO14: ujumbe wa seva unaotafsirika (template/args/details)
      const loc = problemText(d as Parameters<typeof problemText>[0]);
      if (loc && (d as { template?: string }).template) return loc;
      const msg = d.message ?? d.error ?? d.detail ?? d.title;
      if (msg) return d.message && d.detail && d.detail !== d.message ? `${d.message}: ${d.detail}` : msg;
      if (d.errors) {
        const first = Object.values(d.errors).flat()[0];
        if (first) return first;
      }
    }
    if (status === 401) return t('Kikao kimeisha. Tafadhali ingia tena.');
    if (status === 403) return t('Huna ruhusa ya kufanya hili (viongozi pekee).');
    if (status === 404) return t('Haikupatikana.');
  }
  return fallback;
}

// ---------- Kusoma ----------

const base = '/truth';

export async function getGroupTruth(groupId: string): Promise<GroupTruth> {
  return (await apiClient.get<GroupTruth>(`${base}/group/${groupId}`)).data;
}

export async function getMemberTruth(memberId: string): Promise<MemberTruth> {
  return (await apiClient.get<MemberTruth>(`${base}/member/${memberId}`)).data;
}

export async function getMyTruth(groupId: string): Promise<MemberTruth> {
  return (await apiClient.get<MemberTruth>(`${base}/${groupId}/me`)).data;
}

export async function getCashbook(groupId: string): Promise<Cashbook> {
  return (await apiClient.get<Cashbook>(`${base}/${groupId}/cashbook`)).data;
}

export async function getTruthMembers(groupId: string): Promise<MemberLite[]> {
  return (await apiClient.get<MemberLite[]>(`${base}/${groupId}/members`)).data;
}

export async function getTruthSettings(groupId: string): Promise<TruthSettings | null> {
  try {
    return (await apiClient.get<TruthSettings>(`${base}/${groupId}/settings`)).data;
  } catch (e) {
    if (isAxiosError(e) && e.response?.status === 404) return null;
    throw e;
  }
}


// ---------- Malipo ya mkononi (maker-checker) ----------

// Aina za ushahidi — lazima zilingane na enum EvidenceType ya backend (namba = thamani ya enum).
export type EvidenceType = 'Receipt' | 'CashBook' | 'MeetingMinutes' | 'LeaderStatement' | 'TransferSlip' | 'Other';

export const EVIDENCE_TYPES: { value: EvidenceType; code: number; label: string; example: string }[] = [
  { value: 'Receipt', code: 1, label: tk('Risiti'), example: tk('Risiti Na. 12') },
  { value: 'CashBook', code: 2, label: tk('Daftari la fedha'), example: tk('Daftari la mtunza-hazina uk. 4, mstari 7') },
  { value: 'MeetingMinutes', code: 3, label: tk('Kumbukumbu za kikao'), example: tk('Kikao cha 07/03/2026, kipengele 5') },
  { value: 'LeaderStatement', code: 4, label: tk('Tamko la kiongozi'), example: tk('Tamko la Mwenyekiti aliyepokea pesa, 15/03/2026') },
  { value: 'TransferSlip', code: 5, label: tk('Stakabadhi ya benki/simu'), example: tk('Muamala wa benki Na. FT26074XYZ') },
  { value: 'Other', code: 9, label: tk('Nyingine'), example: tk('Eleza ushahidi ulipo') },
];

/** Maandishi ya EVIDENCE_TYPES ni Kiswahili (chanzo); yatafsiriwe mahali yanapoonyeshwa kwa t(). */
export function evidenceLabel(type: EvidenceType | null | undefined): string {
  const label = EVIDENCE_TYPES.find((e) => e.value === type)?.label;
  return label ? t(label) : t('Haikuwekwa');
}

export interface ManualPaymentInput {
  memberId: string;
  amount: number;
  occurredAt: string; // "YYYY-MM-DDTHH:mm:ss" — saa za EAT, BILA "Z"
  isJoinFee: boolean;
  reason: string;
  evidenceType: EvidenceType;
  evidenceReference: string;
}

export async function previewPayment(
  groupId: string,
  input: Omit<ManualPaymentInput, 'reason' | 'evidenceType' | 'evidenceReference'>,
) {
  const { memberId, ...body } = input;
  return (
    await apiClient.post<PaymentPreview>(`${base}/${groupId}/member/${memberId}/preview-payment`, body)
  ).data;
}

export async function listManualPayments(groupId: string): Promise<ManualPaymentItem[]> {
  return (await apiClient.get<ManualPaymentItem[]>(`${base}/${groupId}/manual-payments`)).data;
}

export async function requestManualPayment(groupId: string, input: ManualPaymentInput) {
  const code = EVIDENCE_TYPES.find((e) => e.value === input.evidenceType)?.code;
  return (await apiClient.post(`${base}/${groupId}/manual-payments`, { ...input, evidenceType: code })).data;
}

export async function decideManualPayment(groupId: string, id: string, approve: boolean, note?: string) {
  return (await apiClient.post(`${base}/${groupId}/manual-payments/${id}/decision`, { approve, note })).data;
}

// ---------- Msamaha / Neema ----------

export async function listAdjustments(groupId: string): Promise<AdjustmentItem[]> {
  return (await apiClient.get<AdjustmentItem[]>(`${base}/${groupId}/adjustments`)).data;
}

export async function requestAdjustment(
  groupId: string,
  input: { memberId: string | null; kind: AdjustmentKind; year: number; month: number; extraDays: number; reason: string }
) {
  // Seva haina JsonStringEnumConverter — enum inatumwa kama namba.
  const kind = input.kind === 'FineWaiver' ? 1 : 2;
  return (await apiClient.post<AdjustmentItem>(`${base}/${groupId}/adjustments`, { ...input, kind })).data;
}

export async function decideAdjustment(groupId: string, id: string, approve: boolean, note?: string) {
  return (await apiClient.post<AdjustmentItem>(`${base}/${groupId}/adjustments/${id}/decision`, { approve, note })).data;
}

// ---------- Mapengo ya PDF ----------

export async function listGaps(groupId: string): Promise<GapItem[]> {
  return (await apiClient.get<GapItem[]>(`${base}/${groupId}/gaps`)).data;
}

export async function resolveGap(groupId: string, gapId: string, memberId: string, evidence: string) {
  return (await apiClient.post(`${base}/${groupId}/gaps/${gapId}/resolve`, { memberId, evidence })).data;
}

export async function decideGap(groupId: string, gapId: string, approve: boolean, note?: string) {
  return (await apiClient.post(`${base}/${groupId}/gaps/${gapId}/decision`, { approve, note })).data;
}

// ---------- Namba za simu ----------

export async function getPhones(groupId: string): Promise<PhoneOverviewItem[]> {
  return (await apiClient.get<PhoneOverviewItem[]>(`${base}/${groupId}/phones`)).data;
}

export async function addPhone(
  groupId: string,
  memberId: string,
  phone: string,
  note?: string,
  validFrom?: string | null,
  validTo?: string | null,
) {
  return (
    await apiClient.post<AddPhoneResult>(`${base}/${groupId}/phones`, {
      memberId,
      phone,
      note,
      validFrom: validFrom || null,
      validTo: validTo || null,
    })
  ).data;
}

/** Kiongozi wa pili anaidhinisha/kukataa kuunganisha namba (maker-checker). */
export async function decidePhone(groupId: string, phoneId: string, approve: boolean, note?: string) {
  return (await apiClient.post(`${base}/${groupId}/phones/${phoneId}/decide`, { approve, note })).data;
}

/** Ombi la kufunga namba kuanzia tarehe fulani (malipo ya zamani yanabaki kwa mwenyewe). */
export async function requestEndPhone(groupId: string, phoneId: string, validTo: string, reason: string) {
  return (await apiClient.post(`${base}/${groupId}/phones/${phoneId}/end`, { validTo, reason })).data;
}

export async function decideEndPhone(groupId: string, phoneId: string, approve: boolean, note?: string) {
  return (await apiClient.post(`${base}/${groupId}/phones/${phoneId}/end/decide`, { approve, note })).data;
}

/** Kughairi ombi LAKO linalosubiri (namba iliyoidhinishwa haifutwi — inafungwa kwa tarehe). */
export async function removePhone(groupId: string, phoneId: string) {
  await apiClient.delete(`${base}/${groupId}/phones/${phoneId}`);
}

// ---------- Mipangilio / Import / Reset ----------

export async function saveTruthSettings(groupId: string, dto: TruthSettingsUpdate): Promise<SaveSettingsResult> {
  return (await apiClient.put<SaveSettingsResult>(`${base}/${groupId}/settings`, dto)).data;
}

export async function getSettingsProposals(groupId: string): Promise<SettingsProposal[]> {
  return (await apiClient.get<SettingsProposal[]>(`${base}/${groupId}/settings/proposals`)).data;
}

/** Kiongozi wa pili: approve=true/false. Aliyependekeza: approve=false = kughairi. */
export async function decideSettingsProposal(groupId: string, proposalId: string, approve: boolean, note?: string) {
  return (await apiClient.post(`${base}/${groupId}/settings/proposals/${proposalId}/decide`, { approve, note })).data;
}

// ---------- SASISHO17 (P0-2): PDF ya M-Koba = rasimu → kiongozi wa PILI anaidhinisha ----------

export type StagedImportStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';

export interface StagedImportSummary {
  id: string;
  status: StagedImportStatus;
  fileName: string | null;
  uploadedAt: string;
  decidedAt: string | null;
  newRows: number;
  existingRows: number;
  backdatedRows: number;
  periodFrom: string | null;
  periodTo: string | null;
  lastBalance: number | null;
  mine: boolean;
  canApprove: boolean;
  canCancel: boolean;
}

export interface StagedRow {
  reference: string;
  timestamp: string;
  phone: string;
  payerName: string;
  amount: number;
  balance: number | null;
  isWithdrawal: boolean;
  description: string;
  rowOrder: number;
}

export interface StagedImportDetail extends StagedImportSummary {
  fileHash: string;
  fileSize: number;
  decisionNote: string | null;
  rowsInPdf: number;
  conflictingRows: number;
  report: {
    deposits: number;
    withdrawals: number;
    declaredContributionTotal: number | null;
    declaredGroupBalance: number | null;
    warnings: string[];
    /** SASISHO17: kiolezo + vigeu (tafsiri ya EN) — sambamba na warnings */
    warningsLoc?: { tpl: string; args: string[] }[];
    unreadableRows: string[];
    unknownPhones: { phone: string; mkobaName: string | null; rows: number; total: number }[];
    gaps: {
      newGaps: { afterReference: string | null; beforeReference: string; amount: number; windowStart: string; windowEnd: string }[];
      closedGaps: string[];
      totalGapsAfter: number;
    };
  };
  rows: StagedRow[];
}

/** Kupakia: inatengeneza RASIMU tu — hakuna kinachoingia kwenye taarifa mpaka kiongozi mwingine aidhinishe. */
export async function stageMkobaPdf(groupId: string, file: File): Promise<StagedImportDetail> {
  const form = new FormData();
  form.append('file', file);
  return (
    await apiClient.post<StagedImportDetail>(`${base}/${groupId}/import-mkoba`, form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
  ).data;
}

export async function listMkobaImports(groupId: string): Promise<StagedImportSummary[]> {
  return (await apiClient.get<StagedImportSummary[]>(`${base}/${groupId}/mkoba-imports`)).data;
}

export async function getMkobaImport(groupId: string, importId: string): Promise<StagedImportDetail> {
  return (await apiClient.get<StagedImportDetail>(`${base}/${groupId}/mkoba-imports/${importId}`)).data;
}

export async function downloadMkobaImport(groupId: string, importId: string): Promise<Blob> {
  return (await apiClient.get<Blob>(`${base}/${groupId}/mkoba-imports/${importId}/file`, { responseType: 'blob' })).data;
}

export async function approveMkobaImport(groupId: string, importId: string, note?: string): Promise<{ import: StagedImportDetail; report: ImportReport }> {
  return (await apiClient.post(`${base}/${groupId}/mkoba-imports/${importId}/approve`, { note: note || null })).data;
}

/** Aliyepakia = kughairi; kiongozi mwingine = kukataa (sababu inahitajika). */
export async function rejectMkobaImport(groupId: string, importId: string, note?: string) {
  await apiClient.post(`${base}/${groupId}/mkoba-imports/${importId}/reject`, { note: note || null });
}

// ---------- SASISHO17 (P0-6): kufuta data ya fedha = ombi + idhini ya kiongozi wa pili ----------

export async function previewReset(groupId: string, keepLoans: boolean) {
  return (await apiClient.post<ResetReport>(`${base}/${groupId}/reset`, null, { params: { dryRun: true, keepLoans } })).data;
}

export interface ResetRequestItem {
  id: string;
  keepLoans: boolean;
  status: 'Pending' | 'Approved' | 'Rejected' | 'Expired';
  requestedAt: string;
  expiresAt: string;
  decidedAt: string | null;
  mine: boolean;
  canApprove: boolean;
}

export async function listResetRequests(groupId: string): Promise<ResetRequestItem[]> {
  return (await apiClient.get<ResetRequestItem[]>(`${base}/${groupId}/reset/requests`)).data;
}

export async function requestReset(groupId: string, keepLoans: boolean, confirm: string) {
  return (await apiClient.post<{ id: string; status: string; expiresAt: string }>(`${base}/${groupId}/reset/requests`, { keepLoans, confirm })).data;
}

export async function approveReset(groupId: string, requestId: string, confirm: string) {
  return (await apiClient.post<ResetReport>(`${base}/${groupId}/reset/requests/${requestId}/approve`, { confirm })).data;
}

export async function rejectReset(groupId: string, requestId: string) {
  await apiClient.post(`${base}/${groupId}/reset/requests/${requestId}/reject`);
}

// ---------------------------------------------------------------- Taarifa ya mwezi (viongozi)
export interface MonthlyReportRow {
  memberId: string;
  memberNumber: string;
  fullName: string;
  phone: string | null;
  target: number;
  paid: number;
  paidAt: string | null;
  status: PeriodStatus;
  fineCharged: number;
  finePaid: number;
  fineWaived: boolean;
  outstanding: number;
  totalArrears: number;
  joinFeeOutstanding: number;
}

export interface MonthlyReport {
  groupId: string;
  groupName: string;
  year: number;
  month: number;
  monthLabel: string;
  deadline: string;
  monthClosed: boolean;
  dataUntil: string | null;
  dataComplete: boolean;
  totalMembers: number;
  paidOnTime: number;
  paidLate: number;
  partiallyPaid: number;
  notPaid: number;
  stillOpen: number;
  totalExpected: number;
  totalCollected: number;
  collectionRatePercent: number;
  finesCharged: number;
  finesCollected: number;
  outstandingForMonth: number;
  members: MonthlyReportRow[];
  whatsAppText: string;
}

export async function getMonthlyReport(groupId: string, year: number, month: number): Promise<MonthlyReport> {
  return (await apiClient.get<MonthlyReport>(`${base}/${groupId}/monthly-report`, { params: { year, month } })).data;
}
