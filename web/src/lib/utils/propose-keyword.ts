/**
 * Derive a candidate auto-categorization keyword from a raw transaction memo.
 *
 * Used by the PDF import preview's "learn-with-confirm" affordance: when the
 * user categorizes a row the suggester missed, we pre-fill an editable keyword
 * here so committing the import can persist a category pattern for NEXT time.
 *
 * The heuristic is intentionally simple — the user edits the result before it
 * is saved, so it only needs to land a reasonable first guess:
 *   1. Upper-case the whole memo (patterns are matched upper-cased server-side).
 *   2. Split on whitespace.
 *   3. Drop "noise" tokens: pure digits, card masks (anything containing `*`),
 *      and things that look like dates or money amounts.
 *   4. Take the first 1–2 surviving tokens.
 *   5. Fall back to the first raw word when nothing survives the filter.
 *
 * @param description Raw statement memo (e.g. "LINELLA SRL 1234*5678 04.05").
 * @returns An upper-cased keyword candidate (possibly empty if `description` is).
 */
export function proposeKeyword(description: string): string {
  const upper = (description ?? '').toUpperCase().trim();
  if (upper.length === 0) return '';

  const tokens = upper.split(/\s+/).filter((t) => t.length > 0);
  if (tokens.length === 0) return '';

  const meaningful = tokens.filter((token) => !isNoiseToken(token));

  // Prefer the first 1–2 meaningful tokens; otherwise fall back to the first
  // raw token so the user always has something to edit rather than a blank box.
  const picked = meaningful.length > 0 ? meaningful.slice(0, 2) : tokens.slice(0, 1);
  return picked.join(' ');
}

/**
 * A token is "noise" when it carries no payee signal: card masks, pure
 * numbers, dates, and money amounts. Punctuation is stripped before the
 * digit/amount checks so trailing commas/periods don't hide a number.
 */
function isNoiseToken(token: string): boolean {
  // Card masks / PAN fragments: any token containing an asterisk.
  if (token.includes('*')) return true;

  const stripped = token.replace(/[.,;:]+$/g, '').replace(/^[.,;:]+/g, '');
  if (stripped.length === 0) return true;

  // All-digits (optionally separated by ./-/: — covers "1234", "04.05.2025",
  // "12:30", "100-200").
  if (/^[\d./\-:]+$/.test(stripped)) return true;

  // Money amounts: "100", "1,234.56", "12.50", optional leading sign.
  if (/^[+-]?\d[\d,]*(\.\d+)?$/.test(stripped)) return true;

  return false;
}
