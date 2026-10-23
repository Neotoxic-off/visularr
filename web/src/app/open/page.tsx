"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import type { JSX } from "react";
import * as api from "@/lib/endpoints";
import { useSession } from "@/components/AuthGate";
import { ButtonLink, Empty, Spinner } from "@/components/ui";

// Deep link from modularr: /web/open/?tmdb=603 or ?tvdb=81189 (optionally &type=movie|series) lands on the title's page.
function Open(): JSX.Element {
  const params: URLSearchParams = new URLSearchParams(useSearchParams().toString());
  const router: ReturnType<typeof useRouter> = useRouter();
  const { user } = useSession();
  const [missing, setMissing] = useState(false);
  const tmdb: string | null = /^\d+$/.test(params.get("tmdb") ?? "") ? params.get("tmdb") : null;
  const tvdb: string | null = /^\d+$/.test(params.get("tvdb") ?? "") ? params.get("tvdb") : null;
  const series: boolean = params.get("type") === "series" || (!tmdb && !!tvdb);
  const kinds: string[] = series ? ["series", "movie"] : ["movie", "series"];
  const keys: string = kinds.flatMap((k: string) => [tmdb ? `${k}|tmdb:${tmdb}` : "", tvdb ? `${k}|tvdb:${tvdb}` : ""]).filter(Boolean).join(",");

  useEffect(() => {
    if (!keys) return;
    // Fresh: the link usually follows an import that happened after this tab loaded the index.
    api.providerIndex(user.Id, true)
      .then((index: Map<string, string>) => {
        const id: string | undefined = keys.split(",").map((k: string) => index.get(k)).find(Boolean);
        if (id) router.replace(`/item/?id=${id}`);
        else setMissing(true);
      })
      .catch((): void => setMissing(true));
  }, [keys, user.Id, router]);

  if (!keys) return <Empty icon="search" title="This link does not point to a title" />;
  if (!missing) return <div className="grid place-items-center py-24"><Spinner className="text-ink-2" /></div>;
  return (
    <Empty icon="searchX" title="Not in visularr yet">
      <span className="block">It shows up here once modularr has downloaded and imported it.</span>
      <ButtonLink href="/" className="mt-4" icon="home">Go home</ButtonLink>
    </Empty>
  );
}

export default function OpenPage(): JSX.Element {
  return <Suspense><Open /></Suspense>;
}
