import { CreateGoalDialog } from '@/src/components/goals/create-goal-dialog';
import { GoalsTable } from '@/src/components/goals/goals-table';
import { PageHeader } from '@/src/components/page-header';

export default function GoalsPage() {
  return (
    <>
      <PageHeader
        title="Goals"
        description="Track progress toward savings targets."
        actions={<CreateGoalDialog />}
      />
      <GoalsTable />
    </>
  );
}
