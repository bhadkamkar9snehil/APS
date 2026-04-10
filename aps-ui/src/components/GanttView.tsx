import { useMemo, useState } from "react"

import { fmtDate } from "@/lib/apsFormat"
import type { JsonRecord } from "@/context/ApsContext"
import { cn } from "@/lib/utils"

const OP_LABEL: Record<string, string> = {
  EAF: "Melting",
  LRF: "Refining",
  VD: "Degassing",
  CCM: "Casting",
  RM: "Rolling",
}

const OP_BG: Record<string, string> = {
  EAF: "bg-[var(--eaf)]",
  LRF: "bg-[var(--lrf)]",
  VD: "bg-[var(--vd)]",
  CCM: "bg-[var(--ccm)]",
  RM: "bg-[var(--rm)]",
}

export function GanttView({
  jobs,
  days = 14,
}: {
  jobs: JsonRecord[]
  days?: number
}) {
  const [tip, setTip] = useState<{
    x: number
    y: number
    html: string
  } | null>(null)

  const { t0, resources } = useMemo(() => {
    const starts = jobs
      .map((j) => new Date(String(j.Planned_Start ?? "")))
      .filter((d) => !Number.isNaN(d.getTime()))
    const t0 = starts.length
      ? new Date(Math.min(...starts.map((d) => d.getTime())))
      : new Date()
    t0.setHours(0, 0, 0, 0)
    const order = ["BF", "EAF", "LRF", "VD", "CCM", "RM"]
    const resources = [
      ...new Set(
        jobs.map((j) => String(j.Resource_ID ?? "")).filter(Boolean)
      ),
    ].sort((a, b) => {
      const ga = order.findIndex((o) => a.startsWith(o))
      const gb = order.findIndex((o) => b.startsWith(o))
      return (ga === -1 ? 99 : ga) - (gb === -1 ? 99 : gb) || a.localeCompare(b)
    })
    return { t0, resources }
  }, [jobs])

  const horizonMs = days * 86400000

  const dayLabels = useMemo(() => {
    const m = [
      "Jan",
      "Feb",
      "Mar",
      "Apr",
      "May",
      "Jun",
      "Jul",
      "Aug",
      "Sep",
      "Oct",
      "Nov",
      "Dec",
    ]
    return Array.from({ length: days }, (_, i) => {
      const d = new Date(t0.getTime() + i * 86400000)
      return { day: d.getDate(), mon: m[d.getMonth()] }
    })
  }, [days, t0])

  const stripeBg = `repeating-linear-gradient(90deg, transparent, transparent calc(${100 / days}% - 1px), var(--aps-border-soft) calc(${100 / days}% - 1px), var(--aps-border-soft) calc(${100 / days}%))`

  if (!jobs.length) {
    return (
      <div className="aps-notice">Run Schedule to populate the Gantt.</div>
    )
  }

  return (
    <div
      className="relative mb-3 overflow-auto rounded-2xl border border-[var(--aps-border)] bg-white"
      onMouseLeave={() => setTip(null)}
    >
      <div className="sticky top-0 z-10 flex bg-[var(--aps-panel-muted)]">
        <div className="w-52 shrink-0 border-r border-[var(--aps-border-soft)] p-4 text-[0.65rem] font-bold tracking-wider text-[var(--aps-text-soft)] uppercase">
          Resource
        </div>
        <div className="flex min-w-0 flex-1">
          {dayLabels.map((lb, i) => (
            <div
              key={i}
              className="min-w-0 flex-1 border-r border-[var(--aps-border-soft)] py-3 text-center text-[0.65rem] font-semibold text-[var(--aps-text)]"
            >
              {lb.day}
              <span className="mt-1 block text-[0.65rem] font-normal text-[var(--aps-text-soft)]">
                {lb.mon}
              </span>
            </div>
          ))}
        </div>
      </div>

      {resources.map((resId) => {
        const resJobs = jobs.filter((j) => String(j.Resource_ID ?? "") === resId)
        const op = String(resJobs[0]?.Operation ?? "").toUpperCase()
        const opLabel = OP_LABEL[op] || op
        return (
          <div
            key={resId}
            className="flex border-b border-[var(--aps-border-soft)] transition-colors hover:bg-slate-50/80"
          >
            <div className="w-52 shrink-0 border-r border-[var(--aps-border-soft)] p-3.5">
              <div className="text-[0.8125rem] font-bold text-[var(--aps-text)]">
                {resId}
              </div>
              <div className="mt-0.5 text-[0.6875rem] text-[var(--aps-text-soft)]">
                {opLabel}
              </div>
            </div>
            <div
              className="relative min-h-[4.5rem] min-w-0 flex-1"
              style={{ backgroundImage: stripeBg }}
            >
              {resJobs.map((job, ji) => {
                const s = new Date(String(job.Planned_Start ?? ""))
                const e = new Date(String(job.Planned_End ?? ""))
                if (Number.isNaN(s.getTime()) || Number.isNaN(e.getTime())) return null
                const left = Math.max(0, ((s.getTime() - t0.getTime()) / horizonMs) * 100)
                const width = Math.max(
                  0.5,
                  ((e.getTime() - s.getTime()) / horizonMs) * 100
                )
                if (left > 100) return null
                const jop = String(job.Operation ?? "").toUpperCase()
                const cls = OP_BG[jop] ?? OP_BG.EAF
                const tipHtml = [
                  `<strong>${String(job.Campaign ?? "")} · Heat ${String(job.Heat_No ?? "—")}</strong>`,
                  `Resource: ${String(job.Resource_ID ?? "")}`,
                  `Op: ${String(job.Operation ?? "")}`,
                  `Start: ${fmtDate(job.Planned_Start)}`,
                  `End: ${fmtDate(job.Planned_End)}`,
                  `Grade: ${String(job.Grade ?? "—")}`,
                ].join("<br/>")
                return (
                  <div
                    key={ji}
                    role="presentation"
                    className={cn(
                      "absolute top-4 flex h-10 cursor-grab items-center overflow-hidden rounded-xl px-2 text-[0.6875rem] font-semibold text-white shadow-sm transition-all hover:z-[5] hover:h-12 hover:shadow-md",
                      cls
                    )}
                    style={{
                      left: `${left}%`,
                      width: `${Math.min(width, 100 - left)}%`,
                    }}
                    onMouseEnter={(ev) => {
                      setTip({
                        x: ev.clientX + 14,
                        y: ev.clientY - 10,
                        html: tipHtml,
                      })
                    }}
                    onMouseMove={(ev) => {
                      setTip((t) =>
                        t ? { ...t, x: ev.clientX + 14, y: ev.clientY - 10 } : t
                      )
                    }}
                    onMouseLeave={() => setTip(null)}
                  >
                    <span className="truncate">{String(job.Campaign ?? "")}</span>
                    <span className="ml-1 shrink-0 text-[9px] opacity-70">
                      H{String(job.Heat_No ?? "")}
                    </span>
                  </div>
                )
              })}
            </div>
          </div>
        )
      })}

      {tip ? (
        <div
          className="pointer-events-none fixed z-[1000] max-w-[220px] rounded-lg bg-[#1a1a1a] px-3 py-2.5 text-[0.65rem] leading-snug font-semibold text-[#f0f0f0] shadow-lg"
          style={{ left: tip.x, top: tip.y }}
          dangerouslySetInnerHTML={{ __html: tip.html }}
        />
      ) : null}
    </div>
  )
}
