"use client";

import { useCallback, useEffect, useState } from "react";
import type { JSX } from "react";
import { clock, episodeCode, errorMessage, TICKS_PER_SECOND } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { SessionInfo, SystemInfo } from "@/lib/api";
import { Icon } from "@/components/icons";
import { Alert, Badge, Section, Skeleton, Well, WellList } from "@/components/ui";

const REFRESH_MS: number = 10_000;

// Server facts and who is watching what right now.
export function Dashboard(): JSX.Element {
  const [info, setInfo] = useState<SystemInfo | null>(null);
  const [sessions, setSessions] = useState<SessionInfo[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load: () => void = useCallback((): void => {
    api.sessions().then((s: SessionInfo[]) => { setSessions(s); setError(null); }).catch((e: unknown) => setError(errorMessage(e)));
  }, []);

  useEffect(() => {
    api.systemInfo().then(setInfo).catch((e: unknown) => setError(errorMessage(e)));
    void Promise.resolve().then(load);
    const timer: ReturnType<typeof setInterval> = setInterval(load, REFRESH_MS);
    return () => clearInterval(timer);
  }, [load]);

  const playing: SessionInfo[] = (sessions ?? []).filter((s: SessionInfo) => s.NowPlayingItem);
  const idle: SessionInfo[] = (sessions ?? []).filter((s: SessionInfo) => !s.NowPlayingItem && s.UserName);

  return (
    <Section id="dashboard" title="Server" description="What the server runs and who is connected.">
      <div className="space-y-5">
        {error && <Alert>{error}</Alert>}
        <div className="grid gap-3 sm:grid-cols-3">
          {[["Name", info?.ServerName], ["Version", info?.Version], ["System", info?.OperatingSystemDisplayName]].map(([label, value]: (string | null | undefined)[]) => (
            <Well key={label}>
              <p className="caption">{label}</p>
              {info ? <p className="mt-1 truncate text-body font-semibold">{value || "—"}</p> : <Skeleton className="mt-2 h-4 w-2/3" />}
            </Well>
          ))}
        </div>
        {info?.HasPendingRestart && <Alert tone="warn">The server needs a restart to apply changes.</Alert>}
        <div>
          <h3 className="mb-2 text-body font-semibold">Now playing</h3>
          {!sessions ? <Skeleton className="h-16 rounded-card" /> : playing.length === 0 ? (
            <p className="text-meta text-ink-3">Nobody is watching right now.</p>
          ) : (
            <WellList label="Now playing">
              {playing.map((s: SessionInfo) => {
                const item: NonNullable<SessionInfo["NowPlayingItem"]> = s.NowPlayingItem!;
                const position: number = (s.PlayState?.PositionTicks ?? 0) / TICKS_PER_SECOND;
                const total: number = (item.RunTimeTicks ?? 0) / TICKS_PER_SECOND;
                return (
                  <li key={s.Id} className="flex items-center gap-3 px-4 py-3">
                    <Icon name={s.PlayState?.IsPaused ? "pause" : "play"} size={16} className="shrink-0 text-ok" />
                    <div className="min-w-0 flex-1">
                      <p className="truncate font-semibold">{item.Type === "Episode" ? `${item.SeriesName ?? ""} · ${episodeCode(item)} ${item.Name}` : item.Name}</p>
                      <p className="truncate text-meta text-ink-3">{s.UserName} on {s.DeviceName ?? s.Client} · {clock(position)}{total ? ` of ${clock(total)}` : ""}</p>
                    </div>
                    {s.PlayState?.PlayMethod && <Badge tone={s.PlayState.PlayMethod === "Transcode" ? "warn" : "ok"}>{s.PlayState.PlayMethod === "Transcode" ? "Transcoding" : "Direct"}</Badge>}
                  </li>
                );
              })}
            </WellList>
          )}
        </div>
        {idle.length > 0 && (
          <p className="text-meta text-ink-3">Also connected: {idle.map((s: SessionInfo) => `${s.UserName} (${s.DeviceName ?? s.Client})`).join(", ")}.</p>
        )}
      </div>
    </Section>
  );
}
