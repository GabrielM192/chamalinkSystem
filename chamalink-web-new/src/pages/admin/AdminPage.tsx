import { useCallback, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { AuthShell, authButtonCls, authInputCls } from '../auth/AuthShell';
import { t } from '../../i18n';
import { apiErrorMessage } from '../../api/truth';
import { Preferences } from '../../components/Preferences';
import {
  platformConfirmEnrollment,
  platformEnroll,
  platformVerifyMfa,
  platformLogin,
  platformLogout,
  platformMe,
  setPlatformToken,
  type LoginStage,
  type PlatformMe,
} from '../../api/platform';
import { AdminConsole } from './AdminConsole';

/**
 * SASISHO20 — mlango wa MSIMAMIZI WA MFUMO (/admin).
 *
 * Mzunguko (kama benki):
 *   1. Barua pepe + neno la siri.
 *   2. Mara ya kwanza: kuskani QR (Google Authenticator / Authy) + kuthibitisha code.
 *   3. Kila kuingia baada ya hapo: code ya tarakimu 6.
 *   4. Kikao kinaisha baada ya dakika 60 — msimamizi anaingia tena.
 *
 * Hakuna kiungo cha "nimesahau neno la siri" hapa kwa makusudi: msimamizi wa mfumo
 * HAPASWI kuweza kujipatia nafasi mwenyewe. Neno lake linawekwa upya na msimamizi
 * mwingine (4-eyes) kupitia console.
 */
type Stage = 'loading' | 'login' | 'enroll' | 'mfa' | 'ready';

export function AdminPage() {
  const navigate = useNavigate();
  // Token inakaa kwenye kumbukumbu tu (si localStorage): kila ukurasa unapoanza upya,
  // msimamizi anaingia tena kwa neno la siri + MFA. Hakuna kikao cha "milele".
  const [stage, setStage] = useState<Stage>('login');
  const [me, setMe] = useState<PlatformMe | null>(null);
  const [challenge, setChallenge] = useState<string | null>(null);
  const [enrollment, setEnrollment] = useState<{ secret: string; uri: string; manualHint: string } | null>(null);
  const [fullName, setFullName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');

  const signOut = useCallback(async () => {
    try {
      await platformLogout();
    } catch {
      setPlatformToken(null);
    }
    setMe(null);
    setChallenge(null);
    setEnrollment(null);
    setStage('login');
  }, []);

  function afterStage(result: LoginStage) {
    setFullName(result.fullName);
    if (result.stage === 'EnrollmentRequired' && result.token) {
      setChallenge(result.token);
      setStage('enroll');
      return;
    }
    if (result.stage === 'MfaRequired' && result.token) {
      setChallenge(result.token);
      setStage('mfa');
      return;
    }
    setStage('ready');
    platformMe().then(setMe).catch(() => setStage('login'));
  }

  async function submitPassword(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      afterStage(await platformLogin(email.trim(), password));
      setPassword('');
    } catch (err) {
      setError(apiErrorMessage(err, t('Barua pepe au neno la siri si sahihi.')));
    } finally {
      setBusy(false);
    }
  }

  async function startEnrollment() {
    if (!challenge) return;
    setError(null);
    setBusy(true);
    try {
      setEnrollment(await platformEnroll(challenge));
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  async function submitCode(e: FormEvent) {
    e.preventDefault();
    if (!challenge) return;
    setError(null);
    setBusy(true);
    try {
      const stageNow = enrollment
        ? await platformConfirmEnrollment(challenge, code)
        : await platformVerifyMfa(challenge, code);
      setCode('');
      afterStage(stageNow);
    } catch (err) {
      setError(apiErrorMessage(err, t('Code si sahihi au imeisha muda.')));
    } finally {
      setBusy(false);
    }
  }

  // ---------------------------------------------------------------- skrini
  if (stage === 'loading') {
    return (
      <div className="min-h-screen grid place-items-center bg-paper text-ink-700 text-sm">{t('Inapakia…')}</div>
    );
  }

  if (stage === 'ready' && me) {
    return <AdminConsole me={me} onSignOut={signOut} onGoHome={() => navigate('/')} />;
  }

  if (stage === 'enroll') {
    return (
      <AuthShell title={t('Washa MFA kwenye akaunti yako')} subtitle={t('Hatua ya lazima kabla ya kutumia dashibodi ya mfumo.')}>
        <div className="space-y-4">
          <p className="text-sm text-ink-700">
            {t('Fungua Google Authenticator au Authy, ongeza akaunti kwa kuskani QR, kisha andika code ya tarakimu 6.')}
          </p>
          {!enrollment ? (
            <button type="button" onClick={startEnrollment} disabled={busy} className={authButtonCls}>
              {busy ? t('Inatayarisha…') : t('Onyesha QR')}
            </button>
          ) : (
            <div className="space-y-4">
              <div className="rounded-xl border border-ink-100 bg-paper-raised p-4 space-y-2">
                <p className="text-xs uppercase tracking-wide text-ink-600">{t('Ufunguo wa mkono (kama kamera haifanyi kazi)')}</p>
                <p className="font-mono text-sm break-all text-ink-900" data-testid="admin-otp-secret">{enrollment.secret}</p>
                <p className="text-xs text-ink-600">{enrollment.manualHint}</p>
              </div>
              <form onSubmit={submitCode} className="space-y-4">
                <label htmlFor="otp" className="block text-sm font-medium text-ink-900">{t('Code ya tarakimu 6')}</label>
                <input
                  id="otp"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  maxLength={6}
                  required
                  value={code}
                  onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
                  className={authInputCls}
                  placeholder="123456"
                />
                <button type="submit" disabled={busy || code.length < 6} className={authButtonCls}>
                  {busy ? t('Inathibitisha…') : t('Thibitisha na uingie')}
                </button>
              </form>
            </div>
          )}
          {error && <p role="alert" className="text-sm text-standing-overdue">{error}</p>}
        </div>
      </AuthShell>
    );
  }

  if (stage === 'mfa') {
    return (
      <AuthShell title={t('Code ya uthibitisho')} subtitle={t('Fungua app ya authenticator kisha andika code ya sasa.')}>
        <form onSubmit={submitCode} className="space-y-5">
          <p className="text-sm text-ink-700">{fullName}</p>
          <div>
            <label htmlFor="otp" className="block text-sm font-medium text-ink-900 mb-1.5">{t('Code ya tarakimu 6')}</label>
            <input
              id="otp"
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              required
              autoFocus
              value={code}
              onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
              className={authInputCls}
              placeholder="123456"
            />
          </div>
          {error && <p role="alert" className="text-sm text-standing-overdue">{error}</p>}
          <button type="submit" disabled={busy || code.length < 6} className={authButtonCls}>
            {busy ? t('Inathibitisha…') : t('Ingia')}
          </button>
          <button type="button" onClick={signOut} className="w-full text-sm text-ink-600 hover:text-ink-900">
            {t('Anza upya kwa neno la siri')}
          </button>
        </form>
      </AuthShell>
    );
  }

  return (
    <AuthShell title={t('Msimamizi wa Mfumo')} subtitle={t('Eneo hili ni la msaada na uangalizi pekee — haligusi pesa za kikundi.')}>
      <form onSubmit={submitPassword} className="space-y-5">
        <div>
          <label htmlFor="email" className="block text-sm font-medium text-ink-900 mb-1.5">{t('Barua pepe')}</label>
          <input
            id="email"
            type="email"
            required
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className={authInputCls}
            placeholder={t('wewe@mfano.co.tz')}
          />
        </div>
        <div>
          <label htmlFor="password" className="block text-sm font-medium text-ink-900 mb-1.5">{t('Neno la siri')}</label>
          <input
            id="password"
            type="password"
            required
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className={authInputCls}
          />
        </div>
        {error && <p role="alert" className="text-sm text-standing-overdue">{error}</p>}
        <button type="submit" disabled={busy} className={authButtonCls}>
          {busy ? t('Inaingia…') : t('Endelea')}
        </button>
        <p className="text-xs text-ink-600 leading-relaxed">
          {t('Hatumtumii msimamizi kuweka neno la siri la mtu mwingine. Msaada wa neno la siri ni code ya mara moja; mwenye akaunti anaweka neno jipya mwenyewe.')}
        </p>
        <div className="flex items-center justify-between pt-2">
          <a href="/" className="text-sm text-ink-600 hover:text-ink-900">{t('Rudi kwenye ChamaLink')}</a>
          <Preferences />
        </div>
      </form>
    </AuthShell>
  );
}
