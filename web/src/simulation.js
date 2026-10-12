// World units are illustrative, not a species-specific biological calibration.
export const WORLD_RADIUS = 42;
export const GRID_SIZE = 96;
const CELL = (WORLD_RADIUS * 2) / GRID_SIZE;
export const wrapAngle = a => Math.atan2(Math.sin(a), Math.cos(a));
export function seededRandom(seed = 42) {
  return () => { seed |= 0; seed = seed + 0x6D2B79F5 | 0; let t = Math.imul(seed ^ seed >>> 15, 1 | seed); t ^= t + Math.imul(t ^ t >>> 7, 61 | t); return ((t ^ t >>> 14) >>> 0) / 4294967296; };
}
export class Colony {
  constructor(count = 64, seed = 42) { this.seed = seed; this.reset(count); }
  reset(count = this.ants.length) {
    this.random = seededRandom(this.seed); this.time = 0; this.delivered = 0; this.decay = .06;
    this.scent = new Float32Array(GRID_SIZE * GRID_SIZE);
    this.foods = []; this.ants = []; this.nextFoodId = 0;
    [[18, 12], [-19, 18], [9, -24], [-24, -12]].forEach(([x, z]) => this.addFood(x, z, 90));
    for (let i = 0; i < count; i++) {
      const angle = this.random() * Math.PI * 2, r = 1 + this.random() * 2;
      this.ants.push({ id: i, x: Math.cos(angle) * r, z: Math.sin(angle) * r, heading: angle,
        state: 'searching', phase: this.random() * Math.PI * 2, distance: 0,
        turn: 0, decision: this.random() * .2, rest: this.random() * 2, trip: 0,
        speed: 2.5 + this.random() * .8, source: null });
    }
  }
  addFood(x, z, amount = 60) {
    if (!Number.isFinite(x) || !Number.isFinite(z) || Math.hypot(x, z) > WORLD_RADIUS - 3 || Math.hypot(x, z) < 5) return null;
    const patch = { id: this.nextFoodId++, x, z, amount, initial: amount };
    this.foods.push(patch); return patch;
  }
  index(x, z) {
    const gx = Math.floor((x + WORLD_RADIUS) / CELL), gz = Math.floor((z + WORLD_RADIUS) / CELL);
    return gx < 0 || gz < 0 || gx >= GRID_SIZE || gz >= GRID_SIZE ? -1 : gz * GRID_SIZE + gx;
  }
  sample(x, z) { const i = this.index(x, z); return i < 0 ? 0 : this.scent[i]; }
  get carrying() { return this.ants.reduce((n, a) => n + (a.state === 'carrying'), 0); }
  get remaining() { return this.foods.reduce((n, f) => n + f.amount, 0); }
  step(dt) {
    if (!(dt > 0) || !Number.isFinite(dt)) return;
    // Substeps keep steering, pickup and arrival independent of playback speed.
    const steps = Math.ceil(dt / (1 / 30)), h = dt / steps;
    for (let n = 0; n < steps; n++) this.tick(h);
  }
  tick(dt) {
    this.time += dt;
    const evaporation = Math.exp(-this.decay * dt);
    for (let i = 0; i < this.scent.length; i++) this.scent[i] *= evaporation;
    for (const ant of this.ants) {
      if (ant.rest > 0) { ant.rest -= dt; continue; }
      ant.decision -= dt;
      if (ant.state === 'searching') {
        // Only a nearby patch can be sensed. No global food target is assigned.
        const near = this.foods.find(f => f.amount > 0 && Math.hypot(f.x - ant.x, f.z - ant.z) < 5.5);
        if (near && Math.hypot(near.x - ant.x, near.z - ant.z) < 1.4) {
          near.amount--; ant.state = 'carrying'; ant.source = near.id; ant.trip = 0;
          ant.heading = wrapAngle(ant.heading + Math.PI); ant.rest = .35; continue;
        }
        if (ant.decision <= 0) {
          ant.decision = .18;
          if (near) ant.turn = wrapAngle(Math.atan2(near.z - ant.z, near.x - ant.x) - ant.heading) * 3;
          else {
            const angles = [-.55, 0, .55];
            const signals = angles.map(a => this.sample(ant.x + Math.cos(ant.heading + a) * 2, ant.z + Math.sin(ant.heading + a) * 2));
            const best = signals.indexOf(Math.max(...signals));
            ant.turn = Math.max(...signals) > .15 && this.random() > .16 ? angles[best] * 4 : (this.random() - .5) * 3.8;
            // Colony-boundary exploration reduces indefinite wandering.
            if (Math.hypot(ant.x, ant.z) > 38) ant.turn = wrapAngle(Math.atan2(-ant.z, -ant.x) - ant.heading) * 3;
          }
        }
      } else {
        ant.trip += dt;
        ant.turn = wrapAngle(Math.atan2(-ant.z, -ant.x) - ant.heading) * 4;
        if (Math.hypot(ant.x, ant.z) < 2.4) {
          this.delivered++; ant.state = 'searching'; ant.source = null;
          ant.rest = .8 + this.random(); ant.heading = this.random() * Math.PI * 2; ant.turn = 0; continue;
        }
        const strength = 10 * Math.exp(-ant.trip / 35) * dt;
        const i = this.index(ant.x, ant.z);
        if (i >= 0) this.scent[i] = Math.min(25, this.scent[i] + strength);
        // A narrow surrounding footprint lets antenna sensors intercept the trail.
        for (const [dx, dz] of [[CELL, 0], [-CELL, 0], [0, CELL], [0, -CELL]]) {
          const j = this.index(ant.x + dx, ant.z + dz);
          if (j >= 0) this.scent[j] = Math.min(25, this.scent[j] + strength * .35);
        }
      }
      ant.heading = wrapAngle(ant.heading + Math.max(-3.5, Math.min(3.5, ant.turn)) * dt);
      const travel = ant.speed * (ant.state === 'carrying' ? .78 : 1) * dt;
      ant.x += Math.cos(ant.heading) * travel; ant.z += Math.sin(ant.heading) * travel; ant.distance += travel;
      const radius = Math.hypot(ant.x, ant.z);
      if (radius > WORLD_RADIUS - 1) { ant.x *= (WORLD_RADIUS - 1) / radius; ant.z *= (WORLD_RADIUS - 1) / radius; ant.heading = Math.atan2(-ant.z, -ant.x); }
    }
  }
}
