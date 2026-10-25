import { useState, type ReactNode } from 'react';
import { Button, Field, Modal, Textarea } from '../ui';
import { t } from '../../i18n';

/**
 * Dirisha la kuthibitisha kitendo chenye sababu (kura, kukataa, kughairi).
 * `requireReason` => kitufe kinazimwa mpaka sababu (herufi ≥3) iandikwe — inaingia kwenye audit.
 */
export function ReasonModal({
  open,
  title,
  summary,
  label,
  hint,
  requireReason,
  confirmLabel,
  variant = 'primary',
  onClose,
  onConfirm,
  children,
}: {
  open: boolean;
  title: ReactNode;
  summary?: ReactNode;
  label?: ReactNode;
  hint?: ReactNode;
  requireReason?: boolean;
  confirmLabel: ReactNode;
  variant?: 'primary' | 'good' | 'danger';
  onClose: () => void;
  onConfirm: (reason: string) => Promise<void>;
  children?: ReactNode;
}) {
  return (
    <Modal open={open} onClose={onClose} title={title}>
      {open && (
        <ReasonBody
          summary={summary}
          label={label}
          hint={hint}
          requireReason={requireReason}
          confirmLabel={confirmLabel}
          variant={variant}
          onClose={onClose}
          onConfirm={onConfirm}
        >
          {children}
        </ReasonBody>
      )}
    </Modal>
  );
}

function ReasonBody({
  summary,
  label,
  hint,
  requireReason,
  confirmLabel,
  variant,
  onClose,
  onConfirm,
  children,
}: {
  summary?: ReactNode;
  label?: ReactNode;
  hint?: ReactNode;
  requireReason?: boolean;
  confirmLabel: ReactNode;
  variant: 'primary' | 'good' | 'danger';
  onClose: () => void;
  onConfirm: (reason: string) => Promise<void>;
  children?: ReactNode;
}) {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const blocked = !!requireReason && reason.trim().length < 3;
  return (
    <div className="space-y-4">
      {summary && <div className="rounded-xl bg-ink-50 px-4 py-3 text-sm text-ink-900">{summary}</div>}
      {children}
      <Field label={label ?? (requireReason ? t('Sababu') : t('Maelezo (hiari)'))} hint={hint}>
        <Textarea value={reason} onChange={(e) => setReason(e.target.value)} />
      </Field>
      <div className="flex flex-wrap justify-end gap-2 border-t border-ink-100 pt-3">
        <Button variant="secondary" onClick={onClose} disabled={busy}>
          {t('Ghairi')}
        </Button>
        <Button
          variant={variant}
          loading={busy}
          disabled={blocked}
          onClick={async () => {
            setBusy(true);
            try {
              await onConfirm(reason.trim());
            } finally {
              setBusy(false);
            }
          }}
        >
          {confirmLabel}
        </Button>
      </div>
    </div>
  );
}

