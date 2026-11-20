import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

const apiTarget =
  process.env.services__api__https__0 ?? process.env.services__api__http__0 ?? 'http://localhost:5180'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  build: {
    outDir: '../AppTemplate.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true, secure: false },
      '/mcp': { target: apiTarget, changeOrigin: true, secure: false },
    },
  },
})
