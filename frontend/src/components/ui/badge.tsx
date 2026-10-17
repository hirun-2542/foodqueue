import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "cn"
import { Slot } from "radix-ui"

// DESIGN status badge: 24px pill, 6px dot plus Thai label, soft fill with matching ink.
// The label is the meaning; the colour only reinforces it.
const badgeVariants = cva(
  "group/badge inline-flex h-6 w-fit shrink-0 items-center justify-center gap-1.5 overflow-hidden rounded-full border border-transparent px-2.5 text-[12.5px] font-semibold whitespace-nowrap transition-all outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&>svg]:pointer-events-none [&>svg]:size-3.5!",
  {
    variants: {
      variant: {
        // Status fills: ink on soft, all >= 5.6:1 (WCAG AA).
        pending: "bg-pending-soft text-pending-ink",
        preparing: "bg-preparing-soft text-preparing-ink",
        ready: "bg-ready-soft text-ready-ink",
        cancelled: "bg-cancel-soft text-cancel-ink",
        // Warm Tint accent: selection and counts.
        accent: "bg-orange-soft text-orange-ink",
        neutral: "bg-sunken text-ink-2",
        default: "bg-orange-soft text-orange-ink",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  }
)

const badgeDot = {
  pending: "before:bg-pending-dot",
  preparing: "before:bg-preparing-dot",
  ready: "before:bg-ready-dot",
  cancelled: "before:bg-cancel-dot",
  accent: "before:bg-orange",
  neutral: "before:bg-served-dot before:hidden",
  default: "before:bg-orange before:hidden",
} as const

function Badge({
  className,
  variant = "default",
  asChild = false,
  ...props
}: React.ComponentProps<"span"> &
  VariantProps<typeof badgeVariants> & { asChild?: boolean }) {
  const Comp = asChild ? Slot.Root : "span"

  return (
    <Comp
      data-slot="badge"
      data-variant={variant}
      className={cn(
        badgeVariants({ variant }),
        "before:h-1.5 before:w-1.5 before:shrink-0 before:rounded-full before:content-['']",
        badgeDot[variant ?? "default"],
        className
      )}
      {...props}
    />
  )
}

export { Badge, badgeVariants }
