"use client";

import Link from "next/link";
import { Children, createContext, isValidElement, useContext, useEffect, useId, useRef, useState } from "react";
import type { ButtonHTMLAttributes, ComponentProps, Context, CSSProperties, JSX, KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent, ReactElement, ReactNode, RefObject } from "react";
import { createPortal } from "react-dom";
import { errorMessage } from "@/lib/api";
import { Icon } from "@/components/icons";
import type { IconName } from "@/components/icons";

type Variant = "primary" | "glass" | "ghost" | "danger";
type Size = "sm" | "md";

const variants: Record<Variant, string> = {
  primary: "bg-white text-canvas hover:bg-ink",
  glass: "glass text-ink hover:bg-fill-2",
  ghost: "text-ink-2 hover:bg-fill hover:text-ink",
  danger: "bg-bad/15 text-bad ring-1 ring-bad/30 hover:bg-bad/25",
};

const sizes: Record<Size, string> = { sm: "h-9 px-4 text-meta pointer-coarse:h-11", md: "h-11 px-5 text-body" };

export const buttonClass = (variant: Variant = "glass", size: Size = "md"): string =>
  `inline-flex shrink-0 cursor-pointer items-center justify-center gap-2 rounded-pill font-semibold whitespace-nowrap transition active:scale-[.98] disabled:pointer-events-none disabled:opacity-40 ${sizes[size]} ${variants[variant]}`;

// A link that looks like a Button, for navigation rather than actions.
export function ButtonLink({ href, variant = "glass", size = "md", icon, className = "", children, onClick }: {
  href: string; variant?: Variant; size?: Size; icon?: IconName; className?: string; children: ReactNode; onClick?: () => void;
}): JSX.Element {
  return (
    <Link href={href} onClick={onClick} className={`${buttonClass(variant, size)} ${className}`}>
      {icon && <Icon name={icon} size={size === "sm" ? 14 : 16} />}
      {children}
    </Link>
  );
}

// A spinner takes the icon's place while loading, so the label and the width stay put.
export function Button({ variant = "glass", size = "md", icon, loading = false, className = "", children, disabled, ...props }:
  ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; size?: Size; icon?: IconName; loading?: boolean }): JSX.Element {
  return (
    <button disabled={disabled || loading} className={`${buttonClass(variant, size)} ${className}`} {...props}>
      {loading ? <Spinner /> : icon && <Icon name={icon} size={size === "sm" ? 14 : 16} />}
      {children}
    </button>
  );
}

export function IconButton({ icon, label, className = "", variant = "glass", size = 20, ...props }:
  ButtonHTMLAttributes<HTMLButtonElement> & { icon: IconName; label: string; variant?: "glass" | "ghost"; size?: number }): JSX.Element {
  return (
    <button aria-label={label} title={label}
      className={`relative grid size-11 shrink-0 cursor-pointer place-items-center rounded-full text-ink-2 transition hover:bg-fill-2 hover:text-ink active:scale-[.98] disabled:opacity-40 ${variant === "glass" ? "glass" : ""} ${className}`}
      {...props}>
      <Icon name={icon} size={size} />
    </button>
  );
}

const control: string = "well w-full min-w-0 rounded-ctl text-body text-ink outline-none transition placeholder:text-ink-3 hover:border-white/20 focus:border-white/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white/70";

export function Input({ className = "", icon, ...props }: ComponentProps<"input"> & { icon?: IconName }): JSX.Element {
  if (!icon) return <input className={`h-11 px-3.5 ${control} ${className}`} {...props} />;
  return (
    <div className={`relative min-w-0 ${className}`}>
      <Icon name={icon} size={16} className="pointer-events-none absolute top-1/2 left-3.5 -translate-y-1/2 text-ink-3" />
      <input className={`h-11 pr-3.5 pl-10 ${control}`} {...props} />
    </div>
  );
}

type Choice = { value: string; label: ReactNode };
const textOf = (c: Choice): string => (typeof c.label === "string" ? c.label : c.value);

