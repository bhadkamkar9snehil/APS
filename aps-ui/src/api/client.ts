const API_BASE =
  (import.meta.env.VITE_API_BASE as string | undefined)?.replace(/\/$/, "") ?? ""

export function apiUrl(path: string): string {
  if (path.startsWith("http")) return path
  return `${API_BASE}${path}`
}

export async function apiFetch<T = unknown>(
  path: string,
  init?: RequestInit
): Promise<T> {
  const res = await fetch(apiUrl(path), {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...init?.headers,
    },
  })
  const data = (await res.json().catch(() => ({}))) as Record<string, unknown>
  if (!res.ok) {
    throw new Error(String(data.error ?? `API error ${res.status}`))
  }
  return data as T
}

export async function apiFetchOptional<T = unknown>(
  path: string,
  init?: RequestInit
): Promise<T | null> {
  try {
    return await apiFetch<T>(path, init)
  } catch {
    return null
  }
}
