# Attribution / 帰属表示

## 3D city data — Project PLATEAU (CC BY 4.0)
- 3D都市モデル（Project PLATEAU）目黒区（2023年度） — 国土交通省 (MLIT)
- **© Project PLATEAU / MLIT** — License: **CC BY 4.0**
- https://www.geospatial.jp/ckan/dataset/plateau-13110-meguro-ku-2023
- The in-game buildings / terrain / roads under `Assets/Models/City/` are derived from this
  CityGML (LOD2). Those derived `.glb` files are **not committed** — regenerate via
  [`Assets/Models/City/README.md`](Assets/Models/City/README.md).
- Builds must keep this credit visible (e.g. credits screen):
  **「3D都市モデル（Project PLATEAU）© 国土交通省 / CC BY 4.0」**

## Tools (all open source — not redistributed in this repo)
| Tool | License | Use |
|------|---------|-----|
| [PLATEAU GIS Converter / nusamai](https://github.com/MIERUNE/plateau-gis-converter) | MIT | CityGML → textured glTF |
| [glTF-Transform](https://gltf-transform.dev/) | MIT | terrain decimation / optimisation |
| meshoptimizer | MIT | simplification backend |
| [plateau-creative-mcp](https://github.com/pixelx-jp/plateau-creative-mcp) | MIT | early geometry extraction |
| glTFast (`com.unity.cloud.gltfast`) | Apache-2.0 | glb import into Unity |
