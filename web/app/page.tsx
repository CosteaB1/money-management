import { AccountCards } from '@/src/components/dashboard/account-card';
import { BudgetProgress } from '@/src/components/dashboard/budget-progress';
import { IOweCard } from '@/src/components/dashboard/i-owe-card';
import { MonthlySummaryCard } from '@/src/components/dashboard/monthly-summary-card';
import { NetWorthCard } from '@/src/components/dashboard/net-worth-card';
import { NetWorthTrendChart } from '@/src/components/dashboard/net-worth-trend-chart';
import { RecentTransactions } from '@/src/components/dashboard/recent-transactions';
import { SavingsGoals } from '@/src/components/dashboard/savings-goals';
import { TotalAssetsCard } from '@/src/components/dashboard/total-assets-card';
import { PageHeader } from '@/src/components/page-header';

export default function DashboardPage() {
  return (
    <>
      <PageHeader title="Dashboard" description="A snapshot of your finances." />

      <div className="grid grid-cols-1 gap-4 md:grid-cols-12 md:gap-6">
        {/* Row 1 reads left to right as the net-worth equation itself:
            assets − what you owe = net worth. Splitting the old single
            card into three is the whole point of the layout — the middle
            tile is the term the client-side sum used to drop. */}
        <div className="md:col-span-4">
          <TotalAssetsCard />
        </div>
        <div className="md:col-span-4">
          <IOweCard />
        </div>
        <div className="md:col-span-4">
          <NetWorthCard />
        </div>

        <div className="md:col-span-12">
          <MonthlySummaryCard />
        </div>

        <div className="md:col-span-12">
          <NetWorthTrendChart />
        </div>

        <div className="md:col-span-7">
          <AccountCards />
        </div>
        <div className="md:col-span-5">
          <BudgetProgress />
        </div>

        <div className="md:col-span-5">
          <SavingsGoals />
        </div>
        <div className="md:col-span-7">
          <RecentTransactions />
        </div>
      </div>
    </>
  );
}
