import { useMemo, useState } from "react"

import { useAps, type JsonRecord } from "@/context/ApsContext"
import { DispatchBoard } from "@/components/DispatchBoard"
import { useDispatchMetrics } from "@/hooks/useDispatchMetrics"
import { GanttView } from "@/components/GanttView"
import { PageFrame } from "@/components/PageFrame"
import { StatusBadge } from "@/components/StatusBadge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { fmtDate, num } from "@/lib/apsFormat"
import { cn } from "@/lib/utils"

export function SchedulePage() {
  const { gantt, horizon, patchJobReschedule } = useAps()
  return (
    <PageFrame
      title="Schedule"
      subtitle="Finite timeline generated from /api/aps/schedule/gantt."
      actions={
        <Button
          variant="softGhost"
          className="h-[2.2rem] text-[0.78rem] font-bold"
          onClick={() => void patchJobReschedule()}
        >
          Patch Job
        </Button>
      }
    >
      <div className="mb-2 text-left aps-notice">
        This screen stays iframe-friendly, but the bars below are now based on live
        schedule data rather than static placeholders.
      </div>
      <Card>
        <CardContent className="py-4">
          <GanttView jobs={gantt} days={horizon} />
        </CardContent>
      </Card>
    </PageFrame>
  )
}

export function OrdersPage() {
  const {
    orders,
    campaigns,
    selectedOrders,
    toggleOrderSelection,
    assignSelectedOrders,
    assignOrdersToCampaign,
    createOrder,
    editOrder,
    deleteOrder,
  } = useAps()
  const [search, setSearch] = useState("")
  const [priority, setPriority] = useState("")
  const [grade, setGrade] = useState("")

  const gradeOptions = useMemo(() => {
    const g = new Set<string>()
    for (const o of orders) {
      const gr = o.Grade
      if (gr) g.add(String(gr))
    }
    return [...g].sort()
  }, [orders])

  const rows = useMemo(() => {
    return orders.filter((o) => {
      const text = [o.SO_ID, o.Customer, o.Grade, o.SKU_ID]
        .map((x) => String(x ?? "").toLowerCase())
        .join(" ")
      if (search && !text.includes(search.toLowerCase())) return false
      if (
        priority &&
        String(o.Priority ?? "").toUpperCase() !== priority.toUpperCase()
      )
        return false
      if (grade && String(o.Grade ?? "") !== grade) return false
      return true
    })
  }, [grade, orders, priority, search])

  const dropCampaigns = useMemo(() => {
    return campaigns.filter((c) => {
      const rs = String(
        c.release_status ?? c.Release_Status ?? c.Status ?? ""
      ).toUpperCase()
      return rs.includes("RELEASED") || rs.includes("HOLD")
    })
  }, [campaigns])

  return (
    <PageFrame
      title="Sales Orders"
      subtitle="List, create, update, delete, and assign through the application API."
      actions={
        <>
          <Button
            variant="successSolid"
            className="h-[2.2rem] text-[0.78rem] font-bold"
            onClick={() => void createOrder()}
          >
            New Order
          </Button>
          <Button
            variant="ink"
            className="h-[2.2rem] text-[0.78rem] font-bold"
            disabled={selectedOrders.length === 0}
            onClick={() => void assignSelectedOrders()}
          >
            Assign Selected
          </Button>
        </>
      }
      filters={
        <div className="flex w-full flex-wrap gap-2">
          <Input
            placeholder="Search SO / Grade / Customer…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="h-[2.2rem] max-w-md flex-1 rounded-[0.2rem] border-[var(--aps-border)] text-[0.78rem] shadow-none"
          />
          <select
            className="aps-select min-w-[9rem]"
            aria-label="Priority filter"
            value={priority}
            onChange={(e) => setPriority(e.target.value)}
          >
            <option value="">All priorities</option>
            <option>URGENT</option>
            <option>HIGH</option>
            <option>NORMAL</option>
          </select>
          <select
            className="aps-select min-w-[9rem]"
            aria-label="Grade filter"
            value={grade}
            onChange={(e) => setGrade(e.target.value)}
          >
            <option value="">All grades</option>
            {gradeOptions.map((g) => (
              <option key={g} value={g}>
                {g}
              </option>
            ))}
          </select>
        </div>
      }
      contentClassName="gap-2"
    >
      <div
        className={cn(
          "grid min-h-0 flex-1 gap-2",
          "grid-cols-1 xl:grid-cols-[1.2fr_0.9fr]"
        )}
      >
        <Card className="flex min-h-[240px] min-w-0 flex-col">
          <CardHeader>
            <div className="min-w-0">
              <CardTitle>Open sales orders</CardTitle>
              <CardDescription>{rows.length} orders</CardDescription>
            </div>
          </CardHeader>
          <CardContent className="flex-1 overflow-auto p-0">
            <div className="overflow-x-auto">
              <table className="aps-table">
                <thead>
                  <tr>
                    <th />
                    <th>SO ID</th>
                    <th>Customer</th>
                    <th>Grade</th>
                    <th>Qty MT</th>
                    <th>Due</th>
                    <th>Priority</th>
                    <th>Campaign</th>
                    <th>Status</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {!rows.length ? (
                    <tr>
                      <td colSpan={10} className="text-[var(--aps-text-soft)]">
                        No orders found.
                      </td>
                    </tr>
                  ) : (
                    rows.map((o) => {
                      const so = String(o.SO_ID ?? "")
                      const checked = selectedOrders.includes(so)
                      return (
                        <tr
                          key={so}
                          draggable
                          onDragStart={(ev) => {
                            ev.dataTransfer.setData("so_id", so)
                          }}
                        >
                          <td>
                            <input
                              type="checkbox"
                              checked={checked}
                              onChange={(e) =>
                                toggleOrderSelection(so, e.target.checked)
                              }
                              aria-label={`Select ${so}`}
                            />
                          </td>
                          <td className="font-bold">{so || "—"}</td>
                          <td>{String(o.Customer ?? "—")}</td>
                          <td>{String(o.Grade ?? "—")}</td>
                          <td>{String(o.Order_Qty_MT ?? "—")}</td>
                          <td>{fmtDate(o.Delivery_Date)}</td>
                          <td>{String(o.Priority ?? "—")}</td>
                          <td>{String(o.Campaign_ID ?? "—")}</td>
                          <td>
                            <StatusBadge status={o.Status ?? "Open"} />
                          </td>
                          <td>
                            <div className="flex flex-wrap gap-1">
                              <Button
                                variant="outline"
                                size="xs"
                                className="h-7 text-[0.7rem]"
                                onClick={() => void editOrder(so)}
                              >
                                Edit
                              </Button>
                              <Button
                                variant="destructive"
                                size="xs"
                                className="h-7 text-[0.7rem]"
                                onClick={() => void deleteOrder(so)}
                              >
                                Delete
                              </Button>
                            </div>
                          </td>
                        </tr>
                      )
                    })
                  )}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>
        <Card className="min-h-[200px] min-w-0">
          <CardHeader>
            <div className="min-w-0">
              <CardTitle>Campaign drop zones</CardTitle>
              <CardDescription>Drag or bulk-assign orders.</CardDescription>
            </div>
          </CardHeader>
          <CardContent>
            {!dropCampaigns.length ? (
              <div className="aps-notice">
                No campaigns available for assignment.
              </div>
            ) : (
              <div className="flex flex-col gap-2">
                {dropCampaigns.slice(0, 8).map((c) => {
                  const cid = String(c.campaign_id ?? c.Campaign_ID ?? "")
                  return (
                    <DropZone
                      key={cid}
                      campaignId={cid}
                      title={`${cid} · ${String(c.grade ?? c.Grade ?? "—")}`}
                      sub={`${String(c.total_mt ?? c.Total_MT ?? "—")} MT · ${String(c.heats ?? c.Heats ?? "—")} heats`}
                      onDropSo={(so) =>
                        void assignOrdersToCampaign([so], cid)
                      }
                    />
                  )
                })}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </PageFrame>
  )
}

function DropZone({
  campaignId,
  title,
  sub,
  onDropSo,
}: {
  campaignId: string
  title: string
  sub: string
  onDropSo: (so: string) => void
}) {
  const [over, setOver] = useState(false)
  return (
    <div
      className={cn(
        "rounded-[var(--radius)] border border-dashed border-[#cdd8e5] bg-gradient-to-b from-white to-[#fbfdff] px-3 py-3 transition-colors",
        over && "border-teal-600 bg-teal-50/80"
      )}
      onDragOver={(e) => {
        e.preventDefault()
        setOver(true)
      }}
      onDragLeave={() => setOver(false)}
      onDrop={(e) => {
        e.preventDefault()
        setOver(false)
        const so = e.dataTransfer.getData("so_id")
        if (so) onDropSo(so)
      }}
    >
      <div className="text-[0.8rem] font-extrabold text-[var(--aps-text)]">
        {title}
      </div>
      <div className="mt-0.5 text-[0.71rem] text-[var(--aps-text-soft)]">
        {sub}
      </div>
      <div className="mt-1 text-[0.6rem] text-[var(--aps-text-faint)]">
        ID: {campaignId}
      </div>
    </div>
  )
}

export function DispatchPage() {
  const { gantt, capacity, dispatchBoard } = useAps()
  const metrics = useDispatchMetrics(gantt, dispatchBoard)
  return (
    <PageFrame
      title="Dispatch"
      subtitle="Machine-by-machine execution cards from dispatch board and schedule rows."
      metrics={[
        { label: "Machines active", value: String(metrics.machines) },
        { label: "Jobs visible", value: String(metrics.jobs) },
        { label: "Queue violations", value: String(metrics.violations) },
        { label: "MT dispatched", value: metrics.mt },
      ]}
    >
      <DispatchBoard
        jobs={gantt}
        capacity={capacity}
        dispatchBoard={dispatchBoard}
      />
    </PageFrame>
  )
}

function MaterialStatusPill({ status }: { status: string }) {
  const u = status.toUpperCase()
  let cls =
    "bg-slate-100 text-slate-700 border-slate-200 dark:bg-slate-800 dark:text-slate-200"
  if (u.includes("MAKE") || u.includes("CONVERT"))
    cls = "bg-violet-50 text-violet-800 border-violet-200"
  if (u.includes("SHORT") || u.includes("CRITICAL"))
    cls = "bg-red-50 text-red-700 border-red-200"
  if (u.includes("BLOCKED")) cls = "bg-amber-50 text-amber-900 border-amber-200"
  return (
    <span
      className={cn(
        "inline-flex rounded-full border px-2 py-0.5 text-[0.62rem] font-bold",
        cls
      )}
    >
      {status}
    </span>
  )
}

export function MaterialPage() {
  const { material } = useAps()
  const { summary, campaigns: camps } = material

  const held = camps.filter((c) =>
    String(c.release_status ?? "").toUpperCase().includes("HOLD")
  ).length
  const ok = camps.length - held
  const crit = camps.filter(
    (c) => num(c.shortage_qty) > 1e-9
  ).length

  const sumCampaigns = String(summary.Campaigns ?? camps.length ?? "—")
  const sumReleased = String(
    summary.Released ??
      camps.filter(
        (c) => String(c.release_status ?? "").toUpperCase() === "RELEASED"
      ).length ??
      "—"
  )
  const sumHeld = String(summary.Held ?? held ?? "—")
  const sumShortage = String(summary["Shortage Lines"] ?? "—")
  const sumRequired = Number(
    summary["Total Required Qty"] ?? 0
  ).toLocaleString("en-US", { maximumFractionDigits: 2 })
  const sumInventory = Number(
    summary["Inventory Covered Qty"] ?? 0
  ).toLocaleString("en-US", { maximumFractionDigits: 2 })
  const sumMake = Number(
    summary["Make / Convert Qty"] ?? 0
  ).toLocaleString("en-US", { maximumFractionDigits: 2 })

  return (
    <PageFrame
      title="Material"
      subtitle="Campaign-by-campaign material allocation and shortage trace."
      metrics={[
        { label: "Materials OK", value: String(ok), tone: "success" },
        { label: "On Hold", value: String(held), tone: "warn" },
        { label: "Critical", value: String(crit), tone: "danger" },
        { label: "Campaigns", value: String(camps.length) },
      ]}
    >
      <Card className="mb-2 shrink-0">
        <CardHeader>
          <CardTitle>Material Summary</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-7">
            {[
              ["Campaigns", sumCampaigns],
              ["Released", sumReleased],
              ["Held", sumHeld],
              ["Shortage Lines", sumShortage],
              ["Required Qty", sumRequired],
              ["Inventory Covered", sumInventory],
              ["Make / Convert", sumMake],
            ].map(([label, val]) => (
              <div
                key={label}
                className="flex flex-col gap-0.5 border-b border-[var(--aps-border-soft)] pb-2 sm:border-0 sm:pb-0"
              >
                <span className="text-[0.65rem] font-semibold tracking-wide text-[var(--aps-text-faint)] uppercase">
                  {label}
                </span>
                <span className="text-[0.95rem] font-black text-[var(--aps-text)]">
                  {val}
                </span>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>
      {!camps.length ? (
        <div className="aps-notice text-left">
          Run Schedule to populate material allocation and shortage trace.
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          {camps.map((camp) => {
            const cid = String(camp.campaign_id ?? "—")
            const plants = camp.plants
            const detailRows = camp.detail_rows
            return (
              <Card key={cid}>
                <CardHeader className="flex-row items-center justify-between">
                  <div>
                    <CardTitle className="text-[0.95rem]">{cid}</CardTitle>
                    <CardDescription>
                      {String(camp.grade ?? "—")} ·{" "}
                      {num(camp.required_qty).toLocaleString("en-US", {
                        maximumFractionDigits: 2,
                      })}{" "}
                      MT
                    </CardDescription>
                  </div>
                  <StatusBadge status={camp.release_status ?? "OPEN"} />
                </CardHeader>
                <CardContent>
                  {Array.isArray(plants) && plants.length > 0 ? (
                    <div className="flex flex-col gap-4">
                      {(plants as JsonRecord[]).map((plant, pi) => (
                        <div key={pi}>
                          <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                            <div className="text-[0.8rem] font-extrabold">
                              {String(plant.plant ?? "UNKNOWN")}
                            </div>
                            <div className="flex gap-4 text-[0.7rem] text-[var(--aps-text-soft)]">
                              <span>
                                Req:{" "}
                                {num(plant.required_qty).toLocaleString("en-US", {
                                  maximumFractionDigits: 2,
                                })}{" "}
                                MT
                              </span>
                              <span>
                                Inv:{" "}
                                {num(
                                  plant.inventory_covered_qty
                                ).toLocaleString("en-US", {
                                  maximumFractionDigits: 2,
                                })}{" "}
                                MT
                              </span>
                            </div>
                          </div>
                          <div className="overflow-x-auto">
                            <table className="aps-table text-[0.72rem]">
                              <thead>
                                <tr>
                                  <th>Type</th>
                                  <th>SKU</th>
                                  <th>Name</th>
                                  <th className="text-right">Req</th>
                                  <th className="text-right">Avail</th>
                                  <th className="text-right">Consumed</th>
                                  <th className="text-right">Remaining</th>
                                  <th className="text-center">Status</th>
                                </tr>
                              </thead>
                              <tbody>
                                {(Array.isArray(plant.rows)
                                  ? (plant.rows as JsonRecord[])
                                  : []
                                ).map((row, ri) => (
                                  <tr key={ri}>
                                    <td>{String(row.material_type ?? "")}</td>
                                    <td>
                                      <code className="rounded bg-[var(--aps-panel-muted)] px-1 text-[0.65rem]">
                                        {String(row.material_sku ?? "")}
                                      </code>
                                    </td>
                                    <td>{String(row.material_name ?? "")}</td>
                                    <td className="text-right">
                                      {num(row.required_qty).toFixed(2)}
                                    </td>
                                    <td className="text-right">
                                      {num(row.available_before).toFixed(2)}
                                    </td>
                                    <td className="text-right">
                                      {num(row.consumed).toFixed(2)}
                                    </td>
                                    <td className="text-right">
                                      {num(row.remaining_after).toFixed(2)}
                                    </td>
                                    <td className="text-center">
                                      <MaterialStatusPill
                                        status={String(row.status ?? "OK")}
                                      />
                                    </td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        </div>
                      ))}
                    </div>
                  ) : Array.isArray(detailRows) && detailRows.length > 0 ? (
                    <div className="overflow-x-auto">
                      <table className="aps-table text-[0.72rem]">
                        <thead>
                          <tr>
                            <th>SKU</th>
                            <th>Required</th>
                            <th>Consumed</th>
                            <th>Available</th>
                            <th>Status</th>
                          </tr>
                        </thead>
                        <tbody>
                          {(detailRows as JsonRecord[]).map((row, ri) => (
                            <tr key={ri}>
                              <td>{String(row.sku_id ?? "")}</td>
                              <td>{num(row.required_qty).toFixed(3)}</td>
                              <td>{num(row.consumed_qty).toFixed(3)}</td>
                              <td>{num(row.available_before).toFixed(3)}</td>
                              <td>{String(row.type ?? "")}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  ) : (
                    <div className="text-[0.75rem] text-[var(--aps-text-soft)]">
                      No line detail for this campaign.
                    </div>
                  )}
                </CardContent>
              </Card>
            )
          })}
        </div>
      )}
    </PageFrame>
  )
}

export function CapacityPage() {
  const { capacity } = useAps()
  const sorted = useMemo(
    () =>
      [...capacity].sort(
        (a, b) =>
          num(b["Utilisation_%"] ?? b.utilisation) -
          num(a["Utilisation_%"] ?? a.utilisation)
      ),
    [capacity]
  )
  const avg = sorted.length
    ? (
        sorted.reduce(
          (acc, r) => acc + num(r["Utilisation_%"] ?? r.utilisation),
          0
        ) / sorted.length
      ).toFixed(1)
    : "—"
  const top = sorted[0]
  const bn = top
    ? String(top.Resource_ID ?? top.resource_id ?? "—")
    : "—"
  const bnU = top
    ? `${Math.round(num(top["Utilisation_%"] ?? top.utilisation))}% utilisation`
    : "—"

  return (
    <PageFrame
      title="Capacity"
      subtitle="Utilisation map plus bottleneck list."
      metrics={[
        { label: "Bottleneck resource", value: bn, sub: bnU },
        {
          label: "Average utilisation",
          value: `${avg}%`,
          sub: "Across visible resources",
        },
      ]}
    >
      <Card>
        <CardHeader>
          <div>
            <CardTitle>Capacity map</CardTitle>
            <CardDescription>
              Data from /api/aps/capacity/map and /api/aps/capacity/bottlenecks.
            </CardDescription>
          </div>
        </CardHeader>
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="aps-table">
              <thead>
                <tr>
                  <th>Resource</th>
                  <th>Operation</th>
                  <th>Demand Hrs</th>
                  <th>Avail Hrs</th>
                  <th>Util %</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                {!sorted.length ? (
                  <tr>
                    <td colSpan={6} className="text-[var(--aps-text-soft)]">
                      No capacity rows loaded.
                    </td>
                  </tr>
                ) : (
                  sorted.map((r) => {
                    const util = Math.round(
                      num(r["Utilisation_%"] ?? r.utilisation)
                    )
                    const st =
                      r.Status ??
                      (util > 100 ? "Overloaded" : util > 85 ? "High" : "OK")
                    return (
                      <tr key={String(r.Resource_ID ?? r.resource_id)}>
                        <td className="font-bold">
                          {String(r.Resource_ID ?? r.resource_id ?? "—")}
                        </td>
                        <td>
                          {String(
                            r.Operation_Group ?? r.operation ?? "—"
                          )}
                        </td>
                        <td>{String(r.Demand_Hrs ?? r.demand_hrs ?? "—")}</td>
                        <td>{String(r.Avail_Hrs_14d ?? r.avail_hrs ?? "—")}</td>
                        <td>{util}%</td>
                        <td>
                          <StatusBadge status={st} />
                        </td>
                      </tr>
                    )
                  })
                )}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>
    </PageFrame>
  )
}

export function ScenariosPage() {
  const {
    scenarios,
    applyScenario,
    createScenario,
    editScenario,
    deleteScenario,
  } = useAps()

  return (
    <PageFrame
      title="Scenarios"
      subtitle="Preview, create, update, delete, and apply scenarios."
    >
      <div className="mb-2 grid grid-cols-1 gap-2 sm:grid-cols-3">
        {scenarios.slice(0, 3).map((r, idx) => {
          const key = Object.keys(r)[0] ?? `Scenario ${idx + 1}`
          const value = String(r[key] ?? "")
          const keyVal = String(Object.values(r)[0] ?? key)
          return (
            <Card key={idx}>
              <CardContent className="py-3">
                <div className="text-[0.65rem] font-bold tracking-wide text-[var(--aps-text-faint)] uppercase">
                  Scenario
                </div>
                <div className="text-[1.1rem] font-black text-[var(--aps-text)]">
                  {key}
                </div>
                <div className="mt-0.5 text-[0.65rem] text-[var(--aps-text-soft)]">
                  {value || "Workbook-backed scenario row"}
                </div>
                <div className="mt-3 flex flex-wrap gap-2">
                  <Button
                    variant="ink"
                    size="sm"
                    className="h-8"
                    onClick={() => void applyScenario(key)}
                  >
                    Apply
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    className="h-8"
                    onClick={() => void editScenario(keyVal)}
                  >
                    Edit
                  </Button>
                </div>
              </CardContent>
            </Card>
          )
        })}
        {!scenarios.length ? (
          <div className="aps-notice sm:col-span-3">No scenario rows available.</div>
        ) : null}
      </div>
      <Card>
        <CardHeader className="flex-row flex-wrap items-center justify-between gap-2">
          <div>
            <CardTitle>Scenario list</CardTitle>
            <CardDescription>Workbook-backed scenario rows.</CardDescription>
          </div>
          <Button
            variant="successSolid"
            className="h-8"
            onClick={() => void createScenario()}
          >
            Add Scenario
          </Button>
        </CardHeader>
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="aps-table">
              <thead>
                <tr>
                  <th>Key</th>
                  <th>Value</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {!scenarios.length ? (
                  <tr>
                    <td colSpan={3} className="text-[var(--aps-text-soft)]">
                      No scenarios returned.
                    </td>
                  </tr>
                ) : (
                  scenarios.map((r, i) => {
                    const key = Object.keys(r)[0] ?? "—"
                    const value = String(r[key] ?? "")
                    const keyVal = String(Object.values(r)[0] ?? key)
                    return (
                      <tr key={i}>
                        <td className="font-bold">{key}</td>
                        <td>{value}</td>
                        <td>
                          <div className="flex flex-wrap gap-1">
                            <Button
                              variant="outline"
                              size="xs"
                              className="h-7"
                              onClick={() => void applyScenario(key)}
                            >
                              Apply
                            </Button>
                            <Button
                              variant="outline"
                              size="xs"
                              className="h-7"
                              onClick={() => void editScenario(keyVal)}
                            >
                              Edit
                            </Button>
                            <Button
                              variant="destructive"
                              size="xs"
                              className="h-7"
                              onClick={() => void deleteScenario(keyVal)}
                            >
                              Delete
                            </Button>
                          </div>
                        </td>
                      </tr>
                    )
                  })
                )}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>
    </PageFrame>
  )
}