// A themed listbox: native <select> popups ignore the theme.
function Dropdown({ choices, value, onPick, label, disabled, className, children }: {
  choices: readonly Choice[];
  value: string;
  onPick: (value: string) => void;
  label?: string;
  disabled?: boolean;
  className: string;
  children: ReactNode;
}): JSX.Element {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const [at, setAt] = useState<CSSProperties>({});
  const button: RefObject<HTMLButtonElement | null> = useRef<HTMLButtonElement>(null);
  const list: RefObject<HTMLUListElement | null> = useRef<HTMLUListElement>(null);
  const id: string = useId();

  // Fixed and portalled, so a scrolling panel or modal cannot clip it; it flips up when the space below is short.
  const show = (): void => {
    const box: DOMRect | undefined = button.current?.getBoundingClientRect();
    if (!box) return;
    const below: number = window.innerHeight - box.bottom - 12;
    const up: boolean = below < 240 && box.top > below;
    setAt({ left: box.left, minWidth: box.width, maxHeight: Math.min(320, up ? box.top - 12 : below),
      ...(up ? { bottom: window.innerHeight - box.top + 6 } : { top: box.bottom + 6 }) });
    setActive(Math.max(0, choices.findIndex((c: Choice) => c.value === value)));
    setOpen(true);
  };
  const close = (): void => { setOpen(false); button.current?.focus(); };
  const pick = (i: number): void => {
    close();
    const choice: Choice | undefined = choices[i];
    if (choice && choice.value !== value) onPick(choice.value);
  };

  useEffect(() => {
    if (!open) return;
    list.current?.focus();
    const away = (e: Event): void => {
      const target: Node | null = e.target instanceof Node ? e.target : null;
      if (!target || (!list.current?.contains(target) && !button.current?.contains(target))) setOpen(false);
    };
    document.addEventListener("mousedown", away);
    window.addEventListener("scroll", away, true);
    window.addEventListener("resize", away);
    return () => {
      document.removeEventListener("mousedown", away);
      window.removeEventListener("scroll", away, true);
      window.removeEventListener("resize", away);
    };
  }, [open]);

  useEffect(() => {
    if (open) list.current?.children[active]?.scrollIntoView({ block: "nearest" });
  }, [open, active]);

  function key(e: ReactKeyboardEvent): void {
    const last: number = choices.length - 1;
    const moves: Record<string, number> = { ArrowDown: Math.min(active + 1, last), ArrowUp: Math.max(active - 1, 0), Home: 0, End: last };
    const move: number | undefined = moves[e.key];
    if (move !== undefined) setActive(move);
    else if (e.key === "Enter" || e.key === " ") pick(active);
    else if (e.key === "Escape") { e.stopPropagation(); close(); }
    else if (e.key === "Tab") setOpen(false);
    else if (e.key.length === 1) {
      const letter: string = e.key.toLowerCase();
      const order: number[] = [...choices.keys()].map((n: number) => (active + 1 + n) % choices.length);
      const next: number | undefined = order.find((n: number) => !!choices[n] && textOf(choices[n]).toLowerCase().startsWith(letter));
      if (next !== undefined) setActive(next);
    } else return;
    if (e.key !== "Tab") e.preventDefault();
  }

  return (
    <>
      <button ref={button} type="button" disabled={disabled} aria-haspopup="listbox" aria-expanded={open} aria-controls={open ? id : undefined} aria-label={label}
        onClick={() => (open ? setOpen(false) : show())}
        onKeyDown={(e: ReactKeyboardEvent) => { if (e.key === "ArrowDown" || e.key === "ArrowUp") { e.preventDefault(); show(); } }}
        className={className}>
        {children}
      </button>
      {open && createPortal(
        <ul ref={list} id={id} role="listbox" aria-label={label} tabIndex={-1} onKeyDown={key} style={at}
          aria-activedescendant={`${id}-${active}`} onMouseDown={(e: ReactMouseEvent) => e.stopPropagation()}
          className="glass-pop fixed z-[80] max-w-[min(360px,calc(100vw-24px))] animate-rise overflow-y-auto overscroll-contain rounded-tile p-1.5 text-body outline-none">
          {choices.map((c: Choice, i: number) => (
            <li key={c.value} id={`${id}-${i}`} role="option" aria-selected={c.value === value}
              onMouseMove={() => setActive(i)} onClick={() => pick(i)}
              className={`flex min-h-10 cursor-pointer items-center gap-2 rounded-chip px-3 py-2 ${i === active ? "bg-fill-2 text-ink" : "text-ink-2"}`}>
              <span className="min-w-0 flex-1 truncate">{c.label}</span>
              {c.value === value && <Icon name="check" size={14} className="shrink-0 text-ink" />}
            </li>
          ))}
        </ul>,
        document.body,
      )}
    </>
  );
}

