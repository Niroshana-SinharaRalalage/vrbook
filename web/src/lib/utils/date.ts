/**
 * Date formatting helpers. The API exchanges dates as ISO-8601 strings
 * (`checkinDate: "2026-07-12"` per proposal §6.3); we keep them as strings
 * end-to-end and only parse for display.
 */

const dateFormatterCache = new Map<string, Intl.DateTimeFormat>();

const getFormatter = (locale: string, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat => {
  const key = `${locale}::${JSON.stringify(options)}`;
  let f = dateFormatterCache.get(key);
  if (!f) {
    f = new Intl.DateTimeFormat(locale, options);
    dateFormatterCache.set(key, f);
  }
  return f;
};

/** "Jul 12, 2026" */
export const formatDateShort = (iso: string, locale: string = 'en-US'): string =>
  getFormatter(locale, { month: 'short', day: 'numeric', year: 'numeric' }).format(new Date(iso));

/** "Sunday, July 12, 2026" */
export const formatDateLong = (iso: string, locale: string = 'en-US'): string =>
  getFormatter(locale, {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    year: 'numeric',
  }).format(new Date(iso));

/** "Jul 12 – Jul 19" (omits year if same as the other date). */
export const formatDateRange = (
  fromIso: string,
  toIso: string,
  locale: string = 'en-US',
): string => {
  const from = new Date(fromIso);
  const to = new Date(toIso);
  const sameYear = from.getFullYear() === to.getFullYear();
  const fromFmt = getFormatter(locale, {
    month: 'short',
    day: 'numeric',
    year: sameYear ? undefined : 'numeric',
  }).format(from);
  const toFmt = getFormatter(locale, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  }).format(to);
  return `${fromFmt} – ${toFmt}`;
};

/** Whole-night count between two ISO dates (checkout - checkin). */
export const nightsBetween = (checkinIso: string, checkoutIso: string): number => {
  const ms = new Date(checkoutIso).getTime() - new Date(checkinIso).getTime();
  return Math.max(0, Math.round(ms / (1000 * 60 * 60 * 24)));
};
