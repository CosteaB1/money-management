import { Card, CardContent, CardHeader } from '@/src/components/ui/card';

const ROW_KEYS = ['r1', 'r2', 'r3', 'r4'] as const;

/**
 * Loading skeleton that mirrors the live loan-detail layout (header
 * strip, Outstanding card, Payments list). Kept dumb on purpose — no
 * props — so the loading shape stays stable across visual diffs.
 */
export function LoanDetailSkeleton() {
  return (
    <div className="space-y-6" data-testid="loan-detail-skeleton" aria-busy="true">
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
          <div className="h-3 w-full animate-pulse rounded-full bg-muted" />
          <div className="h-4 w-32 animate-pulse rounded bg-muted" />
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
