import { describe, expect, it } from 'vitest';
import { proposeKeyword } from '@/src/lib/utils/propose-keyword';

describe('proposeKeyword', () => {
  it('upper-cases and keeps the first two meaningful tokens', () => {
    expect(proposeKeyword('linella srl chisinau')).toBe('LINELLA SRL');
  });

  it('drops card masks, dates and amounts', () => {
    expect(proposeKeyword('LINELLA SRL 1234*5678 04.05.2025 120.50')).toBe('LINELLA SRL');
  });

  it('skips a leading pure-number token', () => {
    expect(proposeKeyword('1234 TUCANO COFFEE')).toBe('TUCANO COFFEE');
  });

  it('handles a single meaningful word', () => {
    expect(proposeKeyword('NETFLIX')).toBe('NETFLIX');
  });

  it('falls back to the first raw token when everything is noise', () => {
    expect(proposeKeyword('04.05.2025 100.00')).toBe('04.05.2025');
  });

  it('returns empty string for blank input', () => {
    expect(proposeKeyword('   ')).toBe('');
    expect(proposeKeyword('')).toBe('');
  });

  it('treats trailing punctuation as part of a number token', () => {
    expect(proposeKeyword('PAYMENT 1,234.56,')).toBe('PAYMENT');
  });
});