type OptionProps = { value?: string | number; children?: ReactNode };

// Same API as a native select (value, onChange reading e.target.value, <option> children), drawn with the theme.
export function Select({ className = "", children, value, onChange, disabled, "aria-label": label }: {
  className?: string;
  children: ReactNode;
  value?: string | number;
  onChange?: (e: { target: { value: string } }) => void;
  disabled?: boolean;
  "aria-label"?: string;
}): JSX.Element {
  const choices: Choice[] = Children.toArray(children).filter(isValidElement<OptionProps>)
    .map((o: ReactElement<OptionProps>) => ({ value: String(o.props.value ?? ""), label: o.props.children }));
  const current: string = String(value ?? "");
  return (
    <div className={`relative ${className}`}>
      <Dropdown choices={choices} value={current} onPick={(v: string) => onChange?.({ target: { value: v } })} label={label} disabled={disabled}
        className={`flex h-11 cursor-pointer items-center pr-9 pl-3.5 text-left disabled:cursor-default disabled:opacity-60 ${control}`}>
        <span className="truncate">{choices.find((c: Choice) => c.value === current)?.label}</span>
      </Dropdown>
      <Icon name="chevronDown" size={16} className="pointer-events-none absolute top-1/2 right-3 -translate-y-1/2 text-ink-3" />
    </div>
  );
}

export function Field({ label, hint, children }: { label: string; hint?: ReactNode; children: ReactNode }): JSX.Element {
  return (
    <label className="block space-y-2">
      <span className="text-body font-medium">{label}</span>
      {children}
      {hint && <span className="block text-meta leading-relaxed text-ink-3">{hint}</span>}
    </label>
  );
}

// The whole row is the switch, so the hit area is the label as well as the track.
export function Switch({ checked, onChange, label, description, disabled, hideLabel = false }: {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label: string;
  description?: string;
  disabled?: boolean;
  hideLabel?: boolean;
}): JSX.Element {
  return (
    <button type="button" role="switch" aria-checked={checked} disabled={disabled} onClick={() => onChange(!checked)}
      className={`group flex min-h-11 cursor-pointer items-center gap-3 rounded-ctl text-left disabled:cursor-default disabled:opacity-40 ${hideLabel ? "min-w-11 shrink-0 justify-center" : "w-full"}`}>
      <span className={hideLabel ? "sr-only" : "min-w-0 flex-1"}>
        <span className="block text-body font-medium">{label}</span>
        {description && <span className="block text-meta text-ink-3">{description}</span>}
      </span>
      <span className={`relative h-6 w-10 shrink-0 rounded-pill transition ${checked ? "bg-ok" : "bg-fill-2"}`}>
        <span className={`absolute top-0.5 left-0.5 size-5 rounded-full bg-white shadow transition-transform ${checked ? "translate-x-4" : ""}`} />
      </span>
    </button>
  );
}

export function Chip({ selected = false, onClick, children, className = "" }: { selected?: boolean; onClick?: () => void; children: ReactNode; className?: string }): JSX.Element {
  return (
    <button type="button" onClick={onClick} aria-pressed={selected}
      className={`well inline-flex min-h-11 shrink-0 cursor-pointer items-center gap-1.5 rounded-pill px-4 text-meta font-medium text-ink-2 transition hover:text-ink aria-pressed:bg-fill-2 aria-pressed:text-ink sm:min-h-9 [@media(pointer:coarse)]:min-h-11 ${className}`}>
      {selected && <Icon name="check" size={14} bold />}
      {children}
    </button>
  );
}

type AlertTone = "bad" | "warn" | "info";
const alertTones: Record<AlertTone, string> = { bad: "bg-bad/10 text-bad ring-bad/30", warn: "bg-warn/10 text-warn ring-warn/30", info: "bg-fill text-ink-2 ring-hair" };

export function Alert({ children, tone = "bad" }: { children: ReactNode; tone?: AlertTone }): JSX.Element {
  return (
    <div role={tone === "info" ? "note" : "alert"} className={`flex items-start gap-2.5 rounded-ctl px-4 py-3 text-meta ring-1 ${alertTones[tone]}`}>
      <Icon name={tone === "info" ? "info" : "alert"} size={16} className="mt-0.5 shrink-0" />
      <div className="min-w-0 break-words">{children}</div>
    </div>
  );
}

// Each colour means one state, never decoration.
export type Tone = "neutral" | "ok" | "live" | "seek" | "warn" | "bad";

