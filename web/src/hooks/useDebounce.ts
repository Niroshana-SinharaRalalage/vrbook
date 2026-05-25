'use client';

import { useEffect, useState } from 'react';

/**
 * Debounce a fast-changing value (search-as-you-type, etc).
 * Returns the value after it has been stable for `delayMs`.
 */
export const useDebounce = <T,>(value: T, delayMs: number = 250): T => {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(t);
  }, [value, delayMs]);

  return debounced;
};
