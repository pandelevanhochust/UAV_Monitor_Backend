import { DashboardSkeleton } from '@/components/Skeletons';

/**
 * Next.js App Router loading UI for the /dashboard route segment.
 * Shown while the dashboard layout and page are loading.
 * Automatically replaced when the page is ready.
 */
export default function DashboardLoading() {
  return (
    <div style={{ padding: 24 }}>
      <DashboardSkeleton />
    </div>
  );
}
