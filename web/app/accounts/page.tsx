import { AccountsTable } from '@/src/components/accounts/accounts-table';
import { CreateAccountDialog } from '@/src/components/accounts/create-account-dialog';
import { PageHeader } from '@/src/components/page-header';

export default function AccountsPage() {
  return (
    <>
      <PageHeader
        title="Accounts"
        description="Bank, deposit, investment and crypto accounts."
        actions={<CreateAccountDialog />}
      />
      <AccountsTable />
    </>
  );
}
