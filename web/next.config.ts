import type { NextConfig } from "next";

// Static export served by the visularr server at /web/ (--webdir); the API lives at the same origin's root.
const nextConfig: NextConfig = {
  output: "export",
  basePath: "/web",
  trailingSlash: true,
  images: { unoptimized: true },
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
