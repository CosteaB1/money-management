import { PageHeader } from '@/src/components/page-header';
import { CategoriesManager } from '@/src/components/settings/categories-manager';

export default function CategoriesSettingsPage() {
  return (
    <>
      <PageHeader
        title="Categories"
        description="Manage your transaction categories and the keyword rules that auto-categorize imported statements."
      />
      <div className="space-y-6">
        <CategoriesManager />
      </div>
    </>
  );
}
