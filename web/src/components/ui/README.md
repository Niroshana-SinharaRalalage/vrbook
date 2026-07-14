# VrBook UI primitives (Lane DESIGN / VRB-DS)

The shared component library. Every VrBook surface composes these instead of
copy-pasting inline Tailwind (closes gap G20). Import from the barrel:

```ts
import { Button, Card, Dialog, Input, Field, Badge, Skeleton } from '@/components/ui';
```

## Design language

- **Palette** (brand tokens in `tailwind.config.ts` / `globals.css`): warm
  **stone** canvas, **orange** (`bg-primary`) as the single "action" colour,
  **maroon** (`bg-secondary`) as authority. Status tokens `success` /
  `warning` / `destructive` are the booking-status vocabulary (confirmed /
  pending / rejected). All tokens are themed for light + dark and tuned so
  `text-{token}` clears WCAG 2.2 AA on the mode's background.
- **Radius rhythm**: `rounded-lg` containers (Card, Dialog) → `rounded-md`
  controls (Button, Input) → `rounded-full` pills (Badge).
- **Focus + press signature**: every interactive primitive shares a 2px orange
  `focus-visible` ring offset from the surface (visible, not colour-reliant)
  and a tactile `active:translate-y-px` press that collapses under
  `prefers-reduced-motion`.

## Primitives

| Primitive | Notes |
|---|---|
| `Button` / `buttonVariants` | Variants `primary`\|`secondary`\|`outline`\|`ghost`\|`destructive`\|`link`; sizes `sm`\|`md`\|`lg`\|`icon`. Defaults `type="button"`. `loading` disables + shows a spinner + sets `aria-busy`. `buttonVariants()` styles a non-button (e.g. an `<a>`). |
| `Card` + parts | `Card` / `CardHeader` / `CardTitle` (h3) / `CardDescription` / `CardContent` / `CardFooter`. |
| `Dialog` + parts | Radix-backed modal: focus-trap, Escape + outside-click close, return-focus, scroll-lock. **Always include a `DialogTitle`** (it's the accessible name). |
| `Input` | Text field. Set `aria-invalid="true"` (Field does this) to flip border/ring destructive. |
| `Field` / `Label` | `Field` wires label↔control, `aria-describedby` (description + error, only the ones rendered), `aria-invalid`, `aria-required`; errors get `role="alert"`. Plays with react-hook-form. `Label` is the standalone styled label. |
| `Badge` | Soft status pill. Variants `default`\|`secondary`\|`outline`\|`success`\|`warning`\|`destructive`. `tabular-nums`. |
| `Skeleton` | Loading placeholder; `aria-hidden`, pulses under `motion-safe` only. |
| `ConfirmActionModal` | Pre-existing inline confirm modal (operator flows). |

## Rules for consumers

- Import from `@/components/ui`, not individual files.
- Don't fork a primitive into a feature folder — extend it here (Lane DESIGN
  owns `web/src/components/ui/*`, `tailwind.config.ts`, `globals.css`).
- Every primitive has a Vitest test alongside it; keep them green.
