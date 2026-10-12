import { chromium } from '@playwright/test';
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
const edge = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const browser = await chromium.launch({ headless: true, ...(existsSync(edge) ? { executablePath: edge } : {}), args: ['--enable-webgl', '--ignore-gpu-blocklist'] });
try {
  const page = await browser.newPage();
  await page.goto(`${process.env.ANT_WEB_URL || 'http://127.0.0.1:5173'}/desktop/tools/ant-atlas.html`);
  await page.waitForFunction(() => window.antAtlas);
  const data = await page.evaluate(() => window.antAtlas);
  const output = fileURLToPath(new URL('../AntField.Desktop/Assets/ant-atlas.png', import.meta.url));
  mkdirSync(fileURLToPath(new URL('../AntField.Desktop/Assets/', import.meta.url)), { recursive: true });
  writeFileSync(output, Buffer.from(data.split(',')[1], 'base64'));
  console.log('Generated 24 walking poses plus a resting pose, with and without carried food.');
} finally { await browser.close(); }
