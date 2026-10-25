import { apiClient } from './client';

// =====================================================================
// SASISHO21 — RUFAA
//
// Mwanachama anapinga uamuzi unaomgusa. Rufaa HAIBADILISHI pesa:
// inabadilisha UAMUZI tu (hali ya mwanachama au hali ya dai).
//
// Kiongozi mmoja HAWEZI kuamua: viongozi wanaohitajika wanatoka kwenye
// sera ya kikundi (kamwe chini ya 2), na aliyeomba hapigani kura yake.
// =====================================================================

export type AppealKind = 'StatusChange' | 'Claim' | 'Fine' | 'Other';
export type AppealState = 'Pending' | 'Granted' | 'Dismissed' | 'Withdrawn';

export interface AppealRow {
  id: string;
  groupMemberId: string;
  memberNumber: string;
  memberName: string;
  kind: AppealKind;
  status: AppealState;
  subjectSummary: string;
  reason: string;
  requiredApprovals: number;
  approvals: number;
  rejections: number;
  filedAt: string;
  decidedAt: string | null;
  decisionNote: string | null;
  effectSummary: string | null;
  mine: boolean;
  canVote: boolean;
  canWithdraw: boolean;
  myVote: 'Approve' | 'Reject' | null;
}

export async function listAppeals(groupId: string): Promise<AppealRow[]> {
  return (await apiClient.get<AppealRow[]>(`/appeals/${groupId}`)).data;
}

export async function fileAppeal(
  groupId: string,
  body: { kind: AppealKind; subjectId?: string | null; reason: string },
): Promise<{ appealId: string; status: AppealState; requiredApprovals: number; ujumbe: string }> {
  return (await apiClient.post<{ appealId: string; status: AppealState; requiredApprovals: number; ujumbe: string }>(
    `/appeals/${groupId}`,
    { kind: body.kind, subjectId: body.subjectId ?? null, reason: body.reason },
  )).data;
}

export async function voteAppeal(
  groupId: string,
  appealId: string,
  approve: boolean,
  note?: string | null,
): Promise<{ status: AppealState; approvals: number; rejections: number; requiredApprovals: number; effect: string | null; ujumbe: string }> {
  return (await apiClient.post<{
    status: AppealState;
    approvals: number;
    rejections: number;
    requiredApprovals: number;
    effect: string | null;
    ujumbe: string;
  }>(`/appeals/${groupId}/${appealId}/vote`, { approve, note: note ?? null })).data;
}

export async function withdrawAppeal(groupId: string, appealId: string): Promise<void> {
  await apiClient.delete(`/appeals/${groupId}/${appealId}`);
}
