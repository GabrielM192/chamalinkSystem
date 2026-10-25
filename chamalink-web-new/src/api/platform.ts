import axios, { AxiosError } from 'axios';

/**
 * SASISHO20 — API ya MSIMAMIZI WA MFUMO (/api/platform).
 *
 * Kwa nini client tofauti na `client.ts`?
 *  - Kikao cha msimamizi ni CHA PEKEE: hakitumii token ya kikundi, na hakigusi refresh cookie
 *    ya kikundi. Msimamizi akiwa kwenye kompyuta moja ya kikundi, vikao viwili havichanganyiki.
 *  - Token yake inaisha baada ya dakika 60 (Platform:SessionMinutes) na haizungushwi kimya kimya:
 *    ikisha, msimamizi anaingia tena. Hiyo ni kwa makusudi — nywila + MFA, si refresh ya milele.
 */
const BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5168/api';

export const platformClient = axios.create({ baseURL: `${BASE}/platform` });

// Token ya msimamizi inakaa KWENYE KUMBUKUMBU tu — kama token ya kikundi (SASISHO15).
// XSS haiwezi kuichukua baadaye kutoka hifadhi ya browser, na refresh ya ukurasa inalazimu
// kuingia tena (neno la siri + MFA). Kwa akaunti yenye uwezo wa vikundi VYOTE, hilo ni jambo jema.
let platformToken: string | null = null;

export function getPlatformToken(): string | null {
  return platformToken;
}

export function setPlatformToken(token: string | null): void {
  platformToken = token;
}

