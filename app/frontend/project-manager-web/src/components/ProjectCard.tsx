import { Link } from 'react-router-dom';
import type { ProjectDto } from '../api/types';
import { getStatusDisplay, getDeadlineDisplay } from '../utils/status';

interface Props {
  project: ProjectDto;
  rank?: number;
}

export default function ProjectCard({ project, rank }: Props) {
  const status = getStatusDisplay(project);
  const deadline = getDeadlineDisplay(project.deadline);

  return (
    <Link to={`/projects/${project.id}`} className="project-card">
      <div className="project-card-top">
        {rank != null && <span className="project-rank">#{rank}</span>}
        <span className="project-name">{project.name}</span>
        <span className="project-score" title="Priority score">
          {project.priorityScore}
        </span>
      </div>

      <div className="project-card-meta">
        {project.categoryName && <span className="category-tag">{project.categoryName}</span>}
        <span className={status.className}>{status.label}</span>
        {deadline && <span className={deadline.className}>{deadline.label}</span>}
      </div>

      <div className="progress-bar">
        <div className="progress-bar-fill" style={{ width: `${project.progress}%` }} />
      </div>

      <div className="project-next-action">
        {project.currentNextAction ? (
          <>
            <span className="next-action-label">Next:</span> {project.currentNextAction.description}
          </>
        ) : (
          <span className="next-action-empty">No next action defined</span>
        )}
      </div>
    </Link>
  );
}
