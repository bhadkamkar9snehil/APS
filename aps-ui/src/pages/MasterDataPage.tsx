import { useMemo, useState } from "react"

import { apiFetch } from "@/api/client"
import { useAps, type JsonRecord } from "@/context/ApsContext"
import { PageFrame } from "@/components/PageFrame"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { cn } from "@/lib/utils"

const SECTIONS = [
  "config",
  "resources",
  "routing",
  "queue",
  "changeover",
  "skus",
  "bom",
  "inventory",
  "campaign-config",
  "scenarios",
] as const

const MASTER_LABELS: Record<string, string> = {
  config: "Config",
  resources: "Resource Master",
  routing: "Routing",
  queue: "Queue Times",
  changeover: "Changeover Matrix",
  skus: "SKU Master",
  bom: "BOM",
  inventory: "Inventory",
  "campaign-config": "Campaign Config",
  scenarios: "Scenarios",
}

const MASTER_KEYS: Record<string, string> = {
  config: "Key",
  resources: "Resource_ID",
  routing: "SKU_ID",
  queue: "From_Operation",
  changeover: "From \\ To",
  skus: "SKU_ID",
  bom: "BOM_ID",
  inventory: "SKU_ID",
  "campaign-config": "Grade",
  scenarios: "Parameter",
}

