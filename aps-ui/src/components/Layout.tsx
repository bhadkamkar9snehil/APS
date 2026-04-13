import { useMemo } from "react"
import { NavLink, Outlet } from "react-router-dom"

import { useAps } from "@/context/ApsContext"
import { MetricTile } from "@/components/PageFrame"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { num } from "@/lib/apsFormat"
import { cn } from "@/lib/utils"

const TABS = [
  { code: "DB", label: "Dashboard", path: "/" },
  { code: "CP", label: "Planning", path: "/campaigns" },
  { code: "BM", label: "BOM", path: "/bom" },
  { code: "MT", label: "Material", path: "/material" },
  { code: "GS", label: "Schedule", path: "/schedule" },
  { code: "SO", label: "Orders", path: "/orders" },
  { code: "EQ", label: "Dispatch", path: "/dispatch" },
  { code: "CM", label: "Capacity", path: "/capacity" },
  { code: "CT", label: "CTP", path: "/ctp" },
  { code: "WF", label: "Scenarios", path: "/scenarios" },
  { code: "MD", label: "Master Data", path: "/master-data" },
] as const

export function Layout() {
  const {
    health,
    overview,
    horizon,
    solverDepth,
    setHorizon,
    setSolverDepth,
    runSchedule,
    running,
    loading,
    error,
  } = useAps()

  const summaryTiles = useMemo(() => {
    const s = overview
    if (!s) {
      return [
        {
          label: "Campaigns",
          value: "—",
          sub: loading ? "Loading…" : "Run schedule or connect API",
        },
        { label: "Heats planned", value: "—", sub: "—", tone: "success" as const },
        { label: "MT planned", value: "—", sub: `${horizon}-day horizon` },
        { label: "Throughput", value: "—", sub: "MT/day average" },
        { label: "On-time", value: "—", sub: "—", tone: "warn" as const },
        { label: "Late", value: "—", sub: "—", tone: "danger" as const },
      ]
    }
    const total = num(s.campaigns_total)
    const released = num(s.campaigns_released)
    const held = num(s.campaigns_held)
    const late = num(s.campaigns_late)
    const heats = num(s.total_heats)
    const mt = num(s.total_mt)
    const ot =
      s.on_time_pct == null ? "—" : `${num(s.on_time_pct).toFixed(1)}%`
    const throughput =
      s.throughput_mt_day != null
        ? num(s.throughput_mt_day).toFixed(1)
        : "—"
    const bn = s.bottleneck != null ? String(s.bottleneck) : ""
    return [
      {
        label: "Campaigns",
        value: String(total || "—"),
        sub: `${released} released · ${held} held`,
      },
      {
        label: "Heats planned",
        value: String(heats || "—"),
        sub: mt ? `${mt.toLocaleString()} MT` : "—",
        tone: "success" as const,
      },
      {
        label: "MT planned",
        value: mt ? mt.toLocaleString() : "—",
        sub: `${horizon}-day horizon`,
      },
      {
        label: "Throughput",
        value: throughput,
        sub: "MT/day average",
      },
      {
        label: "On-time",
        value: ot,
        sub: String(s.solver_status ?? "—"),
        tone: "warn" as const,
      },
      {
        label: "Late",
        value: String(late || "—"),
        sub: bn ? `Bottleneck ${bn}` : "No bottleneck yet",
        tone: "danger" as const,
      },
    ]
  }, [horizon, loading, overview])

  const held = overview ? num(overview.campaigns_held) : 0
  const late = overview ? num(overview.campaigns_late) : 0
  const solverLabel = overview
    ? `Solver ${String(overview.solver_status ?? "—")}`
    : "Solver —"

  return (
    <div className="min-h-screen p-2 font-sans text-[var(--aps-text)]">
      <div
        className="flex min-h-[calc(100vh-1rem)] flex-col overflow-hidden rounded-[0.4rem] border border-[rgba(219,228,238,0.92)] bg-white/85 shadow-[0_0.125rem_0.25rem_rgba(15,23,42,0.05),0_1.25rem_2.5rem_rgba(15,23,42,0.08)] backdrop-blur-[14px]"
        style={{ WebkitBackdropFilter: "blur(14px)" }}
      >
        <nav className="flex items-center justify-center gap-2 border-b border-[var(--aps-border-soft)] bg-white/80 px-4 py-[0.45rem]">
          <div className="flex max-w-full flex-nowrap items-center justify-start gap-1 overflow-x-auto pb-0.5 [scrollbar-width:thin] sm:justify-center">
            {TABS.map((tab) => (
              <NavLink
                key={tab.path}
                to={tab.path}
                end={tab.path === "/"}
                className={({ isActive }) =>
                  cn(
                    "inline-flex h-8 items-center rounded-[0.2rem] border px-[0.85rem] text-[0.8rem] font-semibold transition-all",
                    isActive
                      ? "border-[var(--aps-brand)] bg-white text-[var(--aps-text)] shadow-[0_2px_8px_rgba(0,0,0,0.05)]"
                      : "border-[var(--aps-border-soft)] bg-black/[0.02] text-[var(--aps-text-soft)] hover:border-[var(--aps-border)] hover:bg-black/[0.05] hover:text-[var(--aps-text)] dark:bg-white/5"
                  )
                }
              >
                {({ isActive }) => (
                  <>
                    <span
                      className={cn(
                        "mr-1 text-[0.55rem] font-extrabold opacity-70",
                        isActive && "text-[var(--aps-brand)] opacity-100"
                      )}
                    >
                      {tab.code}
                    </span>
                    {tab.label}
                  </>
                )}
              </NavLink>
            ))}
          </div>
        </nav>

        <div className="border-b border-[var(--aps-border-soft)] bg-white">
          <div className="flex flex-wrap items-center justify-between gap-3 border-b border-black/[0.04] bg-black/[0.015] px-4 py-2">
            <div className="flex flex-wrap items-center gap-2">
              <Badge
                dot
                variant={
                  health?.ok
                    ? health.workbook_ok === true
                      ? "success"
                      : "warn"
                    : "danger"
                }
              >
                {health?.ok
                  ? health.workbook_ok === true
                    ? "API + workbook connected"
                    : "API up, workbook issue"
                  : health === null
                    ? "Checking API…"
                    : "API unavailable"}
              </Badge>
              <Badge dot variant="success">
                {solverLabel}
              </Badge>
              <Badge dot variant="warn">
                {held} on hold
              </Badge>
              <Badge dot variant="danger">
                {late} late
              </Badge>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <select
                className="aps-select"
                value={String(horizon)}
                onChange={(e) => setHorizon(Number(e.target.value))}
                aria-label="Planning horizon"
              >
                <option value="7">7 Days Horizon</option>
                <option value="14">14 Days Horizon</option>
                <option value="21">21 Days Horizon</option>
                <option value="30">30 Days Horizon</option>
              </select>
              <select
                className="aps-select"
                value={String(solverDepth)}
                onChange={(e) => setSolverDepth(Number(e.target.value))}
                aria-label="Solver depth"
              >
                <option value="30">Fast 30s</option>
                <option value="60">Standard 60s</option>
                <option value="120">Deep 120s</option>
                <option value="300">Full 5m</option>
              </select>
              <Button
                variant="ink"
                className="h-[2.2rem] px-5 text-[0.78rem] font-bold"
                disabled={running}
                onClick={() => void runSchedule()}
              >
                {running ? "Running…" : "Run Schedule"}
              </Button>
            </div>
          </div>
          {error ? (
            <div className="border-b border-red-200 bg-red-50 px-4 py-1.5 text-[0.75rem] text-red-800">
              {error}
            </div>
          ) : null}
          <div className="px-4 py-2 pb-3">
            <div className="grid grid-cols-2 gap-2 min-[900px]:grid-cols-3 min-[1200px]:grid-cols-6">
              {summaryTiles.map((m) => (
                <MetricTile key={m.label} {...m} />
              ))}
            </div>
          </div>
        </div>

        <main className="min-h-0 flex-1 overflow-auto px-2.5 pt-2 pb-4">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
