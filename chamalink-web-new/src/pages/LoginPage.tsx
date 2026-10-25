import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { ArrowRight, Mail } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { AuthShell, authButtonCls, authFootLinkCls, authInputCls, authLinkCls } from './auth/AuthShell';
import { PasswordField } from '../components/PasswordField';

import { t } from '../i18n';
import { apiErrorMessage } from '../api/truth';
import { locText } from '../lib/loc';

/**
 * Kuingia (SASISHO22). Mpangilio: karatasi tulivu upande wa kulia, paneli ya nembo kushoto.
 * Kila kitu kinachohitajika kiko hapa: barua pepe, neno la siri (unaweza kuliona),
 * "Umesahau?" na "Jisajili". Hakuna kitu kingine kinachoshindana na fomu.
 */
export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const navState = location.state as { from?: string; notice?: string; email?: string } | null;
  const redirectTo = navState?.from ?? '/';
  const notice = navState?.notice;

  const [email, setEmail] = useState(navState?.email ?? '');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setIsSubmitting(true);
    try {
      await login({ email, password });
      navigate(redirectTo, { replace: true });
    } catch (err) {
      // Akaunti iliyofungwa kwa muda inaonyesha ujumbe wa seva; kosa jingine lolote → ujumbe wa jumla
      const msg = apiErrorMessage(err, '');
      setError(/imefungwa|locked/i.test(msg) ? msg : t("Barua pepe au neno la siri si sahihi."));
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <AuthShell title={t('Karibu tena')} subtitle={t('Ingia kuona hali ya kikundi chako.')}>
      {notice && (
        <div
          role="status"
          className="mb-5 rounded-xl border border-standing-good/25 bg-standing-good-bg px-3.5 py-2.5 text-sm text-standing-good"
        >
          {locText(notice)}
        </div>
      )}
      <form onSubmit={handleSubmit} className="space-y-4">
        <div>
          <label htmlFor="email" className="mb-1.5 block text-sm font-medium text-ink-900">
            {t('Barua pepe')}
          </label>
          <div className="relative">
            <Mail size={16} className="pointer-events-none absolute top-1/2 left-3.5 -translate-y-1/2 text-ink-500" aria-hidden="true" />
            <input
              id="email"
              type="email"
              required
              autoComplete="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className={`${authInputCls} pl-10`}
              placeholder={t('wewe@mfano.co.tz')}
            />
          </div>
        </div>

        <PasswordField
          id="password"
          label={t('Neno la siri')}
          value={password}
          onChange={setPassword}
          autoComplete="current-password"
          inputClassName={authInputCls}
          labelAction={
            <Link to="/saidia-nywila" className={authLinkCls}>
              {t('Umesahau?')}
            </Link>
          }
        />

        {error && (
          <p role="alert" className="rounded-xl bg-standing-overdue-bg px-3.5 py-2.5 text-sm text-standing-overdue">
            {error}
          </p>
        )}

        <button type="submit" disabled={isSubmitting} className={`${authButtonCls} group inline-flex items-center justify-center gap-2`}>
          {isSubmitting ? t('Inaingia...') : t('Ingia')}
          {!isSubmitting && <ArrowRight size={17} className="transition-transform group-hover:translate-x-0.5" aria-hidden="true" />}
        </button>
      </form>

      <p className="mt-6 text-sm text-ink-700">
        {t('Bado huna akaunti?')}{' '}
        <Link to="/jisajili" state={location.state} className={authFootLinkCls}>
          {t('Jisajili')}
        </Link>
      </p>
    </AuthShell>
  );
}
