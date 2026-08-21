import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import ErrorBoundary from './components/ErrorBoundary';
import { BASE_PATH } from './config';
import './index.css';

// BASE_PATH is import.meta.env.BASE_URL, which is exactly the value given to
// Vite's `base` option - so the router and the bundler cannot disagree about
// where the app is mounted. On GitHub Pages that is /<repo>/; locally it is /.
// React Router wants a basename without a trailing slash.
const basename = BASE_PATH.replace(/\/+$/, '');

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ErrorBoundary>
      <BrowserRouter basename={basename}>
        <App />
      </BrowserRouter>
    </ErrorBoundary>
  </StrictMode>,
);
