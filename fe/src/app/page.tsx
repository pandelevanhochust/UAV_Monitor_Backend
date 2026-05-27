import { redirect } from 'next/navigation';

/**
 * Root page — immediately redirects.
 * The proxy.ts middleware handles auth-gating, so:
 *   - Unauthenticated → middleware redirects here → /login
 *   - Authenticated   → middleware redirects here → /dashboard
 * This page exists as a fallback in case middleware config changes.
 */
export default function RootPage() {
  redirect('/dashboard');
}
