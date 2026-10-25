// Lebo za mafao/kura (enum za backend -> maandishi ya mtumiaji) + helpers ndogo. Hakuna component hapa.
import type { StatusTone } from './status';
import type { CaseStatus, Category, EvidenceType, ExplanationKind, HeadcountRule, LeaderRole, RelationType, Relationship } from '../api/benefits';
import { getLang, t, tk } from '../i18n';
import { locText, tArg } from './loc';

// ---------------------------------------------------------------- Lebo
const CASE_STATUS: Record<CaseStatus, { label: string; tone: StatusTone }> = {
  AwaitingEvidence: { label: tk('Inasubiri cheti'), tone: 'warn' },
  AwaitingVerification: { label: tk('Cheti kinahakikiwa'), tone: 'warn' },
  Voting: { label: tk('Kura zinaendelea'), tone: 'warn' },
  Approved: { label: tk('Imeidhinishwa — inasubiri malipo'), tone: 'warn' },
  PaymentInitiated: { label: tk('Malipo yameanzishwa — yanasubiri uthibitisho'), tone: 'warn' },
  Paid: { label: tk('Imelipwa'), tone: 'good' },
  Rejected: { label: tk('Imekataliwa'), tone: 'overdue' },
  Cancelled: { label: tk('Imeghairiwa'), tone: 'neutral' },
  HistoricalPendingConfirmation: { label: tk('Ya zamani — inasubiri uthibitisho'), tone: 'warn' },
  AwaitingFunds: { label: tk('Imeidhinishwa — inasubiri pesa M-Koba'), tone: 'overdue' },
};

export function caseStatus(s: CaseStatus) {
  const x = CASE_STATUS[s] ?? { label: s, tone: 'neutral' as StatusTone };
  return { label: t(x.label), tone: x.tone };
}

const CATEGORY: Record<Category, string> = {
  Msiba: tk('Msiba'),
  Sherehe: tk('Sherehe'),
  JobLoss: tk('Kufukuzwa kazi'),
};
export const categoryLabel = (c: Category | string) => t(CATEGORY[c as Category] ?? c);

const RELATIONSHIP: Record<Relationship, string> = {
  Self: tk('Mwanachama mwenyewe'),
  Spouse: tk('Mwenza (mume/mke)'),
  Child: tk('Mtoto'),
  Parent: tk('Mzazi'),
  InLaw: tk('Mkwe / mkoi (familia ya mwenzi)'),
  Guardian: tk('Mlezi'),
};
export const relationshipLabel = (r: Relationship | string) => t(RELATIONSHIP[r as Relationship] ?? r);

const EVIDENCE: Record<EvidenceType, string> = {
  DeathCertificate: tk('Cheti cha kifo'),
  BurialPermit: tk('Kibali cha mazishi'),
  LocalGovernmentLetter: tk('Barua ya serikali ya mtaa'),
  MarriageCertificate: tk('Cheti cha ndoa'),
  BirthCertificate: tk('Cheti cha kuzaliwa'),
  HospitalLetter: tk('Barua ya hospitali'),
  Other: tk('Hati nyingine'),
  PaymentReceipt: tk('Risiti / uthibitisho wa malipo ya taslimu'),
};
export const evidenceLabel = (e: EvidenceType | string) => t(EVIDENCE[e as EvidenceType] ?? e);

const HEADCOUNT: Record<HeadcountRule, string> = {
  AllActiveMembers: tk('Wanachama wote walio hai'),
  MembersWithNoArrears: tk('Wasio na deni la michango'),
  MembersPaidWithinDays: tk('Waliolipa ndani ya siku N'),
};
export const headcountLabel = (h: HeadcountRule | string) => t(HEADCOUNT[h as HeadcountRule] ?? h);

const KIND: Record<ExplanationKind, string> = {
  LoanDisbursement: tk('Mkopo'),
  GroupExpense: tk('Gharama ya kikundi'),
  BankTransfer: tk('Uhamisho benki'),
  Refund: tk('Marejesho'),
  Other: tk('Mengineyo'),
};
export const kindLabel = (k: ExplanationKind | string) => t(KIND[k as ExplanationKind] ?? k);

const MEMBER_STATUS: Record<string, string> = {
  New: tk('Mpya'),
  Active: tk('Hai'),
  Warning: tk('Onyo'),
  EligibleForExpulsion: tk('Anastahili kufukuzwa'),
  Inactive: tk('Hayuko hai'),
  Suspended: tk('Amesimamishwa'),
  Exited: tk('Ametoka'),
  Deceased: tk('Amefariki'),
  Archived: tk('Kumbukumbu'),
};
export const memberStatusLabel = (s: string) => t(MEMBER_STATUS[s] ?? s);

/** Leo kwa saa za EAT (yyyy-mm-dd) — kwa <input type="date">. */
export { todayEat as todayIso } from './truth';
import { num } from './truth';

