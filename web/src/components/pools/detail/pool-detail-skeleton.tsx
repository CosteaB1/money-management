import { Card, CardContent, CardHeader } from '@/src/components/ui/card';

const ROW_KEYS = ['r1', 'r2', 'r3', 'r4'] as const;
const CELL_KEYS = ['c1', 'c2', 'c3'] as const;

/**
 * Loading skeleton mirroring the live pool-detail layout (header strip, value
 * card, roster, ledger). Kept dumb on purpose — no props — so the loading
 * shape stays stable across visual diffs. Mirrors `LoanDetailSkeleton`.
 */
export function PoolDetailSkeleton() {
  return (
    <div className="space-y-6" data-testid="pool-detail-skeleton" aria-busy="true">
      <div className="space-y-3">
        <div className="h-8 w-64 animate-pulse rounded bg-muted" />
        <div className="h-4 w-48 animate-pulse rounded bg-muted" />
      </div>

      <Card>
        <CardHeader className="pb-3">
          <div className="h-4 w-32 animate-pulse rounded bg-muted" />
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="h-8 w-40 animate-pulse rounded bg-muted" />
          <div className="grid gap-4 sm:grid-cols-3">
            {CELL_KEYS.map((key) => (
              <div key={key} className="h-12 w-full animate-pulse rounded bg-muted" />
            ))}
          </div>
        </CardContent>
      </Card>

      <div className="rounded-lg border">
        <div className="space-y-3 p-4">
          {ROW_KEYS.map((key) => (
            <div key={key} className="h-5 w-full animate-pulse rounded bg-muted" />
          ))}
        </div>
      </div>
    </div>
  );
}
