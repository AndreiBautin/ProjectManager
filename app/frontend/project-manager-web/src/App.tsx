import { Route, Routes } from 'react-router-dom';
import Layout from './components/Layout';
import CommandCenter from './pages/CommandCenter';
import Projects from './pages/Projects';
import ProjectDetail from './pages/ProjectDetail';
import AddProject from './pages/AddProject';
import Blocked from './pages/Blocked';
import Completed from './pages/Completed';

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<CommandCenter />} />
        <Route path="projects" element={<Projects />} />
        <Route path="projects/:id" element={<ProjectDetail />} />
        <Route path="add" element={<AddProject />} />
        <Route path="blocked" element={<Blocked />} />
        <Route path="completed" element={<Completed />} />
      </Route>
    </Routes>
  );
}
