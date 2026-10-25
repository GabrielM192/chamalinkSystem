import { apiClient } from './client';

// =====================================================================
// MAFAO (msiba / sherehe) + kura za hali ya mwanachama + arifa.
// Sheria zote (idadi ya idhini, viwango, nani anahesabiwa) zinatoka kwenye
// SERA ya kikundi iliyoidhinishwa na viongozi wawili — frontend haiweki namba yoyote.
// Enums zinatumwa kama NAMBA (API haitumii JsonStringEnumConverter);
// zinarudi kama maandishi (mf. "Msiba", "Voting").
// =====================================================================

// SASISHO19: "kufukuzwa kazi" ni aina yake ya mafao (J3: 10,000 kwa mwanachama)
export const CATEGORY = { Msiba: 1, Sherehe: 2, JobLoss: 3 } as const;
export type Category = keyof typeof CATEGORY;

// SASISHO19: mkwe/mkoi na mlezi (J3: 5,000 kwa mwanachama; mkwe mara moja tu)
export const RELATIONSHIP = { Self: 1, Spouse: 2, Child: 3, Parent: 4, InLaw: 5, Guardian: 6 } as const;
export type Relationship = keyof typeof RELATIONSHIP;

export const HEADCOUNT_RULE = { AllActiveMembers: 1, MembersWithNoArrears: 2, MembersPaidWithinDays: 3 } as const;
export type HeadcountRule = keyof typeof HEADCOUNT_RULE;

export const FUNDING_SOURCE = { GroupFund: 1, NewCollection: 2 } as const;
export type FundingSource = keyof typeof FUNDING_SOURCE;

export const EVIDENCE_TYPE = {
  DeathCertificate: 1,
  BurialPermit: 2,
  LocalGovernmentLetter: 3,
  MarriageCertificate: 4,
  BirthCertificate: 5,
  HospitalLetter: 6,
  Other: 7,
  PaymentReceipt: 8,
} as const;
export type EvidenceType = keyof typeof EVIDENCE_TYPE;

export const EXPLANATION_KIND = { LoanDisbursement: 1, GroupExpense: 2, BankTransfer: 3, Refund: 4, Other: 5 } as const;
export type ExplanationKind = keyof typeof EXPLANATION_KIND;

/** Hali zinazoweza kuombwa kwa kura (MemberStatus kwenye backend). */
export const MEMBER_STATUS = { Active: 2, Inactive: 5, Suspended: 6, Exited: 7, Deceased: 8 } as const;
export type SettableStatus = keyof typeof MEMBER_STATUS;

export type CaseStatus =
  | 'AwaitingEvidence'
  | 'AwaitingVerification'
  | 'Voting'
  | 'Approved'
  | 'Paid'
  | 'Rejected'
  | 'Cancelled'
  | 'HistoricalPendingConfirmation'
  | 'PaymentInitiated'
  | 'AwaitingFunds';

/** SASISHO14: ujumbe unaotafsirika kutoka seva — tpl = kiolezo cha Kiswahili ({0}…), args = vigeu. */
export interface LocMessage {
  message: string;
  tpl: string;
  args: string[];
}

export interface LocMsg {
  tpl: string;
  args: string[];
}

/** GroupRole yote (wapiga kura wanaweza kujumuisha Mwanachama). */
export const ANY_ROLE = { Chairperson: 1, Treasurer: 2, Secretary: 3, Member: 4 } as const;
export type AnyRole = keyof typeof ANY_ROLE;

export const HEADCOUNT_AS_OF = { EventDate: 1, ClaimDate: 2, VerificationDate: 3 } as const;
export type HeadcountAsOf = keyof typeof HEADCOUNT_AS_OF;

export const PAYMENT_CHANNEL = { Mkoba: 1, Cash: 2 } as const;
export type PaymentChannel = keyof typeof PAYMENT_CHANNEL;

/** GroupRole kwenye backend (inatumwa kama namba). */
export const GROUP_ROLE = { Chairperson: 1, Treasurer: 2, Secretary: 3 } as const;
export type LeaderRole = keyof typeof GROUP_ROLE;

