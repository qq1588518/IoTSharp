import type { AxiosInstance } from 'axios';
import type { ApiError } from '@/api/server';

export interface GeoJsonGeometry {
  type: string;
  coordinates: unknown;
}

export interface GeoJsonFeature {
  type: 'Feature';
  geometry: GeoJsonGeometry | null;
  properties?: Record<string, unknown> | null;
}

export interface GeoJsonFeatureCollection {
  type: 'FeatureCollection';
  features: GeoJsonFeature[];
}

export interface TrajectoryQuery {
  db: string;
  measurement: string;
  from?: number;
  to?: number;
  field?: string;
  tags?: Record<string, string>;
  format?: 'points' | 'linestring';
}

export interface TrajectoryResult {
  collection: GeoJsonFeatureCollection | null;
  error: ApiError | null;
}

export async function fetchTrajectory(
  api: AxiosInstance,
  query: TrajectoryQuery,
): Promise<TrajectoryResult> {
  const params = new URLSearchParams();
  if (typeof query.from === 'number') params.set('from', String(query.from));
  if (typeof query.to === 'number') params.set('to', String(query.to));
  if (query.field) params.set('field', query.field);
  if (query.format) params.set('format', query.format);
  for (const [key, value] of Object.entries(query.tags ?? {})) {
    const trimmed = value.trim();
    if (trimmed.length > 0) params.set(key, trimmed);
  }

  const resp = await api.get<GeoJsonFeatureCollection>(
    `/v1/db/${encodeURIComponent(query.db)}/geo/${encodeURIComponent(query.measurement)}/trajectory?${params.toString()}`,
    { validateStatus: () => true },
  );

  if (resp.status >= 400) {
    const payload = resp.data as unknown as { code?: unknown; error?: unknown; message?: unknown } | null;
    return {
      collection: null,
      error: {
        code: typeof payload?.code === 'string'
          ? payload.code
          : typeof payload?.error === 'string'
            ? payload.error
            : `http_${resp.status}`,
        message: typeof payload?.message === 'string' ? payload.message : `HTTP ${resp.status}`,
      },
    };
  }

  return { collection: resp.data, error: null };
}
