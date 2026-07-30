import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
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
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .getProjects('Completed')
      .then((list) =>
        setProjects(
          [...list].sort(
            (a, b) => new Date(b.completedDate ?? 0).getTime() - new Date(a.completedDate ?? 0).getTime(),
          ),
        ),
      )
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load.'))
      .finally(() => setLoading(false));
  }, []);

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
      {error && <div className="error-banner">{error}</div>}

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
