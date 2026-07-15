'use client';

import {
  forwardRef,
  type ComponentPropsWithoutRef,
  type ElementRef,
  type HTMLAttributes,
} from 'react';
import * as DialogPrimitive from '@radix-ui/react-dialog';
import { X } from 'lucide-react';

import { cn } from '@/lib/utils/cn';

/**
 * VrBook Sheet — an edge-anchored slide-over, built on the same Radix Dialog
 * as `Dialog`, so it inherits the full modal a11y contract: focus-trap while
 * open, Escape + outside-click close, focus returns to the trigger,
 * scroll-lock + `aria-hidden` on the rest of the page, `aria-labelledby` its
 * `SheetTitle`. This is the shared focus-trap primitive the mobile nav (and
 * the VRB-110 a11y pass) reuses — don't hand-roll another. Always include a
 * `SheetTitle` (visually-hidden with `className="sr-only"` if needed).
 */

export type SheetSide = 'top' | 'bottom' | 'left' | 'right';

export const Sheet = DialogPrimitive.Root;
export const SheetTrigger = DialogPrimitive.Trigger;
export const SheetClose = DialogPrimitive.Close;
export const SheetPortal = DialogPrimitive.Portal;

export const SheetOverlay = forwardRef<
  ElementRef<typeof DialogPrimitive.Overlay>,
  ComponentPropsWithoutRef<typeof DialogPrimitive.Overlay>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Overlay
    ref={ref}
    className={cn(
      'fixed inset-0 z-50 bg-black/50 backdrop-blur-[1px]',
      'motion-safe:data-[state=open]:[animation:vrb-fade-in_150ms_ease-out]',
      className,
    )}
    {...props}
  />
));
SheetOverlay.displayName = DialogPrimitive.Overlay.displayName;

const sideClasses: Record<SheetSide, string> = {
  right: 'inset-y-0 right-0 h-full w-3/4 max-w-sm border-l',
  left: 'inset-y-0 left-0 h-full w-3/4 max-w-sm border-r',
  top: 'inset-x-0 top-0 w-full border-b',
  bottom: 'inset-x-0 bottom-0 w-full border-t',
};

const sideAnimation: Record<SheetSide, string> = {
  right: 'motion-safe:data-[state=open]:[animation:vrb-slide-in-right_220ms_ease-out]',
  left: 'motion-safe:data-[state=open]:[animation:vrb-slide-in-left_220ms_ease-out]',
  top: 'motion-safe:data-[state=open]:[animation:vrb-slide-in-top_220ms_ease-out]',
  bottom: 'motion-safe:data-[state=open]:[animation:vrb-slide-in-bottom_220ms_ease-out]',
};

export interface SheetContentProps
  extends ComponentPropsWithoutRef<typeof DialogPrimitive.Content> {
  readonly side?: SheetSide;
}

export const SheetContent = forwardRef<
  ElementRef<typeof DialogPrimitive.Content>,
  SheetContentProps
>(({ side = 'right', className, children, ...props }, ref) => (
  <SheetPortal>
    <SheetOverlay />
    <DialogPrimitive.Content
      ref={ref}
      className={cn(
        'fixed z-50 flex flex-col gap-4 bg-background p-6 shadow-lg border-border focus:outline-none',
        sideClasses[side],
        sideAnimation[side],
        className,
      )}
      {...props}
    >
      {children}
      <DialogPrimitive.Close
        className={cn(
          'absolute right-4 top-4 rounded-sm text-muted-foreground opacity-80 transition-opacity',
          'hover:opacity-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background',
          // 44px hit target for the corner close (WCAG 2.2 §2.5.8).
          'inline-flex h-11 w-11 items-center justify-center',
        )}
      >
        <X className="h-5 w-5" aria-hidden="true" />
        <span className="sr-only">Close</span>
      </DialogPrimitive.Close>
    </DialogPrimitive.Content>
  </SheetPortal>
));
SheetContent.displayName = DialogPrimitive.Content.displayName;

export const SheetHeader = ({ className, ...props }: HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('flex flex-col gap-1.5 text-left', className)} {...props} />
);
SheetHeader.displayName = 'SheetHeader';

export const SheetFooter = ({ className, ...props }: HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('mt-auto flex flex-col gap-2', className)} {...props} />
);
SheetFooter.displayName = 'SheetFooter';

export const SheetTitle = forwardRef<
  ElementRef<typeof DialogPrimitive.Title>,
  ComponentPropsWithoutRef<typeof DialogPrimitive.Title>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Title
    ref={ref}
    className={cn('text-lg font-semibold leading-tight tracking-tight text-foreground', className)}
    {...props}
  />
));
SheetTitle.displayName = DialogPrimitive.Title.displayName;

export const SheetDescription = forwardRef<
  ElementRef<typeof DialogPrimitive.Description>,
  ComponentPropsWithoutRef<typeof DialogPrimitive.Description>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Description
    ref={ref}
    className={cn('text-sm text-muted-foreground', className)}
    {...props}
  />
));
SheetDescription.displayName = DialogPrimitive.Description.displayName;
