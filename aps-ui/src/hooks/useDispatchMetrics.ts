import { useMemo } from "react"

import type { JsonRecord } from "@/context/ApsContext"

function num(v: unknown, fallback = 0): number {
  const n = Number(v)
  return Number.isFinite(n) ? n : fallback
}

export function useDispatchMetrics(
  jobs: JsonRecord[],
  dispatchBoard: JsonRecord[]
) {
  return useMemo(() => {
    const grouped: Record<string, JsonRecord[]> = {}
    for (const j of jobs) {
      const rid = String(j.Resource_ID ?? "UNKNOWN")
      if (!grouped[rid]) grouped[rid] = []
      grouped[rid].push(j)
    }
    const fromBoard = dispatchBoard
      .map((b) => String(b.resource_id ?? b.Resource_ID ?? ""))
      .filter(Boolean)
    const ids = [...new Set([...Object.keys(grouped), ...fromBoard])]
    const violations = jobs.filter(
      (j) => j.Queue_Violation && String(j.Queue_Violation) !== "OK"
    ).length
    const totalMt = jobs.reduce((a, j) => a + num(j.Qty_MT ?? j.qty_mt), 0)
    return {
      machines: ids.length || "—",
      jobs: jobs.length || "—",
      violations,
      mt: totalMt.toFixed(1),
    }
  }, [dispatchBoard, jobs])
}
