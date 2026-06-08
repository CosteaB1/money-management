import { BudgetsTable } from '@/src/components/budgets/budgets-table';
import { CreateBudgetDialog } from '@/src/components/budgets/create-budget-dialog';
import { RebuildPeriodsButton } from '@/src/components/budgets/rebuild-periods-button';
import { PageHeader } from '@/src/components/page-header';

export default function BudgetsPage() {
  return (
    <>
      <PageHeader
        title="Budgets"
        description="Monthly spending limits per category."
        actions={
          <div className="flex items-center gap-2">
            <RebuildPeriodsButton />
            <CreateBudgetDialog />
          </div>
        }
      />
      <BudgetsTable />
    </>
  );
}
