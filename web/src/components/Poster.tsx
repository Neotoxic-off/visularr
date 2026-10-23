"use client";

import Link from "next/link";
import { decode } from "blurhash";
import { useEffect, useRef, useState } from "react";
import type { JSX, KeyboardEvent, ReactNode, RefObject } from "react";
import { episodeCode, itemHref, landscape, playHref, poster, progressOf } from "@/lib/api";
import type { Art, Item } from "@/lib/api";
import { Icon } from "@/components/icons";
import type { IconName } from "@/components/icons";
import { Skeleton } from "@/components/ui";

const HASH_SIZE: number = 16;

// The blurhash paints under the image so the card never flashes empty; decoded only near the viewport, and never once the image is in.
function BlurHash({ hash, image }: { hash: string; image: RefObject<HTMLImageElement | null> }): JSX.Element {
  const canvas: RefObject<HTMLCanvasElement | null> = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const el: HTMLCanvasElement | null = canvas.current;
    if (!el) return;
    const paint = (): void => {
      const img: HTMLImageElement | null = image.current;
      if (img?.complete && img.naturalWidth > 0) return;
      const context: CanvasRenderingContext2D | null = el.getContext("2d");
      if (!context) return;
      try {
        const data: ImageData = context.createImageData(HASH_SIZE, HASH_SIZE);
        data.data.set(decode(hash, HASH_SIZE, HASH_SIZE));
        context.putImageData(data, 0, 0);
      } catch { /* a malformed hash leaves the plain fill */ }
    };
    const observer: IntersectionObserver = new IntersectionObserver((entries: IntersectionObserverEntry[]) => {
      if (!entries.some((e: IntersectionObserverEntry) => e.isIntersecting)) return;
      observer.disconnect();
      paint();
    }, { rootMargin: "300px" });
    observer.observe(el);
    return () => observer.disconnect();
  }, [hash, image]);
  return <canvas ref={canvas} width={HASH_SIZE} height={HASH_SIZE} aria-hidden="true" className="absolute inset-0 h-full w-full" />;
}

// Artwork in a box the parent sizes: blurhash first, the image fades in over it, an icon when there is none.
export function Img({ art, alt = "", icon = "film", label, className = "", eager = false, fit = "cover", sizes }: {
  art: Art | null; alt?: string; icon?: IconName; label?: string; className?: string; eager?: boolean; fit?: "cover" | "contain"; sizes?: string;
}): JSX.Element {
  const [loaded, setLoaded] = useState(false);
  const [failed, setFailed] = useState(false);
  const image: RefObject<HTMLImageElement | null> = useRef<HTMLImageElement>(null);
  return (
    <div className={`relative overflow-hidden bg-fill ${className}`}>
      {art?.hash && fit === "cover" && !failed && <BlurHash hash={art.hash} image={image} />}
      {art && !failed ? (
        // The server's images: next/image optimisation is unavailable in a static export.
        // eslint-disable-next-line @next/next/no-img-element
        <img ref={image} src={art.src} srcSet={art.srcSet} sizes={art.srcSet ? sizes ?? art.sizes : undefined} alt={alt}
          loading={eager ? "eager" : "lazy"} decoding="async" onLoad={() => setLoaded(true)} onError={() => setFailed(true)}
          className={`absolute inset-0 h-full w-full transition duration-500 ${fit === "cover" ? "object-cover group-hover:scale-[1.03]" : "object-contain"} ${loaded ? "opacity-100" : "opacity-0"}`} />
      ) : (
        <span className="absolute inset-0 flex flex-col items-center justify-center gap-2 p-3 text-center text-meta font-medium text-ink-3">
          <Icon name={icon} size={20} />{label}
        </span>
      )}
    </div>
  );
}

function Watched({ item }: { item: Item }): ReactNode {
  const unplayed: number = item.UserData?.UnplayedItemCount ?? 0;
  if (item.UserData?.Played) {
    return (
      <span className="absolute top-2 right-2 grid size-7 place-items-center rounded-full bg-white text-canvas shadow-lg shadow-black/40">
        <Icon name="check" size={14} bold /><span className="sr-only">Watched</span>
      </span>
    );
  }
  if ((item.Type === "Series" || item.Type === "Season") && unplayed > 0) {
    return <span className="absolute top-2 right-2 grid h-7 min-w-7 place-items-center rounded-pill bg-black/80 px-2 text-caption font-bold tabular-nums ring-1 ring-white/10">{unplayed}<span className="sr-only"> unwatched</span></span>;
  }
  return null;
}

