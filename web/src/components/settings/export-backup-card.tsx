'use client';

import { Download } from 'lucide-react';
import { Button } from '@/src/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/src/components/ui/card';
import { downloadBackup } from '@/src/lib/api/data';

interface ExportBackupCardProps {
  /** Override the API base URL — tests only; real callers leave it undefined. */
  apiBaseUrl?: string;
}

/**
 * "Export" half of the Data page. Triggers a browser download of the full
 * JSON backup via an anchor-click (no fetch), mirroring `ExportCsvButton`.
 */
export function ExportBackupCard({ apiBaseUrl }: ExportBackupCardProps) {
  return (
    <Card data-testid="export-backup-card">
      <CardHeader>
        <CardTitle>Export</CardTitle>
        <CardDescription>
          Download a complete JSON snapshot of your data — accounts, transactions, categories,
          budgets, and goals. Keep it somewhere safe; it's everything needed to restore this app
          from scratch.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <Button
          type="button"
          onClick={() => downloadBackup(apiBaseUrl)}
          data-testid="download-backup-button"
        >
          <Download className="h-4 w-4" aria-hidden />
          Download backup
        </Button>
      </CardContent>
    </Card>
  );
}