export const RELATION_TYPE = { Spouse: 1, Parent: 2, Child: 3, Sibling: 4, Household: 5, BusinessPartner: 6, Other: 7 } as const;
export type RelationType = keyof typeof RELATION_TYPE;

export type ProposalStatus = 'Pending' | 'Approved' | 'Rejected';

// ---------------------------------------------------------------- Sera
export interface Governance {
  id: string;
  /** SASISHO15 (F-M3): tahadhari ya uwezo — viongozi/wahakiki wachache kuliko sera inavyohitaji */
  capacityWarnings?: LocMessage[];
  status: ProposalStatus;
  requiredApprovals: number;
  minLeaderApprovals: number;
  allowMemberApprovers: boolean;
  rejectionsToReject: number;
  votingDays: number;
  headcountRule: HeadcountRule;
  headcountPaidWithinDays: number | null;
  excludeBeneficiaryFromHeadcount: boolean;
  fundingSource: FundingSource;
  deductJoinFee: boolean;
  deductArrears: boolean;
  requireNoArrearsForEligibility: boolean;
  minMembershipDays: number;
  claimWindowDays: number;
  paymentConfirmationDays: number;
  statusChangeApprovals: number;
  expulsionVotePercent: number;
  note: string | null;
  requestedBy: string;
  requestedAt: string;
  decidedBy: string;
  decidedAt: string | null;
  canDecide: boolean;
  canCancel: boolean;
  description: string;
  // SASISHO13
  version: number;
  requireJoinFeePaid: boolean;
  cooldownDays: number | null;
  maxBenefitAmountPerPeriod: number | null;
  maxBenefitPeriodDays: number | null;
  requireCashAvailability: boolean;
  paymentInitiatorRoles: LeaderRole[];
  paymentConfirmerRoles: LeaderRole[];
  approverCannotInitiatePayment: boolean;
  excludeRelatedParties: boolean;
  frequentClaimsFlagCount: number | null;
  recentPolicyChangeFlagDays: number | null;
  // SASISHO14
  cashCheckAtApproval: boolean;
  conflictRelationTypes: RelationType[];
  approverRoles: AnyRole[];
  evidenceVerifierRoles: LeaderRole[];
  auditors: { id: string; name: string }[];
  highValueThreshold: number | null;
  blockSameSubjectAcrossMembers: boolean;
  deductLoanArrears: boolean;
  effectiveFrom: string;
  headcountAsOf: HeadcountAsOf;
  isUpcoming: boolean;
}

export interface Rate {
  id: string;
  category: Category;
  relationship: Relationship;
  perMemberAmount: number;
  acceptedEvidence: EvidenceType[];
  maxClaimsPerMember: number | null;
  isDisabled: boolean;
  effectiveFrom: string;
  status: ProposalStatus;
  note: string | null;
  requestedBy: string;
  decidedBy: string;
  isCurrent: boolean;
  canDecide: boolean;
  canCancel: boolean;
  effectiveTo: string | null;
  isExpired: boolean;
}

export interface PolicyView {
  active: Governance | null;
  /** Sera zilizoidhinishwa zitakazoanza baadaye */
  upcoming: Governance[];
  iAmAuditor: boolean;
  iAmLeader: boolean;
  pending: Governance[];
  history: Governance[];
  rates: Rate[];
}

