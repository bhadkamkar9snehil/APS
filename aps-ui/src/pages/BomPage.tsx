import { useCallback, useEffect, useState } from "react"

import { apiFetch } from "@/api/client"
import type { JsonRecord } from "@/context/ApsContext"
import { PageFrame } from "@/components/PageFrame"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { cn } from "@/lib/utils"

type PlantGroup = JsonRecord & {
  plant?: string
  material_types?: JsonRecord[]
  gross_req?: number
  net_req?: number
  produced_qty?: number
}

function bomBadgeClass(status: string) {
  const m: Record<string, string> = {
    COVERED: "bg-emerald-100 text-emerald-800 border-emerald-200",
    "PARTIAL SHORT": "bg-amber-100 text-amber-900 border-amber-200",
    SHORT: "bg-red-100 text-red-800 border-red-200",
    BYPRODUCT: "bg-violet-100 text-violet-800 border-violet-200",
  }
  return m[status] ?? "bg-slate-100 text-slate-700 border-slate-200"
}

export function BomPage() {
  const [grouped, setGrouped] = useState<PlantGroup[]>([])
  const [summary, setSummary] = useState<JsonRecord>({})
  const [busy, setBusy] = useState(false)
  const [loadErr, setLoadErr] = useState<string | null>(null)
  const [selected, setSelected] = useState<{
    plant: string
    materialType: JsonRecord | null
  } | null>(null)

  const loadExplosion = useCallback(async () => {
    setLoadErr(null)
    try {
      const d = await apiFetch<{
        grouped_bom?: PlantGroup[]
        summary?: JsonRecord
      }>("/api/aps/bom/explosion")
      setGrouped(Array.isArray(d.grouped_bom) ? d.grouped_bom : [])
      setSummary(
        typeof d.summary === "object" && d.summary !== null ? d.summary : {}
      )
    } catch (e) {
      setGrouped([])
      setSummary({})
      setLoadErr(e instanceof Error ? e.message : "No BOM data loaded")
    }
  }, [])

  useEffect(() => {
    void loadExplosion()
  }, [loadExplosion])

  async function runBom() {
    setBusy(true)
    setLoadErr(null)
    try {
      const d = await apiFetch<{
        grouped_bom?: PlantGroup[]
        summary?: JsonRecord
      }>("/api/run/bom", { method: "POST" })
      setGrouped(Array.isArray(d.grouped_bom) ? d.grouped_bom : [])
      setSummary(
        typeof d.summary === "object" && d.summary !== null ? d.summary : {}
      )
    } catch (e) {
      setLoadErr(e instanceof Error ? e.message : "BOM run failed")
    } finally {
      setBusy(false)
    }
  }

  const detailPlant = grouped.find((p) => p.plant === selected?.plant)
  const mt = selected?.materialType
  const detailRows = mt
    ? (Array.isArray(mt.rows) ? (mt.rows as JsonRecord[]) : [])
    : []

  return (
    <PageFrame
      title="BOM Explosion"
      subtitle="Material requirements breakdown by production stage and finished goods."
      actions={
        <Button
          variant="ink"
          className="h-[2.2rem] font-bold"
          disabled={busy}
          onClick={() => void runBom()}
        >
          {busy ? "Exploding…" : "Run BOM Explosion"}
        </Button>
      }
    >
      {loadErr ? (
        <div className="mb-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-[0.78rem] text-amber-900">
          {loadErr}
        </div>
      ) : null}
      {Object.keys(summary).length > 0 ? (
        <div className="mb-3 flex flex-wrap gap-2 rounded-xl border border-[var(--aps-border)] bg-white p-3 shadow-sm">
          {(
            [
              ["Items", summary.total_sku_lines],
              ["Covered", summary.covered_lines],
              ["Short", summary.short_lines],
              ["Partial", summary.partial_lines],
              [
                "Gross MT",
                `${((Number(summary.total_gross_req) || 0) / 1000).toFixed(1)}k`,
              ],
              [
                "Net MT",
                `${((Number(summary.total_net_req) || 0) / 1000).toFixed(1)}k`,
              ],
            ] as [string, unknown][]
          ).map(([k, v]) => (
            <div
              key={k}
              className="flex min-w-[5rem] flex-col rounded-lg border border-[var(--aps-border-soft)] px-3 py-2"
            >
              <span className="text-[1.1rem] font-black text-[var(--aps-text)]">
                {String(v ?? "—")}
              </span>
              <span className="text-[0.6rem] font-bold tracking-wide text-[var(--aps-text-faint)] uppercase">
                {k}
              </span>
            </div>
          ))}
        </div>
      ) : null}

      <div className="grid min-h-[320px] grid-cols-1 gap-2 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)]">
        <Card className="min-h-0">
          <CardContent className="max-h-[70vh] overflow-auto py-3">
            {!grouped.length ? (
              <div className="aps-notice text-left">
                No BOM data. Run BOM Explosion (or ensure POST /api/run/bom was
                executed) then refresh.
              </div>
            ) : (
              <ul className="space-y-1 text-[0.78rem]">
                {grouped.map((plant) => {
                  const p = String(plant.plant ?? "")
                  const mts = (Array.isArray(plant.material_types)
                    ? plant.material_types
                    : []) as JsonRecord[]
                  return (
                    <li key={p} className="rounded-lg border border-[var(--aps-border-soft)] p-2">
                      <button
                        type="button"
                        className="flex w-full items-center justify-between text-left font-extrabold text-[var(--aps-text)]"
                        onClick={() =>
                          setSelected({
                            plant: p,
                            materialType: mts[0] ?? null,
                          })
                        }
                      >
                        <span>{p || "—"}</span>
                        <span className="text-[0.65rem] font-normal text-[var(--aps-text-faint)]">
                          {mts.length} types
                        </span>
                      </button>
                      <ul className="mt-1 space-y-0.5 border-t border-[var(--aps-border-soft)] pt-1">
                        {mts.map((t, i) => (
                          <li key={i}>
                            <button
                              type="button"
                              className={cn(
                                "w-full rounded px-2 py-1 text-left text-[0.72rem] font-semibold",
                                selected?.plant === p &&
                                  selected?.materialType === t
                                  ? "bg-[var(--aps-brand-soft)] text-[var(--aps-brand)]"
                                  : "hover:bg-[var(--aps-panel-muted)]"
                              )}
                              onClick={() =>
                                setSelected({ plant: p, materialType: t })
                              }
                            >
                              {String(t.material_type ?? "—")} (
                              {String(t.row_count ?? "—")})
                            </button>
                          </li>
                        ))}
                      </ul>
                    </li>
                  )
                })}
              </ul>
            )}
          </CardContent>
        </Card>
        <Card>
          <CardContent className="max-h-[70vh] overflow-auto py-3">
            {!selected || !detailPlant ? (
              <div className="aps-notice text-left">
                Select a plant and material type to view BOM lines.
              </div>
            ) : !mt ? (
              <div className="space-y-2 text-[0.8rem]">
                <div className="font-extrabold">{selected.plant}</div>
                <p className="text-[var(--aps-text-soft)]">
                  Gross {Number(detailPlant.gross_req ?? 0).toFixed(1)} MT · Net{" "}
                  {Number(detailPlant.net_req ?? 0).toFixed(1)} MT
                </p>
              </div>
            ) : (
              <div className="space-y-3">
                <div>
                  <div className="text-[0.95rem] font-extrabold">
                    {selected.plant}
                  </div>
                  <div className="text-[0.75rem] text-[var(--aps-text-soft)]">
                    {String(mt.material_type ?? "")}
                  </div>
                </div>
                <div className="grid grid-cols-3 gap-2 text-[0.7rem]">
                  <div>
                    <div className="text-[var(--aps-text-faint)]">Gross Req</div>
                    <div className="font-bold">
                      {Number(mt.gross_req ?? 0).toFixed(1)} MT
                    </div>
                  </div>
                  <div>
                    <div className="text-[var(--aps-text-faint)]">Produced</div>
                    <div className="font-bold">
                      {Number(mt.produced_qty ?? 0).toFixed(1)} MT
                    </div>
                  </div>
                  <div>
                    <div className="text-[var(--aps-text-faint)]">Net Req</div>
                    <div className="font-bold">
                      {Number(mt.net_req ?? 0).toFixed(1)} MT
                    </div>
                  </div>
                </div>
                <div className="flex flex-col gap-2">
                  {detailRows.map((r, i) => (
                    <div
                      key={i}
                      className="rounded-lg border border-[var(--aps-border)] bg-[var(--aps-panel-muted)]/40 p-2 text-[0.72rem]"
                    >
                      <div className="grid grid-cols-2 gap-1">
                        <div>
                          <div className="text-[var(--aps-text-faint)]">SKU</div>
                          <div className="font-bold">{String(r.sku_id ?? "")}</div>
                        </div>
                        <div>
                          <div className="text-[var(--aps-text-faint)]">Parent</div>
                          <div>{String(r.parent_skus ?? "")}</div>
                        </div>
                      </div>
                      <div className="mt-1 grid grid-cols-2 gap-1">
                        <div>
                          Gross {Number(r.gross_req ?? 0).toFixed(1)} MT
                        </div>
                        <div>
                          Avail {Number(r.available_before ?? 0).toFixed(1)} MT
                        </div>
                      </div>
                      <div className="mt-1 flex flex-wrap gap-1">
                        <span
                          className={cn(
                            "rounded-full border px-2 py-0.5 text-[0.62rem] font-extrabold",
                            bomBadgeClass(String(r.status ?? ""))
                          )}
                        >
                          {String(r.status ?? "")}
                        </span>
                        <span className="rounded-full border border-slate-200 bg-slate-50 px-2 py-0.5 text-[0.62rem] font-bold uppercase">
                          {String(r.flow_type ?? "")}
                        </span>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </PageFrame>
  )
}
