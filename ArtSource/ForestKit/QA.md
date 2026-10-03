# Forest Kit QA

- Assets: 109
- Total triangles: 13646
- Geometry / triangle budgets: PASS for all 109 assets
- FBX round-trip / metres / axes / bottom origin: PASS for all 109 assets
- FBX GUIDs and shared material references: PASS (111 exports including library and demo)
- Flat shading: authored flat normals; no smooth shading or subdivision
- Orthographic inspection: front, side, back, three-quarter, top for each asset
- Unity import: NOT RUN successfully; Editor licence unavailable (exit code 198)

All rigid intersections were cleaned with boolean unions. Stem/foliage meshes are intentionally separate for wind. Ground-facing caps remain to preserve closed manifold meshes.

See manifest.json for geometry metrics and FBX_Roundtrip_QA.json for dimensions after reimport.
