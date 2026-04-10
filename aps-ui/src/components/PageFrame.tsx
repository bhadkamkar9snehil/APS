import * as React from "react"

import { cn } from "@/lib/utils"

export type MetricTone = "default" | "success" | "warn" | "danger"

export function PageFrame({
  title,
  subtitle,
  actions,
  filters,
  metrics,
  className,
  contentClassName,
  children,
}: {
  title: string
  subtitle?: string
  actions?: React.ReactNode
  filters?: React.ReactNode
  metrics?: { label: string; value: string; sub?: string; tone?: MetricTone }[]
  className?: string
  contentClassName?: string
  children?: React.ReactNode
}) {
  return (
    <div className={cn("flex h-full min-h-0 flex-col", className)}>
      <header className="mb-1 flex shrink-0 flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="truncate text-[1.1rem] font-extrabold tracking-tight text-[var(--aps-text)]">
            {title}
          </h1>
          {subtitle ? (
            <p className="mt-0.5 truncate text-[0.65rem] text-[var(--aps-text-soft)]">
              {subtitle}
            </p>
          ) : null}
        </div>
        {actions ? (
          <div className="flex flex-wrap items-center gap-2">{actions}</div>
        ) : null}
      </header>
      {filters ? (
        <div className="mb-2 flex shrink-0 flex-wrap gap-2">{filters}</div>
      ) : null}
      {metrics && metrics.length > 0 ? (
        <div className="mb-2 grid shrink-0 grid-cols-2 gap-2 sm:grid-cols-4">
          {metrics.map((m) => (
            <MetricTile key={m.label} {...m} />
          ))}
        </div>
      ) : null}
      <div
        className={cn(
          "flex min-h-0 flex-1 flex-col overflow-auto pr-0.5",
          contentClassName
        )}
      >
        {children}
      </div>
    </div>
  )
}

export function MetricTile({
  label,
  value,
  sub,
  tone = "default",
}: {
  label: string
  value: string
  sub?: string
  tone?: MetricTone
}) {
  return (
    <div
      className={cn(
        "flex min-h-[3.2rem] min-w-0 flex-col justify-center rounded-[0.3rem] border border-[var(--aps-border)] bg-white px-3 py-2 shadow-[0_1px_3px_rgba(0,0,0,0.02)] transition-all hover:-translate-y-px hover:shadow-[var(--aps-shadow-soft)]",
        tone === "success" &&
          "border-emerald-200/80 bg-[var(--aps-success-soft)]",
        tone === "warn" && "border-amber-200/80 bg-[var(--aps-warning-soft)]",
        tone === "danger" && "border-red-200/80 bg-[var(--aps-danger-soft)]"
      )}
    >
      <div className="truncate text-[0.65rem] font-bold tracking-wider text-[var(--aps-text-faint)] uppercase">
        {label}
      </div>
      <div
        className={cn(
          "mt-0.5 truncate text-[1.1rem] font-black text-[var(--aps-text)]",
          tone === "success" && "text-[var(--aps-success)]",
          tone === "warn" && "text-[var(--aps-warning)]",
          tone === "danger" && "text-[var(--aps-danger)]"
        )}
      >
        {value}
      </div>
      {sub ? (
        <div className="mt-0.5 truncate text-[0.65rem] text-[var(--aps-text-soft)]">
          {sub}
        </div>
      ) : null}
    </div>
  )
}
