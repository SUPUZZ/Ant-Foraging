import * as THREE from 'three';
const sphere = new THREE.SphereGeometry(1, 24, 16);
const cylinder = new THREE.CylinderGeometry(1, 1, 1, 7);
const shell = new THREE.MeshStandardMaterial({ color: 0x382219, roughness: .46, metalness: .08 });
const abdomenMaterial = new THREE.MeshStandardMaterial({ color: 0x291b14, roughness: .4, metalness: .08 });
const jointMaterial = new THREE.MeshStandardMaterial({ color: 0x201510, roughness: .52 });
const eyeMaterial = new THREE.MeshStandardMaterial({ color: 0x080a07, roughness: .3 });
const foodMaterial = new THREE.MeshStandardMaterial({ color: 0xc3d281, roughness: .8 });
const up = new THREE.Vector3(0, 1, 0);
const geometryCache = new Map();
function sharedGeometry(key, create) {
  if (!geometryCache.has(key)) geometryCache.set(key, create());
  return geometryCache.get(key);
}
function ellipsoid(parent, material, x, y, z, sx, sy, sz) {
  const mesh = new THREE.Mesh(sphere, material); mesh.position.set(x, y, z); mesh.scale.set(sx, sy, sz); mesh.castShadow = true; parent.add(mesh); return mesh;
}
function rod(parent, radius, material = jointMaterial) {
  const mesh = new THREE.Mesh(cylinder, material); mesh.userData.radius = radius; mesh.castShadow = true; parent.add(mesh); return mesh;
}
const direction = new THREE.Vector3();
function connect(mesh, start, end) {
  direction.subVectors(end, start); mesh.position.copy(start).add(end).multiplyScalar(.5);
  mesh.scale.set(mesh.userData.radius, direction.length(), mesh.userData.radius);
  mesh.quaternion.setFromUnitVectors(up, direction.normalize());
}
// Local +X is forward. All six legs attach to the thorax, never the abdomen.
export function createAnt() {
  const root = new THREE.Group(), body = new THREE.Group(); root.add(body);
  const abdomen = ellipsoid(body, abdomenMaterial, -.91, .62, 0, .75, .41, .39);
  abdomen.rotation.z = -.12;
  // Fine gastral sutures follow the ellipsoid instead of adding separate body beads.
  for (const offset of [-.32, -.08, .16]) {
    const radius = Math.sqrt(1 - (offset / .75) ** 2);
    const seam = new THREE.Mesh(sharedGeometry(`seam-${offset}`, () => new THREE.TorusGeometry(.39 * radius, .006, 4, 32)), jointMaterial);
    seam.rotation.y = Math.PI / 2; seam.position.set(-.91 + offset, .62 - offset * .12, 0); seam.scale.x = .41 / .39; body.add(seam);
  }
  ellipsoid(body, jointMaterial, -.27, .56, 0, .16, .14, .15); // petiole / narrow waist
  ellipsoid(body, shell, .09, .62, 0, .43, .25, .25); // mesosoma
  ellipsoid(body, shell, .42, .65, 0, .22, .24, .23);
  const head = new THREE.Group(); head.position.set(.73, .66, 0); body.add(head);
  ellipsoid(head, shell, .05, .02, 0, .35, .29, .31);
  for (const side of [-1, 1]) {
    ellipsoid(head, eyeMaterial, .11, .1, side * .282, .076, .085, .046);
    const shape = new THREE.Shape();
    shape.moveTo(.26, side * .2); shape.quadraticCurveTo(.51, side * .25, .6, side * .035);
    shape.lineTo(.47, side * .08); shape.lineTo(.42, side * .02); shape.lineTo(.37, side * .09); shape.lineTo(.28, side * .1); shape.closePath();
    const jaw = new THREE.Mesh(sharedGeometry(`jaw-${side}`, () => new THREE.ExtrudeGeometry(shape, { depth: .055, bevelEnabled: true, bevelSegments: 1, steps: 1, bevelSize: .014, bevelThickness: .012 })), jointMaterial);
    jaw.rotation.x = Math.PI / 2; jaw.position.y = -.12; jaw.castShadow = true; head.add(jaw);
  }
  const legs = [];
  for (let row = 0; row < 3; row++) for (const side of [-1, 1]) {
    legs.push({ row, side, rods: [rod(root, .042), rod(root, .031), rod(root, .018), rod(root, .009)],
      joints: [ellipsoid(root, jointMaterial, 0, 0, 0, .065, .065, .065), ellipsoid(root, jointMaterial, 0, 0, 0, .047, .047, .047)] });
  }
  const antennae = [-1, 1].map(side => ({ side, rods: [rod(head, .026), rod(head, .016)], tip: ellipsoid(head, jointMaterial, 0, 0, 0, .032, .032, .032) }));
  const crumb = ellipsoid(head, foodMaterial, .53, -.13, 0, .18, .16, .19); crumb.visible = false;
  // Sparse, short setae add surface detail without a separate draw call per hair.
  const hairs = new THREE.InstancedMesh(cylinder, jointMaterial, 30), hairDummy = new THREE.Object3D();
  for (let i = 0; i < 30; i++) {
    const angle = i * 2.39996, x = -.91 + Math.sin(i * 1.7) * .6;
    const radius = Math.sqrt(1 - ((x + .91) / .75) ** 2);
    const normal = new THREE.Vector3((x + .91) / .75, Math.abs(Math.sin(angle)), Math.cos(angle)).normalize();
    hairDummy.position.set(x, .62 + .41 * radius * Math.abs(Math.sin(angle)), .39 * radius * Math.cos(angle));
    hairDummy.position.addScaledVector(normal, .025); hairDummy.quaternion.setFromUnitVectors(up, normal); hairDummy.scale.set(.0025, .055, .0025);
    hairDummy.updateMatrix(); hairs.setMatrixAt(i, hairDummy.matrix);
  }
  body.add(hairs);
  const a = new THREE.Vector3(), b = new THREE.Vector3(), c = new THREE.Vector3(), d = new THREE.Vector3(), toe = new THREE.Vector3();
  function animate(distance, time, carrying, moving = true) {
    const gait = distance * 5.5;
    body.position.y = moving ? Math.sin(gait * 2) * .018 : 0;
    head.rotation.y = Math.sin(time * 2.5) * .045;
    crumb.visible = carrying;
    for (const leg of legs) {
      // Front+rear on one side and middle on the other form alternating tripods.
      const phase = gait + ((leg.row + (leg.side > 0 ? 1 : 0)) % 2) * Math.PI;
      const stride = moving ? Math.cos(phase) * .26 : 0;
      const lift = moving ? Math.max(0, Math.sin(phase)) * .22 : 0;
      const baseX = .38 - leg.row * .31;
      a.set(baseX, .61 + body.position.y, leg.side * .17);
      b.set(baseX + (1 - leg.row) * .23 + stride * .3, .64, leg.side * .54);
      c.set(baseX + (1 - leg.row) * .46 + stride * .7, .18 + lift * .6, leg.side * .95);
      d.set(baseX + (1 - leg.row) * .58 + stride, .025 + lift, leg.side * 1.14);
      connect(leg.rods[0], a, b); connect(leg.rods[1], b, c); connect(leg.rods[2], c, d);
      toe.copy(d).add(new THREE.Vector3(.09, -.01, leg.side * .035)); connect(leg.rods[3], d, toe);
      leg.joints[0].position.copy(b); leg.joints[1].position.copy(c);
    }
    for (const antenna of antennae) {
      const sway = Math.sin(time * 5 + antenna.side * 2) * .12;
      a.set(.2, .19, antenna.side * .16);
      b.set(.52, .31, antenna.side * (.33 + sway));
      c.set(.83 + sway, .15, antenna.side * (.5 + sway));
      connect(antenna.rods[0], a, b); connect(antenna.rods[1], b, c); antenna.tip.position.copy(c);
    }
  }
  animate(0, 0, false, false);
  return { root, animate, dispose() { hairs.dispose(); } };
}
