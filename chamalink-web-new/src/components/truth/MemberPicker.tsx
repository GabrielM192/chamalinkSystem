import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Check, ChevronDown, Search, X } from 'lucide-react';
import type { MemberLite } from '../../api/truth';
import { initials, matches } from '../../lib/truth';
import { groupRoleLabel } from '../../lib/status';
import { inputCls } from '../ui';

import { t } from '../../i18n';
/**
 * Kuchagua mwanachama kwa JINA, NAMBA YA UANACHAMA au NAMBA YA SIMU.
 * (Mfumo unatambua watu kwa simu — kwa hiyo simu inaonyeshwa kila mstari
 * ili kiongozi asichanganye watu wenye majina yanayofanana.)
 */
export function MemberPicker({
  members,
  value,
  onChange,
  placeholder = t("Tafuta jina, namba au simu..."),
  meta,
  allowAll,
}: {
  members: MemberLite[];
  value: string | null;
  onChange: (memberId: string | null) => void;
  placeholder?: string;
  meta?: (m: MemberLite) => React.ReactNode;
  allowAll?: string; // lebo ya "wanachama wote" (value = null)
}) {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState('');
  const [hi, setHi] = useState(0);
  const box = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listId = useId();

  const selected = members.find((m) => m.memberId === value) ?? null;
  const list = useMemo(
    () => members.filter((m) => matches(q, m.fullName, m.memberNumber, ...m.phones)).slice(0, 60),
    [members, q]
  );

  useEffect(() => {
    const h = (e: MouseEvent) => {
      if (box.current && !box.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', h);
    return () => document.removeEventListener('mousedown', h);
  }, []);

  function pick(id: string | null) {
    onChange(id);
    setOpen(false);
    setQ('');
  }

  return (
    <div className="relative" ref={box}>
      {!open ? (
        <button
          type="button"
          onClick={() => {
            setOpen(true);
            setTimeout(() => inputRef.current?.focus(), 0);
          }}
          className={`${inputCls} flex items-center gap-3 text-left`}
          aria-haspopup="listbox"
        >
          {selected ? (
            <>
              <Avatar name={selected.fullName} />
              <span className="min-w-0 flex-1">
                <span className="block truncate font-medium">{selected.fullName}</span>
                <span className="figure block truncate text-xs text-ink-600">
                  {selected.phones[0] ?? t("Hana simu iliyounganishwa")} · {selected.memberNumber}
                </span>
              </span>
            </>
          ) : (
            <span className="flex-1 text-ink-500">{value === null && allowAll ? allowAll : t("Chagua mwanachama")}</span>
          )}
          <ChevronDown size={16} className="text-ink-500" />
        </button>
      ) : (
        <div className="relative">
          <Search size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-500" />
          <input
            ref={inputRef}
            value={q}
            onChange={(e) => {
              setQ(e.target.value);
              setHi(0);
            }}
            placeholder={placeholder}
            className={`${inputCls} pl-9 pr-9`}
            role="combobox"
            aria-expanded="true"
            aria-controls={listId}
            onKeyDown={(e) => {
              if (e.key === 'ArrowDown') {
                e.preventDefault();
                setHi((h) => Math.min(h + 1, list.length - 1));
              } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                setHi((h) => Math.max(h - 1, 0));
              } else if (e.key === 'Enter') {
                e.preventDefault();
                if (list[hi]) pick(list[hi].memberId);
              } else if (e.key === 'Escape') setOpen(false);
            }}
          />
          <button
            type="button"
            onClick={() => setOpen(false)}
            className="absolute right-2 top-1/2 -translate-y-1/2 rounded p-1 text-ink-500 hover:bg-ink-50"
            aria-label={t("Funga")}
          >
            <X size={14} />
          </button>
        </div>
      )}

      {open && (
        <ul
          id={listId}
          role="listbox"
          className="absolute z-30 mt-1 max-h-72 w-full overflow-y-auto rounded-xl border border-ink-100 bg-paper-raised py-1 shadow-lg"
        >
          {allowAll && (
            <li>
              <button
                type="button"
                onClick={() => pick(null)}
                className="flex w-full items-center gap-3 px-3 py-2 text-left text-sm hover:bg-ink-50"
              >
                <span className="flex h-8 w-8 items-center justify-center rounded-full bg-gold-100 text-xs font-bold text-gold-700">∀</span>
                <span className="flex-1 font-medium">{allowAll}</span>
                {value === null && <Check size={14} className="text-standing-good" />}
              </button>
            </li>
          )}
          {list.length === 0 && <li className="px-3 py-4 text-center text-sm text-ink-600">{t("Hakuna anayelingana na “")}{q}”.</li>}
          {list.map((m, i) => (
            <li key={m.memberId} role="option" aria-selected={m.memberId === value}>
              <button
                type="button"
                onMouseEnter={() => setHi(i)}
                onClick={() => pick(m.memberId)}
                className={`flex w-full items-center gap-3 px-3 py-2 text-left ${i === hi ? 'bg-ink-50' : ''}`}
              >
                <Avatar name={m.fullName} />
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-medium text-ink-950">
                    {m.fullName}
                    {m.role !== 'Member' && <span className="ml-1.5 text-xs font-normal text-gold-700">· {groupRoleLabel(m.role)}</span>}
                  </span>
                  <span className="figure block truncate text-xs text-ink-600">
                    {m.phones.length ? m.phones.join(', ') : t("Hana simu")} · {m.memberNumber}
                  </span>
                </span>
                {meta?.(m)}
                {m.memberId === value && <Check size={14} className="text-standing-good" />}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export function Avatar({ name, size = 'md' }: { name: string; size?: 'sm' | 'md' | 'lg' }) {
  const s = size === 'sm' ? 'h-7 w-7 text-[10px]' : size === 'lg' ? 'h-11 w-11 text-sm' : 'h-8 w-8 text-xs';
  return (
    <span className={`flex shrink-0 items-center justify-center rounded-full bg-ink-800 font-semibold text-ink-50 ${s}`} aria-hidden="true">
      {initials(name) || '?'}
    </span>
  );
}
