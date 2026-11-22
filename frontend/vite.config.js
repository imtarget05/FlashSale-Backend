import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'VITE_')
  if (['preview', 'production'].includes(env.VITE_ENV)) {
    const apiUrl = env.VITE_API_URL
    if (!apiUrl || !apiUrl.startsWith('https://') || new URL(apiUrl).hostname === 'localhost') {
      throw new Error('Deployed frontend requires VITE_API_URL at a non-local HTTPS origin')
    }
  }
  return {
  plugins: [react()],
  server: {
    host: '0.0.0.0',
    port: 4173,
  },
  preview: {
    host: '0.0.0.0',
    port: 4173,
  },
  }
})
