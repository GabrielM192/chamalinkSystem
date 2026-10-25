import { apiClient } from './client';

// =====================================================================
// SASISHO18 — POCHI ZA VIKUNDI VYA "KILA TUKIO"
//
// Mwanachama ana salio lake ndani ya kikundi. Tukio linapoidhinishwa,
// mchango unakatwa — lakini salio halishuki chini ya "kiwango funali"
// (floor). Kinachobaki ni deni linalokamilishwa kwa amana zijazo.
//
// Namba hizi zinatoka seva (Injini ya Ukweli). Frontend haihesabu
// kiasi cha fedha yenyewe — inaonyesha tu kile seva inachorudisha.
// =====================================================================

const base = '/events';

export interface EventObligation {
  obligationId: string;
  caseId: string;
  caseNumber: string | null;
  createdAt: string;
  dueDate: string | null;
  requiredAmount: number;
  deductedAmount: number;
  outstandingAmount: number;
  status: 'Pending' | 'Settled' | 'Waived' | 'Exempted';
  overdue: boolean;
}

export interface EventWallet {
  groupId: string;
  memberId: string;
  memberNumber: string;
  balance: number;
  /** Kiwango cha chini ambacho salio halishuki chini yake (sera ya kikundi) */
  floor: number;
  /** Kinachopatikana juu ya floor — hiki ndicho kinachokatwa na tukio lijalo */
  available: number;
  /** Jumla ya deni la matukio yaliyopita */
  outstanding: number;
  obligations: EventObligation[];
}

export interface OutstandingRow {
  memberId: string;
  memberNumber: string | null;
  outstanding: number;
  events: number;
  balance: number;
}

/** Pochi yangu (mwanachama yeyote). */
export async function getMyWallet(groupId: string): Promise<EventWallet> {
  return (await apiClient.get<EventWallet>(`${base}/${groupId}/wallet/me`)).data;
}

/** Pochi ya mwanachama yeyote (viongozi). */
export async function getMemberWallet(groupId: string, memberId: string): Promise<EventWallet> {
  return (await apiClient.get<EventWallet>(`${base}/${groupId}/wallet/${memberId}`)).data;
}

/** Wanachama wote walio na deni la matukio (viongozi). */
export async function listOutstanding(groupId: string): Promise<OutstandingRow[]> {
  return (await apiClient.get<OutstandingRow[]>(`${base}/${groupId}/wallet/outstanding`)).data;
}

/** Tumia amana zilizopo kulipa deni la zamani (viongozi). */
export async function sweepWallet(groupId: string, memberId: string): Promise<{ applied: number; message: string }> {
  return (await apiClient.post<{ applied: number; message: string }>(`${base}/${groupId}/wallet/${memberId}/sweep`)).data;
}

// =====================================================================
// SASISHO19 — HATARINI KUONDOLEWA + KURUDI
//
// Mwanachama anaingia orodha ya "hatarini kuondolewa" pale deni la tukio
// linapobaki baada ya faini na siku za neema za kikundi. Orodha inaonekana
// kwa WANACHAMA WOTE. Kuondolewa kunaanzishwa na kiongozi na kunahitaji
// zaidi ya viongozi wawili (>=3) kuidhinisha — seva inasimamisha kanuni hiyo.
// =====================================================================

export interface AtRiskRow {
  memberId: string;
  memberNumber: string | null;
  name: string | null;
  /** Deni la matukio lililobaki */
  outstanding: number;
  /** Faini ya sera iliyotozwa kwenye deni hilo */
  fineCharged: number;
  /** Pengo la kufikia kiwango cha chini (floor) */
  floorShortfall: number;
  balance: number;
  floor: number;
  oldestDueDate: string | null;
  daysOverdue: number;
  pendingEvents: number;
  /** Idadi ya idhini zinazohitajika kama kuna ombi la kuondolewa linalosubiri */
  pendingRemovalApprovals: number | null;
}

/** Orodha ya hatarini — inaruhusiwa kwa mwanachama YEYOTE hai wa kikundi. */
export async function listAtRisk(groupId: string): Promise<AtRiskRow[]> {
  const r = await apiClient.get<{ rows: AtRiskRow[]; members: number }>(`${base}/${groupId}/at-risk`);
  return r.data.rows;
}

/** Kiongozi anaomba kuondolewa (kura za viongozi ZAIDI ya wawili zinahitajika). */
export async function proposeRemoval(
  groupId: string,
  memberId: string,
  reason: string,
): Promise<{ requestId: string; requiredApprovals: number; ujumbe: string }> {
  return (
    await apiClient.post<{ requestId: string; requiredApprovals: number; ujumbe: string }>(
      `${base}/${groupId}/at-risk/${memberId}/remove`,
      { reason },
    )
  ).data;
}

export interface ReinstatementCost {
  outstandingDebt: number;
  fineMultiplier: number;
  fineBase: number;
  reinstatementFine: number;
  joiningFee: number;
  joiningFeeRequired: boolean;
  total: number;
}

/** Gharama ya kurudi kwa mwanachama aliyeondolewa (mhusika au viongozi). */
export async function getReinstatement(groupId: string, memberId: string): Promise<ReinstatementCost> {
  return (await apiClient.get<ReinstatementCost>(`${base}/${groupId}/at-risk/${memberId}/reinstatement`)).data;
}

// =====================================================================
// SASISHO21 — KODI YA MALIPO, DENI KAMILI, NA MATUKIO YA KIKUNDI
//
// Kila kitu kinatoka seva. Ukurasa hauhesabu kiasi cha fedha.
// =====================================================================

/** Mstari mmoja wa deni: deni + faini + akiba + kiasi cha kurejesha. */
export interface OutstandingDetailRow {
  memberId: string;
  memberNumber: string | null;
  name: string | null;
  outstanding: number;
  fines: number;
  balance: number;
  floorShortfall: number;
  /** Deni + pengo la akiba — "nirejeshe ngapi ili nirudi sawa?" */
  restoreAmount: number;
  daysOverdue: number;
  events: number;
}

/** Orodha kamili ya deni (viongozi). */
export async function listOutstandingDetailed(groupId: string): Promise<OutstandingDetailRow[]> {
  return (await apiClient.get<OutstandingDetailRow[]>(`${base}/${groupId}/wallet/outstanding`)).data;
}

/** CSV ya deni (Excel inafungua moja kwa moja). */
export async function downloadOutstandingCsv(groupId: string): Promise<Blob> {
  const r = await apiClient.get(`${base}/${groupId}/wallet/outstanding`, {
    params: { format: 'csv' },
    responseType: 'blob',
  });
  return r.data as Blob;
}

/** Matukio yote ya kikundi + HALI YAKE MWENYEWE kwa kila tukio. */
export interface GroupEventRow {
  caseId: string;
  caseNumber: string | null;
  eventCode: string | null;
  category: string;
  relationship: string;
  subjectName: string | null;
  eventDate: string;
  perMemberAmount: number;
  beneficiaryMemberNumber: string | null;
  dueDate: string | null;
  /** Imelipwa · Hujalipa · Imesamehewa · Hukuhusika */
  myStatus: string;
  myOutstanding: number;
  paymentCode: string | null;
}

export interface GroupEventsResponse {
  groupId: string;
  isEventBased: boolean;
  now: string;
  rows: GroupEventRow[];
  jumla: { matukio: number; nayodaiwa: number; yaliyolipwa: number };
}

export async function listGroupEvents(groupId: string): Promise<GroupEventsResponse> {
  return (await apiClient.get<GroupEventsResponse>(`${base}/${groupId}/group-events`)).data;
}
