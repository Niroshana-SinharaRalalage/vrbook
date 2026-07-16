import { describe, expect, it } from 'vitest';

import robots from './robots';

describe('robots', () => {
  it('allows the site root and disallows every private area', () => {
    const rules = robots().rules;
    const rule = Array.isArray(rules) ? rules[0] : rules;
    expect(rule?.allow).toBe('/');
    const disallow = rule?.disallow as string[];
    for (const path of ['/account', '/admin', '/auth', '/select-tenant']) {
      expect(disallow).toContain(path);
    }
  });

  it('points crawlers at the sitemap', () => {
    expect(robots().sitemap).toMatch(/\/sitemap\.xml$/);
  });
});
