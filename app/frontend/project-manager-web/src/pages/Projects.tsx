import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import ErrorBanner from '../components/ErrorBanner';
import type { ProjectDto } from '../api/types';
import ProjectCard from '../components/ProjectCard';

export default function Projects() {
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [includePaused, setIncludePaused] = useState(true);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  const load = useCallback(async (withPaused: boolean) => {
    setLoading(true);
    setError(null);
    try {
      const list = await api.getProjects(withPaused ? undefined : 'Active,Blocked');
      setProjects(list);
    } catch (e) {
      setError(e);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load(includePaused);
  }, [includePaused, load]);

  return (
    <div>
      <div className="page-header">
        <h1>Projects</h1>
        <div className="page-header-actions">
          <label className="checkbox-label">
            <input
              type="checkbox"
              checked={includePaused}
              onChange={(e) => setIncludePaused(e.target.checked)}
            />
            Show paused
          </label>
          <Link to="/add" className="btn btn-primary">
            + Add project
          </Link>
        </div>
      </div>

      {error != null && <ErrorBanner error={error} onRetry={() => load(includePaused)} />}

      {loading ? (
        <p className="muted">Loading...</p>
      ) : projects.length === 0 ? (
        <p className="muted">No projects yet.</p>
      ) : (
        <div className="project-list">
          {projects.map((p) => (
            <ProjectCard key={p.id} project={p} />
          ))}
        </div>
      )}
    </div>
  );
}
