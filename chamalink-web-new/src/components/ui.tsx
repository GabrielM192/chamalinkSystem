import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type ButtonHTMLAttributes,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import { AlertTriangle, CheckCircle2, Info, Loader2, X, XCircle } from 'lucide-react';
import type { StatusTone } from '../lib/status';
import { ToastCtx } from '../lib/toast';

import { t } from '../i18n';
// Vipande vidogo vya UI vinavyotumika na skrini za Injini ya Ukweli.
// Vinatumia tokens zilezile za index.css (ink / gold / paper / standing-*)
// ili ukurasa mpya uonekane sehemu ya programu ileile.

// ---------------------------------------------------------------- Loading
export function Loading({ label = t("Inapakia...") }: { label?: string }) {
  return (
    <div className="flex items-center justify-center gap-2 py-12 text-sm text-ink-600" role="status">
      <Loader2 size={18} className="animate-spin" />
      {label}
    </div>
  );
}

export function Skeleton({ className = '' }: { className?: string }) {
  return <div className={`animate-pulse rounded-lg bg-ink-100/70 ${className}`} />;
}

// ---------------------------------------------------------------- Notice
const NOTICE: Record<StatusTone, { cls: string; Icon: typeof Info }> = {
  good: { cls: 'bg-standing-good-bg text-standing-good border-standing-good/20', Icon: CheckCircle2 },
  warn: { cls: 'bg-standing-warn-bg text-standing-warn border-standing-warn/20', Icon: AlertTriangle },
  overdue: { cls: 'bg-standing-overdue-bg text-standing-overdue border-standing-overdue/20', Icon: XCircle },
  neutral: { cls: 'bg-ink-50 text-ink-700 border-ink-100', Icon: Info },
};

export function Notice({
  tone = 'neutral',
  title,
  children,
  action,
}: {
  tone?: StatusTone;
  title?: ReactNode;
  children?: ReactNode;
  action?: ReactNode;
}) {
  const { cls, Icon } = NOTICE[tone];
  return (
    <div className={`flex flex-wrap gap-x-3 gap-y-2 rounded-xl border px-4 py-3 text-sm sm:flex-nowrap ${cls}`} role={tone === 'overdue' ? 'alert' : undefined}>
      <Icon size={18} className="mt-0.5 shrink-0" />
      <div className="min-w-0 flex-1">
        {title && <div className="font-semibold">{title}</div>}
        {children && <div className={title ? 'mt-0.5 opacity-90' : ''}>{children}</div>}
      </div>
      {/* Simu: kitendo kinashuka chini ya maandishi (hakibani maandishi pembeni) */}
      {action && <div className="w-full pl-[30px] sm:w-auto sm:shrink-0 sm:self-center sm:pl-0">{action}</div>}
    </div>
  );
}

export function EmptyState({ icon: Icon, title, children }: { icon: typeof Info; title: string; children?: ReactNode }) {
  return (
    <div className="rounded-2xl border border-dashed border-ink-100 bg-paper-raised px-6 py-10 text-center">
      <Icon size={28} className="mx-auto text-ink-500" />
      <div className="mt-3 font-semibold text-ink-900">{title}</div>
      {children && <div className="mx-auto mt-1 max-w-md text-sm text-ink-600">{children}</div>}
    </div>
  );
}

// ---------------------------------------------------------------- Card
export function Card({
  title,
  subtitle,
  action,
  children,
  className = '',
  bodyClassName = 'p-4 sm:p-5',
}: {
  title?: ReactNode;
  subtitle?: ReactNode;
  action?: ReactNode;
  children: ReactNode;
  className?: string;
  bodyClassName?: string;
}) {
  return (
    <section className={`rounded-xl border border-ink-100 bg-paper-raised shadow-[0_1px_2px_rgb(15_27_48/0.05)] ${className}`}>
      {(title || action) && (
        <header className="flex flex-wrap items-start justify-between gap-3 border-b border-ink-100 px-4 py-3 sm:px-5">
          <div className="min-w-0">
            {title && <h2 className="font-semibold text-ink-950">{title}</h2>}
            {subtitle && <p className="mt-0.5 text-xs text-ink-600">{subtitle}</p>}
          </div>
          {action}
        </header>
      )}
      <div className={bodyClassName}>{children}</div>
    </section>
  );
}

