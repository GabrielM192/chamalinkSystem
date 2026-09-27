import { isAxiosError } from 'axios';
import { apiClient } from './client';
import { t, tk } from '../i18n';

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
  finesWaived: number;
  joinFeeDue: number;
  joinFeePaid: number;
  joinFeeOutstanding: number;
  loanRepaid: number;
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
  rejected: { reference: string; timestamp: string; amount: number; reason: string }[];
  warnings: string[];
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
  state: 'Unmapped' | 'NotSeen' | 'Linked';
  hali: string;
}

export interface AddPhoneResult {
  phoneId: string;
  phone: string;
  matchedInMkoba: boolean;
  mkobaNameSeen: string | null;
  depositsFound: number;
  depositsTotal: number;
  ujumbe: string;
}

export interface TruthSettings {
  monthlyContribution: number;
  lateFine: number;
  joiningFee: number;
  dueDateDay: number;
  gracePeriodDays: number;
  fineOnPartialPayment: boolean;
  autoDeductSavings: boolean;
  contributionStartDate: string;
  manualEntryCutoff: string | null;
  foundingMembersCutoff: string | null;
  loanLatePenaltyAmount: number;
  kanuni: string;
}

export type TruthSettingsUpdate = Partial<{
  contributionStartDate: string;
  dueDateDay: number;
  gracePeriodDays: number;
  fineOnPartialPayment: boolean;
  autoDeductSavings: boolean;
  manualEntryCutoff: string;
  foundingMembersCutoff: string;
  monthlyContribution: number;
  lateFine: number;
  joiningFee: number;
  loanLatePenaltyAmount: number;
}>;

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

export interface WelfareDeduction {
  approved: number;
  joinFeeDeducted: number;
  arrearsDeducted: number;
  netPayout: number;
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
    const d = err.response.data as
      | { message?: string; error?: string; title?: string; detail?: string; errors?: Record<string, string[]> }
      | string
      | undefined;
    if (err.response.status === 429) return t('Maombi mengi mno kwa muda mfupi. Subiri dakika moja kisha jaribu tena.');
    if (typeof d === 'string' && d.trim()) return d;
    if (d && typeof d === 'object') {
      if (d.errors) {
        const first = Object.values(d.errors).flat()[0];
        if (first) return first;
      }
      const msg = d.message ?? d.error ?? d.detail ?? d.title;
      if (msg) return d.message && d.detail && d.detail !== d.message ? `${d.message}: ${d.detail}` : msg;
    }
    if (err.response.status === 403) return t('Huna ruhusa ya kufanya hili (viongozi pekee).');
    if (err.response.status === 404) return t('Haikupatikana.');
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

export async function getWelfareDeduction(memberId: string, amount: number, alsoDeductArrears: boolean) {
  return (
    await apiClient.get<WelfareDeduction>(`${base}/member/${memberId}/welfare-deduction`, {
      params: { amount, alsoDeductArrears },
    })
  ).data;
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

export async function addPhone(groupId: string, memberId: string, phone: string, note?: string) {
  return (await apiClient.post<AddPhoneResult>(`${base}/${groupId}/phones`, { memberId, phone, note })).data;
}

export async function removePhone(groupId: string, phoneId: string) {
  await apiClient.delete(`${base}/${groupId}/phones/${phoneId}`);
}

// ---------- Mipangilio / Import / Reset ----------

export async function saveTruthSettings(groupId: string, dto: TruthSettingsUpdate) {
  return (await apiClient.put(`${base}/${groupId}/settings`, dto)).data;
}

export async function importMkobaPdf(groupId: string, file: File): Promise<ImportReport> {
  const form = new FormData();
  form.append('file', file);
  return (
    await apiClient.post<ImportReport>(`${base}/${groupId}/import-mkoba`, form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
  ).data;
}

export async function resetGroupData(groupId: string, opts: { dryRun: boolean; confirm?: string; keepLoans?: boolean }) {
  return (
    await apiClient.post<ResetReport>(`${base}/${groupId}/reset`, null, {
      params: { dryRun: opts.dryRun, confirm: opts.confirm, keepLoans: opts.keepLoans ?? false },
    })
  ).data;
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