export interface GovernanceInput {
  requiredApprovals: number;
  minLeaderApprovals: number;
  allowMemberApprovers: boolean;
  rejectionsToReject: number;
  votingDays: number;
  headcountRule: number;
  headcountPaidWithinDays: number | null;
  excludeBeneficiaryFromHeadcount: boolean;
  fundingSource: number;
  deductJoinFee: boolean;
  deductArrears: boolean;
  requireNoArrearsForEligibility: boolean;
  minMembershipDays: number;
  claimWindowDays: number;
  paymentConfirmationDays: number;
  statusChangeApprovals: number;
  expulsionVotePercent: number;
  requireJoinFeePaid: boolean;
  /** 0 = hakuna sharti */
  cooldownDays: number;
  /** 0 = bila kikomo */
  maxBenefitAmountPerPeriod: number;
  maxBenefitPeriodDays: number;
  requireCashAvailability: boolean;
  paymentInitiatorRoles: number[];
  paymentConfirmerRoles: number[];
  approverCannotInitiatePayment: boolean;
  excludeRelatedParties: boolean;
  /** 0 = hakuna tahadhari */
  frequentClaimsFlagCount: number;
  recentPolicyChangeFlagDays: number;
  // SASISHO14
  cashCheckAtApproval: boolean;
  conflictRelationTypes: number[];
  approverRoles: number[];
  evidenceVerifierRoles: number[];
  auditorMemberIds: string[];
  /** 0 = imezimwa */
  highValueThreshold: number;
  blockSameSubjectAcrossMembers: boolean;
  deductLoanArrears: boolean;
  /** yyyy-mm-dd; leo au baadaye */
  effectiveFrom: string;
  headcountAsOf: number;
  note?: string;
}

export interface RateInput {
  category: number;
  relationship: number;
  perMemberAmount: number;
  acceptedEvidence: number[];
  maxClaimsPerMember?: number | null;
  isDisabled?: boolean;
  effectiveFrom: string;
  /** yyyy-mm-dd — kiwango kinaisha (pamoja na siku hiyo); tupu = hakina mwisho */
  effectiveTo?: string | null;
  note?: string;
}

// ---------------------------------------------------------------- Kesi
export interface Check {
  code: string;
  passed: boolean;
  message: string;
  tpl?: string | null;
  tArgs?: string[] | null;
}

export interface CaseRow {
  id: string;
  caseNumber: string;
  category: Category;
  relationship: Relationship;
  beneficiaryId: string;
  beneficiaryName: string | null;
  subjectName: string;
  eventDate: string;
  status: CaseStatus;
  isHistorical: boolean;
  perMemberAmount: number;
  headcount: number;
  grossAmount: number;
  netAmount: number;
  mkobaReference: string | null;
  createdAt: string;
  votingDeadline: string | null;
  approvals: number;
  rejections: number;
}

export interface Evidence {
  id: string;
  documentType: EvidenceType;
  documentNumber: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  sha256: string;
  status: 'Uploaded' | 'Verified' | 'Rejected';
  uploadedBy: string;
  uploadedAt: string;
  verifiedBy: string;
  verifiedAt: string | null;
  reviewNote: string | null;
  canView: boolean;
  canVerify: boolean;
}

export interface CaseDetail extends Omit<CaseRow, 'approvals' | 'rejections'> {
  description: string | null;
  joinFeeDeducted: number;
  arrearsDeducted: number;
  headcountRule: string | null;
  frozen: boolean;
  snapshotAt: string | null;
  countedMembers: string[] | null;
  eligibility: Check[] | null;
  acceptedEvidence: EvidenceType[];
  approvedAt: string | null;
  paidAt: string | null;
  paidConfirmedBy: string;
  createdBy: string;
  closedReason: string | null;
  closedBy: string;
  required: number | null;
  minLeaders: number | null;
  rejectionsToReject: number | null;
  eligibleVoters: number;
  evidence: Evidence[];
  votes: { name: string | null; role: string; approve: boolean; reason: string | null; at: string }[];
  approvals: number;
  rejections: number;
  leaderApprovals: number;
  canVote: boolean;
  canUpload: boolean;
  canConfirmPayment: boolean;
  confirmBlockedReason: string | null;
  canInitiatePayment: boolean;
  initiateBlockedReason: string | null;
  cash: CashPosition | null;
  policyVersion: number | null;
  paymentInitiatedBy: string;
  paymentInitiatedAt: string | null;
  paymentInitiatedToPhone: string | null;
  cashAvailableAtInitiation: number | null;
  iAmConflicted: boolean;
  ledger: CaseReconciliation | null;
  canConfirmHistorical: boolean;
  canCancel: boolean;
  paymentCandidates: { reference: string; timestamp: string; amount: number; payerName: string; phone?: string | null }[];
  // SASISHO14
  beneficiaryMemberNumber: string | null;
  beneficiaryPhone: string | null;
  loanArrearsDeducted: number;
  /** kesi ya zamani: ArrearsDeducted ilijumuisha deni la mkopo */
  legacyArrears: boolean;
  paymentChannel: PaymentChannel;
  cashRecipientName: string | null;
  cashRecipientPhone: string | null;
  exceptionReason: string | null;
  /** Pesa ipo? (kwa wapiga kura na viongozi, kabla ya kuamua / wakati inasubiri pesa) */
  cashForVoters: CashPosition | null;
  canRecheckFunds: boolean;
  iAmAuditor: boolean;
  confirmHistoricalBlockedReason: string | null;
  initiateBlockedReasonLoc?: LocMsg | null;
  confirmBlockedReasonLoc?: LocMsg | null;
  /** SASISHO16: makato yaliyofungwa > deni la sasa la mnufaika (alilipa baada ya kura kuanza) */
  deductionDrift?: { frozen: number; current: number; excess: number; message: string; messageLoc: LocMsg } | null;
}

