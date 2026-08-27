import { CreateLoanDialog } from '@/src/components/loans/create-loan-dialog';
import { LoansSummary } from '@/src/components/loans/loans-summary';
import { LoansTable } from '@/src/components/loans/loans-table';
import { PageHeader } from '@/src/components/page-header';

export default function LoansPage() {
  return (
    <>
      <PageHeader
        title="Loans"
        description="Track money you borrowed and lent, and the repayments over time."
        actions={<CreateLoanDialog />}
      />
      <div className="space-y-6">
        <LoansSummary />
        <LoansTable />
      </div>
    </>
  );
}
