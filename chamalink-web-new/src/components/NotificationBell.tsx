import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Bell, CheckCheck } from 'lucide-react';
import { notificationsApi, type NotificationItem } from '../api/benefits';
import { useMyGroups } from '../hooks/useMyGroups';
import { realDateTime } from '../lib/truth';
import { t } from '../i18n';
import { notificationText } from '../lib/loc';

/** Muda kati ya kuangalia arifa mpya (ms). Si sheria ya fedha — ni kasi ya kiolesura tu. */
const POLL_MS = 60_000;

/**
 * Kengele ya arifa: kura zinazokusubiri, mgao wa mafao, maombi ya kuidhinisha, n.k.
 * Mtu anaona arifa ZAKE tu (seva inachuja kwa akaunti iliyoingia).
 */
export function NotificationBell() {
  const navigate = useNavigate();
  const { activeGroupId } = useMyGroups();
  const [open, setOpen] = useState(false);
  const [data, setData] = useState<{ unread: number; items: NotificationItem[] } | null>(null);
  const box = useRef<HTMLDivElement>(null);

  const load = useCallback(() => {
    if (!activeGroupId) return;
    notificationsApi
      .list(activeGroupId)
      .then(setData)
      .catch(() => {
        /* kengele isivunje ukurasa — itajaribu tena */
      });
  }, [activeGroupId]);

  useEffect(() => {
    load();
    const id = window.setInterval(() => document.visibilityState === 'visible' && load(), POLL_MS);
    return () => window.clearInterval(id);
  }, [load]);

  useEffect(() => {
    if (!open) return;
    const h = (e: MouseEvent) => box.current && !box.current.contains(e.target as Node) && setOpen(false);
    const k = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', h);
    window.addEventListener('keydown', k);
    return () => {
      document.removeEventListener('mousedown', h);
      window.removeEventListener('keydown', k);
    };
  }, [open]);

  async function openItem(n: NotificationItem) {
    setOpen(false);
    if (!n.readAt) {
      try {
        await notificationsApi.read(n.id);
      } catch {
        /* si muhimu */
      }
      load();
    }
    if (n.link) navigate(n.link);
  }

  const unread = data?.unread ?? 0;

  return (
    <div className="relative" ref={box}>
      <button
        onClick={() => {
          setOpen((o) => !o);
          if (!open) load();
        }}
        className="relative flex h-8 w-8 items-center justify-center rounded-lg text-ink-100/80 transition-colors hover:text-ink-50"
        aria-label={unread ? t('Arifa ({0} mpya)', { 0: unread }) : t('Arifa')}
        aria-expanded={open}
      >
        <Bell size={17} />
        {unread > 0 && (
          <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-standing-overdue px-1 text-[10px] font-bold text-on-accent">
            {unread > 99 ? '99+' : unread}
          </span>
        )}
      </button>
      {open && (
        <div className="fixed inset-x-2 top-14 z-50 max-h-[75vh] overflow-hidden rounded-2xl border border-ink-100 bg-paper-raised text-ink-900 shadow-xl sm:absolute sm:inset-x-auto sm:right-0 sm:top-10 sm:w-96">
          <div className="flex items-center justify-between border-b border-ink-100 px-4 py-2.5">
            <span className="text-sm font-semibold text-ink-950">{t('Arifa')}</span>
            {unread > 0 && (
              <button
                onClick={async () => {
                  try {
                    await notificationsApi.readAll(activeGroupId);
                  } finally {
                    load();
                  }
                }}
                className="inline-flex items-center gap-1 text-xs font-medium text-ink-700 hover:text-ink-950"
              >
                <CheckCheck size={14} /> {t('Soma zote')}
              </button>
            )}
          </div>
          <ul className="max-h-[calc(75vh-3rem)] divide-y divide-ink-100 overflow-y-auto">
            {!data || data.items.length === 0 ? (
              <li className="px-4 py-8 text-center text-sm text-ink-600">{t('Hakuna arifa')}</li>
            ) : (
              data.items.map((n) => (
                <li key={n.id}>
                  <button onClick={() => openItem(n)} className={`block w-full px-4 py-3 text-left hover:bg-ink-50 ${n.readAt ? '' : 'bg-gold-500/5'}`}>
                    <div className="flex items-start gap-2">
                      {!n.readAt && <span className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-gold-600" aria-hidden="true" />}
                      <div className="min-w-0">
                        <div className={`text-sm ${n.readAt ? 'text-ink-800' : 'font-semibold text-ink-950'}`}>{notificationText(n).title}</div>
                        <div className="mt-0.5 text-xs text-ink-700">{notificationText(n).body}</div>
                        <div className="mt-1 text-[11px] text-ink-500">{realDateTime(n.createdAt)}</div>
                      </div>
                    </div>
                  </button>
                </li>
              ))
            )}
          </ul>
        </div>
      )}
    </div>
  );
}
