'use client';

import { BalanceOverTimeSection } from '@/src/components/reports/balance-over-time-section';
import { CategoryBreakdownSection } from '@/src/components/reports/category-breakdown-section';
import { MonthlySummarySection } from '@/src/components/reports/monthly-summary-section';
import { TopPayeesSection } from '@/src/components/reports/top-payees-section';
import { YearOverYearSection } from '@/src/components/reports/year-over-year-section';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/src/components/ui/tabs';

/**
 * Top-level Tabs shell for the Reports page. Kept as a separate client
 * boundary so the page itself remains a Server Component (the `app/`
 * routing layer doesn't need any of this state).
 */
export function ReportsTabs() {
  return (
    <Tabs defaultValue="monthly" className="space-y-4" data-testid="reports-tabs">
      <TabsList className="flex flex-wrap gap-1 h-auto">
        <TabsTrigger value="monthly" data-testid="reports-tab-monthly">
          Monthly summary
        </TabsTrigger>
        <TabsTrigger value="categories" data-testid="reports-tab-categories">
          Categories
        </TabsTrigger>
        <TabsTrigger value="payees" data-testid="reports-tab-payees">
          Top payees
        </TabsTrigger>
        <TabsTrigger value="balance" data-testid="reports-tab-balance">
          Balance over time
        </TabsTrigger>
        <TabsTrigger value="yoy" data-testid="reports-tab-yoy">
          Year over year
        </TabsTrigger>
      </TabsList>
      <TabsContent value="monthly">
        <MonthlySummarySection />
      </TabsContent>
      <TabsContent value="categories">
        <CategoryBreakdownSection />
      </TabsContent>
      <TabsContent value="payees">
        <TopPayeesSection />
      </TabsContent>
      <TabsContent value="balance">
        <BalanceOverTimeSection />
      </TabsContent>
      <TabsContent value="yoy">
        <YearOverYearSection />
      </TabsContent>
    </Tabs>
  );
}
