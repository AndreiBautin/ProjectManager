import type { ProjectDto } from '../api/types';

export interface StatusDisplay {
  label: string;
  className: string;
}

// Mirrors the color-coding from the design doc:
// green = Moving Forward, amber = Blocked-but-actionable, blue = Waiting on a
// date (has a next action, just not eligible yet), purple = waiting on other
// projects (this project's own next action isn't the unblock step - those
// other projects' actions are), red = Blocked-stuck (no defined next action),
// gray = Paused/Completed/no-op.
export function getStatusDisplay(project: ProjectDto): StatusDisplay {
  const action = project.currentNextAction;

  if (project.status === 'Completed') {
    return { label: 'Completed', className: 'pill pill-gray' };
  }
  if (project.status === 'Paused') {
    return { label: 'Paused', className: 'pill pill-gray' };
  }

  if (project.status === 'Blocked' && project.isBlockedByProjects) {
    return { label: 'Blocked - waiting on other projects', className: 'pill pill-purple' };
  }

  if (!action) {
    return project.status === 'Blocked'
      ? { label: 'Blocked - stuck', className: 'pill pill-red' }
      : { label: 'No next action', className: 'pill pill-gray' };
  }

  if (!action.isEligibleNow) {
    return { label: `Waiting until ${formatDate(action.availableFrom)}`, className: 'pill pill-blue' };
  }

  return project.status === 'Blocked'
    ? { label: 'Blocked - actionable', className: 'pill pill-amber' }
    : { label: 'Moving Forward', className: 'pill pill-green' };
}

export function formatDate(iso: string | null): string {
  if (!iso) return '-';
  const d = new Date(iso);
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

// For populating <input type="date">, which needs a plain "YYYY-MM-DD".
export function toDateInputValue(iso: string | null): string {
  if (!iso) return '';
  return iso.slice(0, 10);
}

// className for a <input type="date">, greying out the "mm/dd/yyyy" hint
// when empty so it matches every other placeholder in the app.
export function dateInputClass(value: string): string {
  return value ? 'date-input' : 'date-input date-input-empty';
}

// Mirrors the backend's PriorityEngine.DeadlineRampDays - purely for display,
// the actual score math only ever happens server-side.
const DEADLINE_RAMP_DAYS = 14;

function daysUntil(iso: string): number {
  const target = new Date(iso);
  target.setHours(0, 0, 0, 0);
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  return Math.round((target.getTime() - today.getTime()) / (1000 * 60 * 60 * 24));
}

// Red once it's inside the window where it's actively pulling urgency up
// (or overdue), gray while it's just informational and not affecting score yet.
export function getDeadlineDisplay(deadline: string | null): StatusDisplay | null {
  if (!deadline) return null;
  const days = daysUntil(deadline);
  const isRamping = days <= DEADLINE_RAMP_DAYS;

  let label: string;
  if (days < 0) label = `Overdue by ${Math.abs(days)}d`;
  else if (days === 0) label = 'Due today';
  else if (days === 1) label = 'Due tomorrow';
  else label = `Due in ${days}d`;

  return { label, className: isRamping ? 'pill pill-red' : 'pill pill-gray' };
}
