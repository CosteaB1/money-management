import { PageHeader } from '@/src/components/page-header';
import { ExportBackupCard } from '@/src/components/settings/export-backup-card';
import { ImportBackupCard } from '@/src/components/settings/import-backup-card';

export default function DataSettingsPage() {
  return (
    <>
      <PageHeader
        title="Data"
        description="Back up everything to a single JSON file, or restore the whole app from one. Useful before risky changes or when moving between machines."
      />
      <div className="grid gap-6 md:grid-cols-2">
        <ExportBackupCard />
        <ImportBackupCard />
      </div>
    </>
  );
}
