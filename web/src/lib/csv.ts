/**
 * RFC 4180 CSV writer with UTF-8 BOM + InvariantCulture-equivalent
 * formatting. See docs/SLICE7_PLAN.md §2.8 for the design.
 *
 * - BOM prefix so Excel opens non-ASCII content correctly.
 * - Cells containing `,`, `"`, `\r`, or `\n` are quoted; embedded `"`
 *   doubled.
 * - Numbers via Intl.NumberFormat('en-US', { useGrouping: false }) so
 *   the decimal separator is `.` regardless of the user's OS locale.
 * - Dates are ISO-8601 (`YYYY-MM-DD`).
 */

const numberFmt = new Intl.NumberFormat('en-US', {
  useGrouping: false,
  maximumFractionDigits: 6,
});

const BOM = '﻿';

export type CsvCell = string | number | boolean | null | undefined | Date;

const needsQuote = (s: string): boolean => /[",\r\n]/.test(s);

const formatCell = (cell: CsvCell): string => {
  if (cell === null || cell === undefined) return '';
  if (typeof cell === 'number') {
    if (!Number.isFinite(cell)) return '';
    return numberFmt.format(cell);
  }
  if (typeof cell === 'boolean') return cell ? 'true' : 'false';
  if (cell instanceof Date) {
    // ISO-8601 date portion only
    const iso = cell.toISOString();
    return iso.slice(0, 10);
  }
  return cell;
};

const escapeCell = (raw: string): string =>
  needsQuote(raw) ? `"${raw.replace(/"/g, '""')}"` : raw;

/**
 * Build a CSV string from a header row + rows.
 * Header cells must be strings (snake_case is the convention so
 * spreadsheets don't depend on locale-specific display).
 */
export const buildCsv = (headers: readonly string[], rows: readonly CsvCell[][]): string => {
  const lines = [
    headers.map((h) => escapeCell(h)).join(','),
    ...rows.map((row) => row.map((c) => escapeCell(formatCell(c))).join(',')),
  ];
  return BOM + lines.join('\r\n');
};

/**
 * Trigger a CSV download in the browser.
 */
export const downloadCsv = (
  filename: string,
  headers: readonly string[],
  rows: readonly CsvCell[][],
): void => {
  const csv = buildCsv(headers, rows);
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
};