const badgeTones: Record<Tone, string> = {
  neutral: "bg-fill text-ink-2 ring-hair",
  ok: "bg-ok/12 text-ok ring-ok/25",
  live: "bg-live/12 text-live ring-live/25",
  seek: "bg-seek/12 text-seek ring-seek/25",
  warn: "bg-warn/12 text-warn ring-warn/25",
  bad: "bg-bad/12 text-bad ring-bad/25",
};

export const toneText: Record<Tone, string> = {
  neutral: "text-ink-3", ok: "text-ok", live: "text-live", seek: "text-seek", warn: "text-warn", bad: "text-bad",
};

export const toneDot: Record<Tone, string> = {
  neutral: "bg-ink-3", ok: "bg-ok", live: "bg-live", seek: "bg-seek", warn: "bg-warn", bad: "bg-bad",
};

export function Badge({ tone = "neutral", children, className = "" }: { tone?: Tone; children: ReactNode; className?: string }): JSX.Element {
  return <span className={`inline-flex h-6 items-center gap-1 rounded-pill px-2.5 text-meta font-semibold whitespace-nowrap ring-1 ring-inset ${badgeTones[tone]} ${className}`}>{children}</span>;
}

export function Rating({ value, className = "" }: { value: number | null | undefined; className?: string }): ReactNode {
  if (value == null || value <= 0) return null;
  return (
    <span className={`inline-flex items-center gap-1 font-semibold text-ink-2 tabular-nums ${className}`}>
      <Icon name="star" size={14} fill="currentColor" strokeWidth={0} className="text-ink-3" />{value.toFixed(1)}
      <span className="sr-only"> out of 10</span>
    </span>
  );
}

// Width moves smoothly, but assistive tech hears 10% steps only.
export function Progress({ value, label = "Progress", className = "" }: { value: number; label?: string; className?: string }): JSX.Element {
  const step: number = Math.floor(Math.min(Math.max(value, 0), 1) * 10) * 10;
  return (
    <div role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={step}
      className={`h-1.5 overflow-hidden rounded-full bg-fill-2 ${className}`}>
      <div className="h-full rounded-full bg-ink transition-[width] duration-700" style={{ width: `${value * 100}%` }} />
    </div>
  );
}

export function Well({ className = "", children }: { className?: string; children: ReactNode }): JSX.Element {
  return <div className={`well rounded-card p-3.5 ${className}`}>{children}</div>;
}

export function WellList({ className = "", children, label }: { className?: string; children: ReactNode; label?: string }): JSX.Element {
  return <ul aria-label={label} className={`well divide-y divide-hair-2 overflow-hidden rounded-card ${className}`}>{children}</ul>;
}

export function Section({ id, title, description, actions, children }: { id?: string; title: string; description?: ReactNode; actions?: ReactNode; children: ReactNode }): JSX.Element {
  return (
    <section id={id} className="glass scroll-mt-24 rounded-panel">
      <header className="flex flex-wrap items-start justify-between gap-3 border-b border-hair-2 px-5 py-4">
        <div className="min-w-0">
          <h2 className="text-title font-semibold">{title}</h2>
          {description && <p className="mt-0.5 max-w-prose text-meta text-ink-3">{description}</p>}
        </div>
        {actions}
      </header>
      <div className="p-5">{children}</div>
    </section>
  );
}

export function PageHeader({ title, description, children }: { title: string; description?: ReactNode; children?: ReactNode }): JSX.Element {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        <h1 tabIndex={-1} className="font-display text-[40px] leading-none outline-none">{title}</h1>
        {description && <p className="mt-2 text-meta text-ink-3">{description}</p>}
      </div>
      {children}
    </div>
  );
}

export function Empty({ icon = "film", title, children }: { icon?: IconName; title: string; children?: ReactNode }): JSX.Element {
  return (
    <div className="glass flex flex-col items-center rounded-tile px-6 py-12 text-center">
      <span className="mb-4 grid size-12 place-items-center rounded-full bg-fill-2 text-ink-2"><Icon name={icon} size={20} /></span>
      <p className="text-title font-semibold">{title}</p>
      {children && <div className="mt-1.5 max-w-sm text-meta text-ink-2">{children}</div>}
    </div>
  );
}

export function Spinner({ className = "" }: { className?: string }): JSX.Element {
  return <span role="status" aria-label="Loading" className={`inline-block size-4 animate-spin rounded-full border-2 border-current border-t-transparent ${className}`} />;
}

