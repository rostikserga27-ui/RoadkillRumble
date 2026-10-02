# Geometry gate report — /tmp/claude-1000/-home-avoided7-Projects-unity-RoadkillRumble/26008bb2-6ce7-43c9-808c-9f71f5a97d10/scratchpad/fisherman/v3/SK_Fisherman.blend

class: **character** · meshes: 3 · triangles: 2440 · dims: [1.327, 0.3741, 1.7] · Blender 5.2.2 LTS

**Result: PASS** (0 hard failures, 6 warnings)

| Gate | Measured | Threshold | Verdict | Fix |
| --- | --- | --- | --- | --- |
| triangle budget | 2440 | <= 2500 | PASS |  |
| dimensions (m) | [1.327, 0.3741, 1.7] | plausible for the object | PASS |  |
| lowest point z | -0.0 | |z| <= 0.02 | PASS |  |
| SK_Fisherman_Body: transforms applied | True | True | PASS |  |
| SK_Fisherman_Body: negative scale | False | False | PASS |  |
| SK_Fisherman_Body: UV layers | 1 | >= 1 | PASS |  |
| SK_Fisherman_Body: empty material slots | 0 | 0 | PASS |  |
| SK_Fisherman_Body: degenerate faces | 0 | 0 | PASS |  |
| SK_Fisherman_Body: loose vertices | 0 | 0 | PASS |  |
| SK_Fisherman_Body: loose parts | 31 | <= 3 | WARN | delete debris islands / join intended parts |
| SK_Fisherman_Body: non-manifold edges | 0 | 0 | PASS |  |
| SK_Fisherman_Body: boundary edges | 0 | 0 on closed shapes | PASS |  |
| SK_Fisherman_Body: smooth shading | 0.0% smooth | 100% smooth | WARN | shade_smooth() / smooth by angle |
| SK_Fisherman_Body: materials | 1 | <= 4 | PASS |  |
| SK_Fisherman_Clothes: transforms applied | True | True | PASS |  |
| SK_Fisherman_Clothes: negative scale | False | False | PASS |  |
| SK_Fisherman_Clothes: UV layers | 1 | >= 1 | PASS |  |
| SK_Fisherman_Clothes: empty material slots | 0 | 0 | PASS |  |
| SK_Fisherman_Clothes: degenerate faces | 0 | 0 | PASS |  |
| SK_Fisherman_Clothes: loose vertices | 0 | 0 | PASS |  |
| SK_Fisherman_Clothes: loose parts | 6 | <= 3 | WARN | delete debris islands / join intended parts |
| SK_Fisherman_Clothes: non-manifold edges | 0 | 0 | PASS |  |
| SK_Fisherman_Clothes: boundary edges | 0 | 0 on closed shapes | PASS |  |
| SK_Fisherman_Clothes: smooth shading | 0.0% smooth | 100% smooth | WARN | shade_smooth() / smooth by angle |
| SK_Fisherman_Clothes: materials | 1 | <= 4 | PASS |  |
| SK_Fisherman_Sandals: transforms applied | True | True | PASS |  |
| SK_Fisherman_Sandals: negative scale | False | False | PASS |  |
| SK_Fisherman_Sandals: UV layers | 1 | >= 1 | PASS |  |
| SK_Fisherman_Sandals: empty material slots | 0 | 0 | PASS |  |
| SK_Fisherman_Sandals: degenerate faces | 0 | 0 | PASS |  |
| SK_Fisherman_Sandals: loose vertices | 0 | 0 | PASS |  |
| SK_Fisherman_Sandals: loose parts | 6 | <= 3 | WARN | delete debris islands / join intended parts |
| SK_Fisherman_Sandals: non-manifold edges | 0 | 0 | PASS |  |
| SK_Fisherman_Sandals: boundary edges | 0 | 0 on closed shapes | PASS |  |
| SK_Fisherman_Sandals: smooth shading | 0.0% smooth | 100% smooth | WARN | shade_smooth() / smooth by angle |
| SK_Fisherman_Sandals: materials | 1 | <= 4 | PASS |  |
| inspector note | SK_Fisherman_Body: 31 loose parts (shell soup?) | — | WARN | — |
