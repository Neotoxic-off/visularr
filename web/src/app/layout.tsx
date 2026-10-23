import type { Metadata, Viewport } from "next";
import { Plus_Jakarta_Sans } from "next/font/google";
import type { NextFontWithVariable } from "next/dist/compiled/@next/font/dist/types";
import type { JSX, ReactNode } from "react";
import { AuthGate } from "@/components/AuthGate";
import { Shell } from "@/components/Shell";
import "./globals.css";

const jakarta: NextFontWithVariable = Plus_Jakarta_Sans({ variable: "--font-jakarta", subsets: ["latin"] });

export const metadata: Metadata = {
  title: "visularr",
  description: "Your movies and shows, ready to play.",
  // Stream URLs carry the token; never leak them to another origin as a Referer.
  referrer: "same-origin",
};

export const viewport: Viewport = {
  viewportFit: "cover",
  themeColor: "#000000",
};

export default function RootLayout({ children }: { children: ReactNode }): JSX.Element {
  return (
    <html lang="en" className={`${jakarta.variable} antialiased`}>
      <body className="font-sans text-sm">
        <AuthGate>
          <Shell>{children}</Shell>
        </AuthGate>
      </body>
    </html>
  );
}
