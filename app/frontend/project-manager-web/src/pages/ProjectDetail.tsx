import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import ErrorBanner from '../components/ErrorBanner';
import type { CategoryDto, ProjectDto, ProjectStatus } from '../api/types';
import { formatDate, toDateInputValue, dateInputClass, getDeadlineDisplay } from '../utils/status';

const STATUS_OPTIONS: ProjectStatus[] = ['Active', 'Blocked', 'Paused', 'Completed'];

export default function ProjectDetail() {
  const { id } = useParams<{ id: string }>();
  const projectId = Number(id);
  const navigate = useNavigate();

  const [project, setProject] = useState<ProjectDto | null>(null);
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [newActionText, setNewActionText] = useState('');
  const [newActionDate, setNewActionDate] = useState('');
  const [newCategoryText, setNewCategoryText] = useState('');
  const [showNewCategory, setShowNewCategory] = useState(false);

  // Form fields, kept separate from `project` so edits don't jump around while typing.
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [categoryId, setCategoryId] = useState<number | null>(null);
  const [impact, setImpact] = useState(5);
  const [urgency, setUrgency] = useState(5);
  const [effort, setEffort] = useState(5);
  const [status, setStatus] = useState<ProjectStatus>('Active');
  const [isBlocked, setIsBlocked] = useState(false);
  const [blockReason, setBlockReason] = useState('');
  const [deadline, setDeadline] = useState('');
  const [blockedByProjectIds, setBlockedByProjectIds] = useState<number[]>([]);
  const [otherProjects, setOtherProjects] = useState<ProjectDto[]>([]);

  const applyProjectToForm = (p: ProjectDto) => {
    setName(p.name);
    setDescription(p.description ?? '');
    setCategoryId(p.categoryId);
    setImpact(p.impact);
    setUrgency(p.urgency);
    setEffort(p.effort);
    setStatus(p.status);
    setIsBlocked(p.isBlocked);
    setBlockReason(p.blockReason ?? '');
    setDeadline(toDateInputValue(p.deadline));
    setBlockedByProjectIds(p.blockers.map((b) => b.id));
  };

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [p, cats, others] = await Promise.all([
        api.getProject(projectId),
        api.getCategories(),
        api.getProjects(),
      ]);
      setProject(p);
      setCategories(cats);
      setOtherProjects(others.filter((o) => o.id !== projectId));
      applyProjectToForm(p);
    } catch (e) {
      setError(e);
    } finally {
      setLoading(false);
    }
  }, [projectId]);

  function toggleBlocker(id: number, checked: boolean) {
    setBlockedByProjectIds((prev) => (checked ? [...prev, id] : prev.filter((x) => x !== id)));
  }

  // Candidates to link as a blocker, plus whatever's already linked (even if it's
  // since become Completed) so the checklist doesn't silently drop it.
  const blockerCandidates = [
    ...otherProjects.map((o) => ({ id: o.id, name: o.name, status: o.status })),
    ...(project?.blockers ?? []).map((b) => ({ id: b.id, name: b.name, status: b.status })),
  ].reduce<{ id: number; name: string; status: ProjectStatus }[]>((acc, item) => {
    if (!acc.some((x) => x.id === item.id)) acc.push(item);
    return acc;
  }, []).sort((a, b) => a.name.localeCompare(b.name));

  useEffect(() => {
    load();
  }, [load]);

  async function handleSave() {
    setSaving(true);
    setError(null);
    try {
      const updated = await api.updateProject(projectId, {
        name,
        description: description || null,
        categoryId,
        impact,
        urgency,
        effort,
        status,
        isBlocked,
        blockReason: isBlocked ? blockReason || null : null,
        blockedByProjectIds,
        deadline: deadline || null,
      });
      setProject(updated);
      applyProjectToForm(updated);
    } catch (e) {
      setError(e);
    } finally {
      setSaving(false);
    }
  }

  async function handleAddCategory() {
    const trimmed = newCategoryText.trim();
    if (!trimmed) return;
    const created = await api.createCategory(trimmed);
    setCategories((prev) => [...prev, created].sort((a, b) => a.name.localeCompare(b.name)));
    setCategoryId(created.id);
    setNewCategoryText('');
    setShowNewCategory(false);
  }

  async function handleMarkCompleted() {
    if (!confirm('Mark this project as completed?')) return;
    await api.completeProject(projectId);
    navigate('/completed');
  }

  async function handleDelete() {
    if (!confirm('Delete this project permanently? This cannot be undone.')) return;
    await api.deleteProject(projectId);
    navigate('/projects');
  }

  async function handleAddAction() {
    const trimmed = newActionText.trim();
    if (!trimmed) return;
    await api.createAction(projectId, trimmed, undefined, newActionDate || null);
    setNewActionText('');
    setNewActionDate('');
    await load();
  }

  async function handleToggleAction(actionId: number, done: boolean) {
    await api.updateAction(actionId, { status: done ? 'Done' : 'Pending' });
    await load();
  }

  async function handleActionDateChange(actionId: number, dateValue: string) {
    if (dateValue) {
      await api.updateAction(actionId, { availableFrom: dateValue });
    } else {
      await api.updateAction(actionId, { clearAvailableFrom: true });
    }
    await load();
  }

  async function handleDeleteAction(actionId: number) {
    await api.deleteAction(actionId);
    await load();
  }

  if (loading) return <p className="muted">Loading...</p>;
  if (!project) return <p className="muted">Project not found.</p>;

  const deadlineDisplay = getDeadlineDisplay(project.deadline);
  const doneActionCount = project.actions.filter((a) => a.status === 'Done').length;

  return (
    <div>
      <div className="page-header">
        <h1>{project.name}</h1>
        <div className="page-header-actions">
          <span className="project-score-large">Score: {project.priorityScore}</span>
          {deadlineDisplay && <span className={deadlineDisplay.className}>{deadlineDisplay.label}</span>}
        </div>
      </div>

      {error != null && <ErrorBanner error={error} onRetry={load} />}

      <div className="detail-grid">
        <section className="detail-panel">
          <h2 className="section-title">Details</h2>

          <label className="field">
            Name
            <input value={name} onChange={(e) => setName(e.target.value)} />
          </label>

          <label className="field">
            Description
            <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={3} />
          </label>

          <label className="field">
            Category
            <select
              value={categoryId ?? ''}
              onChange={(e) => {
                const value = e.target.value ? Number(e.target.value) : null;
                setCategoryId(value);
                if (value) {
                  setShowNewCategory(false);
                  setNewCategoryText('');
                }
              }}
            >
              <option value="">(none)</option>
              {categories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          </label>
          {showNewCategory ? (
            <div className="inline-add-category">
              <input
                placeholder="New category name"
                value={newCategoryText}
                onChange={(e) => setNewCategoryText(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && handleAddCategory()}
                autoFocus
              />
              <button className="btn btn-secondary" onClick={handleAddCategory} type="button">
                Add
              </button>
            </div>
          ) : (
            <button type="button" className="link-button" onClick={() => setShowNewCategory(true)}>
              + New category
            </button>
          )}

          <div className="triple-field">
            <label className="field">
              Impact ({impact})
              <input
                type="range"
                min={1}
                max={10}
                value={impact}
                onChange={(e) => setImpact(Number(e.target.value))}
              />
            </label>
            <label className="field">
              Urgency ({urgency})
              <input
                type="range"
                min={1}
                max={10}
                value={urgency}
                onChange={(e) => setUrgency(Number(e.target.value))}
              />
            </label>
            <label className="field">
              Effort ({effort})
              <input
                type="range"
                min={1}
                max={10}
                value={effort}
                onChange={(e) => setEffort(Number(e.target.value))}
              />
            </label>
          </div>

          <label className="field">
            Deadline (optional)
            <input
              type="date"
              className={dateInputClass(deadline)}
              value={deadline}
              onChange={(e) => setDeadline(e.target.value)}
              title="Only if there's a real external cutoff. Urgency ramps up automatically as it nears."
            />
          </label>

          <label className="field">
            Status
            <select value={status} onChange={(e) => setStatus(e.target.value as ProjectStatus)}>
              {STATUS_OPTIONS.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>

          <div className="field">
            Progress ({project.progress}%)
            <div className="progress-bar">
              <div className="progress-bar-fill" style={{ width: `${project.progress}%` }} />
            </div>
            <p className="muted small">
              {doneActionCount} of {project.actions.length} task
              {project.actions.length === 1 ? '' : 's'} done - calculated automatically as you
              check them off.
            </p>
          </div>

          <label className="checkbox-label">
            <input type="checkbox" checked={isBlocked} onChange={(e) => setIsBlocked(e.target.checked)} />
            This project is blocked
          </label>

          {isBlocked && (
            <label className="field">
              Block reason
              <textarea
                value={blockReason}
                onChange={(e) => setBlockReason(e.target.value)}
                rows={2}
                placeholder="Why can't this move forward?"
              />
            </label>
          )}

          <div className="field">
            Blocked by other projects
            <div className="blocker-picker">
              {blockerCandidates.length === 0 && (
                <p className="muted small">No other projects to link yet.</p>
              )}
              {blockerCandidates.map((item) => (
                <label key={item.id} className="checkbox-label">
                  <input
                    type="checkbox"
                    checked={blockedByProjectIds.includes(item.id)}
                    onChange={(e) => toggleBlocker(item.id, e.target.checked)}
                  />
                  {item.name}
                  {item.status === 'Completed' && <span className="pill pill-gray">Completed</span>}
                </label>
              ))}
            </div>
            <p className="muted small">
              This project won't be recommended until every project checked here is Completed - it
              auto-clears on its own once they're done.
            </p>
          </div>

          <div className="detail-actions">
            <button className="btn btn-primary" onClick={handleSave} disabled={saving}>
              {saving ? 'Saving...' : 'Save changes'}
            </button>
            <button className="btn btn-secondary" onClick={handleMarkCompleted} type="button">
              Mark completed
            </button>
            <button className="btn btn-danger" onClick={handleDelete} type="button">
              Delete project
            </button>
          </div>

          <p className="muted small">
            Created {formatDate(project.createdDate)} - Last updated {formatDate(project.updatedDate)}
          </p>
        </section>

        <section className="detail-panel">
          <h2 className="section-title">Actions</h2>
          <ul className="action-list">
            {project.actions.length === 0 && <li className="muted">No actions yet.</li>}
            {project.actions.map((a) => (
              <li key={a.id} className={`action-item${a.status === 'Done' ? ' action-done' : ''}`}>
                <label className="checkbox-label">
                  <input
                    type="checkbox"
                    checked={a.status === 'Done'}
                    onChange={(e) => handleToggleAction(a.id, e.target.checked)}
                  />
                  {a.description}
                  {a.status === 'Pending' && !a.isEligibleNow && (
                    <span className="date-hint">waiting until {formatDate(a.availableFrom)}</span>
                  )}
                </label>
                <div className="action-item-controls">
                  {a.status === 'Pending' && (
                    <input
                      type="date"
                      className={dateInputClass(toDateInputValue(a.availableFrom))}
                      value={toDateInputValue(a.availableFrom)}
                      onChange={(e) => handleActionDateChange(a.id, e.target.value)}
                      title="Leave blank to do anytime (ASAP). Set a date if this can't start until then."
                    />
                  )}
                  <button className="btn-icon" onClick={() => handleDeleteAction(a.id)} title="Delete action">
                    x
                  </button>
                </div>
              </li>
            ))}
          </ul>
          <div className="inline-add-category">
            <input
              placeholder="New action..."
              value={newActionText}
              onChange={(e) => setNewActionText(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleAddAction()}
            />
            <input
              type="date"
              className={dateInputClass(newActionDate)}
              value={newActionDate}
              onChange={(e) => setNewActionDate(e.target.value)}
              title="Leave blank to do anytime (ASAP). Set a date if this can't start until then."
            />
            <button className="btn btn-secondary" onClick={handleAddAction} type="button">
              Add
            </button>
          </div>
        </section>
      </div>
    </div>
  );
}
