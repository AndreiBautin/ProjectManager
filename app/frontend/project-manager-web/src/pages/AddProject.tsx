import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { CategoryDto } from '../api/types';
import { dateInputClass } from '../utils/status';

interface StepDraft {
  text: string;
  availableFrom: string; // '' = anytime/ASAP, otherwise a <input type="date"> value
}

const DEFAULTS = {
  name: '',
  description: '',
  categoryId: null as number | null,
  newCategoryText: '',
  impact: 5,
  urgency: 5,
  effort: 5,
  deadline: '',
  isBlocked: false,
  blockReason: '',
  steps: [{ text: '', availableFrom: '' }] as StepDraft[],
};

export default function AddProject() {
  const navigate = useNavigate();
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [form, setForm] = useState(DEFAULTS);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [justSaved, setJustSaved] = useState<string | null>(null);
  const [showNewCategory, setShowNewCategory] = useState(false);

  useEffect(() => {
    api.getCategories().then(setCategories).catch(() => setCategories([]));
  }, []);

  function update<K extends keyof typeof form>(key: K, value: (typeof form)[K]) {
    setForm((prev) => ({ ...prev, [key]: value }));
  }

  function updateStep(index: number, field: keyof StepDraft, value: string) {
    setForm((prev) => ({
      ...prev,
      steps: prev.steps.map((s, i) => (i === index ? { ...s, [field]: value } : s)),
    }));
  }

  function addStep() {
    setForm((prev) => ({ ...prev, steps: [...prev.steps, { text: '', availableFrom: '' }] }));
  }

  function removeStep(index: number) {
    setForm((prev) => ({
      ...prev,
      steps: prev.steps.length === 1 ? prev.steps : prev.steps.filter((_, i) => i !== index),
    }));
  }

  async function save(andAddAnother: boolean) {
    if (!form.name.trim()) {
      setError('Project name is required.');
      return;
    }
    setSaving(true);
    setError(null);
    setJustSaved(null);
    try {
      const steps = form.steps
        .map((s) => ({ text: s.text.trim(), availableFrom: s.availableFrom || null }))
        .filter((s) => s.text.length > 0);

      const created = await api.createProject({
        name: form.name.trim(),
        description: form.description || null,
        categoryId: form.categoryId,
        newCategoryName: form.categoryId ? null : form.newCategoryText.trim() || null,
        impact: form.impact,
        urgency: form.urgency,
        effort: form.effort,
        isBlocked: form.isBlocked,
        blockReason: form.isBlocked ? form.blockReason || null : null,
        deadline: form.deadline || null,
      });

      // Steps are added one at a time, in order, so the whole plan (with
      // whatever dates were given) is queued up right after creation.
      for (const step of steps) {
        await api.createAction(created.id, step.text, undefined, step.availableFrom);
      }

      if (andAddAnother) {
        setJustSaved(`Added "${created.name}".`);
        setForm(DEFAULTS);
        const cats = await api.getCategories();
        setCategories(cats);
      } else {
        navigate(`/projects/${created.id}`);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to create project.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div>
      <h1>Add project</h1>
      <p className="muted">Dump it here now, prioritize it later. Only the name is required.</p>

      {error && <div className="error-banner">{error}</div>}
      {justSaved && <div className="success-banner">{justSaved}</div>}

      <section className="detail-panel narrow">
        <label className="field">
          Name *
          <input
            value={form.name}
            onChange={(e) => update('name', e.target.value)}
            placeholder="e.g. Replace HVAC system"
            autoFocus
          />
        </label>

        <label className="field">
          Description
          <textarea
            value={form.description}
            onChange={(e) => update('description', e.target.value)}
            rows={2}
          />
        </label>

        <label className="field">
          Category
          <select
            value={form.categoryId ?? ''}
            onChange={(e) => {
              const value = e.target.value ? Number(e.target.value) : null;
              update('categoryId', value);
              if (value) {
                setShowNewCategory(false);
                update('newCategoryText', '');
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
          <label className="field">
            New category name
            <input
              value={form.newCategoryText}
              onChange={(e) => update('newCategoryText', e.target.value)}
              placeholder="e.g. Home"
              autoFocus
            />
          </label>
        ) : (
          <button type="button" className="link-button" onClick={() => setShowNewCategory(true)}>
            + New category
          </button>
        )}

        <div className="triple-field">
          <label className="field">
            Impact ({form.impact})
            <input
              type="range"
              min={1}
              max={10}
              value={form.impact}
              onChange={(e) => update('impact', Number(e.target.value))}
            />
          </label>
          <label className="field">
            Urgency ({form.urgency})
            <input
              type="range"
              min={1}
              max={10}
              value={form.urgency}
              onChange={(e) => update('urgency', Number(e.target.value))}
            />
          </label>
          <label className="field">
            Effort ({form.effort})
            <input
              type="range"
              min={1}
              max={10}
              value={form.effort}
              onChange={(e) => update('effort', Number(e.target.value))}
            />
          </label>
        </div>

        <label className="field">
          Deadline (optional)
          <input
            type="date"
            className={dateInputClass(form.deadline)}
            value={form.deadline}
            onChange={(e) => update('deadline', e.target.value)}
            title="Only if there's a real external cutoff (e.g. a trial expiring). Urgency ramps up automatically as it nears."
          />
        </label>

        <div className="field">
          Steps (optional)
          {form.steps.map((step, i) => (
            <div className="step-row" key={i}>
              <input
                value={step.text}
                onChange={(e) => updateStep(i, 'text', e.target.value)}
                placeholder={i === 0 ? 'e.g. Call second HVAC company for quote' : `Step ${i + 1}...`}
              />
              <input
                type="date"
                className={dateInputClass(step.availableFrom)}
                value={step.availableFrom}
                onChange={(e) => updateStep(i, 'availableFrom', e.target.value)}
                title="Leave blank to do anytime (ASAP). Set a date if this step can't start until then."
              />
              {form.steps.length > 1 && (
                <button
                  type="button"
                  className="btn-icon"
                  onClick={() => removeStep(i)}
                  title="Remove step"
                >
                  x
                </button>
              )}
            </div>
          ))}
          <p className="muted small">Leave a step's date blank to do it anytime. Set a date for steps you can't start until then (like a scheduled appointment).</p>
          <button type="button" className="link-button" onClick={addStep}>
            + Add another step
          </button>
        </div>

        <label className="checkbox-label">
          <input
            type="checkbox"
            checked={form.isBlocked}
            onChange={(e) => update('isBlocked', e.target.checked)}
          />
          This is blocked right now
        </label>
        {form.isBlocked && (
          <label className="field">
            Block reason
            <textarea
              value={form.blockReason}
              onChange={(e) => update('blockReason', e.target.value)}
              rows={2}
            />
          </label>
        )}

        <div className="detail-actions">
          <button className="btn btn-primary" onClick={() => save(false)} disabled={saving}>
            {saving ? 'Saving...' : 'Save project'}
          </button>
          <button className="btn btn-secondary" onClick={() => save(true)} disabled={saving}>
            Add another
          </button>
        </div>
      </section>
    </div>
  );
}
