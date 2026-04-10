import { useEffect, useState } from "react"

import { apiFetch } from "@/api/client"
import { useAps } from "@/context/ApsContext"
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
import { fmtDate } from "@/lib/apsFormat"

export function CtpPage() {
  const { skuOptions, refreshCtp, ctpRequests, ctpOutput, horizon } = useAps()
  const [sku, setSku] = useState("")
  const [qty, setQty] = useState("120")
  const [date, setDate] = useState("")
  const [resultHtml, setResultHtml] = useState("No request yet.")
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const d = new Date(Date.now() + horizon * 86400000)
    setDate((prev) => prev || d.toISOString().slice(0, 10))
  }, [horizon])

  const merged = ctpRequests.slice(0, 12).map((r) => {
    const key = r.Request_ID ?? r.request_id
    const out =
      ctpOutput.find(
        (o) =>
          String(o.Request_ID ?? o.request_id ?? "") === String(key ?? "")
      ) ?? {}
    return {
      sku: String(r.SKU_ID ?? r.sku_id ?? "—"),
      qty: String(r.Qty_MT ?? r.qty_mt ?? "—"),
      requested: r.Requested_Date ?? r.requested_date ?? "",
      earliest: out.Earliest_Delivery ?? out.earliest_delivery ?? "",
      margin:
        out.Lateness_Days ?? out.lateness_days ?? "—",
      feasible:
        out.Feasible ??
        out.feasible ??
        out.Plant_Completion_Feasible ??
        out.plant_completion_feasible,
    }
  })

  async function runCtp() {
    if (!sku) {
      window.alert("Select a SKU first.")
      return
    }
    setBusy(true)
    setResultHtml("Checking…")
    try {
      const payload = {
        sku_id: sku,
        qty_mt: Number(qty || 0),
        requested_date: date,
      }
      await apiFetch("/api/aps/ctp/requests", {
        method: "POST",
        body: JSON.stringify({
          data: {
            Request_ID: `REQ-${Date.now()}`,
            SKU_ID: payload.sku_id,
            Qty_MT: payload.qty_mt,
            Requested_Date: payload.requested_date,
          },
        }),
      }).catch(() => null)
      const d = await apiFetch<Record<string, unknown>>(
        "/api/aps/ctp/check",
        {
          method: "POST",
          body: JSON.stringify(payload),
        }
      )
      const ok = Boolean(d.feasible ?? d.plant_completion_feasible)
      setResultHtml(
        `<strong>${ok ? "Feasible" : "Not feasible"}</strong><br/>` +
          `Earliest delivery: ${fmtDate(d.earliest_delivery)}<br/>` +
          `Margin / lateness days: ${String(d.lateness_days ?? "—")}<br/>` +
          `Material gaps: ${Array.isArray(d.material_gaps) ? d.material_gaps.length : 0}`
      )
      await refreshCtp()
    } catch (e) {
      setResultHtml(
        `CTP failed: ${e instanceof Error ? e.message : String(e)}`
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <PageFrame
      title="CTP"
      subtitle="Capable-to-Promise check plus request/output history."
    >
      <div className="grid grid-cols-1 gap-2 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <div>
              <CardTitle>Run CTP check</CardTitle>
              <CardDescription>Calls /api/aps/ctp/check.</CardDescription>
            </div>
          </CardHeader>
          <CardContent className="flex flex-col gap-2">
            <select
              className="aps-select w-full"
              value={sku}
              onChange={(e) => setSku(e.target.value)}
              aria-label="SKU"
            >
              <option value="">Select SKU…</option>
              {skuOptions.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.label}
                </option>
              ))}
            </select>
            <div className="grid grid-cols-2 gap-2">
              <input
                className="aps-select w-full"
                value={qty}
                onChange={(e) => setQty(e.target.value)}
                aria-label="Quantity MT"
              />
              <input
                type="date"
                className="aps-select w-full"
                value={date}
                onChange={(e) => setDate(e.target.value)}
                aria-label="Requested date"
              />
            </div>
            <Button
              variant="ink"
              className="h-9 w-fit"
              disabled={busy}
              onClick={() => void runCtp()}
            >
              {busy ? "Checking…" : "Run CTP"}
            </Button>
            <div
              className="aps-notice text-left text-[0.78rem]"
              dangerouslySetInnerHTML={{ __html: resultHtml }}
            />
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <div>
              <CardTitle>Recent CTP activity</CardTitle>
              <CardDescription>Merged request and output history.</CardDescription>
            </div>
          </CardHeader>
          <CardContent className="p-0">
            <div className="overflow-x-auto">
              <table className="aps-table">
                <thead>
                  <tr>
                    <th>SKU</th>
                    <th>Qty</th>
                    <th>Requested</th>
                    <th>Earliest</th>
                    <th>Margin</th>
                    <th>Feasible</th>
                  </tr>
                </thead>
                <tbody>
                  {!merged.length ? (
                    <tr>
                      <td colSpan={6} className="text-[var(--aps-text-soft)]">
                        No CTP history yet.
                      </td>
                    </tr>
                  ) : (
                    merged.map((r, i) => (
                      <tr key={i}>
                        <td>{r.sku}</td>
                        <td>{r.qty}</td>
                        <td>{fmtDate(r.requested)}</td>
                        <td>{fmtDate(r.earliest)}</td>
                        <td>{String(r.margin)}</td>
                        <td>
                          <StatusBadge
                            status={
                              r.feasible === true
                                ? "FEASIBLE"
                                : r.feasible === false
                                  ? "NOT FEASIBLE"
                                  : "—"
                            }
                          />
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </CardContent>
        </Card>
      </div>
    </PageFrame>
  )
}
