import { t } from '../../i18n';

/**
 * Maandishi na madarasa ya console ya msimamizi (SASISHO20).
 * Hayakaa kwenye ui.tsx ili Fast Refresh ifanye kazi (faili ya components iwe na components tu).
 */

export const btnCls =
  'rounded-lg bg-ink-900 px-3 py-1.5 text-sm font-medium text-ink-50 hover:bg-ink-800 disabled:opacity-50';
export const btnGhostCls =
  'rounded-lg border border-ink-100 bg-paper-raised px-3 py-1.5 text-sm text-ink-800 hover:bg-ink-50 disabled:opacity-50';

/** Alama ya afya ya kikundi (Critical/Warning/Ok kutoka backend) → rangi + maandishi. */
export function healthTone(health: string): { cls: string; label: string } {
  const map: Record<string, { cls: string; label: string }> = {
    Critical: { cls: 'bg-standing-overdue-bg text-standing-overdue border-standing-overdue/30', label: t('Hatari') },
    Warning: { cls: 'bg-standing-warn-bg text-standing-warn border-standing-warn/30', label: t('Tahadhari') },
    Ok: { cls: 'bg-standing-good-bg text-standing-good border-standing-good/30', label: t('Sawa') },
  };
  return map[health] ?? map.Ok;
}

/** Alama za afya za backend → Kiswahili kinachoeleweka. */
export function flagLabel(flag: string): string {
  const map: Record<string, string> = {
    NoPolicy: t('Hakuna sera'),
    PolicyIncomplete: t('Sera haijakamilika'),
    NoGovernance: t('Hakuna sera ya mafao'),
    NoLeaders: t('Hakuna viongozi'),
    NoStatements: t('Hakuna taarifa ya M-Koba'),
    StaleStatements: t('Taarifa ni ya zamani'),
    ManySuspended: t('Wanachama wengi wamesimamishwa'),
    PendingApprovalsOld: t('Maamuzi ya zamani yanasubiri'),
    InvariantMismatch: t('Hesabu haitengani sawa'),
  };
  return map[flag] ?? flag;
}

/** Aina za maamuzi (backend enum) → Kiswahili. */
export function actionLabel(action: string): string {
  const map: Record<string, string> = {
    BreakGlass: t('Kuona wanachama mmoja mmoja (break-glass)'),
    GrantAdmin: t('Kuongeza msimamizi'),
    ChangeAdminLevel: t('Kubadilisha ngazi ya msimamizi'),
    DeactivateAdmin: t('Kuondoa msimamizi'),
    ResetMfa: t('MFA mpya kwa msimamizi'),
  };
  return map[action] ?? action;
}

export function fmtDate(iso: string | null): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleString(undefined, { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}
