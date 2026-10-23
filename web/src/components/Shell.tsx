"use client";

import Link from "next/link";
import { usePathname, useSearchParams } from "next/navigation";
import { createContext, Suspense, useCallback, useContext, useEffect, useRef, useState } from "react";
import type { Context, JSX, ReactNode, RefObject } from "react";
import { userImage } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { Item, Managed, QueryResult } from "@/lib/api";
import { Ambient } from "@/components/Ambient";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import type { IconName } from "@/components/icons";
import { SearchPalette } from "@/components/SearchPalette";
import { ToastProvider } from "@/components/Toasts";
import { Avatar, Logo, Menu, MenuItem } from "@/components/ui";

export interface Shared {
  views: Item[];
  // linkarr, when it manages this server; null otherwise.
  hub: Managed | null;
  // After libraries change in settings.
  reload: () => void;
}

const SharedContext: Context<Shared> = createContext<Shared>({ views: [], hub: null, reload: () => undefined });
const BackdropContext: Context<(src: string | null) => void> = createContext<(src: string | null) => void>(() => undefined);
const SearchContext: Context<() => void> = createContext<() => void>(() => undefined);

// The signed-in user's libraries and the managing hub, loaded once per session.
export const useShared = (): Shared => useContext(SharedContext);
export const useSearch = (): (() => void) => useContext(SearchContext);

// A page sets the ambient backdrop while it is shown.
export function useBackdrop(src: string | null): void {
  const set: (src: string | null) => void = useContext(BackdropContext);
  useEffect(() => {
    set(src);
    return () => set(null);
  }, [set, src]);
}

export const viewIcon = (view: Item): IconName =>
  view.CollectionType === "movies" ? "film" : view.CollectionType === "tvshows" ? "tv" : view.CollectionType === "music" ? "music" : "folder";

export function Shell({ children }: { children: ReactNode }): JSX.Element {
  const { user } = useSession();
  const [views, setViews] = useState<Item[]>([]);
  const [hub, setHub] = useState<Managed | null>(null);

  const reload: () => void = useCallback((): void => {
    api.views().then((r: QueryResult<Item>) => setViews(r.Items)).catch((): void => undefined);
  }, []);

  useEffect(() => {
    void Promise.resolve().then(reload);
    api.managed().then((m: Managed) => setHub(m.url ? m : null)).catch((): void => setHub(null));
  }, [reload, user.Id]);

  return (
    <ToastProvider>
      <SharedContext value={{ views, hub, reload }}>
        <Suspense><Frame>{children}</Frame></Suspense>
      </SharedContext>
    </ToastProvider>
  );
}

const isCurrent = (pathname: string, href: string, viewId: string | null): boolean => {
  if (href === "/") return pathname === "/";
  if (href.startsWith("/library/")) return pathname.startsWith("/library") && href.endsWith(`id=${viewId}`);
  return pathname.startsWith(href);
};

interface NavLink { href: string; label: string; icon: IconName }

function Frame({ children }: { children: ReactNode }): JSX.Element {
  const pathname: string = usePathname();
  const viewId: string | null = useSearchParams().get("id");
  const { views } = useShared();
  const [backdrop, setBackdrop] = useState<string | null>(null);
  const [searching, setSearching] = useState(false);
  const first: RefObject<boolean> = useRef(true);
  const openSearch = (): void => setSearching(true);

  // Moves focus to the new page's heading so screen readers start there; the first load keeps the browser's focus.
  useEffect(() => {
    if (first.current) { first.current = false; return; }
    const frame: number = requestAnimationFrame(() => document.querySelector<HTMLElement>("main h1")?.focus({ preventScroll: true }));
    return () => cancelAnimationFrame(frame);
  }, [pathname]);

  useEffect(() => {
    const key = (e: KeyboardEvent): void => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setSearching((s: boolean) => !s);
        return;
      }
      const target: HTMLElement = e.target as HTMLElement;
      if (e.ctrlKey || e.metaKey || e.altKey || target.closest("input, textarea, select, [contenteditable], [role=dialog]")) return;
      if (e.key === "/") { e.preventDefault(); setSearching(true); }
    };
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  }, []);

  // The player owns the whole screen.
  if (pathname.startsWith("/play")) return <SearchContext value={openSearch}>{children}</SearchContext>;

  const links: NavLink[] = [
    { href: "/", label: "Home", icon: "home" },
    ...views.map((v: Item): NavLink => ({ href: `/library/?id=${v.Id}`, label: v.Name, icon: viewIcon(v) })),
  ];

  return (
    <BackdropContext value={setBackdrop}>
      <SearchContext value={openSearch}>
        <Ambient src={backdrop} />
        <div id="app" className="flex min-h-dvh">
          <a href="#content" className="sr-only z-50 rounded-pill bg-white px-4 py-2.5 font-semibold text-canvas focus:not-sr-only focus:fixed focus:top-3 focus:left-3">Skip to content</a>
          <aside className="sticky top-0 hidden h-dvh shrink-0 p-3 md:block">
            <div className="glass flex h-full w-[72px] flex-col rounded-panel px-3 py-4 xl:w-60">
              <Link href="/" aria-label="visularr, Home" className="mb-5 flex h-11 items-center rounded-pill px-1.5 max-xl:justify-center max-xl:px-0">
                <span className="xl:hidden"><Logo markOnly /></span>
                <span className="max-xl:hidden"><Logo /></span>
              </Link>
              <nav aria-label="Main" className="flex flex-1 flex-col gap-1 overflow-y-auto">
                {links.map((link: NavLink) => <RailItem key={link.href} link={link} current={isCurrent(pathname, link.href, viewId)} />)}
              </nav>
              <div className="mt-3 border-t border-hair-2 pt-3">
                <RailItem link={{ href: "/settings/", label: "Settings", icon: "settings" }} current={pathname.startsWith("/settings")} />
              </div>
            </div>
          </aside>
          <div className="flex min-w-0 flex-1 flex-col md:pr-3">
            <Header onSearch={openSearch} />
            <main id="content" className="mx-auto w-full max-w-[1600px] min-w-0 flex-1 p-4 pb-28 md:pb-8 lg:p-5">{children}</main>
          </div>
          <nav aria-label="Main" style={{ gridTemplateColumns: `repeat(${Math.min(links.length, 4) + 1}, minmax(0, 1fr))` }}
            className="glass fixed inset-x-0 bottom-0 z-30 mx-3 mb-[max(12px,env(safe-area-inset-bottom))] grid h-16 rounded-panel px-1 md:hidden">
            {[...links.slice(0, 4), { href: "/settings/", label: "Settings", icon: "settings" } satisfies NavLink].map((link: NavLink) => (
              <Link key={link.href} href={link.href} aria-current={isCurrent(pathname, link.href, viewId) ? "page" : undefined}
                className="group flex min-w-0 flex-col items-center justify-center gap-0.5 rounded-tile px-1 text-caption font-semibold text-ink-3 transition aria-[current=page]:text-ink">
                <span className="grid h-8 w-12 place-items-center rounded-pill transition group-aria-[current=page]:bg-fill-2"><Icon name={link.icon} size={20} /></span>
                <span className="max-w-full truncate">{link.label}</span>
              </Link>
            ))}
          </nav>
        </div>
        {searching && <SearchPalette onClose={() => setSearching(false)} />}
      </SearchContext>
    </BackdropContext>
  );
}

