import { AlertFeedSkeleton, PageHeaderSkeleton } from '@/components/Skeletons';

export default function AlertsLoading() {
  return (
    <div>
      <PageHeaderSkeleton />
      <AlertFeedSkeleton items={4} />
    </div>
  );
}