/** Pakua Blob kama faili (vyeti / CSV) — token inatumwa na axios, si kwa link ya moja kwa moja. */
export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 30_000);
}

/**
 * Tahadhari za seva zinatafsiriwa kwa `code` + `args` (seva inarudisha Kiswahili kama akiba).
 * Code isiyojulikana → ujumbe wa seva kama ulivyo.
 */
const ALERT_TEXT: Record<string, string> = {
  UNEXPLAINED_WITHDRAWALS: tk('Utoaji {count} wa M-Koba wa jumla {total} haujaelezwa na kesi ya mafao wala maelezo yaliyoidhinishwa: {list}.'),
  PAYMENT_NOT_CONFIRMED: tk('{case}: imeidhinishwa {date} ({amount}) lakini malipo hayajaonekana M-Koba ndani ya siku {days}.'),
  VOTING_EXPIRED: tk('{case}: muda wa kura umeisha bila uamuzi.'),
  EVIDENCE_WAITING: tk('Kesi {count} zinasubiri ushahidi kuhakikiwa.'),
  HISTORICAL_WAITING: tk('Kesi {count} za zamani zinasubiri kiongozi wa pili kuthibitisha.'),
  VOTING_OPEN: tk('Kesi {count} ziko kwenye kura.'),
  PHONES_WAITING: tk('Maombi {count} ya namba za simu yanasubiri kiongozi wa pili.'),
  POLICY_WAITING: tk('Mapendekezo {count} ya sera/viwango vya mafao yanasubiri idhini.'),
  STATUS_WAITING: tk('Maombi {count} ya kubadilisha hali ya wanachama yanasubiri kura.'),
  NO_GOVERNANCE: tk('Sera ya mafao na idhini haijawekwa — mafao hayawezi kushughulikiwa.'),
  AWAITING_FUNDS: tk('{case}: imeidhinishwa lakini inasubiri pesa ({amount}) — salio la M-Koba halitoshi.'),
};
const MONEY_ARGS = new Set(['total', 'amount']);
export function alertText(a: { code: string; message: string; args?: Record<string, string> | null; tpl?: string | null; tArgs?: string[] | null }): string {
  const tpl = ALERT_TEXT[a.code];
  if (!tpl) return locText(a.message, a.tpl, a.tArgs);
  const args: Record<string, string> = {};
  for (const [k, v] of Object.entries(a.args ?? {})) args[k] = MONEY_ARGS.has(k) && !Number.isNaN(Number(v)) ? num(Number(v)) : v;
  // Seva ya zamani (bila args): tumia ujumbe wake kama ulivyo
  if (/\{\w+\}/.test(tpl) && Object.keys(args).length === 0) return a.message;
  return t(tpl, args);
}

// ---------------------------------------------------------------- SASISHO13
const ROLE: Record<LeaderRole, string> = { Chairperson: tk('Mwenyekiti'), Treasurer: tk('Mtunza-hazina'), Secretary: tk('Katibu') };
export const roleLabel = (r: LeaderRole | string) => t(ROLE[r as LeaderRole] ?? r);

const RELATION: Record<RelationType, string> = {
  Spouse: tk('Mwenza (mume/mke)'),
  Parent: tk('Mzazi'),
  Child: tk('Mtoto'),
  Sibling: tk('Ndugu (kaka/dada)'),
  Household: tk('Kaya moja'),
  BusinessPartner: tk('Mbia wa biashara'),
  Other: tk('Uhusiano mwingine'),
};
export const relationLabel = (r: RelationType | string) => t(RELATION[r as RelationType] ?? r);

const LEDGER_KIND: Record<string, string> = {
  Approved: tk('Mafao yaliyoidhinishwa (jumla)'),
  JoinFeeOffset: tk('Kianzio kilichokatwa'),
  ArrearsOffset: tk('Deni la michango lililokatwa'),
  LoanArrearsOffset: tk('Deni la mkopo lililokatwa'),
  CashPaid: tk('Pesa iliyolipwa'),
};
export const ledgerKindLabel = (k: string) => t(LEDGER_KIND[k] ?? k);

