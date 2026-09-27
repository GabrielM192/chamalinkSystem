import { useEffect, useRef, useState } from 'react';
import { ChevronDown, Check, Users, Plus } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { useMyGroups } from '../hooks/useMyGroups';
import { groupRoleLabel } from '../lib/status';

import { t } from '../i18n';
// The group switcher is infrastructure, not a feature: every screen reads
// `activeGroupId`, so a member who belongs to more than one group must be
// able to choose which one they're looking at - otherwise reports, M-Koba
// imports and (later) loan requests silently target the wrong group, the
// exact failure mode the old "first group only" placeholder created.
//
// A plain dropdown (rather than a headless-ui dependency) keeps the bundle
// lean; outside-click and Escape close it, and the active group is marked
// with a check so the current state is obvious without reading colors.
export function GroupSwitcher() {
  const { groups, activeGroup, setActiveGroupId, loading } = useMyGroups();
  const [open, setOpen] = useState(false);
  const navigate = useNavigate();
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') setOpen(false);
    }
    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  // While groups load, or for a member who belongs to none yet, don't
  // render a broken control - the pages already surface a clear empty
  // state, and the header shouldn't pretend a group exists when it doesn't.
  if (loading || groups.length === 0) return null;

  // Menyu inafunguka daima: pia ina "Jiunge / anzisha kikundi kingine"
  const switchable = groups.length > 0;

  return (
    <div ref={rootRef} className="relative">
      <button
        type="button"
        onClick={() => switchable && setOpen((o) => !o)}
        disabled={!switchable}
        aria-haspopup="listbox"
        aria-expanded={open}
        className="flex items-center gap-2 rounded-lg bg-ink-800/60 px-2.5 py-1.5 sm:px-3 text-sm text-ink-50 transition-colors hover:bg-ink-800 disabled:cursor-default disabled:hover:bg-ink-800/60"
      >
        <Users size={15} className="hidden text-gold-500 shrink-0 sm:block" />
        <span className="max-w-[5.5rem] truncate font-medium min-[400px]:max-w-[8rem] sm:max-w-[12rem]">
          {activeGroup?.groupName ?? '—'}
        </span>
        {switchable && <ChevronDown size={15} className="text-ink-100/70 shrink-0" />}
      </button>

      {open && switchable && (
        <ul
          role="listbox"
          className="theme-surface absolute right-0 z-50 mt-2 w-72 max-w-[80vw] rounded-xl border border-ink-100 bg-paper-raised py-1 shadow-lg shadow-overlay"
        >
          {groups.map((g) => {
            const isActive = g.groupId === activeGroup?.groupId;
            return (
              <li key={g.groupId}>
                <button
                  type="button"
                  role="option"
                  aria-selected={isActive}
                  onClick={() => {
                    setActiveGroupId(g.groupId);
                    setOpen(false);
                  }}
                  className="flex w-full items-center justify-between gap-2 px-3 py-2 text-left transition-colors hover:bg-ink-50"
                >
                  <span className="min-w-0">
                    <span className="block truncate text-sm font-medium text-ink-950">
                      {g.groupName}
                    </span>
                    <span className="text-xs text-ink-600">
                      {groupRoleLabel(g.role)}
                      {g.groupCode ? ` · ${g.groupCode}` : ''}
                    </span>
                  </span>
                  {isActive && <Check size={16} className="text-standing-good shrink-0" />}
                </button>
              </li>
            );
          })}
          <li className="mt-1 border-t border-ink-100 pt-1">
            <button
              type="button"
              onClick={() => {
                setOpen(false);
                navigate('/karibu');
              }}
              className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-ink-700 transition-colors hover:bg-ink-50"
            >
              <Plus size={15} className="shrink-0 text-gold-700" />
              {t("Jiunge au anzisha kikundi kingine")}
            </button>
          </li>
        </ul>
      )}
    </div>
  );
}
