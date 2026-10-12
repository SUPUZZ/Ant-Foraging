import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { Colony, GRID_SIZE, WORLD_RADIUS } from './simulation.js';
import { createAnt } from './ant-model.js';

const $ = id => document.getElementById(id);
const viewport = $('viewport');
let renderer;
try {
  renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
} catch (error) {
  $('loading').className = 'error';
  $('loading').textContent = '无法启动 3D 场景。请启用浏览器硬件加速，或点击右上角进入经典 2D 版本。';
  throw error;
}
renderer.setPixelRatio(Math.min(devicePixelRatio, 1.6));
renderer.shadowMap.enabled = true; renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping; renderer.toneMappingExposure = 1.2;
viewport.appendChild(renderer.domElement);
const scene = new THREE.Scene(); scene.background = new THREE.Color(0x303b2e); scene.fog = new THREE.Fog(0x303b2e, 85, 170);
const camera = new THREE.PerspectiveCamera(43, 1, .1, 250);
camera.position.set(51, 58, 64);
const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true; controls.dampingFactor = .08; controls.target.set(0, 0, 0);
controls.minDistance = 4; controls.maxDistance = 115; controls.maxPolarAngle = Math.PI * .48;
scene.add(new THREE.HemisphereLight(0xe9f1d4, 0x333221, 2.4));
const sun = new THREE.DirectionalLight(0xffe2b8, 3.1); sun.position.set(-28, 52, 22); sun.castShadow = true;
sun.shadow.mapSize.set(2048, 2048); Object.assign(sun.shadow.camera, { left: -47, right: 47, top: 47, bottom: -47, near: 1, far: 125 });
sun.shadow.bias = -.0005; sun.shadow.normalBias = .035; scene.add(sun);
const fill = new THREE.DirectionalLight(0xc5dec2, .7); fill.position.set(25, 14, -20); scene.add(fill);

const colony = new Colony();
function soilTexture() {
  const canvas = document.createElement('canvas'); canvas.width = canvas.height = 512;
  const ctx = canvas.getContext('2d'); ctx.fillStyle = '#77674b'; ctx.fillRect(0, 0, 512, 512);
  let seed = 71;
  const rnd = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
  for (let i = 0; i < 27000; i++) {
    const shade = Math.floor(50 + rnd() * 100); ctx.fillStyle = `rgba(${shade + 15},${shade + 3},${shade * .7},${.12 + rnd() * .3})`;
    ctx.beginPath(); ctx.arc(rnd() * 512, rnd() * 512, rnd() * 1.8 + .3, 0, Math.PI * 2); ctx.fill();
  }
  const texture = new THREE.CanvasTexture(canvas); texture.colorSpace = THREE.SRGBColorSpace;
  texture.wrapS = texture.wrapT = THREE.RepeatWrapping; texture.repeat.set(5, 5); texture.anisotropy = renderer.capabilities.getMaxAnisotropy(); return texture;
}
const ground = new THREE.Mesh(new THREE.CircleGeometry(WORLD_RADIUS + 1, 96), new THREE.MeshStandardMaterial({ map: soilTexture(), roughness: 1, color: 0xb2b08f }));
ground.rotation.x = -Math.PI / 2; ground.receiveShadow = true; scene.add(ground);
const base = new THREE.Mesh(new THREE.CylinderGeometry(43, 43.4, 2.2, 96), new THREE.MeshStandardMaterial({ color: 0x4b4030, roughness: 1 }));
base.position.y = -1.15; base.receiveShadow = true; scene.add(base);
const backdrop = new THREE.Mesh(new THREE.PlaneGeometry(400, 400), new THREE.MeshStandardMaterial({ color: 0x303b2d, roughness: 1 }));
backdrop.rotation.x = -Math.PI / 2; backdrop.position.y = -2.3; backdrop.receiveShadow = true; scene.add(backdrop);

const nestGroup = new THREE.Group(); scene.add(nestGroup);
const nestMaterial = new THREE.MeshStandardMaterial({ color: 0x726043, roughness: 1 });
const rim = new THREE.Mesh(new THREE.TorusGeometry(2.25, .66, 10, 36), nestMaterial); rim.rotation.x = Math.PI / 2; rim.position.y = .18; rim.scale.z = .45; rim.receiveShadow = true; nestGroup.add(rim);
const entrance = new THREE.Mesh(new THREE.CircleGeometry(1.77, 40), new THREE.MeshStandardMaterial({ color: 0x1b1a12, roughness: 1 })); entrance.rotation.x = -Math.PI / 2; entrance.position.y = .025; nestGroup.add(entrance);