platformClient.interceptors.request.use((config) => {
  const token = getPlatformToken();
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

platformClient.interceptors.response.use(
  (r) => r,
  (error: AxiosError) => {
    // 401/403 kwenye API ya msimamizi = kikao kimeisha au ngazi haitoshi. Hatuzungushi token:
    // tunafuta kikao ili msimamizi aingie tena (na atambue kilichotokea).
    if (error.response?.status === 401) setPlatformToken(null);
    return Promise.reject(error);
  },
);

// ------------------------------------------------------------------ Aina (zinalingana na backend)
export type PlatformLevel = 'Support' | 'Oversight' | 'SuperAdmin';
export type PlatformStage = 'EnrollmentRequired' | 'MfaRequired' | 'Ready';

export interface LoginStage {
  stage: PlatformStage;
  token: string | null;
  fullName: string;
  level: PlatformLevel;
  expiresAt: string | null;
}

export interface PlatformMe {
  userId: string;
  fullName: string | null;
  email: string | null;
  level: PlatformLevel;
  mfaEnabled: boolean;
  lastMfaVerifiedAt: string | null;
}

export interface GroupRow {
  id: string;
  name: string;
  shortCode: string | null;
  type: string;
  createdAt: string;
  members: number;
  activeMembers: number;
  leaders: number;
  eventDebt: number;
  lastStatementAt: string | null;
  lastActivityAt: string | null;
  pendingApprovals: number;
  flags: string[];
  health: 'Critical' | 'Warning' | 'Ok' | string;
}

export interface GroupList {
  rows: GroupRow[];
  kikundi: number;
  hatari: number;
  tahadhari: number;
}

export interface IssueRow {
  code: string;
  severity: string;
  detail: string;
}

export interface GroupDetail {
  summary: GroupRow;
  totalAccepted: number;
  savings: number;
  arrears: number;
  loansOutstanding: number;
  eventDebt: number;
  joinFeeOutstanding: number;
  monthsTracked: number;
  issues: IssueRow[];
  breakGlassActive: boolean;
  breakGlassExpiresAt: string | null;
}

export interface MemberRow {
  memberId: string;
  memberNumber: string | null;
  name: string | null;
  status: string;
  role: string;
  balance: number;
  outstanding: number;
  arrears: number;
  active: boolean;
}

export interface PersonRow {
  userId: string;
  fullName: string;
  email: string | null;
  phone: string | null;
  locked: boolean;
  lockedUntil: string | null;
  hasAccount: boolean;
  groups: { groupId: string; groupName: string | null; memberNumber: string | null; role: string; status: string; isLeader: boolean }[];
}

export interface AdminRow {
  id: string;
  userId: string;
  name: string | null;
  email: string | null;
  level: PlatformLevel;
  isActive: boolean;
  mfaEnabled: boolean;
  createdAt: string;
  deactivatedAt: string | null;
}

export interface ApprovalRow {
  id: string;
  action: string;
  status: string;
  groupId: string | null;
  groupName: string | null;
  targetPlatformAdminId: string | null;
  targetPlatformAdminName: string | null;
  targetUserId: string | null;
  targetUserName: string | null;
  requestedByName: string | null;
  requestedByPlatformAdminId: string;
  decidedByName: string | null;
  reason: string;
  decisionNote: string | null;
  payloadJson: string | null;
  createdAt: string;
  decidedAt: string | null;
  expiresAt: string | null;
  canDecide: boolean;
  canCancel: boolean;
  isMine: boolean;
}

export interface AuditRow {
  id: string;
  action: string;
  groupId: string | null;
  groupName: string | null;
  subjectId: string | null;
  actorName: string | null;
  actorUserId: string | null;
  reason: string | null;
  detailJson: string | null;
  ip: string | null;
  at: string;
}

export interface IssuedCode {
  code: string;
  expiresAt: string;
  person: string;
  email: string;
  ujumbe: string;
}

// ------------------------------------------------------------------ Kuingia
export async function platformLogin(email: string, password: string): Promise<LoginStage> {
  const { data } = await platformClient.post<LoginStage>('/auth/login', { email, password });
  if (data.stage === 'Ready' && data.token) setPlatformToken(data.token);
  return data;
}

/** Hatua ya kujisajili MFA: inarudisha siri (QR/maandishi) — token ya hatua ya kwanza inatumika. */
export async function platformEnroll(challengeToken: string): Promise<{ secret: string; uri: string; manualHint: string }> {
  const { data } = await axios.post(
    `${BASE}/platform/auth/mfa/enroll`,
    null,
    { headers: { Authorization: `Bearer ${challengeToken}` } },
  );
  return data;
}

export async function platformConfirmEnrollment(challengeToken: string, code: string): Promise<LoginStage> {
  const { data } = await axios.post<LoginStage>(
    `${BASE}/platform/auth/mfa/confirm`,
    { code },
    { headers: { Authorization: `Bearer ${challengeToken}` } },
  );
  if (data.token) setPlatformToken(data.token);
  return data;
}

export async function platformVerifyMfa(challengeToken: string, code: string): Promise<LoginStage> {
  const { data } = await axios.post<LoginStage>(
    `${BASE}/platform/auth/mfa/verify`,
    { code },
    { headers: { Authorization: `Bearer ${challengeToken}` } },
  );
  if (data.token) setPlatformToken(data.token);
  return data;
}

export async function platformLogout(): Promise<void> {
  try {
    await platformClient.post('/auth/logout');
  } finally {
    setPlatformToken(null);
  }
}

// ------------------------------------------------------------------ Dashibodi
export async function platformMe(): Promise<PlatformMe> {
  const { data } = await platformClient.get<PlatformMe>('/me');
  return data;
}

export async function platformGroups(): Promise<GroupList> {
  const { data } = await platformClient.get<GroupList>('/groups');
  return data;
}

export async function platformGroup(id: string): Promise<GroupDetail> {
  const { data } = await platformClient.get<GroupDetail>(`/groups/${id}`);
  return data;
}

export async function platformMembers(id: string): Promise<{ rows: MemberRow[]; members: number }> {
  const { data } = await platformClient.get<{ rows: MemberRow[]; members: number }>(`/groups/${id}/members`);
  return data;
}

export async function platformNotice(id: string, title: string, body: string): Promise<void> {
  await platformClient.post(`/groups/${id}/notice`, { title, body });
}

export async function platformRotateJoinCode(id: string, reason: string): Promise<string> {
  const { data } = await platformClient.post<{ joinCode: string }>(`/groups/${id}/rotate-join-code`, { reason });
  return data.joinCode;
}

// ------------------------------------------------------------------ Msaada
export async function platformPeople(q: string): Promise<PersonRow[]> {
  const { data } = await platformClient.get<{ rows: PersonRow[] }>('/people', { params: { q } });
  return data.rows;
}

export async function platformUnlock(userId: string, reason: string): Promise<void> {
  await platformClient.post(`/people/${userId}/unlock`, { reason });
}

export async function platformIssueResetCode(userId: string, groupId: string | null, reason: string): Promise<IssuedCode> {
  const { data } = await platformClient.post<IssuedCode>(`/people/${userId}/reset-code`, { groupId, reason });
  return data;
}

/** Kiongozi (bila kuingia) anaweka neno jipya kwa code ya mara moja. */
export async function redeemResetCode(email: string, code: string, newPassword: string): Promise<string> {
  const { data } = await axios.post<{ ujumbe: string }>(`${BASE}/auth/reset-with-code`, { email, code, newPassword });
  return data.ujumbe;
}

// ------------------------------------------------------------------ Wasimamizi + 4-eyes
export async function platformAdmins(): Promise<AdminRow[]> {
  const { data } = await platformClient.get<{ rows: AdminRow[] }>('/admins');
  return data.rows;
}

export async function platformApprovals(pendingOnly = true): Promise<ApprovalRow[]> {
  const { data } = await platformClient.get<{ rows: ApprovalRow[] }>('/approvals', { params: { pendingOnly } });
  return data.rows;
}

export async function platformRequestBreakGlass(groupId: string, reason: string): Promise<{ approvalId: string; ujumbe: string }> {
  const { data } = await platformClient.post('/approvals/break-glass', { groupId, reason });
  return data;
}

export async function platformRequestAdminChange(payload: {
  action: 'GrantAdmin' | 'ChangeAdminLevel' | 'DeactivateAdmin' | 'ResetMfa';
  targetUserId?: string | null;
  targetPlatformAdminId?: string | null;
  level?: PlatformLevel | null;
  reason: string;
}): Promise<{ approvalId: string }> {
  const { data } = await platformClient.post('/approvals/admins', payload);
  return data;
}

export async function platformDecide(approvalId: string, approve: boolean, note: string): Promise<void> {
  await platformClient.post(`/approvals/${approvalId}/decide`, { approve, note });
}

export async function platformCancel(approvalId: string): Promise<void> {
  await platformClient.post(`/approvals/${approvalId}/cancel`);
}

export async function platformRequestMfaReset(platformAdminId: string, reason: string): Promise<void> {
  await platformClient.post(`/admins/${platformAdminId}/reset-mfa`, { reason });
}

export async function platformAudit(params: { groupId?: string; action?: string; take?: number } = {}): Promise<AuditRow[]> {
  const { data } = await platformClient.get<{ rows: AuditRow[] }>('/audit', { params });
  return data.rows;
}