export interface CaseInput {
  category: number;
  relationship: number;
  beneficiaryMemberId: string;
  subjectName: string;
  eventDate: string;
  description?: string;
}

export interface HistoricalInput extends CaseInput {
  mkobaReference: string;
  perMemberAmount: number;
  headcount: number;
  joinFeeDeducted: number;
  arrearsDeducted: number;
  // SASISHO14
  loanArrearsDeducted: number;
  paymentChannel: number;
  cashPaidAt?: string | null;
  cashRecipientName?: string | null;
  cashRecipientPhone?: string | null;
  exceptionReason?: string | null;
}

export interface Assessment {
  eligible: boolean;
  checks: Check[];
  perMemberAmount: number;
  headcount: number;
  gross: number;
  joinFeeDeducted: number;
  arrearsDeducted: number;
  net: number;
  loanArrearsDeducted: number;
  headcountDate: string | null;
  headcountRule: string;
  acceptedEvidence: EvidenceType[];
}

// ---------------------------------------------------------------- M-Koba / daftari / mimi
export interface WithdrawalRow {
  reference: string;
  timestamp: string;
  amount: number;
  phone: string;
  payerName: string;
  balanceAfter: number | null;
  state: 'Explained' | 'Pending' | 'Unexplained';
  hali: string;
  caseId: string | null;
  caseNumber: string | null;
  explanationId: string | null;
  explanationKind: ExplanationKind | null;
  explanationText: string | null;
  canDecideExplanation: boolean;
}

export interface AlertItem {
  level: 'danger' | 'warn' | 'info';
  code: string;
  /** Kiswahili kutoka seva (inatumika kama tafsiri ya code haipo) */
  message: string;
  link: string | null;
  args?: Record<string, string> | null;
  tpl?: string | null;
  tArgs?: string[] | null;
}

export interface LedgerRow {
  caseId: string;
  caseNumber: string;
  paidAt: string | null;
  memberNumber: string;
  phones: string;
  fullName: string;
  category: string;
  relationship: string;
  subjectName: string;
  eventDate: string;
  perMemberAmount: number;
  headcount: number;
  gross: number;
  joinFeeDeducted: number;
  arrearsDeducted: number;
  net: number;
  approvers: string;
  mkobaReference: string | null;
  status: CaseStatus;
  isHistorical: boolean;
  loanArrearsDeducted: number;
  paymentChannel: PaymentChannel;
}

export interface MyBenefits {
  contributed: {
    caseId: string;
    caseNumber: string;
    category: string;
    relationship: string;
    beneficiary: string | null;
    subjectName: string;
    eventDate: string;
    paidAt: string | null;
    share: number | null;
    status: CaseStatus;
    note: string | null;
  }[];
  totalContributed: number;
  received: {
    caseId: string;
    caseNumber: string;
    category: string;
    subjectName: string;
    eventDate: string;
    grossAmount: number;
    joinFeeDeducted: number;
    arrearsDeducted: number;
    netAmount: number;
    paidAt: string | null;
    status: CaseStatus;
  }[];
  totalReceived: number;
  /** GroupMember.Id ya mtumiaji — mnufaika wa ombi lake mwenyewe */
  memberId: string;
  /** Hali yake inaruhusu kuomba mafao */
  canRequest: boolean;
  votesNeeded: { caseId: string; caseNumber: string; category: string; subjectName: string; beneficiary: string | null; votingDeadline: string }[];
}

