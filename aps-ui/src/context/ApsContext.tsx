import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react"

import { apiFetch } from "@/api/client"

export type JsonRecord = Record<string, unknown>

type HealthState =
  | { ok: true; workbook_ok?: boolean }
  | { ok: false }
  | null

type SkuOption = { id: string; label: string }

type ApsContextValue = {
  health: HealthState
  loading: boolean
  running: boolean
  error: string | null
  overview: JsonRecord | null
  campaigns: JsonRecord[]
  gantt: JsonRecord[]
  capacity: JsonRecord[]
  material: { summary: JsonRecord; campaigns: JsonRecord[] }
  dispatchBoard: JsonRecord[]
  scenarios: JsonRecord[]
  ctpRequests: JsonRecord[]
  ctpOutput: JsonRecord[]
  master: Record<string, JsonRecord[]>
  orders: JsonRecord[]
  horizon: number
  solverDepth: number
  campFilter: "all" | "released" | "held" | "late"
  selectedOrders: string[]
  skuOptions: SkuOption[]
  setHorizon: (n: number) => void
  setSolverDepth: (n: number) => void
  setCampFilter: (f: "all" | "released" | "held" | "late") => void
  refreshAll: () => Promise<void>
  refreshOrders: () => Promise<void>
  refreshCtp: () => Promise<void>
  checkHealth: () => Promise<void>
  runSchedule: () => Promise<void>
  toggleOrderSelection: (soId: string, checked: boolean) => void
  clearOrderSelection: () => void
  assignSelectedOrders: () => Promise<void>
  assignOrdersToCampaign: (orderIds: string[], campaignId: string) => Promise<void>
  updateCampaignStatus: (
    campaignId: string,
    fieldName: string,
    value: string
  ) => Promise<void>
  createOrder: () => Promise<void>
  editOrder: (soId: string) => Promise<void>
  deleteOrder: (soId: string) => Promise<void>
  applyScenario: (name: string) => Promise<void>
  createScenario: () => Promise<void>
  editScenario: (keyValue: string) => Promise<void>
  deleteScenario: (keyValue: string) => Promise<void>
  patchJobReschedule: () => Promise<void>
}

const ApsContext = createContext<ApsContextValue | null>(null)

function asItems<T extends JsonRecord>(d: unknown): T[] {
  if (!d || typeof d !== "object") return []
  const items = (d as { items?: unknown }).items
  return Array.isArray(items) ? (items as T[]) : []
}

function asJobs(d: unknown): JsonRecord[] {
  if (!d || typeof d !== "object") return []
  const jobs = (d as { jobs?: unknown }).jobs
  return Array.isArray(jobs) ? (jobs as JsonRecord[]) : []
}

function asResources(d: unknown): JsonRecord[] {
  if (!d || typeof d !== "object") return []
  const resources = (d as { resources?: unknown }).resources
  return Array.isArray(resources) ? (resources as JsonRecord[]) : []
}

