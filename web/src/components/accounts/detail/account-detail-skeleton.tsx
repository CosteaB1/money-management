import { Card, CardContent, CardHeader } from '@/src/components/ui/card';

const STAT_KEYS = ['s1', 's2', 's3', 's4'] as const;
const ROW_KEYS = ['r1', 'r2', 'r3', 'r4', 'r5'] as const;

/**
 * Loading skeleton that mirrors the live detail-page layout (header strip,
 * Performance card, Balance-over-time card, Activity table). Kept dumb on
 * purpose — no props, no animation tweaks per section — so the loading
 * shape stays predictable for visual diffs.
 */
export function AccountDetailSkeleton() {
  return (
    <div className="space-y-6" data-testid="account-detail-skeleton" aria-busy="true">
      <div className="space-y-3">
        <div className="h-8 w-64 animate-pulse rounded bg-muted" />
        <div className="h-4 w-48 animate-pulse rounded bg-muted" />
      </div>

      <Card>
        <CardHeader className="pb-3">
          <div className="h-4 w-40 animate-pulse rounded bg-muted" />
        </CardHeader>
        <CardContent>
          <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
            {STAT_KEYS.map((key) => (
              <div key={key} className="space-y-2">
                <div className="h-3 w-24 animate-pulse rounded bg-muted" />
                <div className="h-6 w-32 animate-pulse rounded bg-muted" />
                <div className="h-3 w-20 animate-pulse rounded bg-muted" />
              </div>
            ))}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="pb-3">
          <div className="h-4 w-40 animate-pulse rounded bg-muted" />
        </CardHeader>
        <CardContent>
          <div className="h-72 w-full animate-pulse rounded bg-muted" />
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