export function Skeleton({ className = "" }: { className?: string }): JSX.Element {
  return <div aria-hidden="true" className={`animate-shimmer rounded-chip bg-[linear-gradient(90deg,rgb(255_255_255/.05)_0%,rgb(255_255_255/.11)_50%,rgb(255_255_255/.05)_100%)] bg-[length:200%_100%] ${className}`} />;
}

export function Segmented<T extends string>({ value, options, onChange, label, className = "" }: {
  value: T;
  options: readonly { value: T; label: string; count?: number }[];
  onChange: (value: T) => void;
  label: string;
  className?: string;
}): JSX.Element {
  const move = (event: ReactKeyboardEvent<HTMLButtonElement>, index: number): void => {
    const step: number = event.key === "ArrowRight" || event.key === "ArrowDown" ? 1 : event.key === "ArrowLeft" || event.key === "ArrowUp" ? -1 : 0;
    if (!step) return;
    event.preventDefault();
    const target: number = (index + step + options.length) % options.length;
    const next: { value: T } | undefined = options[target];
    if (next) onChange(next.value);
    (event.currentTarget.parentElement?.children[target] as HTMLElement | undefined)?.focus();
  };
  return (
    <div role="radiogroup" aria-label={label} className={`glass rail inline-flex h-11 max-w-full shrink-0 items-center gap-1 overflow-x-auto rounded-pill p-1 pointer-coarse:h-12 ${className}`}>
      {options.map((o: { value: T; label: string; count?: number }, i: number) => (
        <button key={o.value} type="button" role="radio" aria-checked={o.value === value} tabIndex={o.value === value ? 0 : -1}
          onClick={() => onChange(o.value)} onKeyDown={(e: ReactKeyboardEvent<HTMLButtonElement>) => move(e, i)}
          className="flex h-full shrink-0 cursor-pointer items-center gap-1.5 rounded-pill px-4 text-meta font-semibold text-ink-2 transition hover:text-ink aria-checked:bg-fill-3 aria-checked:text-ink">
          {o.label}
          {o.count !== undefined && <span className="text-caption text-ink-3 tabular-nums">{o.count}</span>}
        </button>
      ))}
    </div>
  );
}

let openDialogs: number = 0;

// Focus trap, Escape, focus restore and an inert background: shared by every dialog, sheet and popover.
export function useDialog(panel: RefObject<HTMLElement | null>, onClose: () => void): void {
  const close: RefObject<() => void> = useRef(onClose);
  useEffect(() => { close.current = onClose; });

  useEffect(() => {
    const previous: HTMLElement | null = document.activeElement as HTMLElement | null;
    (panel.current?.querySelector<HTMLElement>("[autofocus], input, select") ?? panel.current)?.focus();
    const key = (e: KeyboardEvent): void => {
      if (e.key === "Escape") { e.stopPropagation(); close.current(); }
      if (e.key !== "Tab" || !panel.current) return;
      const focusable: HTMLElement[] = [...panel.current.querySelectorAll<HTMLElement>("button, input, select, textarea, summary, a[href], [tabindex]:not([tabindex='-1'])")].filter((el: HTMLElement) => !el.hasAttribute("disabled"));
      const first: HTMLElement | undefined = focusable[0];
      const last: HTMLElement | undefined = focusable[focusable.length - 1];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last?.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first?.focus(); }
    };
    const app: HTMLElement | null = document.getElementById("app");
    openDialogs += 1;
    app?.setAttribute("inert", "");
    document.body.style.overflow = "hidden";
    window.addEventListener("keydown", key);
    return () => {
      window.removeEventListener("keydown", key);
      openDialogs -= 1;
      if (openDialogs === 0) {
        app?.removeAttribute("inert");
        document.body.style.overflow = "";
      }
      previous?.focus();
    };
  }, [panel]);
}

type ModalSize = "md" | "lg";
const modalLayouts: Record<ModalSize, { overlay: string; panel: string }> = {
  md: { overlay: "sm:items-center sm:p-6", panel: "sm:max-w-xl sm:rounded-panel" },
  lg: { overlay: "sm:items-center sm:p-6", panel: "sm:max-w-4xl sm:rounded-panel" },
};