function Bar({ value }: { value: number }): ReactNode {
  if (value <= 0 || value >= 1) return null;
  return (
    <span aria-hidden="true" className="absolute inset-x-2.5 bottom-2.5 h-1 overflow-hidden rounded-full bg-white/25">
      <span className="block h-full rounded-full bg-white" style={{ width: `${value * 100}%` }} />
    </span>
  );
}

// Roving focus inside rails: the card a rail last focused is its one tab stop.
interface Roving { tabIndex: number; onFocus: () => void }

// Focus ring drawn on the art (the link itself has no outline), scaled like hover.
const focusArt: string = "transition group-focus-visible/card:scale-[1.03] group-focus-visible/card:ring-2 group-focus-visible/card:ring-white";

function subtitle(item: Item): string {
  if (item.Type === "Episode") return [episodeCode(item), item.Name].filter(Boolean).join(" · ");
  if (item.Type === "Series" && item.ProductionYear) {
    const end: string = item.Status === "Continuing" ? "Present" : item.EndDate ? String(new Date(item.EndDate).getFullYear()) : "";
    return end && end !== String(item.ProductionYear) ? `${item.ProductionYear} – ${end}` : String(item.ProductionYear);
  }
  return item.ProductionYear ? String(item.ProductionYear) : "";
}

// A portrait card for movies, series and seasons.
export function PosterCard({ item, className = "", roving }: { item: Item; className?: string; roving?: Roving }): JSX.Element {
  const title: string = item.Type === "Episode" ? item.SeriesName ?? item.Name : item.Name;
  return (
    <article className={`group relative min-w-0 ${className}`}>
      <Link href={itemHref(item)} data-card="" {...roving} className="group/card block rounded-card focus-visible:outline-none">
        <div className={`relative rounded-card ${focusArt}`}>
          <Img art={poster(item)} label={title} sizes="(min-width: 480px) 180px, 140px" className="aspect-[2/3] rounded-card ring-1 ring-white/8" />
          <Watched item={item} />
          <Bar value={progressOf(item)} />
        </div>
        <h3 className="mt-2.5 truncate px-0.5 text-body font-semibold" title={title}>{title}</h3>
      </Link>
      <p className="truncate px-0.5 text-meta text-ink-3">{subtitle(item) || " "}</p>
    </article>
  );
}

// A landscape card for continue watching and next up: it plays straight away.
export function WideCard({ item, className = "", roving }: { item: Item; className?: string; roving?: Roving }): JSX.Element {
  const title: string = item.Type === "Episode" ? item.SeriesName ?? item.Name : item.Name;
  const progress: number = progressOf(item);
  return (
    <article className={`group relative min-w-0 ${className}`}>
      <Link href={playHref(item.Id)} aria-label={`Play ${title}${item.Type === "Episode" ? `, ${subtitle(item)}` : ""}`} data-card="" {...roving} className="group/card block rounded-card focus-visible:outline-none">
        <div className={`relative rounded-card ${focusArt}`}>
          <Img art={landscape(item)} icon="tv" sizes="300px" className="aspect-video rounded-card ring-1 ring-white/8" />
          <span aria-hidden="true" className="absolute inset-0 rounded-card scrim-b opacity-60" />
          <span aria-hidden="true" className="absolute inset-0 grid place-items-center opacity-0 transition group-hover:opacity-100 group-focus-visible/card:opacity-100">
            <span className="grid size-12 place-items-center rounded-full bg-white text-canvas shadow-lg shadow-black/50"><Icon name="play" size={18} fill="currentColor" /></span>
          </span>
          <Bar value={progress} />
        </div>
      </Link>
      <Link href={item.Type === "Episode" && item.SeriesId ? `/item/?id=${item.SeriesId}` : itemHref(item)} className="mt-2.5 block truncate px-0.5 text-body font-semibold hover:underline">{title}</Link>
      <p className="truncate px-0.5 text-meta text-ink-3">{subtitle(item) || " "}</p>
    </article>
  );
}

export function PosterGrid({ children }: { children: ReactNode }): JSX.Element {
  // Off-screen cards skip layout and paint until scrolled near (long libraries); the clip margin keeps the focus ring visible.
  return <div className="[&>*]:[contain-intrinsic-size:auto_300px] [&>*]:[content-visibility:auto] [&>*]:[overflow-clip-margin:8px] grid grid-cols-[repeat(auto-fill,minmax(112px,1fr))] gap-x-4 gap-y-5 min-[480px]:grid-cols-[repeat(auto-fill,minmax(148px,1fr))]">{children}</div>;
}

