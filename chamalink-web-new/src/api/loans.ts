import { apiClient } from './client';
import type { CashPosition } from './benefits';

// =====================================================================
// SASISHO15 (AWAMU L): MIKOPO — njia pekee ya kutoa mkopo:
// ombi → kura za viongozi huru → Mtunza-hazina anaanzisha → kiongozi mwingine
// anathibitisha kwa rejea ya utoaji wa M-Koba. Sheria (riba, kikomo, miezi, kura)
// zote zinatoka kwenye Mipangilio ya kikundi; frontend haiweki namba yoyote.
// =====================================================================

export type LoanApplicationStatus = 'Voting' | 'Approved' | 'DisbursementInitiated' | 'Disbursed' | 'Rejected' | 'Cancelled' | 'Expired';

export interface LoanCheck {
  code: string;
  passed: boolean;
  message: string;
  tpl?: string | null;
  tArgs?: string[] | null;
}

export interface LoanPolicy {
  configured: boolean;
  enabled: boolean;
  /** Swichi ya viongozi (inaweza kuwa true wakati sera haijakamilika). */
  loansEnabled?: boolean;
  /** Kuna mikopo au maombi yaliyopo (yanahudumiwa hata mikopo mipya ikizimwa). */
  hasActivity?: boolean;
  /** Ukurasa/menyu ya Mikopo ionekane. */
  visible?: boolean;
  isLeader?: boolean;
  /** Viongozi tu: kinachokosekana kwenye sera (kwa lugha ya kawaida). */
  missing: string[];
  interestPercent?: number | null;
  repaymentMonths?: number | null;
  maxAmount?: number | null;
  maxMultipleOfContributions?: number | null;
  minMembershipMonths?: number | null;
  allowWithArrears?: boolean;
  allowMultipleActive?: boolean;
  minCashReserve?: number | null;
  requiredApprovals?: number | null;
  approvalValidDays?: number | null;
  latePenaltyAmount?: number | null;
}

export interface LoanAssessment {
  eligible: boolean;
  checks: LoanCheck[];
  amount: number;
  interestPercent: number;
  interestAmount: number;
  totalRepayable: number;
  repaymentMonths: number;
  monthlyInstallment: number;
  requiredApprovals: number;
  maxAllowed: number | null;
  cash: CashPosition | null;
}

export interface LoanApplicationRow {
  id: string;
  applicationNumber: string;
  status: LoanApplicationStatus;
  amount: number;
  interestPercent: number;
  interestAmount: number;
  totalRepayable: number;
  repaymentMonths: number;
  borrowerId: string;
  borrower: string | null;
  createdAt: string;
  approvedAt: string | null;
  disbursedAt: string | null;
  mkobaReference: string | null;
  purpose: string | null;
  approvals: number;
  rejections: number;
  requiredApprovals: number;
}

export interface WithdrawalCandidate {
  reference: string;
  timestamp: string;
  amount: number;
  payerName: string | null;
  phone: string | null;
}

export interface LoanApplicationDetail extends LoanApplicationRow {
  monthlyInstallment: number;
  requestedBy: string | null;
  approvalExpiresAt: string | null;
  initiatedBy: string | null;
  disbursementInitiatedAt: string | null;
  confirmedBy: string | null;
  disbursementConfirmedAt: string | null;
  recipientNote: string | null;
  loanId: string | null;
  closedBy: string | null;
  closedAt: string | null;
  closeReason: string | null;
  eligibility: LoanCheck[] | null;
  votes: { voter: string | null; role: string; approve: boolean; reason: string | null; at: string }[];
  eligibleVoters: number;
  canVote: boolean;
  canInitiate: boolean;
  initiateWhy: string | null;
  canConfirm: boolean;
  candidates: WithdrawalCandidate[];
  canCancel: boolean;
}

export interface LoanRow {
  id: string;
  borrowerId: string;
  borrower: string | null;
  principalAmount: number;
  interestRate: number;
  interestAmount: number;
  totalRepayable: number;
  repaid: number;
  outstanding: number;
  disbursedAt: string;
  dueDate: string | null;
  status: string;
  disbursementReference: string | null;
  loanApplicationId: string | null;
  defaultRequestedBy: string | null;
  defaultReason: string | null;
  canRequestDefault: boolean;
  canDecideDefault: boolean;
  canCancelDefault: boolean;
}

export interface ApplyInput {
  borrowerMemberId?: string | null;
  amount: number;
  purpose?: string | null;
}

const b = (g: string) => `/loan-applications/${g}`;

export const loansApi = {
  policy: async (g: string) => (await apiClient.get<LoanPolicy>(`${b(g)}/policy`)).data,
  preview: async (g: string, dto: ApplyInput) => (await apiClient.post<LoanAssessment>(`${b(g)}/preview`, dto)).data,
  list: async (g: string) => (await apiClient.get<LoanApplicationRow[]>(b(g))).data,
  get: async (g: string, id: string) => (await apiClient.get<LoanApplicationDetail>(`${b(g)}/${id}`)).data,
  apply: async (g: string, dto: ApplyInput) =>
    (await apiClient.post<{ id: string; applicationNumber: string; status: LoanApplicationStatus }>(b(g), dto)).data,
  vote: async (g: string, id: string, approve: boolean, reason?: string) => (await apiClient.post(`${b(g)}/${id}/vote`, { approve, reason })).data,
  initiate: async (g: string, id: string) => (await apiClient.post(`${b(g)}/${id}/initiate-disbursement`)).data,
  confirm: async (g: string, id: string, mkobaReference: string, acknowledgeOtherRecipient: boolean, note?: string) =>
    (await apiClient.post(`${b(g)}/${id}/confirm-disbursement`, { mkobaReference, acknowledgeOtherRecipient, note })).data,
  cancel: async (g: string, id: string, reason: string) => (await apiClient.post(`${b(g)}/${id}/cancel`, { reason })).data,
  loans: async (g: string) => (await apiClient.get<LoanRow[]>(`${b(g)}/loans`)).data,
  requestDefault: async (g: string, loanId: string, reason: string) => (await apiClient.post(`${b(g)}/loans/${loanId}/default`, { reason })).data,
  decideDefault: async (g: string, loanId: string, approve: boolean, note?: string) =>
    (await apiClient.post(`${b(g)}/loans/${loanId}/default/decide`, { approve, note })).data,
};
