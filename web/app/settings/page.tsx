import { Coins, Database, Tags } from 'lucide-react';
import Link from 'next/link';
import { PageHeader } from '@/src/components/page-header';
import { Card, CardDescription, CardHeader, CardTitle } from '@/src/components/ui/card';

const SETTINGS_SECTIONS = [
  {
    href: '/settings/categories',
    title: 'Categories',
    description:
      'Manage transaction categories and the keyword rules that auto-categorize imported statements.',
    icon: Tags,
  },
  {
    href: '/settings/fx-rates',
    title: 'FX rates',
    description:
      'MDL reporting rates that feed every MDL-equivalent total. Add manual rates or refresh from BNM.',
    icon: Coins,
  },
  {
    href: '/settings/data',
    title: 'Data',
    description: 'Export a full JSON backup, or restore the whole app from one.',
    icon: Database,
  },
] as const;

export default function SettingsPage() {
  return (
    <>
      <PageHeader title="Settings" description="Manage FX rates and your data backups." />
      <div className="grid gap-4 sm:grid-cols-2">
        {SETTINGS_SECTIONS.map((section) => {
          const Icon = section.icon;
          return (
            <Link
              key={section.href}
              href={section.href}
              data-testid={`settings-link-${section.title.toLowerCase().replace(/\s+/g, '-')}`}
              className="rounded-lg ring-offset-background transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
            >
              <Card className="h-full transition-colors hover:bg-accent">
                <CardHeader>
                  <CardTitle className="flex items-center gap-2">
                    <Icon className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden />
                    {section.title}
                  </CardTitle>
                  <CardDescription>{section.description}</CardDescription>
                </CardHeader>
              </Card>
            </Link>
          );
        })}
      </div>
    </>
  );
}
