'use client';

import { TriangleAlert } from 'lucide-react';
import { cn } from '@/src/lib/utils/cn';
import { formatShortDate } from '@/src/lib/utils/date';
import { markStaleness } from '@/src/lib/utils/pool';

interface Props {
  /** ISO date (yyyy-MM-dd) of the last confirmed valuation, or null. */
  lastMarkDate: string | null;
  markAgeDays: number | null;
}

/**
 * "This pool's value has not been confirmed recently."
 *
 * **The single most important warning on the page.** Every share is minted or
 * burned at `poolValue / sharesOutstanding`, and the app's `poolValue` is only
 * as fresh as the last figure the user typed. Strike a price against a
 * three-week-old mark in an account returning ~11% a month and somebody bought
 * or sold at the wrong number — permanently, invisibly, and in someone else's
 * favour. The design calls stale marks "the main way this model goes quietly
 * wrong", which is why this sits above the value rather than below it.
 *
 * Silent under a week. Amber from a week. Red from a month, and red again when
 * the pool has never been valued at all — a pool with no mark has priced every
 * share it ever issued against a balance nobody confirmed.
 */
export function MarkStalenessWarning({ lastMarkDate, markAgeDays }: Props) {
  const level = markStaleness(markAgeDays);
  if (level === 'fresh') return null;

  const critical = level === 'critical' || level === 'never';

  return (
    <div
      className={cn(
        'flex items-start gap-2 rounded-md border p-3 text-sm',
        critical
          ? 'border-destructive/50 bg-destructive/10 text-destructive'
          : 'border-amber-500/50 bg-amber-500/10 text-amber-700 dark:text-amber-400',
      )}
      role="alert"
      data-testid="pool-mark-staleness"
      data-level={level}
    >
      <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden />
      <div className="space-y-1">
        {level === 'never' ? (
          <p>
            <strong>This pool has never been valued.</strong> Every share issued so far was priced
            against a balance nobody confirmed.
          </p>
        ) : (
          <p>
            <strong>
              Value last confirmed {markAgeDays} day{markAgeDays === 1 ? '' : 's'} ago
            </strong>
            {lastMarkDate !== null && <> ({formatShortDate(lastMarkDate)})</>}.
          </p>
        )}
        <p>
          Every share is priced against the pool&rsquo;s value, so anything recorded against a stale
          figure hands value to the wrong person and cannot be undone. Read the exchange total and
          record it before the next movement.
        </p>
      </div>
    </div>
  );
}
