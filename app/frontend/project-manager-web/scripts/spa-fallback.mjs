// GitHub Pages has no rewrite rules: a request for /projects/3 looks for a file
// that does not exist and 404s before the SPA ever loads. Pages does serve
// 404.html for any unmatched path, so copying the built shell to that name is
// what makes deep links and page refreshes work on a client-side router.
//
// The page then renders correctly, but the HTTP status really is 404 - see
// docs/DEPLOYMENT.md. That is invisible to a person and visible to a crawler.
import { copyFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const dist = join(process.cwd(), 'dist');
const index = join(dist, 'index.html');
const fallback = join(dist, '404.html');

if (!existsSync(index)) {
  console.error('spa-fallback: dist/index.html not found - did the build run?');
  process.exit(1);
}

copyFileSync(index, fallback);
console.log('spa-fallback: wrote dist/404.html');
