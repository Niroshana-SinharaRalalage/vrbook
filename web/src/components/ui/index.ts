/**
 * VrBook shared UI primitives (Lane DESIGN / VRB-DS).
 *
 * The single import surface for the design system. Downstream lanes consume
 * from here — `import { Button, Field, Dialog } from '@/components/ui'` — so
 * this barrel is the stable public API. Add exports here when you add a
 * primitive; don't deep-import individual files from feature code.
 */

export { Button, buttonVariants } from './Button';
export type { ButtonProps, ButtonVariant, ButtonSize, ButtonVariantsOptions } from './Button';

export {
  Card,
  CardHeader,
  CardTitle,
  CardDescription,
  CardContent,
  CardFooter,
} from './Card';

export {
  Dialog,
  DialogTrigger,
  DialogClose,
  DialogPortal,
  DialogOverlay,
  DialogContent,
  DialogHeader,
  DialogFooter,
  DialogTitle,
  DialogDescription,
} from './Dialog';

export { Input } from './Input';

export { Field, Label } from './Field';
export type { FieldProps } from './Field';

export { Badge } from './Badge';
export type { BadgeProps, BadgeVariant } from './Badge';

export { Skeleton } from './Skeleton';

export { ConfirmActionModal } from './ConfirmActionModal';
export type { ConfirmActionModalProps } from './ConfirmActionModal';
