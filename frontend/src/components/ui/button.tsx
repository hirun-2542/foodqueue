import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "cn"
import { Slot } from "radix-ui"

const buttonVariants = cva(
  // DESIGN buttons: 12px radius, 15px/600 label, 44px (52px large). Focus = 2px Seating Orange outline.
  "group/button inline-flex shrink-0 items-center justify-center rounded-lg border border-transparent bg-clip-padding text-[15px] font-semibold whitespace-nowrap transition-colors outline-none select-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring active:not-aria-[haspopup]:translate-y-px disabled:pointer-events-none disabled:border-border disabled:bg-sunken disabled:text-muted-foreground aria-invalid:border-destructive [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-5",
  {
    variants: {
      variant: {
        // Primary: one per screen.
        default: "bg-primary text-primary-foreground hover:bg-orange-hover",
        // Secondary: white with a stone border.
        outline:
          "border-line-strong bg-surface text-ink hover:bg-sunken aria-expanded:bg-sunken",
        secondary:
          "border-line-strong bg-surface text-ink hover:bg-sunken aria-expanded:bg-sunken",
        ghost:
          "text-ink-2 hover:bg-sunken hover:text-ink aria-expanded:bg-sunken aria-expanded:text-ink",
        // Danger: white with a red-tinted border and red ink.
        destructive:
          "border-cancel-line bg-surface text-cancel-ink hover:bg-cancel-soft",
        link: "text-orange-ink underline-offset-4 hover:underline",
      },
      size: {
        default:
          "h-11 gap-2 px-[18px] has-data-[icon=inline-end]:pr-4 has-data-[icon=inline-start]:pl-4",
        xs: "h-7 gap-1 rounded-sm px-2 text-xs has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&_svg:not([class*='size-'])]:size-3.5",
        sm: "h-9 gap-1.5 rounded-sm px-3.5 text-sm has-data-[icon=inline-end]:pr-3 has-data-[icon=inline-start]:pl-3 [&_svg:not([class*='size-'])]:size-4",
        lg: "h-13 gap-2 px-6 text-base has-data-[icon=inline-end]:pr-5 has-data-[icon=inline-start]:pl-5",
        icon: "size-11",
        "icon-xs":
          "size-7 rounded-sm [&_svg:not([class*='size-'])]:size-3.5",
        "icon-sm":
          "size-9 rounded-sm [&_svg:not([class*='size-'])]:size-4",
        "icon-lg": "size-13",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  }
)

function Button({
  className,
  variant = "default",
  size = "default",
  asChild = false,
  ...props
}: React.ComponentProps<"button"> &
  VariantProps<typeof buttonVariants> & {
    asChild?: boolean
  }) {
  const Comp = asChild ? Slot.Root : "button"

  return (
    <Comp
      data-slot="button"
      data-variant={variant}
      data-size={size}
      className={cn(buttonVariants({ variant, size, className }))}
      {...props}
    />
  )
}

export { Button, buttonVariants }
