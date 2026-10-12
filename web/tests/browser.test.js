import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';

const edge = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const browser = await chromium.launch({
  headless: true,
  ...(existsSync(edge) ? { executablePath: edge } : {}),
  args: ['--enable-webgl', '--ignore-gpu-blocklist']
});
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
const errors = [];
page.on('pageerror', error => errors.push(error.message));
page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
const snapshot = () => page.evaluate(() => window.antObservatory.snapshot());
try {
  await page.goto(process.env.ANT_TEST_URL || 'http://127.0.0.1:5173/');
  await page.waitForFunction(() => window.antObservatory?.snapshot().time > .5);
  assert.equal((await snapshot()).count, 64);
  await page.locator('#pause').click();
  const pausedTime = (await snapshot()).time;
  await page.locator('#model').click();
  assert.equal((await snapshot()).viewMode, 'model');
  assert.equal((await snapshot()).time, pausedTime);
  await page.screenshot({ path: 'assets/ant-3d-model.png' });
  const downloadPromise = page.waitForEvent('download');
  await page.locator('#export-model').click();
  const download = await downloadPromise;
  await download.saveAs('assets/worker-ant.glb');
  const modelBytes = await readFile(await download.path());
  assert.equal(modelBytes.toString('ascii', 0, 4), 'glTF');
  assert.equal(modelBytes.readUInt32LE(4), 2);
  assert.ok(modelBytes.length > 1000);
  await page.locator('#follow').click(); assert.equal((await snapshot()).viewMode, 'follow');
  await page.locator('#overview').click();
  await page.locator('#trails').uncheck(); await page.locator('#trails').check();
  await page.locator('#place-food').click();
  const canvas = await page.locator('canvas').boundingBox();
  await page.mouse.click(canvas.x + canvas.width * .62, canvas.y + canvas.height * .55);
  assert.equal((await snapshot()).foodPatches, 5);
  assert.equal((await snapshot()).remaining, 420);
  await page.keyboard.press('Escape');
  await page.locator('#reset').click();
  assert.equal((await snapshot()).remaining, 360); assert.equal((await snapshot()).time, 0);
  await page.locator('#count').evaluate(el => { el.value = '120'; el.dispatchEvent(new Event('input')); el.dispatchEvent(new Event('change')); });
  assert.equal((await snapshot()).count, 120);
  await page.locator('#count').evaluate(el => { el.value = '64'; el.dispatchEvent(new Event('input')); el.dispatchEvent(new Event('change')); });
  await page.locator('#speed').evaluate(el => { el.value = '4'; el.dispatchEvent(new Event('input')); });
  await page.locator('#pause').click();
  await page.waitForFunction(() => window.antObservatory.snapshot().delivered > 0, null, { timeout: 60000 });
  const state = await snapshot(); assert.equal(state.delivered + state.carrying + state.remaining, 360);
  await page.locator('#pause').click();
  await page.screenshot({ path: 'assets/ant-3d-colony.png' });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: 'assets/ant-3d-mobile.png', fullPage: true });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  assert.deepEqual(errors, []);
  console.log('Browser checks passed:', state);
} finally { await browser.close(); }
