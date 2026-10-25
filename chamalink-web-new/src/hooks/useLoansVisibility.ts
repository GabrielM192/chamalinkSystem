import { useEffect, useState } from 'react';
import { loansApi } from '../api/loans';

// SASISHO16: "Mikopo" inaonekana kwenye menyu TU kama kikundi kimewasha mikopo, au kuna mikopo/maombi yaliyopo
// (mikopo iliyopo inaendelea kuhudumiwa hata huduma ikizimwa). Hii ni urahisi wa UI tu — seva ndiyo inayoamua
// kama ombi jipya linaruhusiwa.
const cache = new Map<string, boolean>();
const listeners = new Set<() => void>();

/** Ita baada ya kubadilisha Mipangilio ya mikopo ili menyu isasishwe. */
export function invalidateLoansVisibility(groupId?: string) {
  if (groupId) cache.delete(groupId);
  else cache.clear();
  listeners.forEach((l) => l());
}

export function useLoansVisibility(groupId: string | null | undefined): boolean | null {
  const [, setTick] = useState(0);
  const [state, setState] = useState<{ id: string; visible: boolean } | null>(null);

  useEffect(() => {
    const l = () => setTick((x) => x + 1);
    listeners.add(l);
    return () => {
      listeners.delete(l);
    };
  }, []);

  const cached = groupId ? cache.get(groupId) : undefined;
  useEffect(() => {
    if (!groupId || cached !== undefined) return;
    let alive = true;
    loansApi
      .policy(groupId)
      .then((p) => {
        const v = !!(p.visible ?? p.enabled);
        cache.set(groupId, v);
        if (alive) setState({ id: groupId, visible: v });
      })
      .catch(() => {
        if (alive) setState({ id: groupId, visible: false });
      });
    return () => {
      alive = false;
    };
  }, [groupId, cached]);

  if (!groupId) return null;
  if (cached !== undefined) return cached;
  return state?.id === groupId ? state.visible : null;
}
