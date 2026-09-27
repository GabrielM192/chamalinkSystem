import type { LucideIcon } from 'lucide-react';

import { t } from '../i18n';
// Lightweight tab navigation — no external dependency. The pill style
// matches the NavLink pattern used in DashboardPage's sidebar so the
// Reports page feels like the same app, not a different UI bolted on.
// On mobile the tabs wrap naturally (flex-wrap), which is fine for 6
// short labels.
export interface Tab {
  id: string;
  label: string;
  icon: LucideIcon;
  // Optional count bubble (e.g. items waiting for approval). Hidden when 0.
  badge?: number;
}

export function Tabs({
  tabs,
  activeTab,
  onTabChange,
  scrollable = false,
}: {
  tabs: Tab[];
  activeTab: string;
  onTabChange: (id: string) => void;
  // Many tabs on a phone: one swipeable row instead of wrapping into
  // three rows that push the content off-screen.
  scrollable?: boolean;
}) {
  return (
    <div
      role="tablist"
      className={`flex gap-1.5 border-b border-ink-100 pb-3 ${
        scrollable ? '-mx-4 overflow-x-auto px-4 sm:mx-0 sm:flex-wrap sm:overflow-visible sm:px-0 [scrollbar-width:none]' : 'flex-wrap'
      }`}
    >
      {tabs.map((tab) => {
        const Icon = tab.icon;
        const isActive = tab.id === activeTab;
        return (
          <button
            key={tab.id}
            role="tab"
            aria-selected={isActive}
            onClick={() => onTabChange(tab.id)}
            className={`inline-flex shrink-0 items-center gap-1.5 whitespace-nowrap rounded-lg px-3 py-1.5 text-sm font-medium transition-colors ${
              isActive
                ? 'bg-ink-800 text-ink-50'
                : 'text-ink-600 hover:text-ink-950 hover:bg-ink-50'
            }`}
          >
            <Icon size={15} />
            {t(tab.label)}
            {!!tab.badge && (
              <span
                className={`ml-0.5 min-w-5 rounded-full px-1.5 text-[11px] font-bold leading-5 ${
                  isActive ? 'bg-gold-500 text-on-gold' : 'bg-standing-overdue text-on-accent'
                }`}
              >
                {tab.badge}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}
