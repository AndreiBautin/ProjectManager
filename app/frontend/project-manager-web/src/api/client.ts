import { API_BASE_URL, SLOW_REQUEST_HINT_MS } from '../config';
import type {
  ProjectDto,
  CategoryDto,
  RecommendationResult,
  CreateProjectRequest,
  UpdateProjectRequest,
  ActionDto,
  HealthDto,
} from './types';

// Locally this is the relative path '/api', which the Vite dev server proxies to
// the backend. Deployed it is an absolute origin, because the SPA and the API are
// hosted separately. See src/config.ts.
const API_BASE = API_BASE_URL;

/**
 * Fires when a request has been outstanding long enough that it is probably a
 * cold start rather than a slow network. The demo API runs on a free tier that
 * spins down after inactivity and takes about a minute to wake; a spinner with
 * no explanation for that long reads as a broken site, so the UI says what is
 * actually happening instead.
 */
type SlowListener = (isSlow: boolean) => void;
const slowListeners = new Set<SlowListener>();
let inFlight = 0;
let slowTimer: ReturnType<typeof setTimeout> | null = null;

export function onSlowRequest(listener: SlowListener): () => void {
  slowListeners.add(listener);
  return () => slowListeners.delete(listener);
}

function emitSlow(isSlow: boolean) {
  slowListeners.forEach((l) => l(isSlow));
}

function startTracking() {
  inFlight += 1;
  if (slowTimer === null) {
    slowTimer = setTimeout(() => emitSlow(true), SLOW_REQUEST_HINT_MS);
  }
}

function stopTracking() {
  inFlight = Math.max(0, inFlight - 1);
  if (inFlight === 0) {
    if (slowTimer !== null) {
      clearTimeout(slowTimer);
      slowTimer = null;
    }
    emitSlow(false);
  }
}

/**
 * A failure that the user can act on, separated from a bug. `isNetworkError`
 * distinguishes "the API did not answer" - which on the demo usually means it is
 * still waking up - from "the API answered with a refusal", which means the
 * request itself was wrong.
 */
export class ApiError extends Error {
  readonly status: number | null;
  readonly isNetworkError: boolean;

  constructor(message: string, status: number | null, isNetworkError = false) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.isNetworkError = isNetworkError;
  }
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  startTracking();
  let res: Response;

  try {
    res = await fetch(`${API_BASE}${path}`, {
      headers: { 'Content-Type': 'application/json' },
      ...options,
    });
  } catch {
    // fetch only rejects for transport-level failures: DNS, connection refused,
    // CORS rejection, offline. An HTTP error status resolves normally.
    throw new ApiError(
      'Could not reach the API. If this is the live demo, the free-tier server may still be waking up - try again in a moment.',
      null,
      true,
    );
  } finally {
    stopTracking();
  }

  if (!res.ok) {
    let detail = '';
    try {
      detail = await res.text();
    } catch {
      // A body we cannot read is not worth failing over; the status still tells
      // the user what happened.
    }

    if (res.status === 429) {
      throw new ApiError('Too many requests in a short window. Give it a minute and try again.', 429);
    }

    throw new ApiError(detail || res.statusText || `Request failed (${res.status})`, res.status);
  }

  if (res.status === 204) {
    return undefined as T;
  }

  return (await res.json()) as T;
}

export const api = {
  getHealth: () => request<HealthDto>('/health'),

  getProjects: (status?: string) =>
    request<ProjectDto[]>(`/projects${status ? `?status=${encodeURIComponent(status)}` : ''}`),

  getProject: (id: number) => request<ProjectDto>(`/projects/${id}`),

  createProject: (payload: CreateProjectRequest) =>
    request<ProjectDto>('/projects', { method: 'POST', body: JSON.stringify(payload) }),

  updateProject: (id: number, payload: UpdateProjectRequest) =>
    request<ProjectDto>(`/projects/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),

  completeProject: (id: number) =>
    request<ProjectDto>(`/projects/${id}/complete`, { method: 'POST' }),

  deleteProject: (id: number) =>
    request<void>(`/projects/${id}`, { method: 'DELETE' }),

  getCategories: () => request<CategoryDto[]>('/categories'),

  createCategory: (name: string) =>
    request<CategoryDto>('/categories', { method: 'POST', body: JSON.stringify({ name }) }),

  createAction: (projectId: number, description: string, order?: number, availableFrom?: string | null) =>
    request<ActionDto>(`/projects/${projectId}/actions`, {
      method: 'POST',
      body: JSON.stringify({ description, order, availableFrom: availableFrom || null }),
    }),

  updateAction: (
    id: number,
    payload: {
      description?: string;
      status?: string;
      order?: number;
      availableFrom?: string | null;
      clearAvailableFrom?: boolean;
    },
  ) => request<ActionDto>(`/actions/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),

  deleteAction: (id: number) => request<void>(`/actions/${id}`, { method: 'DELETE' }),

  getRecommendation: () => request<RecommendationResult>('/recommendation'),
};