// Below 640px every dialog is a bottom sheet.
export function Modal({ title, description, onClose, size = "md", children, footer }: {
  title: ReactNode;
  description?: ReactNode;
  onClose: () => void;
  size?: ModalSize;
  children: ReactNode;
  footer?: ReactNode;
}): ReactNode {
  const id: string = useId();
  const panel: RefObject<HTMLDivElement | null> = useRef<HTMLDivElement>(null);
  useDialog(panel, onClose);

  return createPortal(
    <div className={`fixed inset-0 z-50 flex items-end justify-center bg-overlay ${modalLayouts[size].overlay}`} onMouseDown={onClose}>
      <div ref={panel} role="dialog" aria-modal="true" aria-labelledby={id} tabIndex={-1} onMouseDown={(e: ReactMouseEvent) => e.stopPropagation()}
        className={`glass-pop relative flex max-h-[92dvh] w-full animate-rise flex-col overflow-hidden rounded-t-panel outline-none ${modalLayouts[size].panel}`}>
        <span aria-hidden="true" className="mx-auto mt-2 h-1 w-10 shrink-0 rounded-full bg-fill-3 sm:hidden" />
        <header className="relative flex items-start justify-between gap-4 px-5 pt-4 pb-3">
          <div className="min-w-0 pt-2">
            <h2 id={id} className="text-title font-semibold">{title}</h2>
            {description && <div className="mt-0.5 text-meta text-ink-2">{description}</div>}
          </div>
          <button onClick={onClose} aria-label="Close" className="-mr-2 grid size-11 shrink-0 cursor-pointer place-items-center rounded-full text-ink-2 hover:bg-fill-2 hover:text-ink">
            <Icon name="x" size={20} />
          </button>
        </header>
        <div className="relative min-h-0 flex-1 overflow-y-auto">{children}</div>
        {footer && <footer className="relative flex flex-wrap justify-end gap-2 border-t border-hair-2 px-5 pt-3.5 pb-[max(14px,env(safe-area-inset-bottom))]">{footer}</footer>}
      </div>
    </div>,
    document.body,
  );
}

export interface Action {
  busy: boolean;
  error: string | null;
  done: boolean;
  run: (action: () => Promise<void>) => Promise<void>;
  setError: (error: string | null) => void;
}