// ---------------------------------------------------------------- SASISHO13: pesa, ulinganisho, uwazi, hatari
export interface CashPosition {
  mkobaBalance: number | null;
  asOf: string | null;
  asOfReference: string | null;
  reserved: number;
  reservedCases: string[];
  available: number | null;
  needed: number;
  sufficient: boolean;
}

export interface CaseReconciliation {
  posted: boolean;
  entries: { kind: 'Approved' | 'JoinFeeOffset' | 'ArrearsOffset' | 'LoanArrearsOffset' | 'CashPaid'; amount: number; reference: string | null; postedAt: string }[];
  checks: Check[];
  ok: boolean;
}

export interface TransparencyRow {
  id: string;
  caseNumber: string;
  category: Category;
  relationship: Relationship;
  beneficiaryName: string;
  memberNumber: string | null;
  subjectName: string;
  eventDate: string;
  status: CaseStatus;
  isHistorical: boolean;
  policyVersion: number | null;
  perMemberAmount: number;
  headcount: number;
  grossAmount: number;
  joinFeeDeducted: number;
  arrearsDeducted: number;
  loanArrearsDeducted: number;
  paymentChannel: PaymentChannel;
  netAmount: number;
  mkobaReference: string | null;
  paidAt: string | null;
  approvedAt: string | null;
  approvals: number;
  rejections: number;
  closedReason: string | null;
}

export interface Transparency {
  totals: { paidCases: number; gross: number; joinFeeOffset: number; arrearsOffset: number; loanArrearsOffset: number; cashPaid: number; awaitingPayment: number; awaitingFunds: number };
  rows: TransparencyRow[];
}

export interface RiskFlag {
  severity: 'danger' | 'warn' | 'info';
  code: string;
  message: string;
  caseId: string | null;
  caseNumber: string | null;
  at: string | null;
  args?: Record<string, string> | null;
  tpl?: string | null;
  tArgs?: string[] | null;
}

export interface RiskReport {
  generatedAt: string;
  audit: { ok: boolean; sealed: number; unsealed: number; message: string; tpl?: string | null; tArgs?: string[] | null; sealedNow: number };
  counts: { danger: number; warn: number; info: number };
  flags: RiskFlag[];
}

export interface Reconciliation {
  ok: boolean;
  cases: number;
  failing: number;
  totals: { gross: number; joinFeeOffset: number; arrearsOffset: number; loanArrearsOffset: number; cashPaid: number };
  rows: { id: string; caseNumber: string; ok: boolean; checks: Check[] }[];
}

export interface MemberRelationRow {
  id: string;
  memberAId: string;
  memberAName: string;
  memberBId: string;
  memberBName: string;
  type: RelationType;
  typeName: string;
  note: string | null;
  /** SASISHO15: uhusiano mpya ni Pending mpaka kiongozi wa pili aidhinishe (Rejected = alikataa / aliyesajili aliughairi) */
  status: 'Pending' | 'Active' | 'EndPending' | 'Ended' | 'Rejected';
  createdBy: string;
  createdAt: string;
  endRequestedBy: string;
  endReason: string | null;
  endedBy: string;
  endedAt: string | null;
  canRequestEnd: boolean;
  canDecideEnd: boolean;
  approvedBy: string;
  approvedAt: string | null;
  decisionNote: string | null;
  canDecideAdd: boolean;
  canCancelAdd: boolean;
}

const b = (groupId: string) => `/benefits/${groupId}`;

