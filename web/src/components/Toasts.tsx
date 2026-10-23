"use client";

import Link from "next/link";
import { createContext, useCallback, useContext, useEffect, useRef, useState } from "react";
import type { Context, JSX, ReactNode, RefObject } from "react";
import { createPortal } from "react-dom";
import { Icon } from "@/components/icons";
import type { IconName } from "@/components/icons";
import { IconButton, toneText } from "@/components/ui";
import type { Tone } from "@/components/ui";

export interface Toast {
  // A toast with the same key replaces the previous one.
  key: string;
  icon: IconName;
  tone: Tone;
  title: string;
  message?: string | null;
  href?: string | null;
}

type Shown = Toast & { id: number };
type Show = (toast: Toast) => void;

const LIFETIME_MS: number = 6000;
const ERROR_LIFETIME_MS: number = 10000;
const MAX_TOASTS: number = 4;

const ToastContext: Context<Show> = createContext<Show>(() => undefined);

export const useToast = (): Show => useContext(ToastContext);

export function ToastProvider({ children }: { children: ReactNode }): JSX.Element {
  const [toasts, setToasts] = useState<Shown[]>([]);
  const nextId: RefObject<number> = useRef(0);
  const timers: RefObject<Map<string, ReturnType<typeof setTimeout>>> = useRef(new Map<string, ReturnType<typeof setTimeout>>());
  // Hovered or focused toasts stay until the pointer and focus leave (WCAG 2.2.1).
  const paused: RefObject<boolean> = useRef(false);

  const dismiss: (key: string) => void = useCallback((key: string): void => {
    clearTimeout(timers.current.get(key));
    timers.current.delete(key);
    setToasts((t: Shown[]) => t.filter((x: Shown) => x.key !== key));
  }, []);

  const schedule: (toast: Toast) => void = useCallback((toast: Toast): void => {
    clearTimeout(timers.current.get(toast.key));
    if (!paused.current) timers.current.set(toast.key, setTimeout(() => dismiss(toast.key), toast.tone === "bad" ? ERROR_LIFETIME_MS : LIFETIME_MS));
  }, [dismiss]);

  const show: Show = useCallback((toast: Toast): void => {
    nextId.current += 1;
    const entry: Shown = { ...toast, id: nextId.current };
    setToasts((t: Shown[]) => [...t.filter((x: Shown) => x.key !== toast.key), entry].slice(-MAX_TOASTS));
    schedule(toast);
  }, [schedule]);

  const pause = (): void => {
    paused.current = true;
    timers.current.forEach(clearTimeout);
    timers.current.clear();
  };

  const resume = (list: HTMLElement): void => {
    if (list.matches(":hover") || list.contains(document.activeElement)) return;
    paused.current = false;
    toasts.forEach(schedule);
  };

  useEffect(() => {
    if (toasts.length === 0) paused.current = false;
  }, [toasts.length]);

  useEffect(() => {
    const pending: Map<string, ReturnType<typeof setTimeout>> = timers.current;
    return () => pending.forEach(clearTimeout);
  }, []);

  // Portalled out of #app, so toasts stay usable while a dialog makes the page inert.
  return (
    <ToastContext value={show}>
      {children}
      {createPortal(
        <div aria-label="Notifications"
          onMouseEnter={pause} onFocus={pause}
          onMouseLeave={(e: React.MouseEvent<HTMLDivElement>) => resume(e.currentTarget)}
          onBlur={(e: React.FocusEvent<HTMLDivElement>) => { if (!e.currentTarget.contains(e.relatedTarget)) resume(e.currentTarget); }}
          className="pointer-events-none fixed inset-x-3 top-[calc(64px+env(safe-area-inset-top))] z-[70] flex flex-col items-center gap-2 md:inset-x-auto md:top-[76px] md:right-5 md:w-[380px] md:items-end">
          {toasts.map((t: Shown) => (
            <div key={t.id} role={t.tone === "bad" ? "alert" : "status"}
              className="glass-pop pointer-events-auto flex w-full max-w-[420px] animate-rise items-start gap-3 rounded-tile p-3 pr-1.5">
              <span className={`grid size-9 shrink-0 place-items-center rounded-full bg-fill-2 ${toneText[t.tone]}`}><Icon name={t.icon} size={16} /></span>
              <div className="min-w-0 flex-1 py-0.5">
                {t.href
                  ? <Link href={t.href} onClick={() => dismiss(t.key)} className="block truncate text-body font-semibold hover:underline">{t.title}</Link>
                  : <p className="truncate text-body font-semibold">{t.title}</p>}
                {t.message && <p className="mt-0.5 line-clamp-2 text-meta leading-relaxed text-ink-2">{t.message}</p>}
              </div>
              <IconButton variant="ghost" icon="x" label="Dismiss" size={16} onClick={() => dismiss(t.key)} />
            </div>
          ))}
        </div>,
        document.body,
      )}
    </ToastContext>
  );
}
