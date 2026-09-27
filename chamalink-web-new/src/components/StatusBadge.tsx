import type { StatusTone } from '../lib/status';

const TONE_CLASSES: Record<StatusTone, string> = {
  good: 'bg-standing-good-bg text-standing-good',
  warn: 'bg-standing-warn-bg text-standing-warn',
  overdue: 'bg-standing-overdue-bg text-standing-overdue',
  neutral: 'bg-ink-100 text-ink-700',
};

export function StatusBadge({ label, tone }: { label: string; tone: StatusTone }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs font-semibold ${TONE_CLASSES[tone]}`}
    >
      {label}
    </span>
  );
}