export const benefitsApi = {
  policy: async (g: string) => (await apiClient.get<PolicyView>(`${b(g)}/policy`)).data,
  proposeGovernance: async (g: string, dto: GovernanceInput) => (await apiClient.post(`${b(g)}/policy/governance`, dto)).data,
  decideGovernance: async (g: string, id: string, approve: boolean, note?: string) =>
    (await apiClient.post(`${b(g)}/policy/governance/${id}/decide`, { approve, note })).data,
  cancelGovernance: async (g: string, id: string) => apiClient.post(`${b(g)}/policy/governance/${id}/cancel`),
  proposeRate: async (g: string, dto: RateInput) => (await apiClient.post(`${b(g)}/policy/rates`, dto)).data,
  decideRate: async (g: string, id: string, approve: boolean, note?: string) =>
    (await apiClient.post(`${b(g)}/policy/rates/${id}/decide`, { approve, note })).data,
  cancelRate: async (g: string, id: string) => apiClient.post(`${b(g)}/policy/rates/${id}/cancel`),

  cases: async (g: string) => (await apiClient.get<CaseRow[]>(`${b(g)}/cases`)).data,
  case: async (g: string, id: string) => (await apiClient.get<CaseDetail>(`${b(g)}/cases/${id}`)).data,
  preview: async (g: string, dto: CaseInput) => (await apiClient.post<Assessment>(`${b(g)}/cases/preview`, dto)).data,
  open: async (g: string, dto: CaseInput) => (await apiClient.post<{ id: string; caseNumber: string }>(`${b(g)}/cases`, dto)).data,
  historical: async (g: string, dto: HistoricalInput) =>
    (await apiClient.post<{ id: string; caseNumber: string }>(`${b(g)}/cases/historical`, dto)).data,
  decideHistorical: async (g: string, id: string, approve: boolean, note?: string) =>
    (await apiClient.post(`${b(g)}/cases/${id}/historical/decide`, { approve, note })).data,
  upload: async (g: string, caseId: string, file: File, documentType: number, documentNumber: string) => {
    const fd = new FormData();
    fd.append('file', file);
    fd.append('documentType', String(documentType));
    fd.append('documentNumber', documentNumber);
    return (await apiClient.post(`${b(g)}/cases/${caseId}/evidence`, fd)).data;
  },
  verify: async (g: string, evidenceId: string, approve: boolean, note?: string) =>
    (await apiClient.post<{ caseId: string; status: string; warning: { message: string; tpl: string; args: string[] } | null }>(`${b(g)}/evidence/${evidenceId}/verify`, { approve, note })).data,
  /** Faili la cheti kama Blob (lina token — haliwezi kufunguliwa kwa <a href> ya kawaida). */
  evidenceFile: async (g: string, evidenceId: string) =>
    (await apiClient.get<Blob>(`${b(g)}/evidence/${evidenceId}/file`, { responseType: 'blob' })).data,
  vote: async (g: string, caseId: string, approve: boolean, reason?: string) =>
    (await apiClient.post<{ status: CaseStatus; approvals: number; rejections: number; required: number }>(`${b(g)}/cases/${caseId}/vote`, {
      approve,
      reason,
    })).data,
  recheckFunds: async (g: string, caseId: string) => (await apiClient.post<{ id: string; status: CaseStatus }>(`${b(g)}/cases/${caseId}/recheck-funds`)).data,
  initiatePayment: async (g: string, caseId: string, note?: string) =>
    (await apiClient.post(`${b(g)}/cases/${caseId}/initiate-payment`, { note })).data,
  confirmPayment: async (g: string, caseId: string, mkobaReference: string, acknowledgeOtherRecipient = false, note?: string) =>
    (await apiClient.post(`${b(g)}/cases/${caseId}/confirm-payment`, { mkobaReference, acknowledgeOtherRecipient, note })).data,
  cancelCase: async (g: string, caseId: string, reason: string) => (await apiClient.post(`${b(g)}/cases/${caseId}/cancel`, { reason })).data,

  withdrawals: async (g: string) => (await apiClient.get<WithdrawalRow[]>(`${b(g)}/withdrawals`)).data,
  explain: async (g: string, mkobaReference: string, kind: number, description: string) =>
    (await apiClient.post(`${b(g)}/withdrawals/explain`, { mkobaReference, kind, description })).data,
  decideExplanation: async (g: string, id: string, approve: boolean, note?: string) =>
    (await apiClient.post(`${b(g)}/withdrawals/explanations/${id}/decide`, { approve, note })).data,
  alerts: async (g: string) => (await apiClient.get<AlertItem[]>(`${b(g)}/alerts`)).data,
  ledger: async (g: string) => (await apiClient.get<LedgerRow[]>(`${b(g)}/ledger`)).data,
  ledgerCsv: async (g: string) => (await apiClient.get<Blob>(`${b(g)}/ledger.csv`, { responseType: 'blob' })).data,
  me: async (g: string) => (await apiClient.get<MyBenefits>(`${b(g)}/me`)).data,
  transparency: async (g: string) => (await apiClient.get<Transparency>(`${b(g)}/transparency`)).data,
  reconciliation: async (g: string) => (await apiClient.get<Reconciliation>(`${b(g)}/reconciliation`)).data,
  risk: async (g: string) => (await apiClient.get<RiskReport>(`${b(g)}/risk`)).data,
  relations: async (g: string) => (await apiClient.get<MemberRelationRow[]>(`${b(g)}/relations`)).data,
  addRelation: async (g: string, dto: { memberAId: string; memberBId: string; type: number; note?: string }) =>
    (await apiClient.post(`${b(g)}/relations`, dto)).data,
  endRelation: async (g: string, id: string, reason: string) => (await apiClient.post(`${b(g)}/relations/${id}/end`, { reason })).data,
  decideRelation: async (g: string, id: string, approve: boolean, note?: string) =>
    (await apiClient.post(`${b(g)}/relations/${id}/decide`, { approve, note })).data,
  decideEndRelation: async (g: string, id: string, approve: boolean, note?: string) =>
    (await apiClient.post(`${b(g)}/relations/${id}/end/decide`, { approve, note })).data,
};