export function ApsProvider({ children }: { children: ReactNode }) {
  const [health, setHealth] = useState<HealthState>(null)
  const [loading, setLoading] = useState(false)
  const [running, setRunning] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [overview, setOverview] = useState<JsonRecord | null>(null)
  const [campaigns, setCampaigns] = useState<JsonRecord[]>([])
  const [gantt, setGantt] = useState<JsonRecord[]>([])
  const [capacity, setCapacity] = useState<JsonRecord[]>([])
  const [material, setMaterial] = useState<{
    summary: JsonRecord
    campaigns: JsonRecord[]
  }>({ summary: {}, campaigns: [] })
  const [dispatchBoard, setDispatchBoard] = useState<JsonRecord[]>([])
  const [scenarios, setScenarios] = useState<JsonRecord[]>([])
  const [ctpRequests, setCtpRequests] = useState<JsonRecord[]>([])
  const [ctpOutput, setCtpOutput] = useState<JsonRecord[]>([])
  const [master, setMaster] = useState<Record<string, JsonRecord[]>>({})
  const [orders, setOrders] = useState<JsonRecord[]>([])
  const [horizon, setHorizon] = useState(14)
  const [solverDepth, setSolverDepth] = useState(60)
  const [campFilter, setCampFilter] = useState<
    "all" | "released" | "held" | "late"
  >("all")
  const [selectedOrders, setSelectedOrders] = useState<string[]>([])
  const [skuOptions, setSkuOptions] = useState<SkuOption[]>([])

  const checkHealth = useCallback(async () => {
    try {
      const d = await apiFetch<{ workbook_ok?: boolean }>("/api/health")
      setHealth({ ok: true, workbook_ok: d.workbook_ok })
    } catch {
      setHealth({ ok: false })
    }
  }, [])

  const refreshCtp = useCallback(async () => {
    const [reqs, outs] = await Promise.all([
      apiFetch("/api/aps/ctp/requests").catch(() => ({ items: [] })),
      apiFetch("/api/aps/ctp/output").catch(() => ({ items: [] })),
    ])
    setCtpRequests(asItems(reqs))
    setCtpOutput(asItems(outs))
  }, [])

  const refreshOrders = useCallback(async () => {
    const d = (await apiFetch("/api/aps/orders/list").catch(() => ({
      items: [],
    }))) as { items?: unknown }
    setOrders(asItems(d))
  }, [])

  const loadSkus = useCallback(async () => {
    try {
      const raw = await apiFetch("/api/aps/masterdata/skus").catch(async () =>
        apiFetch("/api/data/skus").catch(() => ({}))
      )
      const d = raw as Record<string, unknown>
      const rows = (Array.isArray(d.items)
        ? d.items
        : Array.isArray(d.skus)
          ? d.skus
          : []) as JsonRecord[]
      setSkuOptions(
        rows
          .map((r) => {
            const id = String(r.SKU_ID ?? r.sku_id ?? "")
            const nm = String(r.SKU_Name ?? r.sku_name ?? "")
            return {
              id,
              label: id + (nm ? ` · ${nm}` : ""),
            }
          })
          .filter((o) => o.id)
      )
    } catch {
      setSkuOptions([])
    }
  }, [])

  const refreshAll = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const [
        overviewRes,
        campaignsRes,
        ganttRes,
        capacityRes,
        materialRes,
        dispatchRes,
        scenariosRes,
        ctpReqs,
        ctpOut,
        masterRes,
      ] = await Promise.all([
        apiFetch("/api/aps/dashboard/overview").catch(() => null),
        apiFetch("/api/aps/campaigns/list").catch(() => ({ items: [] })),
        apiFetch("/api/aps/schedule/gantt").catch(() => ({ jobs: [] })),
        apiFetch("/api/aps/capacity/map").catch(() => ({ items: [] })),
        apiFetch("/api/aps/material/plan").catch(() => ({
          summary: {},
          campaigns: [],
        })),
        apiFetch("/api/aps/dispatch/board").catch(() => ({ resources: [] })),
        apiFetch("/api/aps/scenarios/list").catch(() => ({ items: [] })),
        apiFetch("/api/aps/ctp/requests").catch(() => ({ items: [] })),
        apiFetch("/api/aps/ctp/output").catch(() => ({ items: [] })),
        apiFetch("/api/aps/masterdata").catch(() => ({})),
      ])

      let ov: JsonRecord | null = null
      if (overviewRes && typeof overviewRes === "object") {
        const o = overviewRes as JsonRecord
        const s = o.summary
        ov = (typeof s === "object" && s !== null ? s : o) as JsonRecord
      }
      setOverview(ov)
      setCampaigns(asItems(campaignsRes))
      setGantt(asJobs(ganttRes))
      setCapacity(asItems(capacityRes))

      const mat = materialRes as JsonRecord
      setMaterial({
        summary:
          typeof mat.summary === "object" && mat.summary !== null
            ? (mat.summary as JsonRecord)
            : {},
        campaigns: Array.isArray(mat.campaigns)
          ? (mat.campaigns as JsonRecord[])
          : [],
      })
      setDispatchBoard(asResources(dispatchRes))
      setScenarios(asItems(scenariosRes))
      setCtpRequests(asItems(ctpReqs))
      setCtpOutput(asItems(ctpOut))

      if (masterRes && typeof masterRes === "object" && !Array.isArray(masterRes)) {
        const m: Record<string, JsonRecord[]> = {}
        for (const [k, v] of Object.entries(masterRes)) {
          if (Array.isArray(v)) m[k] = v as JsonRecord[]
        }
        setMaster(m)
      } else {
        setMaster({})
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Load failed")
    } finally {
      setLoading(false)
    }
  }, [])

  const runSchedule = useCallback(async () => {
    setRunning(true)
    setError(null)
    try {
      await apiFetch("/api/aps/schedule/run", {
        method: "POST",
        body: JSON.stringify({
          time_limit: solverDepth,
          horizon_days: horizon,
        }),
      })
      await refreshAll()
      await refreshOrders()
    } catch (e) {
      const msg = e instanceof Error ? e.message : "Schedule failed"
      setError(msg)
      window.alert(`Schedule error: ${msg}`)
    } finally {
      setRunning(false)
    }
  }, [horizon, refreshAll, refreshOrders, solverDepth])

  const toggleOrderSelection = useCallback((soId: string, checked: boolean) => {
    setSelectedOrders((prev) => {
      if (checked) return prev.includes(soId) ? prev : [...prev, soId]
      return prev.filter((x) => x !== soId)
    })
  }, [])

  const clearOrderSelection = useCallback(() => setSelectedOrders([]), [])

  const assignOrdersToCampaign = useCallback(
    async (orderIds: string[], campaignId: string) => {
      await apiFetch("/api/aps/orders/assign", {
        method: "POST",
        body: JSON.stringify({
          assignments: orderIds.map((so_id) => ({ so_id, campaign_id: campaignId })),
        }),
      })
      clearOrderSelection()
      await refreshOrders()
      await refreshAll()
    },
    [clearOrderSelection, refreshAll, refreshOrders]
  )

  const assignSelectedOrders = useCallback(async () => {
    const cid = window.prompt("Enter Campaign ID")
    if (!cid) return
    await assignOrdersToCampaign(selectedOrders, cid)
  }, [assignOrdersToCampaign, selectedOrders])

  const updateCampaignStatus = useCallback(
    async (campaignId: string, fieldName: string, value: string) => {
      const payload: JsonRecord = {}
      payload[fieldName] = value
      await apiFetch(
        `/api/aps/campaigns/${encodeURIComponent(campaignId)}/status`,
        {
          method: "PATCH",
          body: JSON.stringify({ data: payload }),
        }
      )
      await refreshAll()
    },
    [refreshAll]
  )

  const createOrder = useCallback(async () => {
    const so = window.prompt("SO_ID")
    if (!so) return
    const payload = {
      SO_ID: so,
      SKU_ID: window.prompt("SKU_ID", "") || "",
      Customer: window.prompt("Customer", "") || "",
      Grade: window.prompt("Grade", "") || "",
      Order_Qty_MT: Number(window.prompt("Order Qty MT", "0") || 0),
      Delivery_Date: window.prompt("Delivery Date (YYYY-MM-DD)", "") || "",
      Status: window.prompt("Status", "Open") || "Open",
      Priority: window.prompt("Priority", "NORMAL") || "NORMAL",
    }
    await apiFetch("/api/aps/orders", {
      method: "POST",
      body: JSON.stringify(payload),
    })
    await refreshOrders()
  }, [refreshOrders])

  const editOrder = useCallback(
    async (so: string) => {
      const row = orders.find((x) => String(x.SO_ID ?? "") === String(so))
      if (!row) {
        window.alert("Order not found.")
        return
      }
      const payload = {
        SKU_ID: window.prompt("SKU_ID", String(row.SKU_ID ?? "")) || "",
        Customer: window.prompt("Customer", String(row.Customer ?? "")) || "",
        Grade: window.prompt("Grade", String(row.Grade ?? "")) || "",
        Order_Qty_MT: Number(
          window.prompt("Order Qty MT", String(row.Order_Qty_MT ?? 0)) || 0
        ),
        Delivery_Date:
          window.prompt("Delivery Date", String(row.Delivery_Date ?? "")) || "",
        Status: window.prompt("Status", String(row.Status ?? "Open")) || "Open",
        Priority:
          window.prompt("Priority", String(row.Priority ?? "NORMAL")) || "NORMAL",
        Campaign_ID:
          window.prompt("Campaign_ID", String(row.Campaign_ID ?? "")) || "",
      }
      await apiFetch(`/api/aps/orders/${encodeURIComponent(so)}`, {
        method: "PUT",
        body: JSON.stringify(payload),
      })
      await refreshOrders()
    },
    [orders, refreshOrders]
  )

  const deleteOrder = useCallback(
    async (so: string) => {
      if (!window.confirm(`Delete order ${so}?`)) return
      await apiFetch(`/api/aps/orders/${encodeURIComponent(so)}`, {
        method: "DELETE",
      })
      await refreshOrders()
    },
    [refreshOrders]
  )

  const applyScenario = useCallback(async (name: string) => {
    await apiFetch("/api/aps/scenarios/apply", {
      method: "POST",
      body: JSON.stringify({ scenario: name }),
    })
    window.alert(`Scenario apply acknowledged for ${name}`)
  }, [])

  const createScenario = useCallback(async () => {
    const key = window.prompt("Scenario key / parameter")
    if (!key) return
    const value = window.prompt("Scenario value", "") || ""
    await apiFetch("/api/aps/scenarios", {
      method: "POST",
      body: JSON.stringify({ data: { Parameter: key, Value: value } }),
    })
    await refreshAll()
  }, [refreshAll])

  const editScenario = useCallback(
    async (keyValue: string) => {
      const row = scenarios.find((r) =>
        Object.entries(r).some(
          ([k, v]) => String(k) === keyValue || String(v) === keyValue
        )
      )
      if (!row) {
        window.alert("Scenario row not found.")
        return
      }
      const k = Object.keys(row)[0]
      const cur = row[k]
      const value =
        window.prompt("Update value", String(cur ?? "")) ?? String(cur ?? "")
      const pathKey = String(Object.values(row)[0] ?? keyValue)
      await apiFetch(`/api/aps/scenarios/${encodeURIComponent(pathKey)}`, {
        method: "PATCH",
        body: JSON.stringify({ data: { [k]: value } }),
      })
      await refreshAll()
    },
    [refreshAll, scenarios]
  )

  const deleteScenario = useCallback(
    async (keyValue: string) => {
      if (!window.confirm(`Delete scenario ${keyValue}?`)) return
      const row = scenarios.find((r) =>
        Object.entries(r).some(
          ([k, v]) => String(k) === keyValue || String(v) === keyValue
        )
      )
      const pathKey = row
        ? String(Object.values(row)[0] ?? keyValue)
        : keyValue
      await apiFetch(`/api/aps/scenarios/${encodeURIComponent(pathKey)}`, {
        method: "DELETE",
      })
      await refreshAll()
    },
    [refreshAll, scenarios]
  )

  const patchJobReschedule = useCallback(async () => {
    const jobId = window.prompt("Job_ID to patch")
    if (!jobId) return
    const payloadText = window.prompt(
      "JSON patch",
      '{"Planned_Start":"2026-04-04T08:00:00","Planned_End":"2026-04-04T12:00:00"}'
    )
    if (!payloadText) return
    await apiFetch(
      `/api/aps/schedule/jobs/${encodeURIComponent(jobId)}/reschedule`,
      {
        method: "PATCH",
        body: JSON.stringify({ data: JSON.parse(payloadText) }),
      }
    )
    await refreshAll()
  }, [refreshAll])

  useEffect(() => {
    void checkHealth()
    void refreshOrders()
    void loadSkus()
    void refreshAll()
  }, [checkHealth, loadSkus, refreshAll, refreshOrders])

  const value = useMemo<ApsContextValue>(
    () => ({
      health,
      loading,
      running,
      error,
      overview,
      campaigns,
      gantt,
      capacity,
      material,
      dispatchBoard,
      scenarios,
      ctpRequests,
      ctpOutput,
      master,
      orders,
      horizon,
      solverDepth,
      campFilter,
      selectedOrders,
      skuOptions,
      setHorizon,
      setSolverDepth,
      setCampFilter,
      refreshAll,
      refreshOrders,
      refreshCtp,
      checkHealth,
      runSchedule,
      toggleOrderSelection,
      clearOrderSelection,
      assignSelectedOrders,
      assignOrdersToCampaign,
      updateCampaignStatus,
      createOrder,
      editOrder,
      deleteOrder,
      applyScenario,
      createScenario,
      editScenario,
      deleteScenario,
      patchJobReschedule,
    }),
    [
      health,
      loading,
      running,
      error,
      overview,
      campaigns,
      gantt,
      capacity,
      material,
      dispatchBoard,
      scenarios,
      ctpRequests,
      ctpOutput,
      master,
      orders,
      horizon,
      solverDepth,
      campFilter,
      selectedOrders,
      skuOptions,
      refreshAll,
      refreshOrders,
      refreshCtp,
      checkHealth,
      runSchedule,
      toggleOrderSelection,
      clearOrderSelection,
      assignSelectedOrders,
      assignOrdersToCampaign,
      updateCampaignStatus,
      createOrder,
      editOrder,
      deleteOrder,
      applyScenario,
      createScenario,
      editScenario,
      deleteScenario,
      patchJobReschedule,
    ]
  )

  return <ApsContext.Provider value={value}>{children}</ApsContext.Provider>
}

export function useAps(): ApsContextValue {
  const ctx = useContext(ApsContext)
  if (!ctx) throw new Error("useAps must be used within ApsProvider")
  return ctx
}
