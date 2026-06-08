import { Card, CardContent, CardHeader } from '@/src/components/ui/card';

const PACE_KEYS = ['p1', 'p2', 'p3'] as const;
const ROW_KEYS = ['r1', 'r2', 'r3', 'r4'] as const;

/**
 * Loading skeleton that mirrors the live goal-detail layout (header strip,
 * Progress card, Pace card, History chart, Contributions list). Kept dumb
 * on purpose — no props, no per-section animation tweaks — so the loading
 * shape stays stable across visual diffs.
 */
export function GoalDetailSkeleton() {
  return (
    <div className="space-y-6" data-testid="goal-detail-skeleton" aria-busy="true">
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

      <Card>
        <CardHeader className="pb-3">
          <div className="h-4 w-24 animate-pulse rounded bg-muted" />
        </CardHeader>
        <CardContent>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
            {PACE_KEYS.map((key) => (
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
          <div className="h-4 w-32 animate-pulse rounded bg-muted" />
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
