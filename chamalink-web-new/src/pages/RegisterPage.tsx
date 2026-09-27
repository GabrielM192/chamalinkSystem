import { useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { apiErrorMessage } from '../api/truth';
import { AuthShell, authButtonCls, authInputCls } from './auth/AuthShell';

import { t } from '../i18n';
/**
 * Kujisajili = kupata akaunti ya kuingia tu. Kuwa mwanachama ni hatua inayofuata (/karibu):
 * kuanzisha kikundi, au kuomba kujiunga kwa kodi na kusubiri idhini ya viongozi.
 */
export function RegisterPage() {
  const { register } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from;

  const [form, setForm] = useState({ fullName: '', phoneNumber: '', email: '', password: '', confirm: '' });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm((f) => ({ ...f, [k]: e.target.value }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    if (form.password !== form.confirm) {
      setError(t("Maneno ya siri hayalingani."));
      return;
    }
    setBusy(true);
    try {
      await register({ fullName: form.fullName, phoneNumber: form.phoneNumber, email: form.email, password: form.password });
      // Kiungo cha kujiunga (/jiunge/KODI) kinaendelea pale pale; vinginevyo ukurasa wa kuchagua
      navigate(from && from.startsWith('/jiunge/') ? from : '/karibu', { replace: true });
    } catch (err) {
      setError(apiErrorMessage(err, t('Imeshindwa kusajili. Jaribu tena.')));
    } finally {
      setBusy(false);
    }
  }

  const field = (id: keyof typeof form, label: string, type: string, extra: Partial<React.InputHTMLAttributes<HTMLInputElement>> = {}, hint?: string) => (
    <div>
      <label htmlFor={id} className="block text-sm font-medium text-ink-900 mb-1.5">
        {label}
      </label>
      <input id={id} type={type} required value={form[id]} onChange={set(id)} className={authInputCls} {...extra} />
      {hint && <p className="mt-1.5 text-xs text-ink-600">{hint}</p>}
    </div>
  );

  return (
    <AuthShell title={t("Fungua akaunti")} subtitle={t("Baada ya hapa utaanzisha kikundi, au utaomba kujiunga na kikundi chako.")}>
      <form onSubmit={submit} className="space-y-4">
        {field('fullName', t('Jina kamili'), 'text', { autoComplete: 'name', placeholder: t("Mf. Frank Mkoma"), minLength: 2 })}
        {field(
          'phoneNumber',
          t('Namba ya simu'),
          'tel',
          { autoComplete: 'tel', placeholder: '0712 345 678', inputMode: 'tel' },
          t('Tumia namba unayolipia M-Koba. Viongozi wataitumia kukutambua kwenye kumbukumbu za kikundi.')
        )}
        {field('email', t('Barua pepe'), 'email', { autoComplete: 'email', placeholder: 'wewe@mfano.co.tz' })}
        {field('password', t('Neno la siri'), 'password', { autoComplete: 'new-password', minLength: 6 }, t('Angalau herufi 6.'))}
        {field('confirm', t('Rudia neno la siri'), 'password', { autoComplete: 'new-password', minLength: 6 })}

        {error && (
          <p role="alert" className="text-sm text-standing-overdue bg-standing-overdue-bg rounded-lg px-3.5 py-2.5">
            {error}
          </p>
        )}

        <button type="submit" disabled={busy} className={authButtonCls}>
          {busy ? t("Inasajili...") : t("Fungua akaunti")}
        </button>
      </form>

      <p className="mt-6 text-sm text-ink-700">
        {t("Una akaunti tayari?")}{' '}
        <Link to="/login" state={location.state} className="font-medium text-ink-900 underline decoration-gold-500 underline-offset-4 hover:text-ink-700">
          {t("Ingia")}
        </Link>
      </p>
    </AuthShell>
  );
}
