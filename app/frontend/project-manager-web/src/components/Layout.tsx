import { NavLink, Outlet } from 'react-router-dom';

const navItems = [
  { to: '/', label: 'Command Center', end: true },
  { to: '/projects', label: 'Projects' },
  { to: '/add', label: 'Add Project' },
  { to: '/blocked', label: 'Blocked' },
  { to: '/completed', label: 'Completed' },
];

export default function Layout() {
  return (
    <div className="app-shell">
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
      <main className="app-main">
        <Outlet />
      </main>
    </div>
  );
}
