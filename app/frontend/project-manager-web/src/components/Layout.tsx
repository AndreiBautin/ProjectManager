import { useEffect, useState } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { api, onRequestProgress } from '../api/client';
import type { RequestProgress } from '../api/client';
import { BUILD_COMMIT, IS_DEMO } from '../config';
import type { HealthDto } from '../api/types';

const navItems = [
  { to: '/', label: 'Command Center', end: true },
  { to: '/projects', label: 'Projects' },
  { to: '/add', label: 'Add Project' },
  { to: '/blocked', label: 'Blocked' },
  { to: '/completed', label: 'Completed' },
];

export default function Layout() {
  const [progress, setProgress] = useState<RequestProgress | null>(null);
  const [health, setHealth] = useState<HealthDto | null>(null);

  // Tells the user the free-tier API is waking up rather than leaving an
  // unexplained spinner on screen for the better part of a minute - and, once
  // the client starts retrying, says which attempt it is on so the wait reads
  // as progress rather than as a hang.
  useEffect(() => onRequestProgress(setProgress), []);

  // Build identification: which API build is this page actually talking to.
  // Failure here is not worth surfacing - it is a footer, and the pages
  // themselves already report a real connection problem.
  useEffect(() => {
    let cancelled = false;
    api
      .getHealth()
      .then((h) => !cancelled && setHealth(h))
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <div className="app-shell">
      {IS_DEMO && (
        <div className="demo-banner">
          <strong>Demo.</strong> Everything here is generated sample data, shared by all visitors, and
          reset whenever the free-tier server restarts. Edit and delete freely - nothing here is real.
        </div>
      )}

      <header className="app-header">
        <div className="app-title">Personal COO</div>
        <nav className="app-nav">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) => `nav-link${isActive ? ' nav-link-active' : ''}`}
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
      </header>

      {progress?.pending && (
        <div className="waking-banner">
          {progress.attempt > 0 ? (
            <>
              The API did not answer - retrying (attempt {progress.attempt} of{' '}
              {progress.maxAttempts}). A sleeping free-tier server takes about a minute to
              start, so this is expected on the first visit.
            </>
          ) : (
            <>
              Waking the API. The demo backend sleeps after 15 minutes of inactivity and takes
              about a minute to start - this only happens on the first request.
            </>
          )}
        </div>
      )}

      <main className="app-main">
        <Outlet />
      </main>

      <footer className="app-footer">
        <span>web {BUILD_COMMIT}</span>
        {health && <span>api {health.commit}</span>}
      </footer>
    </div>
  );
}