// Instancing keeps the environmental detail inexpensive as the colony grows.
const dummy = new THREE.Object3D();
function scatter(geometry, material, count, place) {
  const mesh = new THREE.InstancedMesh(geometry, material, count);
  for (let i = 0; i < count; i++) { place(dummy, i); dummy.updateMatrix(); mesh.setMatrixAt(i, dummy.matrix); }
  mesh.castShadow = true; mesh.receiveShadow = true; scene.add(mesh); return mesh;
}
scatter(new THREE.DodecahedronGeometry(1, 0), new THREE.MeshStandardMaterial({ color: 0x777666, roughness: 1 }), 160, (o, i) => {
  const angle = i * 2.39996, r = 7 + Math.sqrt((i * 17 % 160) / 160) * 34;
  o.position.set(Math.cos(angle) * r, .07, Math.sin(angle) * r);
  o.rotation.set(i, i * .8, i * .3); const s = .1 + (i % 9) * .055; o.scale.set(s * 1.4, s * .6, s);
});
const leafGeometry = new THREE.SphereGeometry(1, 8, 5);
scatter(leafGeometry, new THREE.MeshStandardMaterial({ color: 0x647444, roughness: .9 }), 85, (o, i) => {
  const angle = i * 2.39996, r = 29 + (i * 7 % 13); o.position.set(Math.cos(angle) * r, .1, Math.sin(angle) * r);
  o.rotation.set(0, i * .77, .1); o.scale.set(.75 + i % 3 * .3, .045, .24);
});
scatter(new THREE.ConeGeometry(.14, 2.5, 3), new THREE.MeshStandardMaterial({ color: 0x6b8150, roughness: 1 }), 240, (o, i) => {
  const cluster = Math.floor(i / 8), angle = cluster * 2.39996, r = 37.5 + cluster % 4;
  o.position.set(Math.cos(angle) * r + Math.sin(i * 4) * .9, .6, Math.sin(angle) * r + Math.cos(i * 3) * .9);
  o.rotation.set(Math.sin(i) * .2, i, Math.cos(i) * .25); o.scale.set(1, .4 + (i % 5) * .2, 1);
});

function labelSprite(text, color = '#e9e3ca') {
  const c = document.createElement('canvas'); c.width = 256; c.height = 64;
  const ctx = c.getContext('2d'); ctx.fillStyle = '#19241dcc'; ctx.beginPath(); ctx.roundRect(0, 0, 256, 64, 12); ctx.fill();
  ctx.font = '24px "Microsoft YaHei", sans-serif'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillStyle = color; ctx.fillText(text, 128, 32);
  const texture = new THREE.CanvasTexture(c), material = new THREE.SpriteMaterial({ map: texture, depthTest: false, transparent: true });
  const sprite = new THREE.Sprite(material); sprite.scale.set(6, 1.5, 1); sprite.renderOrder = 5; return sprite;
}
const nestLabel = labelSprite('巢穴 · HOME'); nestLabel.position.set(0, 3.2, 0); scene.add(nestLabel);
const foodViews = new Map();
const crumbGeometry = new THREE.DodecahedronGeometry(.28, 0);
const crumbMaterial = new THREE.MeshStandardMaterial({ color: 0xb7cb73, roughness: .95 });
function syncFood() {
  for (const food of colony.foods) {
    let view = foodViews.get(food.id);
    if (!view) {
      const mesh = new THREE.InstancedMesh(crumbGeometry, crumbMaterial, food.initial); mesh.castShadow = true; mesh.receiveShadow = true;
      for (let i = 0; i < food.initial; i++) {
        const angle = i * 2.39996, r = Math.sqrt(i / food.initial) * 1.2;
        dummy.position.set(food.x + Math.cos(angle) * r, .18 + (1 - r / 1.2) * .65, food.z + Math.sin(angle) * r);
        dummy.rotation.set(i, i * 2, i * .7); dummy.scale.setScalar(.65 + (i % 4) * .15); dummy.updateMatrix(); mesh.setMatrixAt(i, dummy.matrix);
      }
      const label = labelSprite('食物 · FOOD', '#d9eaba'); label.position.set(food.x, 3, food.z);
      scene.add(mesh, label); view = { mesh, label }; foodViews.set(food.id, view);
    }
    view.mesh.count = food.amount; view.label.visible = food.amount > 0;
  }
}
const pixels = new Uint8Array(GRID_SIZE * GRID_SIZE * 4);
const scentTexture = new THREE.DataTexture(pixels, GRID_SIZE, GRID_SIZE); scentTexture.magFilter = THREE.LinearFilter; scentTexture.minFilter = THREE.LinearFilter;
const scentPlane = new THREE.Mesh(new THREE.PlaneGeometry(WORLD_RADIUS * 2, WORLD_RADIUS * 2), new THREE.MeshBasicMaterial({ map: scentTexture, transparent: true, depthWrite: false, toneMapped: false }));
scentPlane.rotation.x = -Math.PI / 2; scentPlane.position.y = .055; scentPlane.renderOrder = 1; scene.add(scentPlane);
function syncScent() {
  for (let z = 0; z < GRID_SIZE; z++) for (let x = 0; x < GRID_SIZE; x++) {
    const i = z * GRID_SIZE + x, p = ((GRID_SIZE - 1 - z) * GRID_SIZE + x) * 4;
    pixels[p] = 100; pixels[p + 1] = 219; pixels[p + 2] = 167;
    pixels[p + 3] = Math.min(165, Math.sqrt(colony.scent[i]) * 40);
  }
  scentTexture.needsUpdate = true;
}

