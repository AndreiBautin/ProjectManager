export type ProjectStatus = 'Active' | 'Blocked' | 'Paused' | 'Completed';
export type ActionStatus = 'Pending' | 'Done';

export interface BlockerRef {
  id: number;
  name: string;
  status: ProjectStatus;
  isResolved: boolean;
}

export interface ActionDto {
  id: number;
  projectId: number;
  description: string;
  status: ActionStatus;
  order: number;
  availableFrom: string | null;
  isEligibleNow: boolean;
  createdDate: string;
  completedDate: string | null;
}

export interface ProjectDto {
  id: number;
  name: string;
  description: string | null;
  categoryId: number | null;
  categoryName: string | null;
  impact: number;
  urgency: number;
  effort: number;
  priorityScore: number;
  status: ProjectStatus;
  progress: number;
  isBlocked: boolean;
  blockReason: string | null;
  isBlockedByProjects: boolean;
  blockers: BlockerRef[];
  deadline: string | null;
  createdDate: string;
  updatedDate: string;
  completedDate: string | null;
  currentNextAction: ActionDto | null;
  actions: ActionDto[];
}

export interface CategoryDto {
  id: number;
  name: string;
}

export interface RecommendationResult {
  projectId: number | null;
  projectName: string | null;
  actionId: number | null;
  actionDescription: string | null;
  reason: string;
}

export interface CreateProjectRequest {
  name: string;
  description?: string | null;
  categoryId?: number | null;
  newCategoryName?: string | null;
  impact?: number;
  urgency?: number;
  effort?: number;
  isBlocked?: boolean;
  blockReason?: string | null;
  blockedByProjectIds?: number[] | null;
  firstActionDescription?: string | null;
  deadline?: string | null;
}

export interface UpdateProjectRequest {
  name: string;
  description: string | null;
  categoryId: number | null;
  impact: number;
  urgency: number;
  effort: number;
  status: ProjectStatus;
  isBlocked: boolean;
  blockReason: string | null;
  blockedByProjectIds: number[] | null;
  deadline: string | null;
}
