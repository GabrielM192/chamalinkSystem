import { apiClient } from './client';

// Matches ChamaLink.Application.DTOs.MyGroupSummaryDto exactly.
export interface MyGroupSummary {
  groupId: string;
  groupName: string;
  groupCode: string;
  role: string;
  groupType: string;
  memberStatus: string;
}

export async function getMyGroups(): Promise<MyGroupSummary[]> {
  const response = await apiClient.get<MyGroupSummary[]>('/Group/my-groups');
  return response.data;
}
