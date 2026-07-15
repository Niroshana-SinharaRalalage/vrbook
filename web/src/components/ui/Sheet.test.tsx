import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeAll, describe, expect, it, vi } from 'vitest';

import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from './Sheet';

// Radix touches pointer-capture / scrollIntoView which jsdom lacks.
beforeAll(() => {
  Element.prototype.hasPointerCapture ??= () => false;
  Element.prototype.setPointerCapture ??= () => undefined;
  Element.prototype.releasePointerCapture ??= () => undefined;
  Element.prototype.scrollIntoView ??= () => undefined;
});

const Example = ({
  open,
  onOpenChange,
  side,
}: {
  open?: boolean;
  onOpenChange?: (o: boolean) => void;
  side?: 'left' | 'right' | 'top' | 'bottom';
} = {}) => (
  <Sheet open={open} onOpenChange={onOpenChange}>
    <SheetTrigger>Menu</SheetTrigger>
    <SheetContent side={side}>
      <SheetHeader>
        <SheetTitle>Navigation</SheetTitle>
        <SheetDescription>Jump to a section.</SheetDescription>
      </SheetHeader>
      <a href="/properties">Stays</a>
      <SheetClose>Done</SheetClose>
    </SheetContent>
  </Sheet>
);

describe('<Sheet />', () => {
  it('does not render its content until opened', () => {
    render(<Example />);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('opens from the trigger with an accessible name from the title', async () => {
    const user = userEvent.setup();
    render(<Example />);
    await user.click(screen.getByRole('button', { name: 'Menu' }));
    expect(screen.getByRole('dialog')).toHaveAccessibleName('Navigation');
  });

  it('traps focus inside on open', async () => {
    const user = userEvent.setup();
    render(<Example />);
    await user.click(screen.getByRole('button', { name: 'Menu' }));
    expect(screen.getByRole('dialog').contains(document.activeElement)).toBe(true);
  });

  it('closes on Escape and returns focus to the trigger', async () => {
    const user = userEvent.setup();
    render(<Example />);
    const trigger = screen.getByRole('button', { name: 'Menu' });
    await user.click(trigger);
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(document.activeElement).toBe(trigger);
  });

  it('closes when a SheetClose control is activated', async () => {
    const user = userEvent.setup();
    render(<Example />);
    await user.click(screen.getByRole('button', { name: 'Menu' }));
    await user.click(screen.getByRole('button', { name: 'Done' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('renders a built-in labelled close affordance', async () => {
    const user = userEvent.setup();
    render(<Example />);
    await user.click(screen.getByRole('button', { name: 'Menu' }));
    expect(screen.getByRole('button', { name: /close/i })).toBeInTheDocument();
  });

  it('anchors to the right by default and honours the side prop', async () => {
    const user = userEvent.setup();
    const { rerender } = render(<Example />);
    await user.click(screen.getByRole('button', { name: 'Menu' }));
    expect(screen.getByRole('dialog').className).toContain('right-0');
    await user.keyboard('{Escape}');
    rerender(<Example side="left" />);
    await user.click(screen.getByRole('button', { name: 'Menu' }));
    expect(screen.getByRole('dialog').className).toContain('left-0');
  });

  it('supports controlled open state and reports changes', async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    render(<Example open onOpenChange={onOpenChange} />);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
