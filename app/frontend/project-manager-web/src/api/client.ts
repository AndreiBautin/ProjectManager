import type {
  ProjectDto,
  CategoryDto,
  RecommendationResult,
  CreateProjectRequest,
  UpdateProjectRequest,
  ActionDto,
} from './types';

// Relative path - the Vite dev server proxies /api to the backend (see vite.config.ts).
const API_BASE = '/api';

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });

  if (!res.ok) {
    let detail = '';
    try {
      detail = await res.text();
    } catch {
      // ignore
    }
    throw new Error(`Request failed (${res.status}): ${detail || res.statusText}`);
  }

  if (res.status === 204) {
    return undefined as T;
  }

  return (await res.json()) as T;
}

export const api = {
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
