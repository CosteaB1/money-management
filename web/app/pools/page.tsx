import { PageHeader } from '@/src/components/page-header';
import { CreatePoolDialog } from '@/src/components/pools/create-pool-dialog';
import { PoolsSummary } from '@/src/components/pools/pools-summary';
import { PoolsTable } from '@/src/components/pools/pools-table';

export default function PoolsPage() {
  return (
    <>
      <PageHeader
        title="Pools"
        description="Accounts that hold other people's money alongside your own. Everyone holds a share; your net worth only counts yours."
        actions={<CreatePoolDialog />}
      />
      <div className="space-y-6">
        <PoolsSummary />
        <PoolsTable />
      </div>
    </>
  );
}
