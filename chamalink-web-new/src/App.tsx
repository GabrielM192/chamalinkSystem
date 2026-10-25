import { lazy } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './auth/AuthContext';
import { I18nProvider, LangBoundary } from './i18n';
import { ThemeProvider } from './theme';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { LoginPage } from './pages/LoginPage';
import { RegisterPage } from './pages/RegisterPage';
import { DashboardPage } from './pages/DashboardPage';
import { ResetPasswordPage } from './pages/ResetPasswordPage';
import { AdminPage } from './pages/admin/AdminPage';

// SASISHO15: kurasa zinapakiwa zinapohitajika (React.lazy) — bundle ya kwanza ni ndogo (simu/mtandao hafifu).
// Suspense iko kwenye DashboardPage (karibu na <Outlet/>) ili kichwa na menyu visipotee wakati wa kupakia.
const WelcomePage = lazy(() => import('./pages/onboarding/WelcomePage').then((m) => ({ default: m.WelcomePage })));
const MembersPage = lazy(() => import('./pages/members/MembersPage').then((m) => ({ default: m.MembersPage })));
const ReportsPage = lazy(() => import('./pages/reports/ReportsPage').then((m) => ({ default: m.ReportsPage })));
const SettingsPage = lazy(() => import('./pages/SettingsPage').then((m) => ({ default: m.SettingsPage })));
const MyStatementPage = lazy(() => import('./pages/MyStatementPage').then((m) => ({ default: m.MyStatementPage })));
const TruthPage = lazy(() => import('./pages/truth/TruthPage').then((m) => ({ default: m.TruthPage })));
const BenefitsPage = lazy(() => import('./pages/benefits/BenefitsPage').then((m) => ({ default: m.BenefitsPage })));
const LoansPage = lazy(() => import('./pages/loans/LoansPage').then((m) => ({ default: m.LoansPage })));
// SASISHO18: pochi za vikundi vinavyochangishana KILA TUKIO
const WalletPage = lazy(() => import('./pages/events/WalletPage').then((m) => ({ default: m.WalletPage })));
// SASISHO21: matukio ya kikundi (mwanachama), sanduku la malipo na SMS (viongozi)
const GroupEventsPage = lazy(() => import('./pages/events/GroupEventsPage').then((m) => ({ default: m.GroupEventsPage })));
const PaymentInboxPage = lazy(() => import('./pages/treasury/PaymentInboxPage').then((m) => ({ default: m.PaymentInboxPage })));
const SmsPage = lazy(() => import('./pages/treasury/SmsPage').then((m) => ({ default: m.SmsPage })));
const ClaimsPage = lazy(() => import('./pages/treasury/ClaimsPage').then((m) => ({ default: m.ClaimsPage })));
const AppealsPage = lazy(() => import('./pages/members/AppealsPage').then((m) => ({ default: m.AppealsPage })));

function App() {
  return (
    <ThemeProvider>
    <I18nProvider>
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LangBoundary><LoginPage /></LangBoundary>} />
          <Route path="/jisajili" element={<LangBoundary><RegisterPage /></LangBoundary>} />
          {/* SASISHO20: kurejesha neno la siri kwa code ya mara moja (msaada wa msimamizi wa mfumo) */}
          <Route path="/saidia-nywila" element={<LangBoundary><ResetPasswordPage /></LangBoundary>} />
          {/* SASISHO20: console ya msimamizi wa MFUMO — kikao chake ni cha pekee (pa=1 + MFA) */}
          <Route path="/admin" element={<LangBoundary><AdminPage /></LangBoundary>} />

          <Route element={<ProtectedRoute />}>
            <Route path="/" element={<DashboardPage />}>
              {/* Taarifa ya mwanachama kutoka Injini ya Ukweli (chanzo kimoja cha namba) */}
              <Route index element={<MyStatementPage />} />
              <Route path="ukweli" element={<Navigate to="/ukweli/muhtasari" replace />} />
              <Route path="ukweli/:tab" element={<TruthPage />} />

              {/* Import za zamani ziliandika mgawanyo kwenye jedwali la zamani (chanzo cha
                  ripoti zisizo sahihi). Sasa import pekee ni ya Injini ya Ukweli. */}
              <Route path="mkoba" element={<Navigate to="/ukweli/import" replace />} />
              <Route path="hazina" element={<Navigate to="/ukweli/import" replace />} />

              {/* Wasifu wa zamani ulisoma ripoti za mfumo wa zamani (sasa 410) — taarifa pekee ni ya Injini ya Ukweli */}
              <Route path="wasifu" element={<Navigate to="/" replace />} />
              {/* Ripoti na Mipangilio zinasoma Injini ya Ukweli pekee (chanzo kimoja cha namba) */}
              <Route path="ripoti" element={<Navigate to="/ripoti/muhtasari" replace />} />
              <Route path="ripoti/:tab" element={<ReportsPage />} />
              <Route path="mipangilio" element={<SettingsPage />} />
              {/* Mafao (msiba/sherehe): kila mwanachama anaona kesi, kura na utoaji wa M-Koba; viongozi wanasimamia */}
              <Route path="mafao" element={<BenefitsPage />} />
              {/* Mikopo (SASISHO15): ombi → kura → utoaji unaothibitishwa kwa M-Koba */}
              <Route path="mikopo" element={<LoansPage />} />
              {/* SASISHO18 (vikundi vya "kila tukio"): salio la mwanachama, makato ya matukio, deni na floor */}
              <Route path="pochi" element={<WalletPage />} />
              <Route path="matukio" element={<GroupEventsPage />} />
              <Route path="hazina/malipo" element={<PaymentInboxPage />} />
              <Route path="hazina/sms" element={<SmsPage />} />
              <Route path="madai" element={<ClaimsPage />} />
              <Route path="rufaa" element={<AppealsPage />} />

              {/* Usajili: kuanzisha kikundi / kujiunga kwa kodi; viongozi: wanachama na maombi */}
              <Route path="karibu" element={<WelcomePage />} />
              <Route path="jiunge/:code" element={<WelcomePage />} />
              <Route path="wanachama" element={<Navigate to="/wanachama/orodha" replace />} />
              <Route path="wanachama/:tab" element={<MembersPage />} />
            </Route>
          </Route>

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
    </I18nProvider>
    </ThemeProvider>
  );
}

export default App;