export function Stat({
  label,
  value,
  hint,
  tone = 'neutral',
}: {
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  tone?: StatusTone;
}) {
  const color =
    tone === 'good' ? 'text-standing-good' : tone === 'warn' ? 'text-standing-warn' : tone === 'overdue' ? 'text-standing-overdue' : 'text-ink-950';
  return (
    <div className="min-w-0 rounded-xl border border-ink-100 bg-paper-raised px-3 py-3 sm:px-4">
      <div className="text-[11px] font-semibold uppercase tracking-wide text-ink-600">{label}</div>
      <div className={`figure mt-1 whitespace-nowrap text-[15px] font-semibold min-[420px]:text-lg sm:text-xl ${color}`}>{value}</div>
      {hint && <div className="mt-0.5 text-xs text-ink-600">{hint}</div>}
    </div>
  );
}

// ---------------------------------------------------------------- Button
type Variant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'good';
const BTN: Record<Variant, string> = {
  primary: 'bg-ink-900 text-ink-50 hover:bg-ink-800 disabled:bg-ink-500',
  secondary: 'border border-ink-100 bg-paper-raised text-ink-900 hover:bg-ink-50',
  ghost: 'text-ink-700 hover:bg-ink-50',
  danger: 'bg-standing-overdue text-on-accent hover:opacity-90',
  good: 'bg-standing-good text-on-accent hover:opacity-90',
};

export function Button({
  variant = 'primary',
  size = 'md',
  loading,
  icon: Icon,
  children,
  className = '',
  disabled,
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: Variant;
  size?: 'sm' | 'md';
  loading?: boolean;
  icon?: typeof Info;
}) {
  const sz = size === 'sm' ? 'px-2.5 py-1.5 text-xs' : 'px-4 py-2.5 text-sm';
  return (
    <button
      {...rest}
      disabled={disabled || loading}
      className={`inline-flex items-center justify-center gap-1.5 rounded-lg font-medium transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-gold-500 disabled:cursor-not-allowed disabled:opacity-60 ${sz} ${BTN[variant]} ${className}`}
    >
      {loading ? <Loader2 size={size === 'sm' ? 13 : 15} className="animate-spin" /> : Icon ? <Icon size={size === 'sm' ? 13 : 15} /> : null}
      {children}
    </button>
  );
}

// ---------------------------------------------------------------- Form fields
export const inputCls =
  'w-full rounded-lg border border-ink-100 bg-paper-raised px-3 py-2.5 text-sm text-ink-950 placeholder:text-ink-500/70 focus:border-ink-500 focus:outline-none focus:ring-2 focus:ring-gold-500/40 disabled:bg-ink-50';

export function Field({
  label,
  hint,
  error,
  children,
  htmlFor,
}: {
  label: ReactNode;
  hint?: ReactNode;
  error?: ReactNode;
  children: ReactNode;
  htmlFor?: string;
}) {
  return (
    <div className="space-y-1.5">
      <label htmlFor={htmlFor} className="block text-sm font-medium text-ink-900">
        {label}
      </label>
      {children}
      {error ? (
        <p className="text-xs text-standing-overdue">{error}</p>
      ) : hint ? (
        <p className="text-xs text-ink-600">{hint}</p>
      ) : null}
    </div>
  );
}

export function Input(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={`${inputCls} ${props.className ?? ''}`} />;
}
export function Select(props: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select {...props} className={`${inputCls} ${props.className ?? ''}`} />;
}
export function Textarea(props: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea rows={3} {...props} className={`${inputCls} resize-y ${props.className ?? ''}`} />;
}

