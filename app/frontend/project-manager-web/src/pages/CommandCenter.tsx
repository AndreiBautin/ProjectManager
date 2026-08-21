import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import ErrorBanner from '../components/ErrorBanner';
import type { ProjectDto, RecommendationResult } from '../api/types';
import ProjectCard from '../components/ProjectCard';

export default function CommandCenter() {
  const [recommendation, setRecommendation] = useState<RecommendationResult | null>(null);
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [marking, setMarking] = useState(false);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [rec, list] = await Promise.all([
        api.getRecommendation(),
        api.getProjects('Active,Blocked'),
      ]);
      setRecommendation(rec);
      setProjects(list);
    } catch (e) {
      setError(e);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  async function handleMarkDone() {
    if (!recommendation?.actionId) return;
    setMarking(true);
    try {
      await api.updateAction(recommendation.actionId, { status: 'Done' });
      await load();
    } catch (e) {
      setError(e);
    } finally {
      setMarking(false);
    }
  }

  if (loading) return <p className="muted">Loading...</p>;

  return (
    <div>
      {error != null && <ErrorBanner error={error} onRetry={load} />}

      <section className="hero-card">
        <div className="hero-label">Recommended next action</div>
        {recommendation?.projectId ? (
          <>
            <div className="hero-project">{recommendation.projectName}</div>
            <div className="hero-action">{recommendation.actionDescription}</div>
            <div className="hero-reason">{recommendation.reason}</div>
            <button className="btn btn-primary" onClick={handleMarkDone} disabled={marking}>
              {marking ? 'Marking done...' : 'Mark done'}
            </button>
          </>
        ) : (
          <div className="hero-empty">{recommendation?.reason ?? 'Nothing to recommend yet.'}</div>
        )}
      </section>

      <section>
        <h2 className="section-title">Active priorities</h2>
        {projects.length === 0 ? (
          <p className="muted">
            Nothing active yet. <Link to="/add">Add a project</Link> to get started.
          </p>
        ) : (
          <div className="project-list">
            {projects.map((p, i) => (
              <ProjectCard key={p.id} project={p} rank={i + 1} />
            ))}
          </div>
        )}
      </section>
    </div>
  );
}