let antViews = [];
const colonyRender = new THREE.Group(); scene.add(colonyRender);
let batches = [];
const instanceMatrix = new THREE.Matrix4(), hiddenMatrix = new THREE.Matrix4().makeScale(0, 0, 0);
const pickGeometry = new THREE.SphereGeometry(1.15, 6, 4);
const pickMaterial = new THREE.MeshBasicMaterial({ visible: false });
function makeAnts() {
  for (const view of antViews) view.dispose();
  for (const batch of batches) { colonyRender.remove(batch.mesh); batch.mesh.dispose(); }
  antViews = colony.ants.map((ant, index) => {
    const view = createAnt(); const hit = new THREE.Mesh(pickGeometry, pickMaterial); hit.position.y = .4; hit.userData.antIndex = index; view.root.add(hit); view.hit = hit;
    return view;
  });
  const groups = new Map();
  for (const view of antViews) view.root.traverse(mesh => {
    if (!mesh.isMesh || !mesh.material.visible) return;
    const key = `${mesh.geometry.uuid}/${mesh.material.uuid}`;
    if (!groups.has(key)) groups.set(key, { geometry: mesh.geometry, material: mesh.material, parts: [] });
    const group = groups.get(key);
    if (mesh.isInstancedMesh) for (let i = 0; i < mesh.count; i++) group.parts.push({ source: mesh, index: i });
    else group.parts.push({ source: mesh, index: null });
  });
  batches = [...groups.values()].map(group => {
    const mesh = new THREE.InstancedMesh(group.geometry, group.material, group.parts.length);
    mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage); mesh.castShadow = true; mesh.frustumCulled = false;
    colonyRender.add(mesh); return { mesh, parts: group.parts };
  });
}
function syncAntInstances() {
  for (const view of antViews) view.root.updateMatrixWorld(true);
  for (const batch of batches) {
    batch.parts.forEach(({ source, index }, i) => {
      if (!source.visible) batch.mesh.setMatrixAt(i, hiddenMatrix);
      else if (index === null) batch.mesh.setMatrixAt(i, source.matrixWorld);
      else { source.getMatrixAt(index, instanceMatrix); instanceMatrix.premultiply(source.matrixWorld); batch.mesh.setMatrixAt(i, instanceMatrix); }
    });
    batch.mesh.instanceMatrix.needsUpdate = true;
  }
}
const selectedRing = new THREE.Mesh(new THREE.RingGeometry(1.6, 1.68, 48), new THREE.MeshBasicMaterial({ color: 0xe0e9ac, transparent: true, opacity: .65, depthWrite: false }));
selectedRing.rotation.x = -Math.PI / 2; selectedRing.position.y = .08; scene.add(selectedRing);
const showcase = createAnt(); showcase.root.position.set(110, 0, 0); showcase.root.scale.setScalar(4); scene.add(showcase.root);
const podium = new THREE.Mesh(new THREE.CylinderGeometry(10, 10.3, 1, 64), new THREE.MeshStandardMaterial({ color: 0x777965, roughness: .95 })); podium.position.set(110, -.5, 0); podium.receiveShadow = true; scene.add(podium);
const modelLight = new THREE.PointLight(0xffefd0, 150, 45, 2); modelLight.position.set(104, 14, 9); modelLight.castShadow = true; modelLight.shadow.mapSize.set(1024, 1024); modelLight.shadow.bias = -.001; scene.add(modelLight);

