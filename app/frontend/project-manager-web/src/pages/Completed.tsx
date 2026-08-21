import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import ErrorBanner from '../components/ErrorBanner';
import type { ProjectDto } from '../api/types';
import { formatDate } from '../utils/status';

function isWithinDays(iso: string | null, days: number): boolean {
  if (!iso) return false;
  const diff = Date.now() - new Date(iso).getTime();
  return diff <= days * 24 * 60 * 60 * 1000;
}

export default function Completed() {
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  // Named rather than inline so the error banner's Retry can re-run exactly the
  // same load, instead of the user having to reload the whole page.
  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const list = await api.getProjects('Completed');
      setProjects(
        [...list].sort(
          (a, b) => new Date(b.completedDate ?? 0).getTime() - new Date(a.completedDate ?? 0).getTime(),
        ),
      );
    } catch (e) {
      setError(e);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const counts = useMemo(
    () => ({
      month: projects.filter((p) => isWithinDays(p.completedDate, 30)).length,
      quarter: projects.filter((p) => isWithinDays(p.completedDate, 90)).length,
      allTime: projects.length,
    }),
    [projects],
  );

  if (loading) return <p className="muted">Loading...</p>;

  return (
    <div>
      <h1>Completed</h1>
      {error != null && <ErrorBanner error={error} onRetry={load} />}

      <div className="stats-row">
        <div className="stat-box">
          <div className="stat-number">{counts.month}</div>
          <div className="stat-label">Last 30 days</div>
        </div>
        <div className="stat-box">
          <div className="stat-number">{counts.quarter}</div>
          <div className="stat-label">Last 90 days</div>
        </div>
        <div className="stat-box">
          <div className="stat-number">{counts.allTime}</div>
          <div className="stat-label">All time</div>
        </div>
      </div>

      {projects.length === 0 ? (
        <p className="muted">Nothing completed yet - finish something!</p>
      ) : (
        <ul className="completed-list">
          {projects.map((p) => (
            <li key={p.id}>
              <Link to={`/projects/${p.id}`}>{p.name}</Link>
              <span className="muted"> - completed {formatDate(p.completedDate)}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
