import { describe, expect, it } from 'vitest';
import { buildCsv } from './csv';

describe('buildCsv', () => {
  it('prefixes with BOM and CRLF-joins rows', () => {
    const csv = buildCsv(['a', 'b'], [[1, 2]]);
    expect(csv).toBe('﻿a,b\r\n1,2');
  });

  it('quotes cells containing commas', () => {
    const csv = buildCsv(['col'], [['hello, world']]);
    expect(csv).toContain('"hello, world"');
  });

  it('doubles embedded double-quotes', () => {
    const csv = buildCsv(['col'], [['she said "hi"']]);
    expect(csv).toContain('"she said ""hi"""');
  });

  it('quotes cells with embedded newlines', () => {
    const csv = buildCsv(['col'], [['line1\nline2']]);
    expect(csv).toContain('"line1\nline2"');
  });

  it('formats numbers with `.` decimal regardless of OS locale', () => {
    const csv = buildCsv(['x'], [[1.5]]);
    expect(csv).toContain('1.5');
    expect(csv).not.toContain('1,5');
  });

  it('does not group thousands separators', () => {
    const csv = buildCsv(['x'], [[1234567.89]]);
    expect(csv).toContain('1234567.89');
    expect(csv).not.toContain('1,234,567.89');
  });

  it('renders null and undefined as empty cells', () => {
    const csv = buildCsv(['a', 'b', 'c'], [[null, undefined, 'x']]);
    expect(csv).toContain(',,x');
  });

  it('renders Date values as ISO YYYY-MM-DD', () => {
    const csv = buildCsv(['d'], [[new Date('2026-07-15T12:34:56Z')]]);
    expect(csv).toContain('2026-07-15');
  });

  it('passes non-ASCII through unmodified (BOM lets Excel decode)', () => {
    const csv = buildCsv(['name'], [['Beachside Café — São Paulo']]);
    expect(csv).toContain('Beachside Café — São Paulo');
  });

  it('handles infinity and NaN as empty cells', () => {
    const csv = buildCsv(['x'], [[Number.NaN, Number.POSITIVE_INFINITY]]);
    const rows = csv.split('\r\n');
    expect(rows[1]).toBe(',');
  });
});
