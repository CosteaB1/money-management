import { PageHeader } from '@/src/components/page-header';
import { BackfillBnmRatesButton } from '@/src/components/settings/backfill-bnm-rates-button';
import { CreateFxRateDialog } from '@/src/components/settings/create-fx-rate-dialog';
import { FxRatesTable } from '@/src/components/settings/fx-rates-table';
import { RefreshBnmRatesButton } from '@/src/components/settings/refresh-bnm-rates-button';

export default function FxRatesPage() {
  return (
    <>
      <PageHeader
        title="FX rates"
        description="MDL is the reporting currency. These rates feed the dashboard's MDL-equivalent totals and any cross-currency conversions."
        actions={
          <>
            <RefreshBnmRatesButton />
            <BackfillBnmRatesButton />
            <CreateFxRateDialog />
          </>
        }
      />
      <FxRatesTable />
    </>
  );
}