/** Kichwa kifupi cha kila bendera ya hatari (maelezo kamili yanatoka kwa seva). */
const RISK_TITLE: Record<string, string> = {
  AUDIT_CHAIN_BROKEN: tk('Kumbukumbu za ukaguzi zimeharibiwa'),
  SUBJECT_MULTI_CLAIM: tk('Mhusika mmoja anadaiwa na wanachama wawili'),
  FREQUENT_CLAIMS: tk('Madai mengi kwa mwanachama mmoja'),
  APPROVER_IS_PARTY: tk('Aliyeidhinisha ni mnufaika/aliyefungua kesi'),
  APPROVER_RELATED: tk('Aliyeidhinisha ni ndugu wa mnufaika'),
  VERIFIER_IS_UPLOADER: tk('Aliyehakiki cheti ndiye aliyekipakia'),
  CONFIRMER_IS_INITIATOR: tk('Mweka-malipo ndiye mthibitishaji'),
  PAYMENT_BEFORE_APPROVAL: tk('Malipo kabla ya idhini'),
  PAYMENT_AMOUNT_MISMATCH: tk('Kiasi cha malipo si sawa na kilichoidhinishwa'),
  PAYMENT_TO_OTHER_PHONE: tk('Malipo kwa namba isiyo ya mnufaika'),
  CANCELLED_AFTER_INITIATION: tk('Kesi imeghairiwa baada ya malipo kuanzishwa'),
  POLICY_CHANGED_BEFORE_CLAIM: tk('Sera ilibadilika muda mfupi kabla ya dai'),
  CASH_INSUFFICIENT: tk('Pesa ya M-Koba haitoshi'),
  MANUAL_AFTER_CUTOFF: tk('Kumbukumbu za mkononi baada ya tarehe ya mwisho'),
  UNEXPLAINED_WITHDRAWALS: tk('Utoaji wa M-Koba usioelezwa'),
  PAYMENT_NOT_CONFIRMED: tk('Malipo yaliyoidhinishwa hayajaonekana M-Koba'),
  LEDGER_MISMATCH: tk('Maandishi ya fedha hayalingani'),
  BLOCKED_DUPLICATE_FILE: tk('Jaribio: cheti kilekile mara mbili'),
  BLOCKED_DUPLICATE_DOCUMENT: tk('Jaribio: namba ya cheti imerudiwa'),
  BLOCKED_CONFIRM_BEFORE_INITIATION: tk('Jaribio: kuthibitisha kabla ya kuanzisha malipo'),
  BLOCKED_CONFIRMER_IS_INITIATOR: tk('Jaribio: mweka-malipo kujithibitishia'),
  BLOCKED_PAYMENT_AMOUNT_MISMATCH: tk('Jaribio: kiasi tofauti na kilichoidhinishwa'),
  BLOCKED_PAYMENT_BEFORE_APPROVAL: tk('Jaribio: malipo ya kabla ya idhini'),
  BLOCKED_CASH_INSUFFICIENT: tk('Jaribio: kuanzisha malipo bila pesa ya kutosha'),
  BLOCKED_EVIDENCE_TAMPERED: tk('Faili la cheti limebadilishwa'),
  AWAITING_FUNDS: tk('Imeidhinishwa lakini pesa haitoshi'),
};

// ---------------------------------------------------------------- SASISHO14
const ANY_ROLE_LABEL: Record<string, string> = {
  Chairperson: tk('Mwenyekiti'),
  Treasurer: tk('Mtunza-hazina'),
  Secretary: tk('Katibu'),
  Member: tk('Mwanachama'),
};
export const anyRoleLabel = (r: string) => t(ANY_ROLE_LABEL[r] ?? r);

const HEADCOUNT_AS_OF_LABEL: Record<string, string> = {
  EventDate: tk('Siku ya tukio'),
  ClaimDate: tk('Siku dai lilipofunguliwa'),
  VerificationDate: tk('Siku ya uhakiki wa ushahidi'),
};
export const headcountAsOfLabel = (h: string) => t(HEADCOUNT_AS_OF_LABEL[h] ?? h);

const CHANNEL_LABEL: Record<string, string> = { Mkoba: tk('M-Koba'), Cash: tk('Taslimu (ya zamani)') };
export const channelLabel = (c: string) => t(CHANNEL_LABEL[c] ?? c);
export const riskTitle = (code: string) => (RISK_TITLE[code] ? t(RISK_TITLE[code]) : code);

/** "Kanuni ya idadi" iliyogandishwa kwenye kesi (maandishi ya seva) — sehemu zinazojulikana zinatafsiriwa. */
export function headcountRuleText(s: string): string {
  if (getLang() === 'sw') return s;
  return s
    .split(' — ')
    .map((part) => {
      for (const sfx of [' (bila mnufaika)', ' (pamoja na mnufaika)']) {
        if (part.endsWith(sfx)) return tArg(part.slice(0, -sfx.length)) + t(sfx);
      }
      const m = part.match(/^(.*) (\d{2}\/\d{2}\/\d{4})$/);
      if (m) return `${tArg(m[1])} ${m[2]}`;
      return tArg(part);
    })
    .join(' — ');
}

/** Sababu ya kufunga kesi: kiambishi cha seva kinatafsiriwa; maneno ya viongozi yanabaki kama yalivyo. */
export function closedReasonText(s: string | null): string {
  if (!s) return '';
  if (getLang() === 'sw') return s;
  for (const pre of ['Imekataliwa kwa kura: ']) if (s.startsWith(pre)) return t(pre) + s.slice(pre.length);
  return locText(s);
}
