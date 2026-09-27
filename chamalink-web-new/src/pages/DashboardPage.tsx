import { Preferences } from '../components/Preferences';
import { LogOut, Settings, Scale, UserRound, BarChart3, Users } from 'lucide-react';
import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useMyGroups } from '../hooks/useMyGroups';
import { GroupSwitcher } from '../components/GroupSwitcher';
import { ToastProvider } from '../components/ui';
import { LEADER_ROLES } from '../lib/truth';

import { t } from '../i18n';
// Layout shell. Destinations:
//  - "Taarifa Yangu" (default): the member's own statement from the Truth
//    Engine - the same numbers leaders see, computed from raw M-Koba data.
//  - "Ukweli wa Kikundi" (leaders): group overview, manual (pre-M-Koba)
//    payments with live preview + maker-checker, phones, PDF gaps,
//    waivers/grace, M-Koba import and contribution rules.
//  - Ripoti (leaders): group reports from the same Truth Engine + the raw
//    M-Koba cashbook. Mipangilio (leaders): the ONE place for group rules.
//  - Wanachama (leaders): member records, join requests (approve + link to an
//    existing record by phone), join code and official member numbers.
//
// The old "M-Koba" and "Hazina" import screens are no longer in the nav:
// they wrote derived allocations into the legacy tables, which is exactly
// what produced reports that disagreed with reality. Their routes now
// redirect to /ukweli/import (see App.tsx) so old bookmarks still land
// somewhere correct.
//
// NOTE: nav visibility is a UI convenience, not access control - every
// leader-only endpoint is enforced by the backend (GroupAuthorizationService).
const NAV_LINK_CLASSES =
  'inline-flex shrink-0 items-center gap-1.5 whitespace-nowrap px-3 py-1.5 rounded-lg text-sm font-medium transition-colors';

function navClass({ isActive }: { isActive: boolean }) {
  return `${NAV_LINK_CLASSES} ${isActive ? 'bg-ink-800 text-ink-50' : 'text-ink-100/70 hover:text-ink-50'}`;
}

export function DashboardPage() {
  const { user, logout } = useAuth();
  const { activeGroup } = useMyGroups();
  const isLeader = !!activeGroup && LEADER_ROLES.has(activeGroup.role);

  const nav = (
    <>
      <NavLink to="/" end className={navClass}>
        <UserRound size={14} />
        {t("Taarifa Yangu")}
      </NavLink>
      {isLeader && (
        <NavLink to="/ukweli" className={navClass}>
          <Scale size={14} />
          {t("Ukweli wa Kikundi")}
        </NavLink>
      )}
      {isLeader && (
        <NavLink to="/ripoti" className={navClass}>
          <BarChart3 size={14} />
          {t("Ripoti")}
        </NavLink>
      )}
      {isLeader && (
        <NavLink to="/wanachama" className={navClass}>
          <Users size={14} />
          {t("Wanachama")}
        </NavLink>
      )}
      {isLeader && (
        <NavLink to="/mipangilio" className={navClass}>
          <Settings size={14} />
          {t("Mipangilio")}
        </NavLink>
      )}
    </>
  );

  return (
    <ToastProvider>
      <div className="min-h-screen bg-paper">
        <header className="theme-chrome sticky top-0 z-40 bg-ink-900 text-ink-50 shadow-sm print:hidden">
          <div className="mx-auto flex max-w-6xl items-center justify-between gap-3 px-4 py-3 sm:px-6">
            <div className="flex min-w-0 items-center gap-6">
              <span className="shrink-0 font-semibold tracking-tight text-gold-500">ChamaLink</span>
              <nav className="hidden items-center gap-1 xl:flex">{nav}</nav>
            </div>
            <div className="flex shrink-0 items-center gap-2 text-sm sm:gap-4">
              <GroupSwitcher />
              <Preferences />
              <span className="hidden max-w-[10rem] truncate whitespace-nowrap text-ink-100/80 2xl:inline" title={user?.fullName}>{user?.fullName}</span>
              <button
                onClick={logout}
                className="flex items-center gap-1.5 text-ink-100/80 transition-colors hover:text-ink-50"
                aria-label={t("Toka")}
              >
                <LogOut size={16} />
                <span className="hidden whitespace-nowrap sm:inline xl:hidden 2xl:inline">{t("Toka")}</span>
              </button>
            </div>
          </div>
          {/* Simu/tablet/laptop ndogo: mstari wa pili unaoteleza (menyu ya mstari mmoja inaanzia xl) */}
          <nav className="flex gap-1 overflow-x-auto border-t border-ink-800 px-3 py-2 xl:hidden [scrollbar-width:none]">{nav}</nav>
        </header>

        <main className="mx-auto max-w-6xl px-4 py-6 sm:px-6 sm:py-10 print:max-w-none print:p-0">
          <Outlet />
        </main>
      </div>
    </ToastProvider>
  );
}
