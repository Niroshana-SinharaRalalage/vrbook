import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { Field, Label } from './Field';
import { Input } from './Input';

describe('<Label />', () => {
  it('renders a <label> that points at its control', () => {
    render(<Label htmlFor="x">Email</Label>);
    const label = screen.getByText('Email');
    expect(label.tagName).toBe('LABEL');
    expect(label).toHaveAttribute('for', 'x');
  });
});

describe('<Field />', () => {
  it('associates the label with the control (clickable/lookup by label text)', () => {
    render(
      <Field label="Email">
        <Input type="email" />
      </Field>,
    );
    // getByLabelText only succeeds when htmlFor <-> id are wired.
    expect(screen.getByLabelText('Email')).toBeInstanceOf(HTMLInputElement);
  });

  it('wires aria-describedby to the description', () => {
    render(
      <Field label="Email" description="We send your booking confirmation here.">
        <Input type="email" />
      </Field>,
    );
    const input = screen.getByLabelText('Email');
    const desc = screen.getByText('We send your booking confirmation here.');
    expect(desc.id).toBeTruthy();
    expect(input.getAttribute('aria-describedby')).toContain(desc.id);
  });

  it('marks the control invalid and shows the error in an alert when error is set', () => {
    render(
      <Field label="Email" error="Enter a valid email.">
        <Input type="email" />
      </Field>,
    );
    const input = screen.getByLabelText('Email');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Enter a valid email.');
    expect(input.getAttribute('aria-describedby')).toContain(alert.id);
  });

  it('does not mark the control invalid when there is no error', () => {
    render(
      <Field label="Email">
        <Input type="email" />
      </Field>,
    );
    expect(screen.getByLabelText('Email')).not.toHaveAttribute('aria-invalid');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('treats an empty-string error as no error (react-hook-form boolean validators yield "")', () => {
    render(
      <Field label="Email" error="">
        <Input type="email" />
      </Field>,
    );
    // An empty message must not paint a valid field as invalid nor fire an empty alert.
    expect(screen.getByLabelText('Email')).not.toHaveAttribute('aria-invalid');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('never references a description id that was not rendered', () => {
    render(
      <Field label="Email" error="Bad">
        <Input type="email" />
      </Field>,
    );
    const input = screen.getByLabelText('Email');
    const describedby = input.getAttribute('aria-describedby') ?? '';
    // Only the error id should be referenced; no dangling description id.
    for (const id of describedby.split(/\s+/).filter(Boolean)) {
      expect(document.getElementById(id)).not.toBeNull();
    }
  });

  it('flags a required field to assistive tech and shows a visual marker', () => {
    render(
      <Field label="Email" required>
        <Input type="email" />
      </Field>,
    );
    const input = screen.getByLabelText(/Email/);
    expect(input).toHaveAttribute('aria-required', 'true');
    // marker is visual only, hidden from AT.
    expect(screen.getByTestId('field-required-marker')).toHaveAttribute('aria-hidden', 'true');
  });

  it('generates unique ids for sibling fields (no id collision)', () => {
    render(
      <>
        <Field label="First">
          <Input />
        </Field>
        <Field label="Second">
          <Input />
        </Field>
      </>,
    );
    const first = screen.getByLabelText('First');
    const second = screen.getByLabelText('Second');
    expect(first.id).toBeTruthy();
    expect(second.id).toBeTruthy();
    expect(first.id).not.toEqual(second.id);
  });

  it("keeps a control's explicitly provided id", () => {
    render(
      <Field label="Email">
        <Input id="my-email" />
      </Field>,
    );
    const input = screen.getByLabelText('Email');
    expect(input.id).toBe('my-email');
  });
});
