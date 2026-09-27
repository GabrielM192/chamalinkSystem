import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './auth/AuthContext';
import { I18nProvider, LangBoundary } from './i18n';
import { ThemeProvider } from './theme';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { LoginPage } from './pages/LoginPage';
import { RegisterPage } from './pages/RegisterPage';
import { WelcomePage } from './pages/onboarding/WelcomePage';
import { MembersPage } from './pages/members/MembersPage';
import { DashboardPage } from './pages/DashboardPage';
import { ReportsPage } from './pages/reports/ReportsPage';
import { SettingsPage } from './pages/SettingsPage';
import { MyStatementPage } from './pages/MyStatementPage';
import { TruthPage } from './pages/truth/TruthPage';

function App() {
  return (
    <ThemeProvider>
    <I18nProvider>
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LangBoundary><LoginPage /></LangBoundary>} />
          <Route path="/jisajili" element={<LangBoundary><RegisterPage /></LangBoundary>} />

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