/** Kiasi cha fedha: kinaonyesha koma (10,000) wakati wa kuandika, kinarudisha namba. */
export function MoneyInput({
  value,
  onChange,
  id,
  placeholder = '0',
  quick,
}: {
  value: number | null;
  onChange: (v: number | null) => void;
  id?: string;
  placeholder?: string;
  quick?: number[];
}) {
  return (
    <div className="space-y-2">
      <div className="relative">
        <span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-xs font-semibold text-ink-600">TSH</span>
        <input
          id={id}
          inputMode="numeric"
          autoComplete="off"
          placeholder={placeholder}
          value={value == null ? '' : value.toLocaleString('en-US')}
          onChange={(e) => {
            const digits = e.target.value.replace(/\D/g, '').slice(0, 10);
            onChange(digits ? Number(digits) : null);
          }}
          className={`${inputCls} figure pl-12 text-base`}
        />
      </div>
      {quick && quick.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {quick.map((q) => (
            <button
              key={q}
              type="button"
              onClick={() => onChange(q)}
              className={`figure rounded-full border px-2.5 py-1 text-xs transition-colors ${
                value === q ? 'border-ink-800 bg-ink-800 text-ink-50' : 'border-ink-100 bg-paper-raised text-ink-700 hover:border-ink-500'
              }`}
            >
              {q.toLocaleString('en-US')}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

export function Segmented<T extends string>({
  value,
  onChange,
  options,
  stack = false,
}: {
  value: T;
  onChange: (v: T) => void;
  options: { value: T; label: ReactNode; hint?: ReactNode }[];
  /** Simu: chaguo moja kwa mstari (maneno marefu kama "Mwanachama" hayakatiki); skrini pana: safu moja. */
  stack?: boolean;
}) {
  const cols = { '--seg-n': options.length } as React.CSSProperties;
  return (
    <div
      className={`grid gap-2 ${
        stack ? 'grid-cols-1 sm:[grid-template-columns:repeat(var(--seg-n),minmax(0,1fr))]' : '[grid-template-columns:repeat(var(--seg-n),minmax(0,1fr))]'
      }`}
      style={cols}
      role="radiogroup"
    >
      {options.map((o) => {
        const active = o.value === value;
        return (
          <button
            key={o.value}
            type="button"
            role="radio"
            aria-checked={active}
            onClick={() => onChange(o.value)}
            className={`rounded-xl border px-3 py-2.5 text-left transition-colors ${
              active ? 'border-ink-800 bg-ink-900 text-ink-50' : 'border-ink-100 bg-paper-raised text-ink-900 hover:border-ink-500'
            }`}
          >
            <div className="text-sm font-semibold">{o.label}</div>
            {o.hint && <div className={`mt-0.5 text-xs ${active ? 'text-ink-100/80' : 'text-ink-600'}`}>{o.hint}</div>}
          </button>
        );
      })}
    </div>
  );
}

// ---------------------------------------------------------------- Drawer / Modal
function useLockScroll(open: boolean) {
  useEffect(() => {
    if (!open) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = prev;
    };
  }, [open]);
}

function useEsc(open: boolean, onClose: () => void) {
  useEffect(() => {
    if (!open) return;
    const h = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, [open, onClose]);
}

/** Paneli ya pembeni (desktop) / skrini nzima kutoka chini (simu). */
export function Drawer({
  open,
  onClose,
  title,
  subtitle,
  children,
  wide,
}: {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  subtitle?: ReactNode;
  children: ReactNode;
  wide?: boolean;
}) {
  useLockScroll(open);
  useEsc(open, onClose);
  return (
    <AnimatePresence>
      {open && (
        <div className="fixed inset-0 z-50" role="dialog" aria-modal="true">
          <motion.div
            className="absolute inset-0 bg-overlay"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={onClose}
          />
          <motion.div
            className={`absolute inset-x-0 bottom-0 top-8 flex flex-col rounded-t-2xl bg-paper sm:inset-y-0 sm:left-auto sm:right-0 sm:top-0 sm:rounded-none sm:rounded-l-2xl ${
              wide ? 'sm:w-[min(900px,92vw)]' : 'sm:w-[min(560px,92vw)]'
            }`}
            initial={{ y: 40, opacity: 0 }}
            animate={{ y: 0, opacity: 1 }}
            exit={{ y: 40, opacity: 0 }}
            transition={{ type: 'spring', damping: 30, stiffness: 320 }}
          >
            <header className="flex items-start justify-between gap-3 border-b border-ink-100 bg-paper-raised px-4 py-3 sm:rounded-tl-2xl sm:px-6 rounded-t-2xl sm:rounded-tr-none">
              <div className="min-w-0">
                <h2 className="truncate text-base font-semibold text-ink-950">{title}</h2>
                {subtitle && <div className="mt-0.5 text-xs text-ink-600">{subtitle}</div>}
              </div>
              <button onClick={onClose} className="rounded-lg p-1.5 text-ink-600 hover:bg-ink-50" aria-label={t("Funga")}>
                <X size={18} />
              </button>
            </header>
            <div className="flex-1 overflow-y-auto px-4 py-4 sm:px-6">{children}</div>
          </motion.div>
        </div>
      )}
    </AnimatePresence>
  );
}

export function Modal({
  open,
  onClose,
  title,
  children,
  footer,
}: {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
}) {
  useLockScroll(open);
  useEsc(open, onClose);
  return (
    <AnimatePresence>
      {open && (
        <div className="fixed inset-0 z-50 flex items-end justify-center p-0 sm:items-center sm:p-4" role="dialog" aria-modal="true">
          <motion.div className="absolute inset-0 bg-overlay" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} onClick={onClose} />
          <motion.div
            className="relative w-full max-w-lg rounded-t-2xl bg-paper-raised shadow-xl sm:rounded-2xl"
            initial={{ y: 24, opacity: 0 }}
            animate={{ y: 0, opacity: 1 }}
            exit={{ y: 24, opacity: 0 }}
          >
            <header className="flex items-center justify-between border-b border-ink-100 px-5 py-3">
              <h2 className="font-semibold text-ink-950">{title}</h2>
              <button onClick={onClose} className="rounded-lg p-1.5 text-ink-600 hover:bg-ink-50" aria-label={t("Funga")}>
                <X size={18} />
              </button>
            </header>
            <div className="max-h-[70vh] overflow-y-auto px-5 py-4">{children}</div>
            {footer && <footer className="flex flex-wrap justify-end gap-2 border-t border-ink-100 px-5 py-3">{footer}</footer>}
          </motion.div>
        </div>
      )}
    </AnimatePresence>
  );
}

// ---------------------------------------------------------------- Decision (approve / reject)
/**
 * Dirisha la uamuzi (maker-checker). Kukataa kunahitaji sababu; kuidhinisha
 * kunaruhusu maelezo ya hiari.
 */
export function DecisionModal({
  open,
  approve,
  onClose,
  onConfirm,
  summary,
}: {
  open: boolean;
  approve: boolean;
  onClose: () => void;
  onConfirm: (note: string) => Promise<void>;
  summary: ReactNode;
}) {
  return (
    <Modal open={open} onClose={onClose} title={approve ? t("Thibitisha idhini") : t("Kataa ombi")}>
      {/* Mwili unapachikwa upya kila dirisha linapofunguliwa => maelezo huanza tupu */}
      <DecisionBody approve={approve} onClose={onClose} onConfirm={onConfirm} summary={summary} />
    </Modal>
  );
}

function DecisionBody({
  approve,
  onClose,
  onConfirm,
  summary,
}: {
  approve: boolean;
  onClose: () => void;
  onConfirm: (note: string) => Promise<void>;
  summary: ReactNode;
}) {
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const needNote = !approve && note.trim().length < 3;
  return (
    <div className="space-y-4">
      <div className="rounded-xl bg-ink-50 px-4 py-3 text-sm text-ink-900">{summary}</div>
      <Field
        label={approve ? t("Maelezo (hiari)") : t("Sababu ya kukataa")}
        hint={approve ? t("Mf. \"Nimeona risiti Na. 12\"") : t("Lazima — itaonekana kwenye kumbukumbu za ukaguzi.")}
      >
        <Textarea value={note} onChange={(e) => setNote(e.target.value)} placeholder={approve ? '' : t("Mf. Tarehe si sahihi")} />
      </Field>
      {approve && (
        <p className="text-xs text-ink-600">{t("Ukiidhinisha, taarifa za mwanachama zitahesabiwa upya mara moja kwa kujumuisha hili.")}</p>
      )}
      <div className="flex flex-wrap justify-end gap-2 border-t border-ink-100 pt-3">
        <Button variant="secondary" onClick={onClose} disabled={busy}>
          {t("Ghairi")}
        </Button>
        <Button
          variant={approve ? 'good' : 'danger'}
          loading={busy}
          disabled={needNote}
          onClick={async () => {
            setBusy(true);
            try {
              await onConfirm(note.trim());
            } finally {
              setBusy(false);
            }
          }}
        >
          {approve ? t("Idhinisha") : t("Kataa")}
        </Button>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------- Toasts
interface Toast {
  id: number;
  tone: StatusTone;
  text: string;
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const seq = useRef(0);
  const push = useCallback((text: string, tone: StatusTone = 'good') => {
    const id = ++seq.current;
    setToasts((list) => [...list, { id, tone, text }]);
    window.setTimeout(() => setToasts((list) => list.filter((x) => x.id !== id)), tone === 'overdue' ? 7000 : 4000);
  }, []);
  return (
    <ToastCtx.Provider value={push}>
      {children}
      <div className="pointer-events-none fixed inset-x-0 bottom-4 z-[60] flex flex-col items-center gap-2 px-4 sm:bottom-6 sm:right-6 sm:left-auto sm:items-end" aria-live="polite">
        <AnimatePresence>
          {toasts.map((toast) => {
            const { cls, Icon } = NOTICE[toast.tone];
            return (
              <motion.div
                key={toast.id}
                initial={{ opacity: 0, y: 12 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: 12 }}
                className={`pointer-events-auto flex max-w-sm items-start gap-2 rounded-xl border px-4 py-3 text-sm shadow-lg ${cls}`}
              >
                <Icon size={16} className="mt-0.5 shrink-0" />
                <span>{toast.text}</span>
              </motion.div>
            );
          })}
        </AnimatePresence>
      </div>
    </ToastCtx.Provider>
  );
}
