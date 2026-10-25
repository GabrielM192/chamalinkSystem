import { createContext, useContext } from 'react';
import type { MyGroupSummary } from '../api/groups';

export interface GroupContextValue {
  groups: MyGroupSummary[];
  activeGroupId: string | null;
  activeGroup: MyGroupSummary | null;
  setActiveGroupId: (id: string) => void;
  loading: boolean;
  error: string | null;
  /** Pakia upya orodha (baada ya kuanzisha kikundi au ombi la kujiunga kuidhinishwa). */
  reload: (selectGroupId?: string) => void;
}

export const GroupContext = createContext<GroupContextValue | undefined>(undefined);

export function useGroupContext(): GroupContextValue {
  const context = useContext(GroupContext);
  if (!context) {
    throw new Error('useGroupContext must be used within a GroupProvider');
  }
  return context;
}
