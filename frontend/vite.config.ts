import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: true,
    // Pin these regardless of a developer's .env.local (e.g. VITE_API_URL pointed at a
    // real backend for manual dev-server testing) — tests must always exercise the MSW
    // mocks via the default relative /api path, never a live server.
    env: {
      VITE_API_URL: '/api',
      VITE_API_MOCKING: 'enabled',
    },
  },
})
