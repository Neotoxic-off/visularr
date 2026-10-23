"use client";

import { useSearchParams } from "next/navigation";
import { Suspense } from "react";
import type { JSX } from "react";
import { itemId } from "@/lib/api";
import { Player } from "@/components/Player";
import { Centered } from "@/components/AuthGate";
import { Empty } from "@/components/ui";

// Static export has no dynamic segments, so the item comes from ?id=, a playlist to play through from ?list=; ?t= starts at that second (0 = over) instead of resuming.
function Play(): JSX.Element {
  const params: URLSearchParams = useSearchParams();
  const id: string | null = itemId(params.get("id"));
  const t: string | null = params.get("t");
  const start: number | null = t !== null && /^\d+(\.\d+)?$/.test(t) ? Number(t) : null;
  if (!id) return <Centered><Empty icon="search" title="This link does not point to a video" /></Centered>;
  return <Player key={id} id={id} start={start} list={itemId(params.get("list"))} />;
}

export default function PlayPage(): JSX.Element {
  return <Suspense><Play /></Suspense>;
}
