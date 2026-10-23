"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent, ReactNode, RefObject } from "react";
import { createPortal } from "react-dom";
import { episodeCode, errorMessage, itemHref, poster } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { Item, QueryResult } from "@/lib/api";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { Img } from "@/components/Poster";
import { useShared } from "@/components/Shell";
import { Rating, Spinner, useDialog } from "@/components/ui";

const LIBRARY_DELAY_MS: number = 180;
const RECENT_KEY: string = "visularr.recentSearches";
const RECENT_MAX: number = 8;
const typeLabel: Record<string, string> = { Movie: "Movie", Series: "Series", Episode: "Episode" };

// Every row the arrow keys walk and Enter runs, in on-screen order.
interface Option { key: string; run: () => void }

function readRecent(): string[] {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(RECENT_KEY) ?? "[]");
    return Array.isArray(value) ? value.filter((v: unknown): v is string => typeof v === "string").slice(0, RECENT_MAX) : [];
  } catch { return []; }
}

function saveRecent(list: string[]): void {
  try {
    if (list.length) localStorage.setItem(RECENT_KEY, JSON.stringify(list));
    else localStorage.removeItem(RECENT_KEY);
  } catch { /* private mode: nothing is remembered */ }
}

// Search over the user's libraries; when linkarr manages the server, a missing title can be requested there.
export function SearchPalette({ onClose }: { onClose: () => void }): ReactNode {
  const router: ReturnType<typeof useRouter> = useRouter();
  const { user } = useSession();
  const { hub } = useShared();
  const panel: RefObject<HTMLDivElement | null> = useRef<HTMLDivElement>(null);
  const [term, setTerm] = useState("");
  const [hits, setHits] = useState<Item[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [active, setActive] = useState(0);
  const [recent, setRecent] = useState<string[]>(readRecent);
  const [played, setPlayed] = useState<Item[]>([]);
  useDialog(panel, onClose);

  const needle: string = term.trim();
  const request: string | null = hub?.url && needle ? `${hub.url}/discover/?q=${encodeURIComponent(needle)}` : null;

  // Shown while the box is empty: what is in progress first, then what was finished last.
  useEffect(() => {
    const empty: QueryResult<Item> = { Items: [], TotalRecordCount: 0, StartIndex: 0 };
    Promise.all([api.resume(user.Id, 4).catch((): QueryResult<Item> => empty), api.recentlyPlayed(user.Id).catch((): QueryResult<Item> => empty)])
      .then(([resume, done]: [QueryResult<Item>, QueryResult<Item>]) => {
        const seen: Set<string> = new Set<string>();
        setPlayed([...resume.Items, ...done.Items].filter((i: Item) => !seen.has(i.Id) && !!seen.add(i.Id)).slice(0, 6));
      });
  }, [user.Id]);

  useEffect(() => {
    if (!needle) return;
    let live: boolean = true;
    const timer: ReturnType<typeof setTimeout> = setTimeout(() => {
      api.items({ userId: user.Id, searchTerm: needle, IncludeItemTypes: "Movie,Series,Episode", Recursive: true, Limit: 24 })
        .then((r: QueryResult<Item>) => { if (live) { setHits(r.Items); setError(null); } })
        .catch((e: unknown) => { if (live) setError(errorMessage(e)); });
    }, LIBRARY_DELAY_MS);
    return () => { live = false; clearTimeout(timer); };
  }, [needle, user.Id]);

  const libraryHits: Item[] = needle ? hits ?? [] : [];

  function remember(): void {
    if (!needle) return;
    const list: string[] = [needle, ...recent.filter((t: string) => t.toLowerCase() !== needle.toLowerCase())].slice(0, RECENT_MAX);
    setRecent(list);
    saveRecent(list);
  }

  function clearRecent(): void {
    setRecent([]);
    saveRecent([]);
    setActive(0);
  }

  function search(text: string): void {
    setTerm(text);
    setActive(0);
    setHits(null);
  }

  function open(item: Item): void {
    remember();
    onClose();
    router.push(itemHref(item));
  }

  function requestIt(): void {
    if (!request) return;
    remember();
    window.open(request, "_blank", "noopener,noreferrer");
  }

  const options: Option[] = needle
    ? [
      ...libraryHits.map((item: Item): Option => ({ key: item.Id, run: () => open(item) })),
      ...(request ? [{ key: "request", run: requestIt }] : []),
    ]
    : [
      ...recent.map((t: string): Option => ({ key: `recent:${t}`, run: () => search(t) })),
      ...played.map((item: Item): Option => ({ key: `played:${item.Id}`, run: () => open(item) })),
    ];
  const at: number = Math.min(active, Math.max(options.length - 1, 0));
  // Option index of each row, by its key.
  const index = (key: string): number => options.findIndex((o: Option) => o.key === key);

  function key(e: KeyboardEvent): void {
    const count: number = options.length;
    if (!count) return;
    if (e.key === "ArrowDown") { e.preventDefault(); setActive((at + 1) % count); }
    if (e.key === "ArrowUp") { e.preventDefault(); setActive((at - 1 + count) % count); }
    if (e.key === "Enter" && (e.target as HTMLElement).tagName === "INPUT") { e.preventDefault(); options[at]?.run(); }
  }

  const row: string = "flex min-h-12 cursor-pointer items-center gap-3 rounded-ctl p-2 aria-selected:bg-fill-2";
  const loading: boolean = !!needle && hits === null && !error;

  const itemRow = (item: Item, optionKey: string): ReactNode => {
    const i: number = index(optionKey);
    return (
      <li key={optionKey} id={`option-${i}`} role="option" aria-selected={i === at}
        onMouseMove={() => setActive(i)} onClick={() => open(item)} className={row}>
        <Img art={poster(item, 120)} className="h-16 w-11 shrink-0 rounded-chip" />
        <div className="min-w-0 flex-1">
          <p className="truncate text-body font-semibold">{item.Type === "Episode" ? `${item.SeriesName ?? ""} · ${item.Name}` : item.Name}</p>
          <p className="mt-0.5 flex items-center gap-2.5 text-meta text-ink-3">
            <span>{typeLabel[item.Type] ?? item.Type}</span>
            {item.Type === "Episode" ? <span>{episodeCode(item)}</span> : !!item.ProductionYear && <span>{item.ProductionYear}</span>}
            <Rating value={item.CommunityRating} className="text-meta" />
          </p>
        </div>
      </li>
    );
  };

  return createPortal(
    <div className="fixed inset-0 z-50 flex justify-center bg-overlay px-3 pt-[calc(env(safe-area-inset-top)+12px)] sm:pt-[12vh]" onMouseDown={onClose}>
      <div ref={panel} role="dialog" aria-modal="true" aria-label="Search" tabIndex={-1} onMouseDown={(e: React.MouseEvent) => e.stopPropagation()} onKeyDown={key}
        className="glass-pop flex h-fit max-h-[80dvh] w-full max-w-2xl animate-rise flex-col overflow-hidden rounded-tile outline-none">
        <div className="flex items-center gap-3 border-b border-hair-2 px-5">
          {loading ? <Spinner className="text-ink-2" /> : <Icon name="search" className="text-ink-2" />}
          <input value={term} onChange={(e: React.ChangeEvent<HTMLInputElement>) => search(e.target.value)}
            role="combobox" aria-expanded={options.length > 0} aria-controls="search-results" aria-activedescendant={options.length ? `option-${at}` : undefined}
            placeholder="Movies, shows, episodes" aria-label="Search your libraries"
            className="h-16 min-w-0 flex-1 bg-transparent text-title outline-none placeholder:text-ink-3 focus-visible:outline-none" />
          <button onClick={onClose} className="h-11 cursor-pointer rounded-pill px-3 text-meta font-medium text-ink-2 hover:bg-fill-2 hover:text-ink">
            <span className="max-sm:hidden">Esc</span><span className="sm:hidden">Cancel</span>
          </button>
        </div>

        <div className="min-h-0 overflow-y-auto">
          <ul id="search-results" role="listbox" aria-label="Search results" className="p-2">
            {!needle && recent.length > 0 && (
              <li role="presentation" className="flex items-center justify-between px-2 pt-2 pb-1.5">
                <span className="caption">Recent searches</span>
                <button onClick={clearRecent} className="cursor-pointer rounded-pill px-2 py-1 text-meta font-medium text-ink-2 hover:bg-fill-2 hover:text-ink">Clear</button>
              </li>
            )}
            {!needle && recent.map((t: string) => {
              const i: number = index(`recent:${t}`);
              return (
                <li key={t} id={`option-${i}`} role="option" aria-selected={i === at} onMouseMove={() => setActive(i)} onClick={() => search(t)} className={`${row} min-h-11`}>
                  <span className="grid size-8 shrink-0 place-items-center text-ink-3"><Icon name="clock" size={16} /></span>
                  <span className="truncate text-body">{t}</span>
                </li>
              );
            })}
            {!needle && played.length > 0 && <li role="presentation" className="caption px-2 pt-3 pb-1.5">Recently played</li>}
            {!needle && played.map((item: Item) => itemRow(item, `played:${item.Id}`))}

            {libraryHits.length > 0 && <li role="presentation" className="caption px-2 pt-2 pb-1.5">In your library</li>}
            {libraryHits.map((item: Item) => itemRow(item, item.Id))}

            {request && (
              <li id={`option-${index("request")}`} role="option" aria-selected={index("request") === at} onMouseMove={() => setActive(index("request"))} onClick={requestIt}
                className={`${row} mt-2 border-t border-hair-2 pt-3`}>
                <span className="grid size-8 shrink-0 place-items-center text-ink-3"><Icon name="external" size={16} /></span>
                <span className="min-w-0 flex-1 truncate text-body">Request “{needle}” in linkarr</span>
                <span className="sr-only">(opens in a new tab)</span>
              </li>
            )}
          </ul>
          {error ? (
            <p className="px-5 pb-6 text-meta text-bad">Search is unavailable right now: {error}</p>
          ) : !needle ? (
            options.length === 0 && <p className="px-5 pt-4 pb-5 text-meta text-ink-2">Type a movie, show or episode{hub ? "; titles you don’t have yet can be requested in linkarr" : ""}.</p>
          ) : hits && libraryHits.length === 0 ? (
            <p className="px-5 pt-4 pb-5 text-meta text-ink-2">Nothing in your library matches “{needle}”.</p>
          ) : null}
        </div>
      </div>
    </div>,
    document.body,
  );
}
