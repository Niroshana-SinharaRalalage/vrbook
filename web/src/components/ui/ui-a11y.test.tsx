import { render } from '@testing-library/react';
import { axe, toHaveNoViolations } from 'jest-axe';
import { describe, expect, it } from 'vitest';

import {
  Badge,
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Field,
  Input,
  Skeleton,
} from './index';

expect.extend(toHaveNoViolations);

// VRB-110 — axe on the design-system primitives, exercising the states most
// likely to produce violations (an errored/described field, status badges).
describe('ui primitives — axe (WCAG 2.2 AA)', () => {
  it('a composed form (labels, description, error) has no violations', async () => {
    const { container } = render(
      <form aria-label="Profile">
        <Field
          label="Email"
          description="We send your booking confirmation here."
          error="Enter a valid email address."
          required
        >
          <Input type="email" defaultValue="not-an-email" />
        </Field>
        <Field label="Full name">
          <Input defaultValue="Ada" />
        </Field>
        <Button type="submit">Save changes</Button>
        <Button type="button" variant="outline">
          Cancel
        </Button>
      </form>,
    );
    expect(await axe(container)).toHaveNoViolations();
  });

  it('a card with status badges and a skeleton has no violations', async () => {
    const { container } = render(
      <Card>
        <CardHeader>
          <CardTitle>Sunset Villa</CardTitle>
          <CardDescription>Malibu, US</CardDescription>
        </CardHeader>
        <CardContent>
          <Badge variant="success">Confirmed</Badge> <Badge variant="warning">Pending</Badge>{' '}
          <Badge variant="destructive">Rejected</Badge>
          <Skeleton className="mt-2 h-4 w-24" />
        </CardContent>
      </Card>,
    );
    expect(await axe(container)).toHaveNoViolations();
  });
});
