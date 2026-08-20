import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import type { ProjectDto, RecommendationResult } from '../api/types';
import { formatDate } from '../utils/status';

export default function Blocked() {
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [recommendation, setRecommendation] = useState<RecommendationResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([api.getProjects('Blocked'), api.getRecommendation()])
      .then(([list, rec]) => {
        setProjects(list);
        setRecommendation(rec);
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load.'))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <p className="muted">Loading...</p>;

  return (
    <div>
      <h1>Blocked</h1>
      {error && <div className="error-banner">{error}</div>}

      {projects.length === 0 ? (
        <p className="muted">Nothing is blocked right now.</p>
      ) : (
        <div className="blocked-list">
          {projects.map((p) => (
            <Link key={p.id} to={`/projects/${p.id}`} className="blocked-card">
              <div className="blocked-card-top">
                <span className="project-name">{p.name}</span>
                <span className="project-score">{p.priorityScore}</span>
              </div>

              {p.isBlockedByProjects && (
                <div className="blocked-reason">
                  <strong>Blocked by:</strong>{' '}
                  {p.blockers
                    .filter((b) => !b.isResolved)
                    .map((b) => b.name)
                    .join(', ')}
                </div>
              )}

              {p.blockReason && (
                <div className="blocked-reason">
                  <strong>Blocked because:</strong> {p.blockReason}
                </div>
              )}

              {p.isBlockedByProjects ? (
                <div className="blocked-unblock">
                  <strong>Own next step (not actionable yet):</strong>{' '}
                  {p.currentNextAction ? p.currentNextAction.description : '(none defined yet)'}
                </div>
              ) : (
                <div className="blocked-unblock">
                  <strong>Unblock action:</strong>{' '}
                  {p.currentNextAction ? p.currentNextAction.description : '(none defined yet)'}
                  {p.currentNextAction && !p.currentNextAction.isEligibleNow && (
                    <span className="date-hint"> - waiting until {formatDate(p.currentNextAction.availableFrom)}</span>
                  )}
                </div>
              )}

              {recommendation?.projectId === p.id && (
                <div className="recommended-flag">Currently the top recommendation</div>
              )}
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
