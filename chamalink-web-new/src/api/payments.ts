import { apiClient } from './client';

// =====================================================================
// SASISHO21 — SANDUKU LA MALIPO
//
// Statement ya M-Koba ina mistari mingi; kila mstari ni pesa ya MTU.
// Sanduku linatuambia: huyu ni nani, kwa kanuni gani, na nani alithibitisha.
//
// Kanuni kuu: kuthibitisha kunabadilisha MMILIKI wa mstari mmoja — hakuna
// pesa mpya. Kiasi kikubwa kinahitaji kiongozi wa pili (sera ya kikundi).
// =====================================================================

export type ReceiptStatus = 'Unmatched' | 'Suggested' | 'Confirmed' | 'Rejected' | 'AutoMatched';

export interface InboxRow {
  receiptId: string;
  source: string;
  status: ReceiptStatus;
  reference: string | null;
  amount: number;
  occurredAt: string;
  phone: string | null;
  payerName: string | null;
  description: string | null;
  paymentCodeSeen: string | null;
  matchRule: string | null;
  suggestedMemberId: string | null;
  suggestedMemberNumber: string | null;
  suggestedName: string | null;
  needsSecondApprover: boolean;
  note: string | null;
}

export interface InboxSummary {
  total: number;
  unmatched: number;
  suggested: number;
  confirmed: number;
  rejected: number;
  unmatchedAmount: number;
  confirmedAmount: number;
  pendingClaims: number;
}

export interface InboxResponse {
  summary: InboxSummary;
  rows: InboxRow[];
}

export async function syncInbox(groupId: string): Promise<{ added: number; ujumbe: string }> {
  return (await apiClient.post<{ added: number; ujumbe: string }>(`/payments/${groupId}/inbox/sync`)).data;
}

export async function listInbox(groupId: string, status?: ReceiptStatus | 'all'): Promise<InboxResponse> {
  const params = status && status !== 'all' ? { status } : undefined;
  return (await apiClient.get<InboxResponse>(`/payments/${groupId}/inbox`, { params })).data;
}

export interface ThreeWay {
  statement: InboxSummary;
  claims: { status: string; count: number; amount: number }[];
  expected: { status: string; count: number; outstanding: number }[];
}

export async function getThreeWay(groupId: string): Promise<ThreeWay> {
  return (await apiClient.get<ThreeWay>(`/payments/${groupId}/inbox/three-way`)).data;
}

export interface ConfirmResult {
  status: string;
  balance?: number;
  owed?: number;
  message?: string;
  ujumbe?: string;
}

export async function confirmReceipt(
  groupId: string,
  receiptId: string,
  memberId: string,
  note: string | null,
  secondApproval = false,
): Promise<ConfirmResult> {
  return (
    await apiClient.post<ConfirmResult>(`/payments/${groupId}/inbox/${receiptId}/confirm`, {
      memberId,
      note,
      secondApproval,
    })
  ).data;
}

export async function rejectReceipt(groupId: string, receiptId: string, reason: string): Promise<{ status: string }> {
  return (await apiClient.post<{ status: string }>(`/payments/${groupId}/inbox/${receiptId}/reject`, { reason })).data;
}

// ---------------------------------------------------------------- madai

export interface ClaimRow {
  id: string;
  memberId: string;
  memberNumber: string | null;
  name: string | null;
  amount: number;
  reference: string;
  paymentCode: string | null;
  status: 'Pending' | 'Verified' | 'Rejected' | 'Withdrawn';
  note: string | null;
  createdAt: string;
  decidedAt: string | null;
  /** true = dai la mwanachama anayeangalia. */
  mine: boolean;
}

/** Mwanachama anadai "nmelipa" — HAKUBADILISHI deni. */
export async function submitClaim(
  groupId: string,
  body: { amount: number; reference: string; paymentCode?: string | null; note?: string | null },
): Promise<{ claimId: string; status: string; ujumbe: string }> {
  return (await apiClient.post<{ claimId: string; status: string; ujumbe: string }>(`/payments/${groupId}/claims`, body))
    .data;
}

/** Mwanachama anaona yake; kiongozi anaona yote. */
export async function listClaims(groupId: string): Promise<ClaimRow[]> {
  return (await apiClient.get<ClaimRow[]>(`/payments/${groupId}/claims`)).data;
}

export async function decideClaim(
  groupId: string,
  claimId: string,
  approve: boolean,
  note: string | null,
  receiptId: string | null,
): Promise<{ status: string }> {
  return (
    await apiClient.post<{ status: string }>(`/payments/${groupId}/claims/${claimId}/decide`, {
      approve,
      note,
      receiptId,
    })
  ).data;
}

// ---------------------------------------------------------------- PDF
/**
 * Risiti ya malipo (PDF). Mwanachama anapakua yake; kiongozi yeyote wa kikundi.
 * PDF inatoka kwenye kumbukumbu zilizoandikwa tayari — haibadilishi deni.
 */
export async function downloadReceiptPdf(groupId: string, receiptId: string): Promise<Blob> {
  const r = await apiClient.get(`/payments/${groupId}/receipts/${receiptId}.pdf`, { responseType: 'blob' });
  return r.data as Blob;
}

/** Ripoti ya sanduku la malipo (PDF) — viongozi pekee. */
export async function downloadInboxReportPdf(
  groupId: string,
  range?: { from?: string; to?: string },
): Promise<Blob> {
  const params = {
    from: range?.from,
    to: range?.to,
  };
  const r = await apiClient.get(`/payments/${groupId}/inbox/report.pdf`, { params, responseType: 'blob' });
  return r.data as Blob;
}
