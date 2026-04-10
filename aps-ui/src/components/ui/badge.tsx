import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"

import { cn } from "@/lib/utils"

const badgeVariants = cva(
  "inline-flex h-7 shrink-0 items-center gap-1.5 rounded-full border px-2.5 text-[0.65rem] font-semibold whitespace-nowrap transition-colors",
  {
    variants: {
      variant: {
        default:
          "border-[var(--aps-border-soft)] bg-[var(--aps-panel-muted)] text-[var(--aps-text-soft)]",
        success:
          "border-emerald-200/80 bg-emerald-50 text-emerald-600 dark:border-emerald-900/40 dark:bg-emerald-950/40 dark:text-emerald-400",
        warn: "border-amber-200/80 bg-orange-50 text-amber-600 dark:border-amber-900/40 dark:bg-orange-950/40 dark:text-amber-400",
        danger:
          "border-red-200/80 bg-red-50 text-red-600 dark:border-red-900/40 dark:bg-red-950/40 dark:text-red-400",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  }
)

function Badge({
  className,
  variant,
  dot,
  children,
  ...props
}: React.ComponentProps<"span"> &
  VariantProps<typeof badgeVariants> & { dot?: boolean }) {
  return (
    <span
      data-slot="badge"
      className={cn(badgeVariants({ variant }), className)}
      {...props}
    >
      {dot ? (
        <span
          className={cn(
            "size-1.5 shrink-0 rounded-full bg-slate-300",
            variant === "success" && "bg-emerald-600",
            variant === "warn" && "bg-amber-600",
            variant === "danger" && "bg-red-600"
          )}
          aria-hidden
        />
      ) : null}
      {children}
    </span>
  )
}

export { Badge }
