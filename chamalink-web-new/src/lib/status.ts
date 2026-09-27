// Tafsiri ya enum za backend (Kiingereza) kwenda lugha ya mtumiaji (Kiswahili)
// na "tone" ya rangi. Faili moja ili kila skrini isome hali kwa njia ileile.
// (Lebo za alama/risk ya mikopo za mfumo wa zamani zimeondolewa pamoja na /wasifu.)
import { t, tk } from '../i18n';

export type StatusTone = 'good' | 'warn' | 'overdue' | 'neutral';

const GROUP_ROLE_LABELS: Record<string, string> = {
  Chairperson: tk('Mwenyekiti'),
  Treasurer: tk('Mtunza Hazina'),
  Secretary: tk('Katibu'),
  Member: tk('Mwanachama'),
  MemberRepresentative: tk('Mwakilishi wa Wanachama'),
};

export function groupRoleLabel(value: string): string {
  const sw = GROUP_ROLE_LABELS[value];
  return sw ? t(sw) : value;
}
