// Rasterize the editable SVG and package multiple resolutions into a Windows ICO.
// Run from desktop/tools: npm ci && npm run generate:icon
import { chromium } from '@playwright/test';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const assets = new URL('../AntField.Desktop/Assets/', import.meta.url);
const svg = readFileSync(new URL('ant-icon.svg', assets), 'utf8');
const edge = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const browser = await chromium.launch({ headless: true, ...(existsSync(edge) ? { executablePath: edge } : {}) });
try {
  const page = await browser.newPage();
  const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
  const frames = [];
  for (const size of sizes) {
    const png = await page.evaluate(async ({ svg, size }) => {
      const image = new Image();
      image.src = `data:image/svg+xml;base64,${btoa(unescape(encodeURIComponent(svg)))}`;
      await image.decode();
      const canvas = document.createElement('canvas');
      canvas.width = canvas.height = size;
      canvas.getContext('2d').drawImage(image, 0, 0, size, size);
      return canvas.toDataURL('image/png').split(',')[1];
    }, { svg, size });
    frames.push(Buffer.from(png, 'base64'));
  }
  const directory = Buffer.alloc(6 + sizes.length * 16);
  directory.writeUInt16LE(1, 2);
  directory.writeUInt16LE(sizes.length, 4);
  let offset = directory.length;
  sizes.forEach((size, index) => {
    const entry = 6 + index * 16;
    directory[entry] = directory[entry + 1] = size === 256 ? 0 : size;
    directory.writeUInt16LE(1, entry + 4);
    directory.writeUInt16LE(32, entry + 6);
    directory.writeUInt32LE(frames[index].length, entry + 8);
    directory.writeUInt32LE(offset, entry + 12);
    offset += frames[index].length;
  });
  writeFileSync(new URL('ant-icon.ico', assets), Buffer.concat([directory, ...frames]));
  writeFileSync(new URL('ant-icon.png', assets), frames.at(-1));
  console.log(`Generated ${fileURLToPath(new URL('ant-icon.ico', assets))} (${sizes.join(', ')} px).`);
} finally {
  await browser.close();
}
