/**
 * Currency formatting helpers built on Intl.NumberFormat.
 * All amounts are stored on the API as decimal money (proposal §6.3); we never
 * round in the UI — we format.
 */

const formatterCache = new Map<string, Intl.NumberFormat>();

const getFormatter = (currency: string, locale: string): Intl.NumberFormat => {
  const key = `${locale}::${currency}`;
  let f = formatterCache.get(key);
  if (!f) {
    f = new Intl.NumberFormat(locale, {
      style: 'currency',
      currency,
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
    formatterCache.set(key, f);
  }
  return f;
};

export const formatCurrency = (
  amount: number,
  currency: string = 'USD',
  locale: string = 'en-US',
): string => getFormatter(currency, locale).format(amount);

/** Render a money amount without the currency symbol (for compact tables). */
export const formatMoneyPlain = (amount: number, locale: string = 'en-US'): string =>
  new Intl.NumberFormat(locale, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount);
