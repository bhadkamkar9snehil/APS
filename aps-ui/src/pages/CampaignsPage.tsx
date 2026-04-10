import { Link } from "react-router-dom"

import { useAps, type JsonRecord } from "@/context/ApsContext"
import { PageFrame } from "@/components/PageFrame"
import { StatusBadge } from "@/components/StatusBadge"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { fmtDate, num } from "@/lib/apsFormat"
import { cn } from "@/lib/utils"

const FILTERS = [
  { id: "all", label: "All" },
  { id: "released", label: "Released" },
  { id: "held", label: "Held" },
  { id: "late", label: "Late" },
] as const

function campaignId(c: JsonRecord) {
  return String(c.campaign_id ?? c.Campaign_ID ?? "")
}

export function CampaignsPage() {
  const {
    campaigns,
    campFilter,
    setCampFilter,
    runSchedule,
    running,
    updateCampaignStatus,
  } = useAps()

  const filtered = campaigns.filter((c) => {
    const rs = String(
      c.release_status ?? c.Release_Status ?? c.Status ?? ""
    ).toUpperCase()
    const st = String(c.Status ?? "").toUpperCase()
    if (campFilter === "released") return rs === "RELEASED"
    if (campFilter === "held") return rs.includes("HOLD")
    if (campFilter === "late") return st.includes("LATE")
    return true
  })

  return (
    <PageFrame
      title="Campaigns"
      subtitle="Status transitions, holds, due margins, SO assignment visibility."
      actions={
        <Button
          variant="ink"
          className="h-[2.2rem] px-4 text-[0.78rem] font-bold"
          disabled={running}
          onClick={() => void runSchedule()}
        >
          {running ? "Running…" : "Re-run Schedule"}
        </Button>
      }
      filters={
        <>
          {FILTERS.map((f) => (
            <button
              key={f.id}
              type="button"
              onClick={() => setCampFilter(f.id)}
              className={cn(
                "inline-flex h-[1.85rem] cursor-pointer items-center rounded-[0.2rem] border px-3.5 text-[0.65rem] font-semibold transition-all",
                campFilter === f.id
                  ? "border-slate-900 bg-slate-900 text-white dark:border-slate-100 dark:bg-slate-100 dark:text-slate-900"
                  : "border-[var(--aps-border-soft)] bg-[var(--aps-panel-muted)] text-[var(--aps-text-soft)] hover:bg-black/[0.05] hover:text-[var(--aps-text)] dark:hover:bg-white/10"
              )}
            >
              {f.label}
            </button>
          ))}
        </>
      }
    >
      <div className="flex flex-col gap-2">
        {!filtered.length ? (
          <div className="aps-notice text-left">
            No campaigns match the current filter.
          </div>
        ) : (
          filtered.map((c, idx) => {
            const cid = campaignId(c)
            const status =
              c.release_status ?? c.Release_Status ?? c.Status ?? "—"
            const margin =
              c.Margin_Hrs == null
                ? "—"
                : `${num(c.Margin_Hrs) >= 0 ? "+" : ""}${Math.round(num(c.Margin_Hrs))}h margin`
            return (
              <Card key={cid || `camp-${idx}`}>
                <CardContent className="py-3">
                  <div className="grid grid-cols-1 items-center gap-3 min-[720px]:grid-cols-[1.3fr_repeat(3,minmax(0,0.8fr))_1.2fr]">
                    <div className="min-w-0">
                      <div className="text-[1.02rem] font-extrabold text-[var(--aps-text)]">
                        {cid || "—"}
                      </div>
                      <div className="text-[0.65rem] text-[var(--aps-text-soft)]">
                        {String(c.grade ?? c.Grade ?? "—")}
                      </div>
                    </div>
                    <div>
                      <div className="text-[0.65rem] font-bold tracking-wide text-[var(--aps-text-faint)] uppercase">
                        Volume
                      </div>
                      <div className="text-[0.8rem] font-extrabold">
                        {String(c.total_mt ?? c.Total_MT ?? "—")} MT
                      </div>
                      <div className="text-[0.65rem] text-[var(--aps-text-soft)]">
                        {String(c.heats ?? c.Heats ?? "—")} heats
                      </div>
                    </div>
                    <div>
                      <div className="text-[0.65rem] font-bold tracking-wide text-[var(--aps-text-faint)] uppercase">
                        Due
                      </div>
                      <div className="text-[0.8rem] font-extrabold">
                        {fmtDate(c.Due_Date)}
                      </div>
                      <div className="text-[0.65rem] text-[var(--aps-text-soft)]">
                        {margin}
                      </div>
                    </div>
                    <div>
                      <div className="text-[0.65rem] font-bold tracking-wide text-[var(--aps-text-faint)] uppercase">
                        Status
                      </div>
                      <div className="mt-0.5">
                        <StatusBadge status={status} />
                      </div>
                    </div>
                    <div className="flex flex-wrap justify-end gap-2">
                      <Button
                        variant="successSolid"
                        size="sm"
                        className="h-8 text-[0.72rem]"
                        disabled={!cid}
                        onClick={() =>
                          void updateCampaignStatus(
                            cid,
                            "Release_Status",
                            "RELEASED"
                          )
                        }
                      >
                        Release
                      </Button>
                      <Button
                        className="h-8 border-amber-600/30 bg-amber-600 text-[0.72rem] text-white hover:bg-amber-600/90"
                        size="sm"
                        disabled={!cid}
                        onClick={() =>
                          void updateCampaignStatus(
                            cid,
                            "Release_Status",
                            "MATERIAL HOLD"
                          )
                        }
                      >
                        Hold
                      </Button>
                      <Button
                        variant="softGhost"
                        size="sm"
                        className="h-8 text-[0.72rem]"
                        nativeButton={false}
                        render={(p) => <Link {...p} to="/schedule" />}
                      >
                        Schedule
                      </Button>
                    </div>
                  </div>
                </CardContent>
              </Card>
            )
          })
        )}
      </div>
    </PageFrame>
  )
}
