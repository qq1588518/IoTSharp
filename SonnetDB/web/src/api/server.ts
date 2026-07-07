import type { AxiosInstance, AxiosResponse } from 'axios';

export interface ApiError {
  code: string;
  message: string;
}

export interface DatabaseListResult {
  databases: string[];
  error: ApiError | null;
}

export interface SegmentCountsResult {
  counts: Record<string, number>;
  error: ApiError | null;
}

export async function listDatabases(api: AxiosInstance): Promise<DatabaseListResult> {
  const resp = await api.get('/v1/db', { validateStatus: () => true });
  if (resp.status >= 400) {
    return { databases: [], error: toApiError(resp) };
  }

  const raw = resp.data as { databases?: unknown } | null | undefined;
  const databases = Array.isArray(raw?.databases)
    ? raw.databases.map((value) => String(value))
    : [];

  return { databases, error: null };
}

export async function loadSegmentCounts(api: AxiosInstance): Promise<SegmentCountsResult> {
  const resp = await api.get('/metrics', {
    responseType: 'text',
    transformResponse: (value) => value,
    validateStatus: () => true,
  });

  if (resp.status >= 400 || typeof resp.data !== 'string') {
    return { counts: {}, error: toApiError(resp) };
  }

  const counts: Record<string, number> = {};
  const lines = resp.data.split(/\r?\n/);
  for (const rawLine of lines) {
    const line = rawLine.trim();
    const match = /^sonnetdb_segments\{db="([^"]+)"\}\s+([0-9]+(?:\.[0-9]+)?)$/.exec(line);
    if (!match) {
      continue;
    }

    const [, database, rawCount] = match;
    const value = Number(rawCount);
    counts[database] = Number.isFinite(value) ? value : 0;
  }

  return { counts, error: null };
}

function toApiError(resp: AxiosResponse<unknown>): ApiError {
  const payload = resp.data as { code?: unknown; error?: unknown; message?: unknown } | string | null | undefined;
  if (payload && typeof payload === 'object') {
    const code = typeof payload.code === 'string'
      ? payload.code
      : typeof payload.error === 'string'
        ? payload.error
        : `http_${resp.status}`;
    const message = typeof payload.message === 'string' ? payload.message : `HTTP ${resp.status}`;
    return { code, message };
  }

  return { code: `http_${resp.status}`, message: `HTTP ${resp.status}` };
}
