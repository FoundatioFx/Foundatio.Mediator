import { authApi, type LoginRequest, type UserInfo } from '$lib/api';

/**
 * Reactive authentication store using Svelte 5 runes.
 * Tracks the current user and provides login / logout / check helpers.
 */
function createAuthStore() {
  let user = $state<UserInfo | null>(null);
  let loading = $state(true);
  let error = $state<string | null>(null);

  return {
    get user() {
      return user;
    },
    get loading() {
      return loading;
    },
    get error() {
      return error;
    },
    get isAuthenticated() {
      return user !== null;
    },

    /** Check the server for an existing session cookie. */
    async check() {
      loading = true;
      error = null;
      try {
        const response = await authApi.getCurrentUser();
        user = response.status === 200 ? (response.data ?? null) : null;
      } catch {
        user = null;
      } finally {
        loading = false;
      }
    },

    /** Log in with username / password. */
    async login(request: LoginRequest) {
      loading = true;
      error = null;
      try {
        const response = await authApi.login(request);
        if (response.status === 200 && response.data) {
          user = response.data;
        } else {
          error = 'Invalid username or password';
        }
      } catch {
        error = 'Invalid username or password';
      } finally {
        loading = false;
      }
    },

    /** Log out and clear the session. */
    async logout() {
      try {
        await authApi.logout();
      } finally {
        user = null;
      }
    }
  };
}

export const auth = createAuthStore();
