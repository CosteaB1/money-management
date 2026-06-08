'use client';

import { AlertTriangle, Loader2, Upload } from 'lucide-react';
import * as React from 'react';
import { toast } from 'sonner';
import { Button } from '@/src/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/src/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/src/components/ui/dialog';
import { Input } from '@/src/components/ui/input';
import { Label } from '@/src/components/ui/label';
import { useImportData } from '@/src/lib/api/data';
import type { ImportDataResponse } from '@/src/types/api';

/**
 * Turns an {@link ImportDataResponse} into a one-line human summary of the
 * restored row counts. Walks the response's numeric entries defensively —
 * excluding the `schemaVersion` discriminator — so minor field-name drift
 * from the backend still renders. Field keys are de-camelCased for display.
 */
export function summarizeImport(result: ImportDataResponse): string {
  const parts: string[] = [];
  for (const [key, value] of Object.entries(result)) {
    if (key === 'schemaVersion') continue;
    if (typeof value !== 'number') continue;
    parts.push(`${value} ${humanizeKey(key)}`);
  }
  return parts.length > 0 ? `Restored ${parts.join(', ')}.` : 'Restore complete.';
}

function humanizeKey(key: string): string {
  return key
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .toLowerCase()
    .trim();
}

/**
 * Maps a caught error to a user-facing message. ApiError extends Error, so the
 * single `instanceof Error` check covers both the backend's RFC-7807
 * ProblemDetails (ApiError) and a fetch-level TypeError; the friendly fallback
 * is only hit for a non-Error throw. Exported for unit testing.
 */
export function errorMessage(err: unknown): string {
  return err instanceof Error
    ? err.message
    : 'Failed to restore the backup. Check the file and try again.';
}

/**
 * "Import" half of the Data page. The user picks a `.json` backup, then
 * "Restore from backup" opens a destructive confirmation dialog before any
 * data is touched. Confirming POSTs the file via {@link useImportData},
 * toasts the restored counts, and resets the picker. The restore REPLACES
 * ALL existing data, so the warning copy is deliberately blunt.
 */
export function ImportBackupCard() {
  const [file, setFile] = React.useState<File | null>(null);
  const [confirmOpen, setConfirmOpen] = React.useState(false);
  const [errorText, setErrorText] = React.useState<string | null>(null);
  const fileInputRef = React.useRef<HTMLInputElement>(null);
  const { mutateAsync, isPending } = useImportData();

  const resetPicker = () => {
    setFile(null);
    if (fileInputRef.current) fileInputRef.current.value = '';
  };

  const handleFileChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setErrorText(null);
    setFile(event.target.files?.[0] ?? null);
  };

  const handleConfirm = async () => {
    if (!file) return;
    setErrorText(null);
    try {
      const result = await mutateAsync(file);
      setConfirmOpen(false);
      resetPicker();
      toast.success(summarizeImport(result));
    } catch (err) {
      const message = errorMessage(err);
      setErrorText(message);
      setConfirmOpen(false);
      toast.error(message);
    }
  };

  return (
    <Card data-testid="import-backup-card">
      <CardHeader>
        <CardTitle>Import</CardTitle>
        <CardDescription>
          Restore from a previously exported JSON backup. This replaces everything currently in the
          app with the contents of the file.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="backup-file">Backup file</Label>
          <Input
            ref={fileInputRef}
            id="backup-file"
            type="file"
            accept=".json,application/json"
            onChange={handleFileChange}
            data-testid="backup-file-input"
            aria-describedby={errorText ? 'backup-import-error' : undefined}
          />
          {file && (
            <p className="text-sm text-muted-foreground" data-testid="selected-backup-name">
              Selected: {file.name}
            </p>
          )}
          {errorText && (
            <p id="backup-import-error" className="text-sm text-destructive" role="alert">
              {errorText}
            </p>
          )}
        </div>

        <Button
          type="button"
          variant="destructive"
          disabled={!file || isPending}
          onClick={() => {
            setErrorText(null);
            setConfirmOpen(true);
          }}
          data-testid="restore-backup-button"
        >
          <Upload className="h-4 w-4" aria-hidden />
          Restore from backup
        </Button>
      </CardContent>

      <Dialog
        open={confirmOpen}
        onOpenChange={(open) => {
          // Don't let the dialog close mid-flight.
          if (isPending) return;
          setConfirmOpen(open);
        }}
      >
        <DialogContent data-testid="restore-confirm-dialog">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <AlertTriangle className="h-5 w-5 text-destructive" aria-hidden />
              Replace all data?
            </DialogTitle>
            <DialogDescription data-testid="restore-confirm-warning">
              This permanently REPLACES ALL current data (accounts, transactions, categories,
              budgets, goals) with the contents of this backup. This cannot be undone.
            </DialogDescription>
          </DialogHeader>
          {file && (
            <p className="text-sm text-muted-foreground">
              Restoring from <span className="font-medium text-foreground">{file.name}</span>.
            </p>
          )}
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              disabled={isPending}
              onClick={() => setConfirmOpen(false)}
              data-testid="restore-cancel-button"
            >
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              disabled={isPending}
              onClick={handleConfirm}
              data-testid="restore-confirm-button"
            >
              {isPending && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />}
              {isPending ? 'Restoring...' : 'Yes, replace everything'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Card>
  );
}
