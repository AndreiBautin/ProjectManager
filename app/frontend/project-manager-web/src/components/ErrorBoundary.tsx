import { Component } from 'react';
import type { ErrorInfo, ReactNode } from 'react';
import { BUILD_COMMIT } from '../config';

interface Props {
  children: ReactNode;
}

interface State {
  error: Error | null;
}

/**
 * Catches render-time exceptions so a bug in one component degrades to a
 * recoverable screen rather than a blank white page.
 *
 * <p>
 * This is the production/development split the app needs most visibly. A
 * developer gets the message and the component stack; a visitor gets a
 * recovery path, the build id to quote, and no internals. React error
 * boundaries only catch errors thrown during render, in lifecycle methods and
 * in constructors below them - not in event handlers, and not in async code.
 * Those paths are handled where they occur, by ApiError in the API client.
 * </p>
 *
 * Still a class component because React provides no hook equivalent of
 * componentDidCatch.
 */
export default class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // The platform's own log is the destination. Deliberately no third-party
    // error reporting: it would mean shipping this app's contents to a vendor
    // for information a personal project does not need.
    console.error('Unhandled render error', error, info.componentStack);
  }

  handleReset = () => {
    this.setState({ error: null });
  };

  render() {
    const { error } = this.state;
    if (!error) return this.props.children;

    return (
      <div className="app-shell">
        <main className="app-main">
          <div className="error-boundary">
            <h1>Something broke on this screen.</h1>
            <p className="muted">
              The rest of the app is fine - your data has not been touched. Try again, or reload the page.
            </p>

            <div className="error-boundary-actions">
              <button className="btn btn-primary" onClick={this.handleReset}>
                Try again
              </button>
              <button className="btn" onClick={() => window.location.reload()}>
                Reload
              </button>
            </div>

            {import.meta.env.DEV ? (
              <pre className="error-boundary-detail">
                {error.name}: {error.message}
                {error.stack ? `\n\n${error.stack}` : ''}
              </pre>
            ) : (
              <p className="muted error-boundary-build">Build {BUILD_COMMIT}</p>
            )}
          </div>
        </main>
      </div>
    );
  }
}
