import { ApiError } from '../api/client';

interface Props {
  error: unknown;
  /** When supplied, renders a Retry button that re-runs the failed load. */
  onRetry?: () => void;
  retrying?: boolean;
}

/**
 * The one place a load failure is rendered.
 *
 * The distinction it exists to draw: a request that never reached the API is a
 * different situation from one the API refused, and telling the user to "try
 * again in a moment" is only honest for the first. Once the client has already
 * retried across a full cold-start window, saying it again would be a promise
 * the app has no reason to make.
 */
export default function ErrorBanner({ error, onRetry, retrying = false }: Props) {
  const apiError = error instanceof ApiError ? error : null;
  const message = error instanceof Error ? error.message : 'Something went wrong.';

  return (
    <div className="error-banner">
      <div className="error-banner-message">{message}</div>

      {apiError?.isNetworkError && (
        <div className="error-banner-hint">
          This usually means the demo API has not been started, or is still waking up.
          Nothing you typed has been lost.
        </div>
      )}

      {onRetry && (
        <button className="btn btn-primary error-banner-retry" onClick={onRetry} disabled={retrying}>
          {retrying ? 'Retrying...' : 'Retry'}
        </button>
      )}
    </div>
  );
}
