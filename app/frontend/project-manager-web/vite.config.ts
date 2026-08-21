import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const apiProxy = {
  '/api': {
    target: process.env.VITE_DEV_API_TARGET || 'http://localhost:5071',
    changeOrigin: true,
  },
}

// A GitHub Pages *project* site is served from https://<user>.github.io/<repo>/,
// so every asset URL needs that prefix. VITE_BASE_PATH carries it; locally it is
// unset and everything serves from the root.
//
// This value also reaches the router, but not by being written down twice: Vite
// exposes whatever is passed here as `import.meta.env.BASE_URL`, and src/config.ts
// derives the router basename from that. One value, two consumers.
const base = process.env.VITE_BASE_PATH || '/'

// https://vite.dev/config/
export default defineConfig({
  base,
  plugins: [react()],
  server: {
    port: 5174,
    strictPort: true,
    proxy: apiProxy,
  },
  preview: {
    port: 5174,
    proxy: apiProxy,
  },
})

// Deep links on a static host: no file exists at /projects/3, so the host 404s
// before the SPA ever loads. GitHub Pages serves 404.html for any unmatched
// path, so the build step copies index.html to 404.html and React Router picks
// the route up from the URL - see the `build` script in package.json.
//
// Honest caveat: the page renders correctly but the HTTP status really is 404.
// That is invisible to a person and visible to a crawler. Fixing it properly
// needs a host that supports rewrites, which GitHub Pages does not.
