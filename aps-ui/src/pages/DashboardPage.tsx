import { useMemo } from "react"
import { Link } from "react-router-dom"

import { useAps, type JsonRecord } from "@/context/ApsContext"
import { PageFrame } from "@/components/PageFrame"
import { StatusBadge } from "@/components/StatusBadge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  fmtDate,
  formatStatus,
  num,
} from "@/lib/apsFormat"
import { cn } from "@/lib/utils"

const BAR_COLORS = [
  "bg-orange-500",
  "bg-emerald-500",
  "bg-violet-500",
  "bg-amber-500",
  "bg-sky-500",
  "bg-pink-500",
]

function campaignId(c: JsonRecord) {
  return String(c.campaign_id ?? c.Campaign_ID ?? "—")
}

function releaseRows(campaigns: JsonRecord[]) {
  return [...campaigns]
    .sort((a, b) =>
      String(a.Due_Date ?? "").localeCompare(String(b.Due_Date ?? ""))
    )
    .slice(0, 8)
}

export function DashboardPage() {
  const { overview, campaigns, capacity, loading } = useAps()

  const queueRows = useMemo(() => releaseRows(campaigns), [campaigns])
  const timelineGroups = useMemo(() => campaigns.slice(0, 8), [campaigns])

  const topCapacity = useMemo(() => {
    return [...capacity]
      .sort(
        (a, b) =>
          num(b["Utilisation_%"] ?? b.Utilisation_Percent ?? b.utilisation) -
          num(a["Utilisation_%"] ?? a.Utilisation_Percent ?? a.utilisation)
      )
      .slice(0, 5)
  }, [capacity])

  const alerts = useMemo(() => {
    if (!overview) return []
    const out: { cls: string; title: string; sub: string }[] = []
    const lateN = num(overview.campaigns_late)
    const heldN = num(overview.campaigns_held)
    if (lateN)
      out.push({
        cls: "border-l-red-500 bg-red-50/90",
        title: `${lateN} late campaigns`,
        sub: "Review overdue campaigns and due-date margin.",
      })
    if (heldN)
      out.push({
        cls: "border-l-amber-500 bg-amber-50/90",
        title: `${heldN} campaigns on material hold`,
        sub: "Material shortages are blocking release.",
      })
    const shortageAlerts = overview.shortage_alerts
    if (Array.isArray(shortageAlerts)) {
      shortageAlerts.slice(0, 3).forEach((s: unknown) => {
        const r = s as JsonRecord
        const sev = String(r.severity ?? "")
        out.push({
          cls:
            sev === "HIGH"
              ? "border-l-red-500 bg-red-50/90"
              : "border-l-amber-500 bg-amber-50/90",
          title: `${String(r.sku_id ?? "SKU")} short`,
          sub: `${String(r.shortage_qty ?? "—")} MT shortage on ${String(r.campaign_id ?? "campaign")}`,
        })
      })
    }
    const ss = String(overview.solver_status ?? "").toUpperCase()
    if (ss && !["OPTIMAL", "NOT RUN", "WORKBOOK"].includes(ss)) {
      out.push({
        cls: "border-l-sky-500 bg-sky-50/90",
        title: `Solver ${String(overview.solver_status)}`,
        sub:
          formatStatus(overview.solver_detail) ||
          "A non-optimal or fallback condition was reported.",
      })
    }
    if (!out.length) {
      out.push({
        cls: "border-l-slate-400 bg-slate-50/90",
        title: "Plan healthy",
        sub: "No major lateness or hold conditions detected.",
      })
    }
    return out
  }, [overview])

  const peakUtil =
    overview?.max_utilisation != null
      ? `${Math.round(num(overview.max_utilisation))}% peak`
      : "—"

  return (
    <PageFrame title="Dashboard">
      <div className="grid grid-cols-1 gap-2 lg:grid-cols-[1.65fr_0.95fr]">
        <div className="flex min-w-0 flex-col gap-2">
          <Card>
            <CardHeader className="items-center">
              <div className="min-w-0">
                <CardTitle>Campaign timeline overview</CardTitle>
              </div>
              <CardAction>
                <Button
                  variant="softGhost"
                  size="sm"
                  className="h-8 no-underline"
                  nativeButton={false}
                  render={(props) => <Link {...props} to="/schedule" />}
                >
                  Open schedule
                </Button>
              </CardAction>
            </CardHeader>
            <CardContent>
              {!timelineGroups.length ? (
                <div className="aps-notice text-left">
                  Run Schedule to populate campaign execution timeline.
                </div>
              ) : (
                <div className="flex flex-col gap-2">
                  {timelineGroups.map((c, i) => {
                    const width = Math.max(
                      10,
                      Math.min(96, num(c.total_mt ?? c.Total_MT) / 30)
                    )
                    const left = Math.min(78, i * 8 + 2)
                    return (
                      <div
                        key={campaignId(c) + i}
                        className="grid grid-cols-[minmax(0,7rem)_1fr_auto] items-center gap-2"
                      >
                        <div className="truncate text-[0.78rem] font-bold text-[var(--aps-text)]">
                          {campaignId(c)}
                        </div>
                        <div className="relative h-2.5 rounded-full bg-[var(--aps-border-soft)]">
                          <div
                            className={cn(
                              "absolute top-0 h-2.5 rounded-full",
                              BAR_COLORS[i % BAR_COLORS.length]
                            )}
                            style={{ left: `${left}%`, width: `${width}%` }}
                          />
                        </div>
                        <div className="shrink-0 text-[0.72rem] text-[var(--aps-text-soft)]">
                          {String(c.total_mt ?? c.Total_MT ?? "—")} MT
                        </div>
                      </div>
                    )
                  })}
                </div>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <div className="min-w-0">
                <CardTitle>Campaign release queue</CardTitle>
                <CardDescription>
                  Sorted by due date from the application API.
                </CardDescription>
              </div>
            </CardHeader>
            <CardContent className="p-0">
              <div className="overflow-x-auto">
                <table className="aps-table">
                  <thead>
                    <tr>
                      <th>Campaign</th>
                      <th>Grade</th>
                      <th>MT</th>
                      <th>Due</th>
                      <th>Margin</th>
                      <th>Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {loading && !queueRows.length ? (
                      <tr>
                        <td colSpan={6} className="text-[var(--aps-text-soft)]">
                          Loading…
                        </td>
                      </tr>
                    ) : !queueRows.length ? (
                      <tr>
                        <td colSpan={6} className="text-[var(--aps-text-soft)]">
                          Run Schedule to populate the queue.
                        </td>
                      </tr>
                    ) : (
                      queueRows.map((r) => {
                        const m =
                          r.Margin_Hrs == null
                            ? "—"
                            : `${num(r.Margin_Hrs) >= 0 ? "+" : ""}${Math.round(num(r.Margin_Hrs))}h`
                        return (
                          <tr key={campaignId(r)}>
                            <td className="font-bold">{campaignId(r)}</td>
                            <td>{String(r.grade ?? r.Grade ?? "—")}</td>
                            <td>{String(r.total_mt ?? r.Total_MT ?? "—")}</td>
                            <td>{fmtDate(r.Due_Date)}</td>
                            <td>{m}</td>
                            <td>
                              <StatusBadge
                                status={
                                  r.release_status ??
                                  r.Release_Status ??
                                  r.Status ??
                                  "—"
                                }
                              />
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
        </div>

        <div className="flex min-w-0 flex-col gap-2">
          <Card>
            <CardHeader>
              <div className="min-w-0">
                <CardTitle>Bottleneck analysis</CardTitle>
                <CardDescription>
                  Current peak resource and top loads.
                </CardDescription>
              </div>
              <span className="shrink-0 text-[0.65rem] text-[var(--aps-text-soft)]">
                {peakUtil}
              </span>
            </CardHeader>
            <CardContent className="space-y-3">
              <div className="rounded-[0.35rem] border border-[var(--aps-border-soft)] bg-[var(--aps-panel-muted)]/60 px-3 py-2.5">
                <div className="text-[1.02rem] font-extrabold text-[var(--aps-text)]">
                  {overview?.bottleneck != null
                    ? String(overview.bottleneck)
                    : "—"}
                </div>
                <p className="mt-0.5 text-[0.76rem] text-[var(--aps-text-soft)]">
                  {overview?.bottleneck
                    ? "Highest utilisation in current horizon."
                    : "Run Schedule to identify the bottleneck."}
                </p>
              </div>
              {!topCapacity.length ? (
                <div className="text-[0.78rem] text-[var(--aps-text-faint)]">
                  No capacity rows loaded.
                </div>
              ) : (
                <div className="flex flex-col gap-2">
                  {topCapacity.map((r) => {
                    const util = Math.round(
                      num(r["Utilisation_%"] ?? r.Utilisation_Percent ?? r.utilisation)
                    )
                    const u = util > 100 ? "bg-red-500" : util > 85 ? "bg-amber-500" : util > 60 ? "bg-emerald-500" : "bg-sky-500"
                    return (
                      <div
                        key={String(r.Resource_ID ?? r.resource_id)}
                        className="grid grid-cols-[1fr_1fr_auto] items-center gap-2 text-[0.75rem]"
                      >
                        <div className="truncate font-semibold text-[var(--aps-text)]">
                          {String(r.Resource_ID ?? r.resource_id ?? "—")}
                        </div>
                        <div className="h-2 rounded-full bg-[var(--aps-border-soft)]">
                          <div
                            className={cn("h-2 rounded-full", u)}
                            style={{
                              width: `${Math.min(Math.max(util, 0), 100)}%`,
                            }}
                          />
                        </div>
                        <div className="text-right font-extrabold tabular-nums">
                          {util}%
                        </div>
                      </div>
                    )
                  })}
                </div>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <div className="min-w-0">
                <CardTitle>Planning alerts</CardTitle>
                <CardDescription>
                  Late campaigns, material holds, solver condition.
                </CardDescription>
              </div>
              <span className="shrink-0 text-[0.65rem] text-[var(--aps-text-soft)]">
                {alerts.length} alerts
              </span>
            </CardHeader>
            <CardContent className="space-y-2">
              {alerts.map((a, i) => (
                <div
                  key={i}
                  className={cn(
                    "rounded-[0.35rem] border border-[var(--aps-border-soft)] border-l-[0.22rem] px-3 py-2",
                    a.cls
                  )}
                >
                  <div className="text-[0.78rem] font-extrabold text-[var(--aps-text)]">
                    {a.title}
                  </div>
                  <div className="mt-0.5 text-[0.72rem] text-[var(--aps-text-soft)]">
                    {a.sub}
                  </div>
                </div>
              ))}
            </CardContent>
          </Card>
        </div>
      </div>
    </PageFrame>
  )
}