// ---------------------------------------------------------------- Kura za hali ya mwanachama
export interface StatusChangeRequest {
  id: string;
  memberId: string;
  memberName: string | null;
  fromStatus: string;
  toStatus: string;
  isExpulsion: boolean;
  effectiveDate: string;
  reason: string;
  evidenceNote: string | null;
  requiredApprovals: number;
  rejectionsToReject: number;
  leadersOnly: boolean;
  status: ProposalStatus;
  requestedAt: string;
  decidedAt: string | null;
  requestedBy: string | null;
  approvals: number;
  rejections: number;
  votes: { name: string | null; approve: boolean; reason: string | null; at: string }[];
  canVote: boolean;
  canCancel: boolean;
  /** SASISHO21: ombi linamhusu mwanachama anayeangalia (kwa rufaa). */
  mine: boolean;
}

const gv = (groupId: string) => `/governance/${groupId}/status-changes`;

export const governanceApi = {
  list: async (g: string) => (await apiClient.get<StatusChangeRequest[]>(gv(g))).data,
  request: async (
    g: string,
    dto: { memberId: string; toStatus: number; isExpulsion: boolean; effectiveDate: string; reason: string; evidenceNote?: string },
  ) => (await apiClient.post<{ id: string; status: string; requiredApprovals: number }>(gv(g), dto)).data,
  vote: async (g: string, id: string, approve: boolean, reason?: string) => (await apiClient.post(`${gv(g)}/${id}/vote`, { approve, reason })).data,
  cancel: async (g: string, id: string) => apiClient.post(`${gv(g)}/${id}/cancel`),
};

// ---------------------------------------------------------------- Arifa
export interface NotificationItem {
  id: string;
  groupId: string;
  kind: string;
  title: string;
  body: string;
  /** SASISHO14: {"title":{tpl,args},"body":{tpl,args}} — kwa tafsiri */
  locJson?: string | null;
  link: string | null;
  createdAt: string;
  readAt: string | null;
}

export const notificationsApi = {
  list: async (groupId?: string | null) =>
    (await apiClient.get<{ unread: number; items: NotificationItem[] }>('/notifications', { params: { groupId: groupId || undefined, take: 30 } })).data,
  read: async (id: string) => apiClient.post(`/notifications/${id}/read`),
  readAll: async (groupId?: string | null) => apiClient.post('/notifications/read-all', null, { params: { groupId: groupId || undefined } }),
};
