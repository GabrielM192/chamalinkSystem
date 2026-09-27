import { createContext, useContext } from 'react';
import type { MemberLite, TruthSettings } from '../../api/truth';

// Hali inayoshirikiwa na tabo zote za /ukweli: kikundi, wanachama,
// mipangilio, na "version" — inaongezeka baada ya kila badiliko ili kila
// tabo ipakie upya data yake (hakuna data ya zamani inayobaki kwenye skrini).
export interface TruthCtx {
  groupId: string;
  groupName: string;
  role: string;
  canReset: boolean;
  members: MemberLite[];
  settings: TruthSettings | null;
  version: number;
  /** Baada ya kuandika chochote: pakia upya wanachama, mipangilio, idadi ya idhini na tabo zote. */
  refresh: () => void;
  goTab: (tab: string, params?: Record<string, string>) => void;
  /** Fungua taarifa ya mwanachama kwenye paneli. */
  openMember: (memberId: string) => void;
  pendingCount: number;
}

export const TruthContext = createContext<TruthCtx | null>(null);

export function useTruth(): TruthCtx {
  const c = useContext(TruthContext);
  if (!c) throw new Error('useTruth nje ya TruthPage');
  return c;
}
