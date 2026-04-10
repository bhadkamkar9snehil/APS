import { Badge } from "@/components/ui/badge"
import { statusTone, str } from "@/lib/apsFormat"
import { cn } from "@/lib/utils"

export function StatusBadge({
  status,
  className,
}: {
  status: unknown
  className?: string
}) {
  const label = str(status || "—").replace(/_/g, " ") || "—"
  const t = statusTone(status)
  const variant =
    t === "success"
      ? "success"
      : t === "warn"
        ? "warn"
        : t === "danger"
          ? "danger"
          : "default"
  return (
    <Badge variant={variant} className={cn("font-extrabold", className)}>
      {label}
    </Badge>
  )
}
