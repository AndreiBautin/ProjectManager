import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const apiProxy = {
  '/api': {
    target: 'http://localhost:5071',
    changeOrigin: true,
  },
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5174,
    strictPort: true,
    proxy: apiProxy,
  },
  preview: {
    proxy: apiProxy,
  },
})
