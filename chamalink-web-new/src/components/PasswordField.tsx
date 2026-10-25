import { useState } from 'react';
import { Eye, EyeOff } from 'lucide-react';
import { t } from '../i18n';

/**
 * Sehemu ya neno la siri yenye kitufe cha "onyesha / ficha".
 *
 * Sababu: kwenye simu, kosa la herufi moja linamfanya mtu ashindwe kuingia mara tatu
 * kisha akaombe msaada kwa msimamizi. Kumruhusu kuona alichoandika kunapunguza
 * simu za msaada, na hakuna hatari: mtu anaweza kuona neno lake tu, si la mwingine.
 */
export function PasswordField({
  id,
  label,
  value,
  onChange,
  autoComplete,
  placeholder = '••••••••',
  hint,
  minLength,
  maxLength = 128,
  required = true,
  inputClassName,
  labelAction,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  autoComplete?: string;
  placeholder?: string;
  hint?: string;
  minLength?: number;
  maxLength?: number;
  required?: boolean;
  inputClassName: string;
  /** Kiungo cha pembeni ya lebo (mf. "Umesahau?") */
  labelAction?: React.ReactNode;
}) {
  const [shown, setShown] = useState(false);
  return (
    <div>
      <div className="mb-1.5 flex items-baseline justify-between gap-3">
        <label htmlFor={id} className="block text-sm font-medium text-ink-900">
          {label}
        </label>
        {labelAction}
      </div>
      <div className="relative">
        <input
          id={id}
          type={shown ? 'text' : 'password'}
          required={required}
          autoComplete={autoComplete}
          minLength={minLength}
          maxLength={maxLength}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          className={`${inputClassName} pr-11`}
        />
        <button
          type="button"
          onClick={() => setShown((s) => !s)}
          aria-label={shown ? t('Ficha neno la siri') : t('Onyesha neno la siri')}
          aria-pressed={shown}
          title={shown ? t('Ficha neno la siri') : t('Onyesha neno la siri')}
          className="absolute inset-y-0 right-0 flex w-11 items-center justify-center rounded-r-lg text-ink-500 transition-colors hover:text-ink-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-ink-700"
        >
          {shown ? <EyeOff size={17} /> : <Eye size={17} />}
        </button>
      </div>
      {hint && <p className="mt-1.5 text-xs text-ink-600">{hint}</p>}
    </div>
  );
}
