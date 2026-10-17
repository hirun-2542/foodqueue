import * as React from "react"
import { cn } from "cn"

// DESIGN inputs: white, 10px radius, Control Border, 44px.
// Focus swaps the border to Seating Orange and adds a 3px Warm Tint ring; errors use the cancel ink.
function Input({ className, type, ...props }: React.ComponentProps<"input">) {
  return (
    <input
      type={type}
      data-slot="input"
      className={cn(
        "h-11 w-full min-w-0 rounded-md border border-input bg-surface px-3.5 py-1 text-[15px] text-ink transition-colors outline-none file:inline-flex file:h-6 file:border-0 file:bg-transparent file:text-sm file:font-medium file:text-foreground placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-orange-soft disabled:pointer-events-none disabled:cursor-not-allowed disabled:bg-sunken disabled:text-muted-foreground aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-cancel-soft md:text-[15px]",
        className
      )}
      {...props}
    />
  )
}

export { Input }