export function PosterSkeleton({ wide = false, className = "" }: { wide?: boolean; className?: string }): JSX.Element {
  return (
    <div className={className}>
      <Skeleton className={`${wide ? "aspect-video" : "aspect-[2/3]"} rounded-card`} />
      <Skeleton className="mt-3 h-3.5 w-3/4" />
      <Skeleton className="mt-1.5 h-3 w-1/3" />
    </div>
  );
}

// Arrow keys: ←/→ move along a rail, ↑/↓ jump to the same rail stop of the rail above or below.
function railKeys(e: KeyboardEvent<HTMLDivElement>): void {
  const target: HTMLElement = e.target as HTMLElement;
  if (!target.matches("[data-card]")) return;
  const cards: HTMLElement[] = [...e.currentTarget.querySelectorAll<HTMLElement>("[data-card]")];
  const at: number = cards.indexOf(target);
  let next: HTMLElement | null | undefined = null;
  if (e.key === "ArrowRight") next = cards[at + 1];
  else if (e.key === "ArrowLeft") next = cards[at - 1];
  else if (e.key === "Home") next = cards[0];
  else if (e.key === "End") next = cards[cards.length - 1];
  else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
    const rails: HTMLElement[] = [...document.querySelectorAll<HTMLElement>("[data-rail]")];
    const rail: HTMLElement | undefined = rails[rails.indexOf(e.currentTarget) + (e.key === "ArrowDown" ? 1 : -1)];
    next = rail?.querySelector<HTMLElement>("[data-card][tabindex='0']") ?? rail?.querySelector<HTMLElement>("[data-card]");
  }
  if (!next) return;
  e.preventDefault();
  next.focus();
}

// A horizontal rail with snap scrolling; arrow buttons page it on pointer devices.
export function Rail({ title, href, wide = false, items, loading = false }: { title: string; href?: string; wide?: boolean; items: Item[]; loading?: boolean }): ReactNode {
  const track: RefObject<HTMLDivElement | null> = useRef<HTMLDivElement>(null);
  const [stop, setStop] = useState(0);
  if (!loading && items.length === 0) return null;
  const active: number = Math.min(stop, items.length - 1);
  const page = (direction: number): void => {
    const el: HTMLDivElement | null = track.current;
    el?.scrollBy({ left: direction * el.clientWidth * 0.85, behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth" });
  };
  const width: string = wide ? "w-[260px] sm:w-[300px]" : "w-[132px] sm:w-[156px]";
  return (
    <section aria-label={title} className="min-w-0">
      <header className="mb-3 flex items-center justify-between gap-3">
        <h2 className="text-title font-semibold">
          {href ? <Link href={href} className="inline-flex items-center gap-1 hover:underline">{title}<Icon name="chevronRight" size={16} /></Link> : title}
        </h2>
        <div className="flex gap-1 pointer-coarse:hidden">
          <button onClick={() => page(-1)} aria-label={`Scroll ${title} back`} className="grid size-9 cursor-pointer place-items-center rounded-full text-ink-2 hover:bg-fill-2 hover:text-ink"><Icon name="chevronLeft" size={18} /></button>
          <button onClick={() => page(1)} aria-label={`Scroll ${title} forward`} className="grid size-9 cursor-pointer place-items-center rounded-full text-ink-2 hover:bg-fill-2 hover:text-ink"><Icon name="chevronRight" size={18} /></button>
        </div>
      </header>
      <div ref={track} data-rail="" onKeyDown={railKeys} className="rail -mx-4 -mt-2 flex gap-4 overflow-x-auto scroll-px-4 px-4 pt-2 pb-2 lg:-mx-5 lg:scroll-px-5 lg:px-5">
        {loading
          ? Array.from({ length: 8 }, (_: unknown, i: number) => <PosterSkeleton key={i} wide={wide} className={`shrink-0 ${width}`} />)
          : items.map((item: Item, i: number) => {
            const roving: Roving = { tabIndex: i === active ? 0 : -1, onFocus: () => setStop(i) };
            return wide
              ? <WideCard key={item.Id} item={item} roving={roving} className={`shrink-0 ${width}`} />
              : <PosterCard key={item.Id} item={item} roving={roving} className={`shrink-0 ${width}`} />;
          })}
      </div>
    </section>
  );
}
