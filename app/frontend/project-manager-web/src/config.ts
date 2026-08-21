/**
 * Every value that differs between local development, the demo deployment and
 * a production build, read in one place.
 *
 * Vite inlines `import.meta.env.*` at build time, so these are build-time
 * configuration, not runtime configuration - changing them means rebuilding.
 * That is the right trade for a static site: there is no server to read an
 * environment variable at request time.
 *
 * NOTE ON THE `VITE_` PREFIX: Vite only exposes variables beginning with
 * `VITE_` to client code, and everything it exposes is compiled into the
 * JavaScript bundle that anyone can read. A `VITE_`-prefixed variable is
 * therefore PUBLIC by definition and must never hold a credential. This app
 * has no client-side secrets, and it should stay that way.
 */

/**
 * Where the API lives.
 *
 * Unset (local development): the relative path `/api`, which the Vite dev
 * server proxies to the backend on :5071 - see vite.config.ts.
 *
 * Set (deployed): an absolute origin, because the SPA is served from GitHub
 * Pages and the API from a different host entirely. That cross-origin call is
 * what the backend's CORS allow-list exists for.
 */
export const API_BASE_URL: string = (import.meta.env.VITE_API_BASE_URL ?? '/api').replace(/\/+$/, '');

/**
 * The single source of truth for the deployment base path.
 *
 * A GitHub Pages project site is served from `/<repo>/`, not from `/`. Both the
 * bundler's asset base and the router's basename have to agree about that, and
 * the classic failure is setting one and forgetting the other: assets load
 * correctly and then every route 404s, or vice versa. `import.meta.env.BASE_URL`
 * IS the value passed to Vite's `base` option, so deriving the router basename
 * from it means there is exactly one place the path is configured.
 */
export const BASE_PATH: string = import.meta.env.BASE_URL;

/** Injected by CI so a deployed page can be tied back to the commit that built it. */
export const BUILD_COMMIT: string = import.meta.env.VITE_BUILD_COMMIT ?? 'dev';

/** True when this build points at a public demo rather than a personal instance. */
export const IS_DEMO: boolean = import.meta.env.VITE_DEMO_MODE === 'true';

/**
 * How long to wait before telling the user the API is probably asleep rather
 * than broken. The demo backend runs on a free tier that spins down after
 * inactivity and takes roughly a minute to wake, and an unexplained spinner for
 * that long reads as a broken site.
 */
export const SLOW_REQUEST_HINT_MS = 2500;

/**
 * Backoff schedule for retrying a read that could not reach the API.
 *
 * A sleeping free-tier instance does not answer slowly - it does not answer at
 * all, so the very first request after 15 minutes of quiet *fails*. Without
 * retries the first visitor gets a dead end and has to know to reload, which
 * is exactly the wrong first impression.
 *
 * These five delays span roughly 75 seconds, which comfortably covers Render's
 * documented cold start of about a minute. They are deliberately increasing:
 * an instance that is genuinely gone should not be hammered, and one that is
 * waking needs time rather than frequency.
 */
export const COLD_START_RETRY_DELAYS_MS = [2000, 5000, 12000, 25000, 30000];