function RailItem({ link, current }: { link: NavLink; current: boolean }): JSX.Element {
  return (
    <Link href={link.href} aria-current={current ? "page" : undefined} title={link.label}
      className="relative flex h-11 shrink-0 items-center gap-3 rounded-pill px-3.5 text-body font-medium text-ink-2 transition hover:bg-fill hover:text-ink aria-[current=page]:bg-fill-2 aria-[current=page]:text-ink max-xl:justify-center max-xl:px-0">
      <Icon name={link.icon} size={20} className="shrink-0" />
      <span className="flex-1 truncate max-xl:sr-only">{link.label}</span>
    </Link>
  );
}

function Header({ onSearch }: { onSearch: () => void }): JSX.Element {
  const [scrolled, setScrolled] = useState(false);

  useEffect(() => {
    const check = (): void => setScrolled(window.scrollY > 8);
    check();
    window.addEventListener("scroll", check, { passive: true });
    return () => window.removeEventListener("scroll", check);
  }, []);

  return (
    <header data-scrolled={scrolled || undefined}
      className="glass sticky top-0 z-30 flex h-[calc(56px+env(safe-area-inset-top))] items-center gap-2 rounded-none border-x-0 border-t-0 px-3 pt-[env(safe-area-inset-top)] transition-[background-color,border-color,box-shadow,backdrop-filter] duration-300 md:top-3 md:mt-3 md:h-16 md:rounded-panel md:border md:px-3 md:pl-5 md:not-data-scrolled:border-transparent md:not-data-scrolled:bg-transparent md:not-data-scrolled:shadow-none md:not-data-scrolled:backdrop-blur-none">
      <Link href="/" aria-label="visularr, Home" className="rounded-pill md:hidden"><Logo /></Link>
      <div className="ml-auto flex items-center gap-2">
        <button onClick={onSearch} aria-keyshortcuts="Control+K Meta+K /"
          className="glass flex h-11 cursor-pointer items-center gap-2.5 rounded-pill px-4 text-body font-medium text-ink-2 transition hover:bg-fill-2 hover:text-ink max-md:w-11 max-md:justify-center max-md:px-0 md:w-64 lg:w-80">
          <Icon name="search" size={20} className="shrink-0" />
          <span className="flex-1 text-left max-md:sr-only">Search…</span>
          <kbd className="rounded-chip bg-fill px-1.5 py-0.5 font-sans text-caption text-ink-3 max-md:hidden">⌘K</kbd>
        </button>
        <UserMenu />
      </div>
    </header>
  );
}

function UserMenu(): JSX.Element {
  const { user, admin, logout } = useSession();
  const { hub } = useShared();
  const avatar: string | null = userImage(user, 88);
  return (
    <Menu label={`Account menu for ${user.Name}`}
      trigger={avatar ? <Avatar name={user.Name} src={avatar} className="size-11" /> : user.Name[0]}
      className={`grid size-11 cursor-pointer place-items-center overflow-hidden rounded-full font-display text-[22px] leading-none uppercase transition ${avatar ? "hover:opacity-85" : "bg-white text-canvas hover:bg-ink"}`}>
      <div className="mb-1 border-b border-hair-2 px-3 pt-2 pb-3">
        <p className="truncate text-body font-semibold">{user.Name}</p>
        <p className="text-meta text-ink-3">{admin ? "Administrator" : "Member"}</p>
      </div>
      <MenuItem icon="user" href="/settings/#account">Account</MenuItem>
      {hub?.url && (
        // The other apps, through linkarr: it signs the user in and knows where each one lives.
        <div role="group" aria-label="Apps" className="my-1 border-y border-hair-2 py-1">
          <MenuItem icon="compass" href={hub.url} external>linkarr</MenuItem>
          <MenuItem icon="download" href={`${hub.url}/go/app/modularr`} external>modularr</MenuItem>
        </div>
      )}
      <MenuItem icon="logout" onClick={() => void logout()}>Sign out</MenuItem>
    </Menu>
  );
}
