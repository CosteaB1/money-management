import { PageHeader } from '@/src/components/page-header';
import { ReportsTabs } from '@/src/components/reports/reports-tabs';

export default function ReportsPage() {
  return (
    <>
      <PageHeader
        title="Reports"
        description="Income, expenses, net worth and category breakdowns."
      />
      <ReportsTabs />
    </>
  );
}
