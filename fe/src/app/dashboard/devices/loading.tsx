import { DeviceTableSkeleton, PageHeaderSkeleton } from '@/components/Skeletons';
import { Card } from 'antd';

export default function DevicesLoading() {
  return (
    <div style={{ padding: '0' }}>
      <PageHeaderSkeleton />
      <Card size="small">
        <DeviceTableSkeleton rows={8} />
      </Card>
    </div>
  );
}
