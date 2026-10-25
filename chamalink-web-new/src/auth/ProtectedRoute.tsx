import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from './AuthContext';
import { GroupProvider } from '../groups/GroupContext';
import { LangBoundary } from '../i18n';

// Redirects to /login for any route nested under this one when there is
// no signed-in user - the token interceptor in api/client.ts handles the
// case where a token exists but the backend has since rejected it.
//
// GroupProvider wraps the outlet so the active-group state is shared by
// every protected screen (Dashboard, MemberProfile, M-Koba, Reports)
// rather than each page fetching and guessing its own. See
// groups/GroupContext.tsx for why that matters.
export function ProtectedRoute() {
  const { user, ready } = useAuth();
  const location = useLocation();

  // Kikao kinarejeshwa kutoka cookie ya HttpOnly — subiri kabla ya kuamua kumpeleka mtu kwenye login
  if (!ready) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-paper text-sm text-ink-600" role="status">
        …
      </div>
    );
  }

  if (!user) {
    // Rudi pale pale baada ya kuingia (mf. kiungo cha kujiunga /jiunge/KODI kilichotumwa WhatsApp)
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }

  return (
    <GroupProvider>
      <LangBoundary>
        <Outlet />
      </LangBoundary>
    </GroupProvider>
  );
}
