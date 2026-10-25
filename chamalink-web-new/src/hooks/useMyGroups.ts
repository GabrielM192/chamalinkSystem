import { useGroupContext } from '../groups/groupContextCore';
import type { MyGroupSummary } from '../api/groups';

interface UseMyGroupsResult {
  groups: MyGroupSummary[];
  // The group the user is currently looking at. Owned by GroupProvider
  // (see groups/GroupContext.tsx) so every screen that calls this hook
  // agrees on the same group - and so the header's switcher can change
  // it for all of them at once. Persisted across reloads per user.
  activeGroupId: string | null;
  activeGroup: MyGroupSummary | null;
  setActiveGroupId: (id: string) => void;
  loading: boolean;
  error: string | null;
  reload: (selectGroupId?: string) => void;
}

// Thin consumer of GroupContext. Previously this hook fetched
// /Group/my-groups on every mount - several pages
// each fired their own request and each
// independently picked groups[0] as "active", with no way to switch.
// Now a single GroupProvider (mounted in ProtectedRoute) fetches once
// and owns the active group, so the four screens never disagree.
export function useMyGroups(): UseMyGroupsResult {
  return useGroupContext();
}
