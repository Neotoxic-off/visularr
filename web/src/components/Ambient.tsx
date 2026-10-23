"use client";

import { useState } from "react";
import type { JSX } from "react";

const fallback: string = "bg-[radial-gradient(60%_40%_at_50%_-5%,rgb(255_255_255/.06),transparent)]";

// The page's artwork as a faint, desaturated glow behind the chrome; near-black with a soft top light when there is none.
export function Ambient({ src }: { src: string | null }): JSX.Element {
  const [layers, setLayers] = useState<readonly (string | null)[]>([src]);
  // Keeps the previous backdrop underneath while the new one fades in.
  if (layers.at(-1) !== src) setLayers([layers.at(-1) ?? null, src]);
  return (
    <div aria-hidden="true" className="pointer-events-none fixed inset-0 -z-10 overflow-hidden bg-canvas">
      {layers.map((layer: string | null, i: number) => (
        <div key={`${layer}${i}`} className={`absolute inset-0 ${i > 0 ? "animate-[fade_600ms_ease-out_both]" : ""}`}>
          {layer
            // eslint-disable-next-line @next/next/no-img-element
            ? <img src={layer} alt="" className="h-full w-full scale-125 object-cover opacity-[.18] blur-[90px] saturate-50" />
            : <div className={`h-full w-full ${fallback}`} />}
        </div>
      ))}
      <div className="absolute inset-0 bg-[radial-gradient(120%_90%_at_50%_0%,transparent,rgb(0_0_0/.85))]" />
      <div className="absolute inset-0 bg-canvas/80" />
    </div>
  );
}