export function MasterDataPage() {
  const { master, refreshAll } = useAps()
  const [section, setSection] = useState<string>(SECTIONS[0])
  const [selectedKey, setSelectedKey] = useState<string | null>(null)
  const [modal, setModal] = useState<"create" | "edit" | "bulk" | null>(null)
  const [fields, setFields] = useState<Record<string, string>>({})
  const [bulkText, setBulkText] = useState("")

  const rows = useMemo(
    () => master[section] ?? [],
    [master, section]
  )
  const cols = useMemo(() => {
    if (!rows.length) return [] as string[]
    return Object.keys(rows[0] as object)
  }, [rows])

  const keyField = MASTER_KEYS[section] ?? cols[0]

  function openCreate() {
    const sample = rows[0] ?? {}
    const next: Record<string, string> = {}
    for (const c of Object.keys(sample)) next[c] = ""
    setFields(next)
    setModal("create")
  }

  function openEdit() {
    if (!selectedKey) {
      window.alert("Select a row first.")
      return
    }
    const row = rows.find(
      (r) =>
        String(r[keyField] ?? r[cols[0]] ?? "") === String(selectedKey)
    )
    if (!row) {
      window.alert("Selected row not found.")
      return
    }
    const next: Record<string, string> = {}
    for (const c of cols) next[c] = String(row[c] ?? "")
    setFields(next)
    setModal("edit")
  }

  function openBulk() {
    setBulkText(JSON.stringify(rows, null, 2))
    setModal("bulk")
  }

  async function submitModal() {
    try {
      if (modal === "bulk") {
        const items = JSON.parse(bulkText || "[]") as unknown
        if (!Array.isArray(items)) throw new Error("Bulk JSON must be an array")
        await apiFetch(
          `/api/aps/masterdata/${encodeURIComponent(section)}/bulk-replace`,
          { method: "PUT", body: JSON.stringify({ items }) }
        )
      } else {
        const data: JsonRecord = { ...fields }
        if (modal === "create") {
          await apiFetch(`/api/aps/masterdata/${encodeURIComponent(section)}`, {
            method: "POST",
            body: JSON.stringify({ data }),
          })
        } else if (modal === "edit" && selectedKey) {
          await apiFetch(
            `/api/aps/masterdata/${encodeURIComponent(section)}/${encodeURIComponent(selectedKey)}`,
            { method: "PATCH", body: JSON.stringify({ data }) }
          )
        }
      }
      setModal(null)
      setSelectedKey(null)
      await refreshAll()
    } catch (e) {
      window.alert(
        `Master data save failed: ${e instanceof Error ? e.message : String(e)}`
      )
    }
  }

  async function deleteRow() {
    if (!selectedKey) {
      window.alert("Select a row first.")
      return
    }
    if (!window.confirm(`Delete ${selectedKey} from ${section}?`)) return
    try {
      await apiFetch(
        `/api/aps/masterdata/${encodeURIComponent(section)}/${encodeURIComponent(selectedKey)}`,
        { method: "DELETE" }
      )
      setSelectedKey(null)
      await refreshAll()
    } catch (e) {
      window.alert(e instanceof Error ? e.message : "Delete failed")
    }
  }

  return (
    <PageFrame
      title="Master Data"
      subtitle="Workbook sections via /api/aps/masterdata."
    >
      <div className="mb-2 flex flex-wrap items-center gap-2">
        <select
          className="aps-select min-w-[11rem]"
          value={section}
          onChange={(e) => {
            setSection(e.target.value)
            setSelectedKey(null)
          }}
          aria-label="Section"
        >
          {SECTIONS.map((s) => (
            <option key={s} value={s}>
              {MASTER_LABELS[s] ?? s}
            </option>
          ))}
        </select>
        <Button variant="softGhost" className="h-8" onClick={() => void refreshAll()}>
          Refresh
        </Button>
        <Button variant="successSolid" className="h-8" onClick={openCreate}>
          Add Row
        </Button>
        <Button variant="softGhost" className="h-8" onClick={openEdit}>
          Patch Row
        </Button>
        <Button variant="destructive" className="h-8" onClick={() => void deleteRow()}>
          Delete Row
        </Button>
        <Button
          className="h-8 border-amber-600/40 bg-amber-500 text-white hover:bg-amber-500/90"
          onClick={openBulk}
        >
          Bulk Replace
        </Button>
        <span className="text-[0.65rem] text-[var(--aps-text-faint)]">
          {selectedKey ? `Selected: ${selectedKey}` : "No row selected."}
        </span>
      </div>

      <Card className="min-h-0 flex-1">
        <CardHeader>
          <CardTitle>{MASTER_LABELS[section] ?? section}</CardTitle>
          <CardDescription>Rows from /api/aps/masterdata.</CardDescription>
        </CardHeader>
        <CardContent className="overflow-auto p-0">
          {!rows.length ? (
            <div className="p-4 text-[0.78rem] text-[var(--aps-text-soft)]">
              No rows returned for this section.
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="aps-table">
                <thead>
                  <tr>
                    {cols.map((c) => (
                      <th key={c}>{c}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r, ri) => {
                    const k = String(r[keyField] ?? r[cols[0]] ?? ri)
                    const sel = selectedKey === k
                    return (
                      <tr
                        key={k + ri}
                        className={cn(
                          "cursor-pointer",
                          sel && "bg-[#f4f7ff]"
                        )}
                        onClick={() => setSelectedKey(k)}
                      >
                        {cols.map((c) => (
                          <td key={c}>{String(r[c] ?? "—")}</td>
                        ))}
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      {modal ? (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-sm">
          <div className="flex max-h-[90vh] w-full max-w-3xl flex-col overflow-hidden rounded-2xl border border-[var(--aps-border)] bg-white shadow-xl">
            <div className="flex items-center justify-between border-b border-[var(--aps-border-soft)] px-4 py-3">
              <div className="text-[1rem] font-extrabold">
                {modal === "bulk"
                  ? `Bulk replace — ${MASTER_LABELS[section]}`
                  : modal === "create"
                    ? `Add — ${MASTER_LABELS[section]}`
                    : `Edit — ${MASTER_LABELS[section]}`}
              </div>
              <Button variant="softGhost" size="sm" onClick={() => setModal(null)}>
                Close
              </Button>
            </div>
            <div className="min-h-0 flex-1 overflow-auto p-4">
              {modal === "bulk" ? (
                <textarea
                  className="min-h-[16rem] w-full rounded-xl border border-[var(--aps-border)] p-3 font-mono text-[0.75rem]"
                  value={bulkText}
                  onChange={(e) => setBulkText(e.target.value)}
                />
              ) : (
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  {cols.map((c) => (
                    <label key={c} className="grid gap-1 text-[0.72rem]">
                      <span className="font-semibold text-[var(--aps-text-soft)]">
                        {c}
                      </span>
                      <input
                        className="rounded-xl border border-[var(--aps-border)] px-3 py-2 text-[0.8rem]"
                        value={fields[c] ?? ""}
                        onChange={(e) =>
                          setFields((f) => ({ ...f, [c]: e.target.value }))
                        }
                      />
                    </label>
                  ))}
                </div>
              )}
            </div>
            <div className="flex justify-end gap-2 border-t border-[var(--aps-border-soft)] px-4 py-3">
              <Button variant="softGhost" onClick={() => setModal(null)}>
                Cancel
              </Button>
              <Button variant="ink" onClick={() => void submitModal()}>
                Save
              </Button>
            </div>
          </div>
        </div>
      ) : null}
    </PageFrame>
  )
}
