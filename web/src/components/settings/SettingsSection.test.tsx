import { render, screen } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import { describe, expect, it } from 'vitest';

import { SettingsSection } from './SettingsSection';

expect.extend(toHaveNoViolations);

describe('<SettingsSection /> (VRB-110-followup a11y)', () => {
  it('renders a titled section with no axe violations', async () => {
    const { container } = render(
      <SettingsSection title="Cancellation policy" description="How refunds work.">
        <p>body</p>
      </SettingsSection>,
    );
    expect(screen.getByText('Cancellation policy')).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });
});
