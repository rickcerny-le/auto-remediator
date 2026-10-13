import path from 'node:path'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// The AppHost's WithReference(api) injects the API's service-discovery URLs. The dev server
// proxies /api to it so the browser only ever talks to this origin, exactly like nginx when deployed.
const apiTarget =
  process.env.services__api__https__0 ?? process.env.services__api__http__0 ?? 'http://localhost:5000'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  server: {
    proxy: {
      // The local API uses the ASP.NET Core dev certificate, which Node does not trust.
      '/api': { target: apiTarget, changeOrigin: true, secure: false },
    },
  },
})
