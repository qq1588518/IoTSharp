import type { AxiosInstance } from 'axios';

export interface SetupStatus {
  needsSetup: boolean;
  suggestedServerId: string;
  serverId: string | null;
  organization: string | null;
  userCount: number;
  databaseCount: number;
}

export interface SetupInitializePayload {
  serverId: string;
  organization: string;
  username: string;
  password: string;
  bearerToken: string;
}

export interface SetupInitializeResponse {
  serverId: string;
  organization: string;
  username: string;
  token: string;
  tokenId: string;
  isSuperuser: boolean;
}

export async function fetchSetupStatus(api: AxiosInstance): Promise<SetupStatus> {
  const resp = await api.get<SetupStatus>('/v1/setup/status');
  return resp.data;
}

export async function initializeSetup(
  api: AxiosInstance,
  payload: SetupInitializePayload,
): Promise<SetupInitializeResponse> {
  const resp = await api.post<SetupInitializeResponse>('/v1/setup/initialize', payload);
  return resp.data;
}
