"use client";

import type { JSX } from "react";
import { useSession } from "@/components/AuthGate";
import { Account } from "@/components/settings/Account";
import { ApiKeys } from "@/components/settings/ApiKeys";
import { Dashboard } from "@/components/settings/Dashboard";
import { Libraries } from "@/components/settings/Libraries";
import { useShared } from "@/components/Shell";
import { PageHeader } from "@/components/ui";

const SECTIONS: readonly { id: string; label: string; admin: boolean }[] = [
  { id: "account", label: "Account", admin: false },
  { id: "dashboard", label: "Server", admin: true },
  { id: "libraries", label: "Libraries", admin: true },
  { id: "keys", label: "API keys", admin: true },
];

// One page of sections; members see their account only.
export default function Settings(): JSX.Element {
  const { admin } = useSession();
  const { hub } = useShared();
  return (
    <>
      <PageHeader title="Settings" />
      {admin && (
        <nav aria-label="Settings sections" className="rail sticky top-[calc(64px+env(safe-area-inset-top))] z-20 -mx-4 mb-5 flex gap-2 overflow-x-auto px-4 py-2 md:top-[88px] lg:-mx-5 lg:px-5">
          {SECTIONS.map((s: { id: string; label: string }) => (
            <a key={s.id} href={`#${s.id}`} className="glass inline-flex min-h-11 shrink-0 items-center rounded-pill px-4 text-meta font-semibold text-ink-2 hover:text-ink sm:min-h-9">{s.label}</a>
          ))}
        </nav>
      )}
      <div className="space-y-5">
        <Account hub={hub} />
        {admin && (
          <>
            <Dashboard />
            <Libraries />
            <ApiKeys />
          </>
        )}
      </div>
    </>
  );
}
