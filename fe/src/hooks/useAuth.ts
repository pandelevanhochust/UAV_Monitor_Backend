'use client';

import useSWR from 'swr';
import { useRouter } from 'next/navigation';

// ─── Types ─────────────────────────────────────────────────────────────────

export interface UserInfo {
  id: string;
  role: 'Admin' | 'Monitor';
  email: string | null;
  name: string | null;
}

// ─── SWR fetcher ────────────────────────────────────────────────────────────

const fetcher = async (url: string): Promise<UserInfo> => {
  const res = await fetch(url, { cache: 'no-store' });
  if (!res.ok) {
    const err = new Error('Not authenticated');
    throw err;
  }
  return res.json();
};

// ─── useAuth hook ────────────────────────────────────────────────────────────

/**
 * useAuth()
 *
 * Client-side hook that provides:
 *   - `user`      — the current authenticated user's identity (from JWT claims via /api/auth/me)
 *   - `isLoading` — true while the SWR fetch is in-flight
 *   - `isAdmin`   — convenience boolean for role checks
 *   - `login()`   — POSTs credentials to /api/auth/login, redirects to /dashboard on success
 *   - `logout()`  — POSTs to /api/auth/logout, clears SWR cache, redirects to /login
 */
export function useAuth() {
  const router = useRouter();

  const {
    data: user,
    error,
    isLoading,
    mutate,
  } = useSWR<UserInfo>('/api/auth/me', fetcher, {
    // Don't retry on 401 — user is simply not logged in
    shouldRetryOnError: false,
    // Re-validate when window regains focus (tab switching)
    revalidateOnFocus: true,
  });

  const isAdmin = user?.role === 'Admin';
  const isAuthenticated = !!user && !error;

  /**
   * login — calls the Next.js server-side login proxy.
   * Throws an Error with a user-facing message on failure.
   */
  async function login(email: string, password: string): Promise<void> {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    });

    if (!res.ok) {
      let message = 'Login failed. Please check your credentials.';
      try {
        const data = await res.json();
        message = data.message ?? message;
      } catch {
        // Non-JSON error body — use default message
      }
      throw new Error(message);
    }

    // Revalidate user info after login sets the cookie
    await mutate();
    router.push('/dashboard');
    router.refresh(); // Force Next.js to re-run proxy.ts with the new cookie
  }

  /**
   * logout — clears the server-side cookie and resets SWR state.
   */
  async function logout(): Promise<void> {
    await fetch('/api/auth/logout', { method: 'POST' });
    await mutate(undefined, { revalidate: false });
    router.push('/login');
    router.refresh();
  }

  return {
    user,
    isLoading,
    isError: !!error,
    isAuthenticated,
    isAdmin,
    login,
    logout,
  };
}
