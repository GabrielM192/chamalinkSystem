import { Preferences } from '../components/Preferences';
import { LogOut, Settings, Scale, UserRound, BarChart3, Users, HandHeart, HandCoins, MonitorX, Wallet, CalendarHeart, Inbox, MessageSquare } from 'lucide-react';
import { Suspense } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useMyGroups } from '../hooks/useMyGroups';
import { useLoansVisibility } from '../hooks/useLoansVisibility';
import { GroupSwitcher } from '../components/GroupSwitcher';
import { BrandLockup } from '../components/Brand';
import { NotificationBell } from '../components/NotificationBell';
import { Loading, ToastProvider } from '../components/ui';
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
  return `${NAV_LINK_CLASSES} ${isActive ? 'bg-white/10 text-ink-50' : 'text-ink-100/65 hover:bg-white/5 hover:text-ink-50'}`;
}

export function DashboardPage() {
  const { user, logout, logoutEverywhere } = useAuth();
  const { activeGroup } = useMyGroups();
  const isLeader = !!activeGroup && LEADER_ROLES.has(activeGroup.role);
  const loansVisible = useLoansVisibility(activeGroup?.groupId);
  // SASISHO18: menyu ya "Pochi" inaonekana tu kwa vikundi vinavyochangishana KILA TUKIO.
  const isEventGroup = activeGroup?.groupType === 'EventBased';
  const onLoansPage = useLocation().pathname.startsWith('/mikopo');

  const nav = (
    <>
      <NavLink to="/" end className={navClass}>
        <UserRound size={14} />
        {t("Taarifa Yangu")}
      </NavLink>
      {activeGroup && isEventGroup && (
        <NavLink to="/pochi" className={navClass}>
          <Wallet size={14} />
          {t("Pochi")}
        </NavLink>
      )}
      {activeGroup && isEventGroup && (
        <NavLink to="/matukio" className={navClass}>
          <CalendarHeart size={14} />
          {t("Matukio")}
        </NavLink>
      )}
      {activeGroup && isEventGroup && (
        <NavLink to="/madai" className={navClass}>
          <HandCoins size={14} />
          {t("Madai")}
        </NavLink>
      )}
      {activeGroup && (
        <NavLink to="/rufaa" className={navClass}>
          <Scale size={14} />
          {t("Rufaa")}
        </NavLink>
      )}
      {activeGroup && (
        <NavLink to="/mafao" className={navClass}>
          <HandHeart size={14} />
          {t("Mafao")}
        </NavLink>
      )}
      {activeGroup && (loansVisible || onLoansPage) && (
        <NavLink to="/mikopo" className={navClass}>
          <HandCoins size={14} />
          {t("Mikopo")}
        </NavLink>
      )}
      {isLeader && isEventGroup && (
        <NavLink to="/hazina/malipo" className={navClass}>
          <Inbox size={14} />
          {t("Malipo")}
        </NavLink>
      )}
      {isLeader && isEventGroup && (
        <NavLink to="/hazina/sms" className={navClass}>
          <MessageSquare size={14} />
          {t("SMS")}
        </NavLink>
      )}
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
      <div className="app-canvas min-h-screen">
        <header className="chrome-panel theme-chrome sticky top-0 z-40 text-ink-50 print:hidden">
          <div className="mx-auto flex max-w-6xl items-center justify-between gap-3 px-4 py-3 sm:px-6">
            <BrandLockup size={26} className="shrink-0" />
            <div className="flex shrink-0 items-center gap-2 text-sm sm:gap-4">
              <GroupSwitcher />
              {activeGroup && <NotificationBell />}
              <Preferences />
              <span className="hidden max-w-[12rem] truncate whitespace-nowrap text-ink-100/80 lg:inline" title={user?.fullName}>{user?.fullName}</span>
              <button
                type="button"
                onClick={() => {
                  if (window.confirm(t("Toka kwenye vifaa vyote? Kila simu/kompyuta iliyoingia kwa akaunti hii italazimika kuingia upya."))) void logoutEverywhere();
                }}
                className="hidden items-center text-ink-100/80 transition-colors hover:text-ink-50 md:inline-flex"
                aria-label={t("Toka kwenye vifaa vyote")}
                title={t("Toka kwenye vifaa vyote")}
              >
                <MonitorX size={16} />
              </button>
              <button
                onClick={() => void logout()}
                className="flex items-center gap-1.5 text-ink-100/80 transition-colors hover:text-ink-50"
                aria-label={t("Toka")}
              >
                <LogOut size={16} />
                <span className="hidden whitespace-nowrap sm:inline">{t("Toka")}</span>
              </button>
            </div>
          </div>
          {/* Menyu ina mstari wake kwa upana WOTE: haigongani na jina la kikundi/arifa hata vipengele vikiongezeka.
              Kwenye skrini nyembamba inateleza kushoto-kulia. */}
          <div className="border-t border-white/10">
            <nav className="mx-auto flex max-w-6xl gap-1 overflow-x-auto px-3 py-2 sm:px-5 [scrollbar-width:none]">{nav}</nav>
          </div>
        </header>

        <main className="mx-auto max-w-6xl px-4 py-6 sm:px-6 sm:py-10 print:max-w-none print:p-0">
          <Suspense fallback={<Loading />}>
            <Outlet />
          </Suspense>
        </main>
      </div>
    </ToastProvider>
  );
}
