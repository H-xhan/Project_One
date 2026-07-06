PostIt Clean Material Pack for Unity
====================================

Purpose
-------
The Tripo-generated BaseColor/Normal/Roughness maps had baked grime/noise artifacts.
This pack replaces the body with a clean yellow paper material and provides a separate 5-toe paw-print decal.

Recommended workflow
--------------------
1. Back up your current Tripo model/material/texture folder first.
2. Copy the files inside `DropIn_Overwrite_Current` over the existing Tripo files with the same names.
   - This keeps the original GUIDs, so Unity should keep existing material references.
   - The body will become clean yellow paper.
   - Normal/roughness/metallic dirt is neutralized.
3. In Unity, select the Post-it Mesh Renderer and make sure it uses `tripo_mat_db2e8d0b` or `tripo_mat_db2e8d0b 1`.
4. Add a small Quad/Plane child in front of the Post-it for the paw print.
5. Assign `Decal_PawPrint_5Toes/M_PawPrint_5Toes_Decal.mat` to that Quad.
6. Move the paw Quad slightly in front of the paper surface to avoid z-fighting:
   Local offset: 0.001 ~ 0.003
7. Place it at the lower-right area of the note and scale it to taste.

Important notes
---------------
- The clean drop-in BaseColor intentionally does NOT include the paw print.
  This is on purpose: Tripo UVs are fragmented, so baking the paw back into the atlas cleanly is unreliable.
- Use the separate transparent paw decal for the cleanest result.
- The old Normal/Roughness/Metallic maps should not be used for this paper prop.
- For this object, URP/Unlit or URP/Lit with low Smoothness is best.

Suggested Unity material settings
---------------------------------
Post-it body:
- Shader: Universal Render Pipeline/Lit or Universal Render Pipeline/Unlit
- Base Color: White if using the BaseColor texture, or #FAD667 if using no texture
- Metallic: 0
- Smoothness: 0 ~ 0.1
- Normal Map: None
- Occlusion Map: None

Paw decal:
- Shader: URP/Lit Transparent, or manually change to URP/Unlit Transparent if preferred
- Base Map: T_PawPrint_5Toes_Decal.png
- Surface Type: Transparent
- Offset from paper: 0.001 ~ 0.003

Files
-----
DropIn_Overwrite_Current/
- Same filenames as your current Tripo export, designed for direct overwrite.

Decal_PawPrint_5Toes/
- Transparent 5-toe paw print texture and material.

Alternative_Textures/
- Clean PNG versions and experimental texture variants.

If the material becomes pink
----------------------------
Your URP shader GUID may differ. In that case:
1. Select the material in Unity.
2. Change Shader to Universal Render Pipeline/Lit or Universal Render Pipeline/Unlit.
3. Reassign the Base Map texture manually.
