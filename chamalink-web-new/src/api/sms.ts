import { apiClient } from './client';

// =====================================================================
// SASISHO21 — SMS ZA NO-REPLY
//
// Mwanachama asiye na smartphone anafikiwa kwa SMS. Salio ni CREDITS
// (hazina), si bajeti ya mwezi: credits zikiisha, SMS zinasimama na
// viongozi wanajulishwa.
// =====================================================================

export interface SmsOverview {
  credits: number;
  totalPurchased: number;
  totalSent: number;
  queued: number;
  failed: number;
  suppressed: number;
  lowThreshold: number;
  enabled: boolean;
}

export interface SmsRow {
  id: string;
  memberNumber: string | null;
  /** Namba imefichwa (faragha): 2557****123 */
  phone: string;
  templateKey: string;
  body: string;
  status: 'Queued' | 'Sent' | 'Delivered' | 'Failed' | 'Suppressed';
  attempts: number;
  providerStatus: string | null;
  failureReason: string | null;
  suppressedReason: string | null;
  createdAt: string;
  sentAt: string | null;
  deliveredAt: string | null;
}

export async function getSmsOverview(groupId: string): Promise<SmsOverview> {
  return (await apiClient.get<SmsOverview>(`/sms/${groupId}/overview`)).data;
}

export async function listSms(groupId: string, status?: string): Promise<SmsRow[]> {
  const params = status && status !== 'all' ? { status } : undefined;
  return (await apiClient.get<SmsRow[]>(`/sms/${groupId}/messages`, { params })).data;
}

export async function topUpSms(
  groupId: string,
  credits: number,
  reason: string | null,
): Promise<{ credits: number; totalPurchased: number }> {
  return (await apiClient.post<{ credits: number; totalPurchased: number }>(`/sms/${groupId}/credits`, { credits, reason }))
    .data;
}

export async function dispatchSms(
  groupId: string,
): Promise<{ attempted: number; sent: number; failed: number; skipped: number }> {
  return (
    await apiClient.post<{ attempted: number; sent: number; failed: number; skipped: number }>(`/sms/${groupId}/dispatch`)
  ).data;
}
