import { useMemo } from "react"

import type { JsonRecord } from "@/context/ApsContext"
import { fmtDateTime } from "@/lib/apsFormat"
import { cn } from "@/lib/utils"

function num(v: unknown, fallback = 0): number {
  const n = Number(v)
  return Number.isFinite(n) ? n : fallback
}

const OP_ORDER = ["BF", "EAF", "LRF", "VD", "CCM", "RM"] as const

function opClass(op: string) {
  const u = op.toUpperCase()
  return OP_ORDER.includes(u as (typeof OP_ORDER)[number])
    ? u.toLowerCase()
    : "rm"
}

export function DispatchBoard({
  jobs,
  capacity,
  dispatchBoard,
}: {
  jobs: JsonRecord[]
  capacity: JsonRecord[]
  dispatchBoard: JsonRecord[]
}) {
  const { ids, grouped } = useMemo(() => {
    const grouped: Record<string, JsonRecord[]> = {}
    for (const j of jobs) {
      const rid = String(j.Resource_ID ?? "UNKNOWN")
      if (!grouped[rid]) grouped[rid] = []
      grouped[rid].push(j)
    }
    const fromBoard = dispatchBoard
      .map((b) => String(b.resource_id ?? b.Resource_ID ?? ""))
      .filter(Boolean)
    const ids = [
      ...new Set([...Object.keys(grouped), ...fromBoard]),
    ].sort((a, b) => {
      const ga = OP_ORDER.findIndex((o) => a.startsWith(o))
      const gb = OP_ORDER.findIndex((o) => b.startsWith(o))
      return (ga === -1 ? 99 : ga) - (gb === -1 ? 99 : gb) || a.localeCompare(b)
    })
    return { ids, grouped }
  }, [dispatchBoard, jobs])

  if (!ids.length) {
    return (
      <div className="aps-notice">
        Run Schedule to populate the dispatch view.
      </div>
    )
  }

  return (
    <div className="grid grid-cols-[repeat(auto-fit,minmax(22rem,1fr))] gap-3">
      {ids.map((rid) => {
        const list = (grouped[rid] ?? []).slice(0, 12)
        const utilRow = capacity.find(
          (c) => String(c.Resource_ID ?? c.resource_id ?? "") === rid
        )
        const util = Math.round(
          num(
            utilRow?.["Utilisation_%"] ??
              utilRow?.Utilisation_Percent ??
              utilRow?.utilisation
          )
        )
        const op = String(
          list[0]?.Operation ??
            utilRow?.Operation_Group ??
            utilRow?.operation ??
            "—"
        )
        const badgeCls = opClass(op)
        const pillCls = util > 100 ? "over" : util > 85 ? "high" : "ok"
        return (
          <div
            key={rid}
            className="overflow-hidden rounded-2xl border border-[var(--aps-border)] bg-white shadow-[var(--aps-shadow-soft)]"
          >
            <div className="flex items-center gap-2.5 border-b border-[var(--aps-border-soft)] px-3.5 py-3">
              <div
                className={cn(
                  "grid size-[2.2rem] shrink-0 place-items-center rounded-[0.55rem] text-[0.55rem] font-black tracking-wide text-white",
                  badgeCls === "eaf" && "bg-[var(--eaf)]",
                  badgeCls === "lrf" && "bg-[var(--lrf)]",
                  badgeCls === "vd" && "bg-[var(--vd)]",
                  badgeCls === "ccm" && "bg-[var(--ccm)]",
                  badgeCls === "rm" && "bg-[var(--rm)]"
                )}
              >
                {op.slice(0, 3).toUpperCase()}
              </div>
              <div className="min-w-0">
                <div className="text-[0.8rem] font-extrabold text-[var(--aps-text)]">
                  {rid}
                </div>
                <div className="mt-0.5 text-[0.65rem] text-[var(--aps-text-soft)]">
                  {op}
                </div>
              </div>
              <div
                className={cn(
                  "ml-auto rounded-full px-2 py-0.5 text-[0.65rem] font-extrabold",
                  pillCls === "ok" &&
                    "bg-[var(--aps-success-soft)] text-[var(--aps-success)]",
                  pillCls === "high" &&
                    "bg-[var(--aps-warning-soft)] text-[var(--aps-warning)]",
                  pillCls === "over" &&
                    "bg-[var(--aps-danger-soft)] text-[var(--aps-danger)]"
                )}
              >
                {util || 0}%
              </div>
            </div>
            <div className="flex max-h-[26rem] flex-col gap-1.5 overflow-auto p-2.5">
              {list.length ? (
                list.map((j, idx) => {
                  const running =
                    String(j.Status ?? "").toUpperCase() === "RUNNING"
                  const late = String(j.Status ?? "")
                    .toUpperCase()
                    .includes("LATE")
                  const hasViol =
                    Boolean(j.Queue_Violation) &&
                    String(j.Queue_Violation) !== "OK"
                  const dot = opClass(String(j.Operation ?? ""))
                  return (
                    <div
                      key={idx}
                      className={cn(
                        "grid grid-cols-[1.8rem_1fr_auto] items-center gap-2 rounded-[0.55rem] border-l-[0.2rem] border-transparent bg-[var(--aps-panel-muted)] px-2.5 py-2",
                        running &&
                          "border-l-[var(--info)] bg-sky-50 dark:bg-sky-950/30",
                        late &&
                          "border-l-[var(--aps-danger)] bg-[var(--aps-danger-soft)]",
                        hasViol &&
                          "border-l-[var(--aps-warning)] bg-[var(--aps-warning-soft)]"
                      )}
                    >
                      <div className="text-center text-[0.65rem] font-black text-[var(--aps-text-faint)]">
                        {idx + 1}
                      </div>
                      <div className="min-w-0">
                        <div className="truncate text-[0.65rem] font-extrabold text-[var(--aps-text)]">
                          {String(j.Campaign ?? j.campaign_id ?? "—")} · Heat{" "}
                          {String(j.Heat_No ?? "—")}
                        </div>
                        <div className="mt-0.5 flex flex-wrap items-center gap-1.5 text-[0.63rem] text-[var(--aps-text-soft)]">
                          <span
                            className={cn(
                              "inline-block size-2 shrink-0 rounded-full",
                              dot === "eaf" && "bg-[var(--eaf)]",
                              dot === "lrf" && "bg-[var(--lrf)]",
                              dot === "vd" && "bg-[var(--vd)]",
                              dot === "ccm" && "bg-[var(--ccm)]",
                              dot === "rm" && "bg-[var(--rm)]"
                            )}
                          />
                          {String(j.Grade ?? "—")} ·{" "}
                          {String(j.Qty_MT ?? j.qty_mt ?? "—")} MT
                            {hasViol ? (
                            <span className="rounded bg-amber-200/80 px-1 text-[0.6rem] font-bold text-amber-900">
                              {String(j.Queue_Violation)}
                            </span>
                          ) : null}
                        </div>
                      </div>
                      <div className="shrink-0 text-[0.65rem] text-[var(--aps-text-soft)]">
                        {fmtDateTime(j.Planned_Start)}
                      </div>
                    </div>
                  )
                })
              ) : (
                <div className="aps-notice">No jobs for this resource.</div>
              )}
            </div>
          </div>
        )
      })}
    </div>
  )
}