// Runs a save or delete so every form reports errors and success the same way.
export function useAction(): Action {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const run = async (action: () => Promise<void>): Promise<void> => {
    setBusy(true);
    setError(null);
    setDone(false);
    try {
      await action();
      setDone(true);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };
  return { busy, error, done, run, setError };
}

const CloseMenu: Context<() => void> = createContext<() => void>(() => undefined);

// A dropdown of actions: arrow keys move, Escape and outside clicks close it and focus goes back to the trigger.
export function Menu({ label, trigger, className = "", align = "right", children }: {
  label: string;
  trigger?: ReactNode;
  className?: string;
  align?: "left" | "right";
  children: ReactNode;
}): JSX.Element {
  const [at, setAt] = useState<CSSProperties | null>(null);
  const button: RefObject<HTMLButtonElement | null> = useRef<HTMLButtonElement>(null);
  const menu: RefObject<HTMLDivElement | null> = useRef<HTMLDivElement>(null);
  const id: string = useId();
  const open: boolean = at !== null;

  // Fixed and portalled like Dropdown, so a clipping list or panel cannot cut it; it flips up when the space below is short.
  const show = (): void => {
    const box: DOMRect | undefined = button.current?.getBoundingClientRect();
    if (!box) return;
    const below: number = window.innerHeight - box.bottom - 12;
    const up: boolean = below < 280 && box.top > below;
    setAt({
      ...(align === "right" ? { right: Math.max(12, window.innerWidth - box.right) } : { left: Math.max(12, box.left) }),
      ...(up ? { bottom: window.innerHeight - box.top + 8 } : { top: box.bottom + 8 }),
      maxHeight: Math.max(160, (up ? box.top - 12 : below) - 8),
    });
  };
  const close = (): void => setAt(null);

  useEffect(() => {
    if (!open) return;
    const items = (): HTMLElement[] => [...(menu.current?.querySelectorAll<HTMLElement>("[role=menuitem]:not(:disabled)") ?? [])];
    const inside = (e: Event): boolean => e.target instanceof Node && (!!menu.current?.contains(e.target) || !!button.current?.contains(e.target));
    const away = (e: Event): void => { if (!inside(e)) setAt(null); };
    const key = (e: KeyboardEvent): void => {
      if (e.key === "Escape") {
        e.stopPropagation();
        setAt(null);
        button.current?.focus();
        return;
      }
      // Focus goes back to the trigger first, so the browser's own Tab moves on from there rather than off the page's end.
      if (e.key === "Tab") { setAt(null); button.current?.focus(); return; }
      const list: HTMLElement[] = items();
      const now: number = list.indexOf(document.activeElement as HTMLElement);
      const moves: Record<string, number> = { ArrowDown: now + 1, ArrowUp: now - 1, Home: 0, End: list.length - 1 };
      const move: number | undefined = moves[e.key];
      if (move === undefined || list.length === 0) return;
      e.preventDefault();
      list[(move + list.length) % list.length]?.focus();
    };
    document.addEventListener("mousedown", away);
    document.addEventListener("keydown", key);
    window.addEventListener("scroll", away, true);
    window.addEventListener("resize", close);
    items()[0]?.focus();
    return () => {
      document.removeEventListener("mousedown", away);
      document.removeEventListener("keydown", key);
      window.removeEventListener("scroll", away, true);
      window.removeEventListener("resize", close);
    };
  }, [open]);

  return (
    <>
      <button ref={button} type="button" onClick={() => (open ? close() : show())} aria-haspopup="menu" aria-expanded={open} aria-controls={open ? id : undefined} aria-label={label} title={label}
        className={className || "glass grid size-11 cursor-pointer place-items-center rounded-full text-ink-2 transition hover:bg-fill-2 hover:text-ink"}>
        {trigger ?? <Icon name="more" size={20} bold />}
      </button>
      {at && createPortal(
        // Nearly opaque: a menu often opens over another blurred panel, whose own blur cannot reach behind it.
        <div ref={menu} id={id} role="menu" aria-label={label} style={at}
          className="glass-pop fixed z-[60] w-64 max-w-[calc(100vw-24px)] animate-rise overflow-y-auto overscroll-contain rounded-tile p-2">
          <CloseMenu value={close}>{children}</CloseMenu>
        </div>,
        document.body,
      )}
    </>
  );
}

export function MenuItem({ icon, href, external = false, onClick, danger = false, disabled, className = "", children }: {
  icon: IconName;
  href?: string;
  // Another app: a plain link that opens in a new tab.
  external?: boolean;
  onClick?: () => void;
  danger?: boolean;
  disabled?: boolean;
  className?: string;
  children: ReactNode;
}): JSX.Element {
  const close: () => void = useContext(CloseMenu);
  const look: string = `flex min-h-11 w-full cursor-pointer items-center gap-3 rounded-ctl px-3 text-left text-body font-medium transition hover:bg-fill-2 disabled:opacity-40 ${danger ? "text-bad" : "text-ink-2 hover:text-ink"} ${className}`;
  const content: JSX.Element = <><Icon name={icon} size={16} className="shrink-0" />{children}</>;
  if (href && external) {
    return (
      <a role="menuitem" href={href} target="_blank" rel="noopener noreferrer" onClick={close} className={look}>
        {content}<span className="sr-only"> (opens in a new tab)</span>
      </a>
    );
  }
  return href
    ? <Link role="menuitem" href={href} onClick={close} className={look}>{content}</Link>
    : <button role="menuitem" type="button" disabled={disabled} onClick={() => { close(); onClick?.(); }} className={look}>{content}</button>;
}

// The user's picture, or the first letter of their name.
export function Avatar({ name, src, className = "size-8 text-meta" }: { name: string; src: string | null; className?: string }): JSX.Element {
  return src
    // eslint-disable-next-line @next/next/no-img-element
    ? <img src={src} alt="" className={`shrink-0 rounded-full object-cover ${className}`} />
    : <span aria-hidden="true" className={`grid shrink-0 place-items-center rounded-full bg-fill-2 font-semibold text-ink uppercase ${className}`}>{name[0]}</span>;
}

export function Logo({ markOnly = false }: { markOnly?: boolean }): JSX.Element {
  return (
    // The mark is the word's first letter: [v]isularr.
    <span className="flex items-center gap-1">
      <span aria-hidden="true" className="grid size-8 shrink-0 place-items-center rounded-chip bg-white pb-0.5 font-display text-[22px] leading-none text-canvas">v</span>
      <span className="sr-only">visularr</span>
      {!markOnly && <span aria-hidden="true" className="font-display text-[24px] leading-none">isularr</span>}
    </span>
  );
}
