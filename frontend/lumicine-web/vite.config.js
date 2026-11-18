import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import basicSsl from '@vitejs/plugin-basic-ssl';
import { existsSync, readFileSync } from 'node:fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const certificate = path.resolve(__dirname, '../../.local/https/lumicine-web.pem');
const privateKey = path.resolve(__dirname, '../../.local/https/lumicine-web.key');
const hasLocalCertificate = existsSync(certificate) && existsSync(privateKey);

export default defineConfig({
  plugins: hasLocalCertificate ? [react()] : [react(), basicSsl()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    https: hasLocalCertificate ? { cert: readFileSync(certificate), key: readFileSync(privateKey) } : undefined,
    proxy: {
      '/api': {
        target: 'https://localhost:7005',
        changeOrigin: true,
        secure: false,
      },
      '/hubs': {
        target: 'https://localhost:7005',
        changeOrigin: true,
        ws: true,
        secure: false,
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: true,
  },
});
