import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { AuthShell, authButtonCls, authInputCls } from './auth/AuthShell';
import { PasswordField } from '../components/PasswordField';
import { t } from '../i18n';
import { apiErrorMessage } from '../api/truth';
import { redeemResetCode } from '../api/platform';

/**
 * SASISHO20 — "Nina code ya msaada".
 *
 * Hii ni upande wa MWENYE akaunti: kiongozi aliyesahau neno la siri anapata code ya
 * mara moja kwa simu kutoka kwa msimamizi wa mfumo, kisha anajiwekea neno JIPYA hapa.
 *
 * Msimamizi HAJUI neno hili jipya — hilo ni kanuni, si mapambo: mtu mmoja asiwe
 * na uwezo wa kuingia kwenye akaunti ya mwingine.
 */
export function ResetPasswordPage() {
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    if (password !== confirm) {
      setError(t('Neno la siri na uthibitisho wake havitengani.'));
      return;
    }
    setBusy(true);
    try {
      const msg = await redeemResetCode(email.trim(), code.trim(), password);
      navigate('/login', {
        replace: true,
        state: { email: email.trim(), notice: msg || t('Neno la siri limewekwa. Ingia kwa neno jipya.') },
      });
    } catch (err) {
      setError(apiErrorMessage(err, t('Code si sahihi au imeisha muda. Omba code mpya kwa msimamizi wa mfumo.')));
    } finally {
      setBusy(false);
    }
  }

  return (
    <AuthShell
      title={t('Weka neno jipya la siri')}
      subtitle={t('Tumia code ya mara moja uliyopewa kwa simu na msimamizi wa mfumo.')}
    >
      <form onSubmit={submit} className="space-y-5">
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
          <label htmlFor="code" className="block text-sm font-medium text-ink-900 mb-1.5">{t('Code ya msaada (herufi 10)')}</label>
          <input
            id="code"
            required
            autoComplete="one-time-code"
            maxLength={10}
            value={code}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
            className={`${authInputCls} font-mono tracking-widest`}
            placeholder="ABCDE-23456"
          />
        </div>
        <PasswordField
          id="password"
          label={t('Neno jipya la siri')}
          value={password}
          onChange={setPassword}
          autoComplete="new-password"
          minLength={8}
          inputClassName={authInputCls}
        />
        <PasswordField
          id="confirm"
          label={t('Rudia neno jipya')}
          value={confirm}
          onChange={setConfirm}
          autoComplete="new-password"
          minLength={8}
          inputClassName={authInputCls}
        />
        {error && <p role="alert" className="rounded-xl bg-standing-overdue-bg px-3.5 py-2.5 text-sm text-standing-overdue">{error}</p>}
        <button type="submit" disabled={busy || code.trim().length < 10 || password.length < 8} className={authButtonCls}>
          {busy ? t('Inahifadhi…') : t('Weka neno jipya')}
        </button>
        <p className="text-xs text-ink-600 leading-relaxed">
          {t('Vikao vyote vya zamani vya akaunti hii vitafungwa mara moja. Code inatumika mara moja tu na inaisha baada ya dakika 30.')}
        </p>
        <Link to="/login" className="block text-sm text-ink-600 hover:text-ink-900">{t('Rudi kuingia')}</Link>
      </form>
    </AuthShell>
  );
}
