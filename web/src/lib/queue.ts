// The play order of a playlist for this tab: its own order, or a shuffle of it. The player walks it for next and previous.
const key = (listId: string): string => `visularr.queue.${listId}`;

export function saveQueue(listId: string, ids: string[]): void {
  try { sessionStorage.setItem(key(listId), JSON.stringify(ids)); } catch { /* storage blocked: the player falls back to the playlist's order */ }
}

export function loadQueue(listId: string): string[] | null {
  try {
    const raw: string | null = sessionStorage.getItem(key(listId));
    const ids: unknown = raw ? JSON.parse(raw) : null;
    return Array.isArray(ids) && ids.every((i: unknown) => typeof i === "string") ? (ids as string[]) : null;
  } catch { return null; }
}

// Fisher-Yates.
export function shuffled<T>(list: readonly T[]): T[] {
  const out: T[] = [...list];
  for (let i: number = out.length - 1; i > 0; i--) {
    const j: number = Math.floor(Math.random() * (i + 1));
    [out[i], out[j]] = [out[j] as T, out[i] as T];
  }
  return out;
}
