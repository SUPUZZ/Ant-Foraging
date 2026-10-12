import test from 'node:test';
import assert from 'node:assert/strict';
import { Colony, WORLD_RADIUS } from '../src/simulation.js';

test('ants discover finite food, carry it home, and conserve all food', () => {
  const colony = new Colony(64);
  const total = colony.remaining;
  let carryingSeen = false;
  for (let i = 0; i < 1800; i++) {
    colony.step(1 / 15);
    carryingSeen ||= colony.carrying > 0;
    assert.equal(colony.remaining + colony.carrying + colony.delivered, total);
    for (const ant of colony.ants) assert.ok(Number.isFinite(ant.x) && Math.hypot(ant.x, ant.z) <= WORLD_RADIUS);
  }
  assert.ok(carryingSeen);
  assert.ok(colony.delivered > 20, `delivered ${colony.delivered}`);
  assert.ok(colony.scent.some(v => v > 0));
});
test('local sensing does not teleport food; pickup requires proximity', () => {
  const c = new Colony(1);
  c.foods = []; c.addFood(20, 0, 1);
  Object.assign(c.ants[0], { x: 20, z: 0, rest: 0, heading: 0 });
  c.step(.02); assert.equal(c.carrying, 1); assert.equal(c.remaining, 0); assert.equal(c.delivered, 0);
  c.step(15); assert.equal(c.delivered, 1); assert.equal(c.carrying, 0);
});
test('pheromones decay exponentially even when no ants are present', () => {
  const c = new Colony(0); c.scent.fill(10); c.step(10);
  assert.ok(Math.abs(c.scent[0] - 10 * Math.exp(-.6)) < .001);
});
test('restart clears time and trails and yields the same seeded colony', () => {
  const c = new Colony(12); const original = structuredClone(c.ants);
  c.step(40); c.reset(12);
  assert.deepEqual(c.ants, original); assert.equal(c.delivered, 0); assert.equal(c.time, 0);
  assert.equal(c.remaining, 360); assert.ok(c.scent.every(v => v === 0));
});
test('food placement rejects the nest, boundaries, and invalid coordinates', () => {
  const c = new Colony(12);
  assert.equal(c.addFood(0, 0), null); assert.equal(c.addFood(100, 100), null); assert.equal(c.addFood(NaN, 5), null);
  assert.ok(c.addFood(10, 10)); assert.equal(c.remaining, 420);
});
test('playback substeps preserve behavior for the same elapsed time', () => {
  const a = new Colony(12), b = new Colony(12);
  for (let i = 0; i < 1200; i++) a.step(1 / 30);
  for (let i = 0; i < 300; i++) b.step(4 / 30);
  assert.equal(a.delivered, b.delivered); assert.equal(a.remaining, b.remaining);
  for (let i = 0; i < a.ants.length; i++) assert.ok(Math.hypot(a.ants[i].x - b.ants[i].x, a.ants[i].z - b.ants[i].z) < 1e-6);
});
