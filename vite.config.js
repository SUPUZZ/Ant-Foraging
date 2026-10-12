import { defineConfig } from 'vite';
export default defineConfig({
  base: './',
  build: { rollupOptions: {
    input: { main: 'index.html', colony: 'ant-foraging-3d.html', classic: 'ant-foraging.html' },
    output: { manualChunks: { three: ['three'] } }
  } }
});
