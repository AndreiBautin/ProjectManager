import { API_BASE_URL, COLD_START_RETRY_DELAYS_MS, SLOW_REQUEST_HINT_MS } from '../config';
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
 * What the UI is told about a request that is taking an unusual amount of time.
 *
 * The demo API runs on a free tier that spins down after 15 minutes and takes
 * roughly a minute to wake. During that window the server does not answer slowly
 * - it does not answer *at all*, so `fetch` rejects. Reporting only "slow" was
 * not enough: the interesting state is "we failed, and we are retrying", which
 * is what `attempt` carries.
 */
export interface RequestProgress {
  /** A request has been outstanding longer than the hint threshold, or is being retried. */
  pending: boolean;
  /** 1-based retry number. 0 means the first attempt is merely slow, not yet retried. */
  attempt: number;
  /** Total attempts that will be made before giving up. */
  maxAttempts: number;
}

type ProgressListener = (progress: RequestProgress) => void;
const progressListeners = new Set<ProgressListener>();
let inFlight = 0;
let slowTimer: ReturnType<typeof setTimeout> | null = null;
let currentAttempt = 0;

const MAX_ATTEMPTS = COLD_START_RETRY_DELAYS_MS.length + 1;

export function onRequestProgress(listener: ProgressListener): () => void {
  progressListeners.add(listener);
  return () => progressListeners.delete(listener);
}

function emitProgress(pending: boolean, attempt: number) {
  const progress: RequestProgress = { pending, attempt, maxAttempts: MAX_ATTEMPTS };
  progressListeners.forEach((l) => l(progress));
}

function startTracking() {
  inFlight += 1;
  if (slowTimer === null) {
    slowTimer = setTimeout(() => emitProgress(true, currentAttempt), SLOW_REQUEST_HINT_MS);
  }
}

function stopTracking() {
  inFlight = Math.max(0, inFlight - 1);
  if (inFlight === 0) {
    if (slowTimer !== null) {
      clearTimeout(slowTimer);
      slowTimer = null;
    }
    currentAttempt = 0;
    emitProgress(false, 0);
  }
}

/**
 * A failure the user can act on, separated from a bug. `isNetworkError`
 * distinguishes "the API never answered" from "the API answered with a
 * refusal", which means the request itself was wrong. `attemptsMade` lets the
 * UI say what was actually tried instead of guessing.
 */
export class ApiError extends Error {
  readonly status: number | null;
  readonly isNetworkError: boolean;
  readonly attemptsMade: number;

  constructor(message: string, status: number | null, isNetworkError = false, attemptsMade = 1) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.isNetworkError = isNetworkError;
    this.attemptsMade = attemptsMade;
  }
}

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Retried only for requests that are safe to repeat.
 *
 * A network failure is ambiguous by construction: the browser cannot tell us
 * whether the request reached the server before the connection died. Replaying
 * a POST on that ambiguity risks creating the same project twice, so only
 * methods with no side effects are retried. A failed write surfaces immediately
 * and the user decides.
 */
function isRetryableMethod(method: string | undefined): boolean {
  const m = (method ?? 'GET').toUpperCase();
  return m === 'GET' || m === 'HEAD';
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const retryable = isRetryableMethod(options?.method);
  const maxAttempts = retryable ? MAX_ATTEMPTS : 1;

  startTracking();
  try {
    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
      let res: Response;

      try {
        res = await fetch(`${API_BASE}${path}`, {
          headers: { 'Content-Type': 'application/json' },
          ...options,
        });
      } catch {
        // fetch rejects only for transport-level failures: DNS, connection
        // refused, CORS rejection, offline. An HTTP error status resolves
        // normally and is handled below.
        if (attempt < maxAttempts) {
          currentAttempt = attempt;
          emitProgress(true, attempt);
          await sleep(COLD_START_RETRY_DELAYS_MS[attempt - 1]);
          continue;
        }

        const seconds = Math.round(
          COLD_START_RETRY_DELAYS_MS.reduce((a, b) => a + b, 0) / 1000,
        );

        throw new ApiError(
          retryable
            ? `Could not reach the API after ${maxAttempts} attempts over about ${seconds} seconds. ` +
              'It may be down rather than merely asleep.'
            : 'Could not reach the API. Your change was not saved.',
          null,
          true,
          attempt,
        );
      }

      if (!res.ok) {
        let detail = '';
        try {
          detail = await res.text();
        } catch {
          // A body we cannot read is not worth failing over; the status still
          // tells the user what happened.
        }

        if (res.status === 429) {
          throw new ApiError('Too many requests in a short window. Give it a minute and try again.', 429);
        }

        // A free instance that is mid-wake answers with a gateway error rather
        // than refusing the connection, so this is the same cold start seen
        // from a different angle - and worth the same retry.
        if (retryable && res.status >= 502 && res.status <= 504 && attempt < maxAttempts) {
          currentAttempt = attempt;
          emitProgress(true, attempt);
          await sleep(COLD_START_RETRY_DELAYS_MS[attempt - 1]);
          continue;
        }

        throw new ApiError(detail || res.statusText || `Request failed (${res.status})`, res.status, false, attempt);
      }

      if (res.status === 204) {
        return undefined as T;
      }

      return (await res.json()) as T;
    }

    // Unreachable: the loop either returns or throws. Present so the compiler
    // does not have to take that on faith.
    throw new ApiError('Could not reach the API.', null, true, maxAttempts);
  } finally {
    stopTracking();
  }
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
