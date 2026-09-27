import { useEffect, useState } from 'react';
import { apiErrorMessage } from '../api/truth';

/**
 * Kupakia data kutoka API bila "setState ndani ya effect" na bila kuonyesha
 * data ya zamani ya kitu kingine.
 *
 *  - `scope`  : utambulisho wa KITU (mf. groupId, memberId). Ukibadilika, data
 *               ya zamani inafichwa mara moja (loading) — hakuna kuchanganya vikundi.
 *  - `deps`   : vichocheo vya KUPAKIA UPYA kitu kilekile (mf. `version`). Data ya
 *               zamani inabaki kuonekana wakati mpya inakuja (hakuna "flash").
 *  - fetcher = null => hakuna ombi.
 */
export function useApi<T>(fetcher: (() => Promise<T>) | null, scope: unknown, deps: unknown[] = []) {
  const scopeKey = JSON.stringify(scope);
  const key = JSON.stringify([scope, ...deps]);
  const [st, setSt] = useState<{ key: string | null; scopeKey: string | null; data?: T; error?: string }>({
    key: null,
    scopeKey: null,
  });

  useEffect(() => {
    if (!fetcher) return;
    let cancelled = false;
    fetcher()
      .then((data) => !cancelled && setSt({ key, scopeKey, data }))
      .catch((e) => !cancelled && setSt({ key, scopeKey, error: apiErrorMessage(e) }));
    return () => {
      cancelled = true;
    };
    // `fetcher` ni closure mpya kila render; `key` ndiyo inayowakilisha vigezo vyake.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, !!fetcher]);

  const sameScope = st.scopeKey === scopeKey;
  const current = st.key === key;
  return {
    data: sameScope ? st.data : undefined,
    error: current ? st.error : undefined,
    loading: !!fetcher && !current,
  };
}