let paused = false, speed = 1, viewMode = 'overview', selected = 0, placing = false;
function setMode(mode) {
  viewMode = mode;
  showcase.root.visible = mode === 'model'; podium.visible = mode === 'model'; modelLight.visible = mode === 'model';
  colonyRender.visible = mode !== 'model';
  $('export-model').hidden = mode !== 'model';
  $('export-model').style.cssText = 'width:100%;margin-top:10px';
  document.querySelector('.scene-label').innerHTML = mode === 'model' ? '<span class="live-dot"></span> 工蚁形态观察 <small>头部 / 胸部 / 腹柄 / 腹部</small>' : '<span class="live-dot"></span> 林地微观生态 <small>3D OBSERVATORY</small>';
  for (const name of ['overview', 'follow', 'model']) $(name).classList.toggle('active', mode === name);
  if (mode === 'overview') { camera.position.set(51, 58, 64); controls.target.set(0, 0, 0); }
  if (mode === 'follow') { const ant = colony.ants[selected]; controls.target.set(ant.x, .5, ant.z); camera.position.set(ant.x + 9, 7, ant.z + 11); }
  if (mode === 'model') { controls.target.set(110, 2, 0); camera.position.set(120, 9, 17); }
  controls.update();
}
function reset() {
  const decay = Number($('decay').value) / 100;
  colony.reset(Number($('count').value)); colony.decay = decay; selected = 0;
  for (const { mesh, label } of foodViews.values()) { scene.remove(mesh, label); mesh.dispose(); label.material.map.dispose(); label.material.dispose(); }
  foodViews.clear(); makeAnts(); syncFood(); syncScent(); setMode(viewMode); syncStats();
}
$('pause').onclick = () => { paused = !paused; $('pause').textContent = paused ? '继续' : '暂停'; $('status').textContent = paused ? '已暂停' : '运行中'; };
$('export-model').onclick = async () => {
  const button = $('export-model'); button.disabled = true; button.textContent = '正在导出…';
  try {
    const { GLTFExporter } = await import('three/addons/exporters/GLTFExporter.js');
    const model = createAnt(); model.root.name = 'Worker ant';
    const data = await new GLTFExporter().parseAsync(model.root, { binary: true });
    model.dispose();
    const url = URL.createObjectURL(new Blob([data], { type: 'model/gltf-binary' }));
    const link = document.createElement('a'); link.href = url; link.download = 'worker-ant.glb'; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  } catch (error) { $('hint').textContent = '模型导出失败，请重试。'; console.error(error); }
  finally { button.disabled = false; button.textContent = '导出蚂蚁模型 .glb'; }
};
$('reset').onclick = reset;
$('count').oninput = () => { $('count-value').textContent = $('count').value; };
$('count').onchange = reset;
$('speed').oninput = () => { speed = Number($('speed').value); $('speed-value').textContent = `${speed}×`; };
$('decay').oninput = () => { colony.decay = Number($('decay').value) / 100; $('decay-value').textContent = `${$('decay').value}%`; };
$('trails').onchange = () => { scentPlane.visible = $('trails').checked; };
for (const mode of ['overview', 'follow', 'model']) $(mode).onclick = () => { setPlacing(false); setMode(mode); };
function setPlacing(value) {
  placing = value; $('place-food').classList.toggle('active', value); controls.enableRotate = !value;
  renderer.domElement.style.cursor = value ? 'crosshair' : 'grab';
  $('hint').innerHTML = value ? '点击地面放置食物，避开巢口和场地边缘。<br>再次点击按钮或按 Esc 退出。' : '拖动旋转 · 滚轮缩放 · 点击蚂蚁选择<br>触屏：单指旋转，双指缩放';
}
$('place-food').onclick = () => { if (viewMode === 'model') setMode('overview'); setPlacing(!placing); };
window.addEventListener('keydown', e => { if (e.key === 'Escape') setPlacing(false); });
const raycaster = new THREE.Raycaster(), mouse = new THREE.Vector2();
let down = null;
renderer.domElement.addEventListener('pointerdown', e => { down = { x: e.clientX, y: e.clientY }; });
renderer.domElement.addEventListener('pointerup', e => {
  if (!down || Math.hypot(e.clientX - down.x, e.clientY - down.y) > 6) return;
  down = null;
  const rect = renderer.domElement.getBoundingClientRect(); mouse.set((e.clientX - rect.left) / rect.width * 2 - 1, -(e.clientY - rect.top) / rect.height * 2 + 1);
  raycaster.setFromCamera(mouse, camera);
  if (placing) {
    const hit = raycaster.intersectObject(ground)[0];
    if (hit && colony.foods.filter(f => f.amount > 0).length < 30 && colony.addFood(hit.point.x, hit.point.z)) { syncFood(); syncStats(); }
    else $('hint').textContent = '请选择距离巢口 5 单位外的地面；场地最多容纳 30 个有效食物点。';
  } else if (viewMode !== 'model') {
    const hit = raycaster.intersectObjects(antViews.map(v => v.hit), false)[0];
    if (hit) { selected = hit.object.userData.antIndex; syncStats(); }
  }
});
renderer.domElement.addEventListener('pointercancel', () => { down = null; });
function resize() { const { width, height } = viewport.getBoundingClientRect(); camera.aspect = width / height; camera.updateProjectionMatrix(); renderer.setSize(width, height); }
new ResizeObserver(resize).observe(viewport); resize();
function syncStats() {
  $('delivered').textContent = colony.delivered; $('carrying').textContent = colony.carrying; $('food').textContent = colony.remaining;
  const min = Math.floor(colony.time / 60), sec = Math.floor(colony.time % 60); $('elapsed').textContent = `${String(min).padStart(2, '0')}:${String(sec).padStart(2, '0')}`;
  const ant = colony.ants[selected];
  $('selected').replaceChildren(document.createTextNode(`工蚁 #${String(ant.id + 1).padStart(2, '0')}`));
  const state = document.createElement('span'); state.textContent = ant.rest > 0 ? (ant.state === 'carrying' ? '夹取食物' : '巢口休整') : ant.state === 'carrying' ? '搬运归巢' : '探索中'; $('selected').appendChild(state);
}
makeAnts(); syncFood(); syncStats();
const clock = new THREE.Clock(); let lastStats = -1;
function animate() {
  const dt = Math.min(clock.getDelta(), .06);
  if (!paused) colony.step(dt * speed);
  colony.ants.forEach((ant, i) => {
    const view = antViews[i]; view.root.position.set(ant.x, 0, ant.z); view.root.rotation.y = -ant.heading;
    view.animate(ant.distance, colony.time + ant.phase, ant.state === 'carrying', ant.rest <= 0);
  });
  syncAntInstances();
  const ant = colony.ants[selected]; selectedRing.position.set(ant.x, .08, ant.z);
  showcase.animate(ant.distance, colony.time + ant.phase, ant.state === 'carrying', ant.rest <= 0);
  if (viewMode === 'follow') {
    const target = new THREE.Vector3(ant.x, .5, ant.z), delta = target.clone().sub(controls.target);
    camera.position.add(delta); controls.target.copy(target);
  }
  if (Math.abs(colony.time - lastStats) > .12 || lastStats < 0) { syncStats(); syncFood(); syncScent(); lastStats = colony.time; }
  controls.update(); renderer.render(scene, camera);
}
setMode('overview'); renderer.setAnimationLoop(animate); $('loading').remove();
// Small read-only diagnostic snapshot for browser verification.
window.antObservatory = { snapshot: () => ({ time: colony.time, delivered: colony.delivered, carrying: colony.carrying, remaining: colony.remaining, count: colony.ants.length, viewMode, paused, foodPatches: colony.foods.length, drawCalls: renderer.info.render.calls }) };
