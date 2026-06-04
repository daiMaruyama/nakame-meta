# Nakameguro City Assets — Regeneration

The city `.glb` files (`CityTiles/Buildings.glb`, `Terrain.glb`, `Roads.glb`) and
`NakameguroStation.*` are **derived from Project PLATEAU CityGML (CC BY 4.0)** and are
git-ignored. Regenerate them with the pipeline below. See repo-root `ATTRIBUTION.md`.

Tools live under `PlateauOut/tools/` and `.mcp-servers/plateau/` (both ignored; restore as noted).
Requires Node 22 (`/opt/homebrew/opt/node@22`).

## 1. Source CityGML
Download 目黒区 2023 (LOD2, textured `_2_op`, 270MB) and unzip to `PlateauOut/citygml/extracted/`:
- https://www.geospatial.jp/ckan/dataset/plateau-13110-meguro-ku-2023

## 2. Tools
- nusamai (PLATEAU GIS Converter), Apple-Silicon CLI build, to `PlateauOut/tools/nusamai`
  (`xattr -d com.apple.quarantine` after download): https://github.com/MIERUNE/plateau-gis-converter/releases
- gltf-transform: `cd .mcp-servers/plateau && npm install @gltf-transform/cli`

## 3. Convert (textured glTF, metric, one shared origin)
Nakameguro = mesh tiles `53393565 / 53393566 / 53393575 / 53393576` (central = `…575`).
```sh
BLDG=PlateauOut/citygml/extracted/udx/bldg; TRAN=PlateauOut/citygml/extracted/udx/tran
DEM=PlateauOut/citygml/extracted/udx/dem
./PlateauOut/tools/nusamai \
  $BLDG/5339356{5,6}_bldg_6697_op.gml $BLDG/5339357{5,6}_bldg_6697_op.gml \
  $DEM/533935_dem_6697_op.gml \
  $TRAN/5339356{5,6}_tran_6697_op.gml $TRAN/5339357{5,6}_tran_6697_op.gml \
  --sink gltf -t use_lod=textured_max_lod --epsg 6677 \
  --output PlateauOut/gltf/nakameguro_full.glb
```
`--epsg 6677` (JGD2011 plane CS IX, metres) is required — default is lat/lon degrees.

## 4. Decimate the terrain (289MB → ~0.7MB) and crop to the building footprint
```sh
cd .mcp-servers/plateau
node decimate_dem.mjs \
  ../../PlateauOut/gltf/nakameguro_full.glb/dem_ReliefFeature.glb \
  ../../PlateauOut/gltf/dem_cropped.glb 0.2 0.001 480 2400 -1950 100
```

## 5. Copy into Unity
```sh
cp PlateauOut/gltf/nakameguro_full.glb/bldg_Building.glb Assets/Models/City/CityTiles/Buildings.glb
cp PlateauOut/gltf/dem_cropped.glb                        Assets/Models/City/CityTiles/Terrain.glb
cp PlateauOut/gltf/nakameguro_full.glb/tran_Road.glb      Assets/Models/City/CityTiles/Roads.glb
```
In Unity, place all three under one empty at Transform (0,0,0) — they share an origin.
Terrain/Roads have no textures: assign materials via **Tools → NakameMeta → Assign Ground / Road Material**.
