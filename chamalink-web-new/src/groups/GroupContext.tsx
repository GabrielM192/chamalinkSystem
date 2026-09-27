import {
  createContext,
  useContext,
  useEffect,
  useState,
  useCallback,
  type ReactNode,
} from 'react';
import { useAuth } from '../auth/AuthContext';
import { getMyGroups, type MyGroupSummary } from '../api/groups';

import { t } from '../i18n';
// The single source of truth for "which group is the user looking at right
// now". Before this existed, useMyGroups fired /Group/my-groups on every
// mount - kurasa kadhaa (Dashboard, Ukweli, Ripoti, Mipangilio) each
// made their own request and each independently picked groups[0] as the
// "active" group, with no way to change it. That meant four requests for
// the same data and, worse, four separate notions of which group was
// active - so a member belonging to more than one group could never switch,
// and if the first group happened to be wrong, every screen was silently
// wrong together.
//
// GroupProvider lives inside ProtectedRoute, so it mounts only while a user
// is signed in and tears down on logout - no chance of one user's selected
// group leaking into another's session.
interface GroupContextValue {
  groups: MyGroupSummary[];
  activeGroupId: string | null;
  activeGroup: MyGroupSummary | null;
  setActiveGroupId: (id: string) => void;
  loading: boolean;
  error: string | null;
  /** Pakia upya orodha (baada ya kuanzisha kikundi au ombi la kujiunga kuidhinishwa). */
  reload: (selectGroupId?: string) => void;
}

const GroupContext = createContext<GroupContextValue | undefined>(undefined);

// Per-user storage key: the selected group is remembered across reloads,
// but scoped to the user so it can never be read by a different account.
function storageKey(userId: string | undefined): string {
  return `chamalink.activeGroup${userId ? `:${userId}` : ''}`;
}

export function GroupProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const [groups, setGroups] = useState<MyGroupSummary[]>([]);
  const [activeGroupId, setActiveGroupIdState] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [preferred, setPreferred] = useState<string | null>(null);

  // Fetch once per authenticated session. The dependency on `user` means a
  // fresh login re-runs this; a logout unmounts the provider entirely.
  useEffect(() => {
    if (!user) return;
    let cancelled = false;
    setLoading(true);
    setError(null);

    getMyGroups()
      .then((data) => {
        if (cancelled) return;
        setGroups(data);
        // Reconcile the persisted selection: keep it only if the user
        // still belongs to that group. Otherwise fall back to the first
        // group - never silently keep a group the user can no longer see,
        // which was the exact failure mode of the old SAMPLE_GROUP_ID.
        const persisted = preferred ?? localStorage.getItem(storageKey(user.userId));
        const stillValid =
          !!persisted && data.some((g) => g.groupId === persisted);
        const next = stillValid ? persisted : data[0]?.groupId ?? null;
        setActiveGroupIdState(next);
        if (next) localStorage.setItem(storageKey(user.userId), next);
      })
      .catch(() => {
        if (!cancelled) setError(t("Imeshindwa kupakia makundi yako."));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [user, reloadKey, preferred]);

  const reload = useCallback((selectGroupId?: string) => {
    if (selectGroupId) setPreferred(selectGroupId);
    setReloadKey((k) => k + 1);
  }, []);

  const setActiveGroupId = useCallback(
    (id: string) => {
      setActiveGroupIdState(id);
      if (user) localStorage.setItem(storageKey(user.userId), id);
    },
    [user]
  );

  const activeGroup = groups.find((g) => g.groupId === activeGroupId) ?? null;

  return (
    <GroupContext.Provider
      value={{ groups, activeGroupId, activeGroup, setActiveGroupId, loading, error, reload }}
    >
      {children}
    </GroupContext.Provider>
  );
}

export function useGroupContext(): GroupContextValue {
  const context = useContext(GroupContext);
  if (!context) {
    throw new Error('useGroupContext must be used within a GroupProvider');
  }
  return context;
}
