// Crop a PLATEAU DEM glb to an XZ bbox, then decimate it.
// Drops NORMAL/_FEATURE_ID/TEXCOORD so vertices weld by position; glTFast
// regenerates normals on import.
// args: in out ratio error [xmin xmax zmin zmax]
import { NodeIO } from '@gltf-transform/core';
import { weld, simplify, prune, dedup } from '@gltf-transform/functions';
import { MeshoptSimplifier } from 'meshoptimizer';

const a = process.argv.slice(2);
const inPath = a[0], outPath = a[1];
const ratio = parseFloat(a[2] ?? '0.1');
const error = parseFloat(a[3] ?? '0.001');
const [xmin, xmax, zmin, zmax] = a.slice(4).map(Number);
const doCrop = a.length >= 8 && [xmin, xmax, zmin, zmax].every(v => !Number.isNaN(v));

const io = new NodeIO();
const doc = await io.read(inPath);

for (const mesh of doc.getRoot().listMeshes()) {
  for (const prim of mesh.listPrimitives()) {
    prim.setAttribute('NORMAL', null);
    prim.setAttribute('_FEATURE_ID_0', null);
    prim.setAttribute('TEXCOORD_0', null);

    if (doCrop) {
      const pos = prim.getAttribute('POSITION');
      const idxAcc = prim.getIndices();
      const triCount = (idxAcc ? idxAcc.getCount() : pos.getCount()) / 3;
      const gi = (t, k) => idxAcc ? idxAcc.getScalar(t * 3 + k) : t * 3 + k;
      const v = [0, 0, 0];
      const kept = [];
      for (let t = 0; t < triCount; t++) {
        let cx = 0, cz = 0;
        for (let k = 0; k < 3; k++) { pos.getElement(gi(t, k), v); cx += v[0]; cz += v[2]; }
        cx /= 3; cz /= 3;
        if (cx >= xmin && cx <= xmax && cz >= zmin && cz <= zmax)
          for (let k = 0; k < 3; k++) kept.push(gi(t, k));
      }
      const newIdx = doc.createAccessor().setType('SCALAR').setArray(Uint32Array.from(kept));
      prim.setIndices(newIdx);
    }
  }
}

await MeshoptSimplifier.ready;
await doc.transform(
  dedup(),
  weld(),
  simplify({ simplifier: MeshoptSimplifier, ratio, error, lockBorder: false }),
  prune(),
);

await io.write(outPath, doc);

let tris = 0, verts = 0;
for (const mesh of doc.getRoot().listMeshes())
  for (const prim of mesh.listPrimitives()) {
    const idx = prim.getIndices();
    tris += (idx ? idx.getCount() : prim.getAttribute('POSITION').getCount()) / 3;
    verts += prim.getAttribute('POSITION').getCount();
  }
console.log(`done: ${Math.round(tris)} tris, ${Math.round(verts)} verts`);
