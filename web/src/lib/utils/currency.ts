// Show the ISO code (e.g. "MDL", "USD") rather than the locale symbol —
// Moldova's leu renders as a bare "L" otherwise, which is ambiguous.
const formatter = new Intl.NumberFormat('ro-MD', {
  style: 'currency',
  currency: 'MDL',
  currencyDisplay: 'code',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

export function formatMDL(amount: number): string {
  return formatter.format(amount);
}

export function formatMDLCompact(amount: number): string {
  return new Intl.NumberFormat('ro-MD', {
    style: 'currency',
    currency: 'MDL',
    currencyDisplay: 'code',
    notation: 'compact',
    maximumFractionDigits: 1,
  }).format(amount);
}

/**
 * Effective cross-currency rate label, computed LOCALLY as
 * `sourceAmount / destinationAmount` (source-ccy per dest-ccy). Returns null
 * when the inputs can't yield a finite positive rate (e.g. a blank/zero
 * destination amount), so callers can hide the line. ~4–6 significant figures.
 *
 * Example: 17163 MDL → 1000 USD ⇒ "Rate: 17.163 MDL/USD".
 */
export function formatEffectiveRate(
  sourceAmount: number,
  destinationAmount: number,
  sourceCurrency: string,
  destinationCurrency: string,
): string | null {
  if (
    !Number.isFinite(sourceAmount) ||
    !Number.isFinite(destinationAmount) ||
    sourceAmount <= 0 ||
    destinationAmount <= 0
  ) {
    return null;
  }
  const rate = sourceAmount / destinationAmount;
  const formatted = new Intl.NumberFormat('en-US', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
    useGrouping: false,
  }).format(rate);
  return `Rate: ${formatted} ${sourceCurrency}/${destinationCurrency}`;
}

/**
 * Format a money amount in an arbitrary ISO 4217 currency.
 * Falls back to MDL if the supplied currency code is invalid.
 */
export function formatMoney(amount: number, currency: string): string {
  try {
    return new Intl.NumberFormat('ro-MD', {
      style: 'currency',
      currency,
      currencyDisplay: 'code',
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(amount);
  } catch {
    return formatter.format(amount);
  }
}
