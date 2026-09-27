import { apiClient } from './client';
import { tk } from '../i18n';

// Usajili na uanachama — inalingana na ChamaLink.API.Controllers.MembershipController.
// Ruhusa zinakaguliwa na seva (dhidi ya DB); kuficha vitufe hapa ni urahisi tu, si ulinzi.

export type GroupTypeName = 'MonthlySavings' | 'EventBased' | 'Hybrid';
export type RequestableRole = 'Member' | 'Secretary' | 'Treasurer';

export interface GroupCreated {
  groupId: string;
  name: string;
  shortCode: string;
  joinCode: string;
  chairMemberId: string;
  chairMemberNumber: string;
}

export interface JoinPreview {
  groupId: string;
  groupName: string;
  groupType: GroupTypeName;
  alreadyMember: boolean;
  hasPendingRequest: boolean;
}

export interface MyRequest {
  requestId: string;
  groupId: string;
  groupName: string;
  requestedRole: RequestableRole;
  status: 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';
  createdAt: string;
  decisionNote: string | null;
}

export interface GroupProfile {
  groupId: string;
  name: string;
  type: GroupTypeName;
  shortCode: string | null;
  shortCodeLocked: boolean;
  joinCode: string | null;
  members: number;
  pendingRequests: number;
}

export interface SuggestedMember {
  memberId: string;
  memberNumber: string;
  fullName: string;
  phones: string[];
}

export interface PendingRequest {
  requestId: string;
  userId: string;
  fullName: string;
  email: string;
  phone: string;
  requestedRole: RequestableRole;
  note: string | null;
  createdAt: string;
  suggested: SuggestedMember | null;
  youCanDecide: boolean;
}

export interface MemberRow {
  memberId: string;
  memberNumber: string;
  numberIsOfficial: boolean;
  fullName: string;
  role: string;
  status: string;
  hasLogin: boolean;
  email: string | null;
  phones: string[];
  joinedAt: string;
}

export interface IssueResult {
  issued: number;
  changes: { fullName: string; oldNumber: string; newNumber: string }[];
}

const base = '/membership';

export async function createGroup(body: { name: string; description?: string; type: GroupTypeName; shortCode: string }) {
  return (await apiClient.post<GroupCreated>(`${base}/groups`, body)).data;
}
export async function previewJoin(code: string) {
  return (await apiClient.get<JoinPreview>(`${base}/join/${encodeURIComponent(code.trim())}`)).data;
}
export async function requestToJoin(joinCode: string, requestedRole: RequestableRole, note?: string) {
  return (await apiClient.post<{ requestId: string; status: string }>(`${base}/join`, { joinCode, requestedRole, note })).data;
}
export async function getMyRequests() {
  return (await apiClient.get<MyRequest[]>(`${base}/my-requests`)).data;
}
export async function cancelMyRequest(requestId: string) {
  await apiClient.delete(`${base}/my-requests/${requestId}`);
}
export async function getGroupProfile(groupId: string) {
  return (await apiClient.get<GroupProfile>(`${base}/${groupId}/profile`)).data;
}
export async function setShortCode(groupId: string, shortCode: string) {
  return (await apiClient.put<GroupProfile>(`${base}/${groupId}/short-code`, { shortCode })).data;
}
export async function regenerateJoinCode(groupId: string) {
  return (await apiClient.post<{ joinCode: string }>(`${base}/${groupId}/join-code`)).data;
}
export async function getPendingRequests(groupId: string) {
  return (await apiClient.get<PendingRequest[]>(`${base}/${groupId}/requests`)).data;
}
export async function decideRequest(groupId: string, requestId: string, approve: boolean, linkMemberId: string | null, note?: string) {
  return (await apiClient.post(`${base}/${groupId}/requests/${requestId}/decision`, { approve, linkMemberId, note })).data;
}
export async function getMembersAdmin(groupId: string) {
  return (await apiClient.get<MemberRow[]>(`${base}/${groupId}/members`)).data;
}
export async function addMemberRecord(groupId: string, body: { fullName: string; phone?: string; joinedAt?: string }) {
  return (await apiClient.post<MemberRow>(`${base}/${groupId}/members`, body)).data;
}
export async function changeRole(groupId: string, memberId: string, role: string) {
  return (await apiClient.put<MemberRow>(`${base}/${groupId}/members/${memberId}/role`, { role })).data;
}
export async function issueMemberNumbers(groupId: string) {
  return (await apiClient.post<IssueResult>(`${base}/${groupId}/member-numbers/issue`)).data;
}

export const GROUP_TYPE_LABEL: Record<GroupTypeName, { label: string; hint: string }> = {
  MonthlySavings: { label: tk('Kila mwezi'), hint: tk('Mchango wa kila mwezi kwa tarehe maalum') },
  EventBased: { label: tk('Kila tukio'), hint: tk('Mchango unapohitajika (msiba, harusi)') },
  Hybrid: { label: tk('Vyote viwili'), hint: tk('Mchango wa mwezi + michango ya matukio') },
};
