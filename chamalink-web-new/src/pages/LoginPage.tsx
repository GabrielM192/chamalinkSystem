import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { AuthShell, authButtonCls, authInputCls } from './auth/AuthShell';

import { t } from '../i18n';
export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const redirectTo = (location.state as { from?: string } | null)?.from ?? '/';

  const [email, setEmail] = useState('');
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
    } catch {
      setError(t("Barua pepe au neno la siri si sahihi."));
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <AuthShell title={t("Karibu tena")} subtitle={t("Ingia kuona hali ya kikundi chako.")}>
      <form onSubmit={handleSubmit} className="space-y-5">
        <div>
          <label htmlFor="email" className="block text-sm font-medium text-ink-900 mb-1.5">
            {t("Barua pepe")}
          </label>
          <input
            id="email"
            type="email"
            required
            autoComplete="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className={authInputCls}
            placeholder={t("wewe@mfano.co.tz")}
          />
        </div>

        <div>
          <label htmlFor="password" className="block text-sm font-medium text-ink-900 mb-1.5">
            {t("Neno la siri")}
          </label>
          <input
            id="password"
            type="password"
            required
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className={authInputCls}
            placeholder="••••••••"
          />
        </div>

        {error && (
          <p role="alert" className="text-sm text-standing-overdue bg-standing-overdue-bg rounded-lg px-3.5 py-2.5">
            {error}
          </p>
        )}

        <button type="submit" disabled={isSubmitting} className={authButtonCls}>
          {isSubmitting ? t("Inaingia...") : t("Ingia")}
        </button>
      </form>

      <p className="mt-6 text-sm text-ink-700">
        {t("Bado huna akaunti?")}{' '}
        <Link to="/jisajili" state={location.state} className="font-medium text-ink-900 underline decoration-gold-500 underline-offset-4 hover:text-ink-700">
          {t("Jisajili")}
        </Link>
      </p>
    </AuthShell>
  );
}
