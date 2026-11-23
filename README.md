# **B2U Material Master (Unity Package)**

**Documentation & Guides:** [B2U Learn](https://dnart.ca/b2u/learn/) · [API Reference](https://dnart.ca/b2u/api/v1.0/) · [DNArt Website](https://dnart.ca/)  
**Source & Bug Reports:** [GitHub Repository](https://github.com/CalebDNArt/B2U-Unity) · [Report a Bug for Unity](https://github.com/CalebDNArt/B2U-Unity/issues) · [Report a Bug for Blender](https://github.com/CalebDNArt/B2U-Blender/issues)

> [!WARNING]
> **BETA STATE:** This package is currently in a beta state. There will be issues and bugs. Please report any bugs or feedback on [GitHub for Unity](https://github.com/CalebDNArt/B2U-Unity/issues) or [Github for Blender](https://github.com/CalebDNArt/B2U-Blender/issues)

> [!NOTE]
> **AI USE DISCLAIMER:** AI was used in the development of code for this package. As a solo developer, I would not have been able to build this package in less than a year without it. I wrote the majority of the code myself, with AI used for bug fixing and code review. **NO AI HAS BEEN USED FOR ANY IMAGE ASSETS.** All default images were created by hand in Blender or Photoshop. If you do not feel comfortable using the package due to AI usage, I absolutely respect your right to decline—please feel free to reference the source code if you would like to develop your own version. Thank you!

---

## **About**

The goal of **B2U Material Master** is to take the manual, repetitive work out of material development for developers at all levels. The pipeline uses baked vertex data and Look-Up Tables (LUTs) to drive the majority of rendering in a project. By combining LUTs with texture array atlases, nearly every object in your game can share the same master shaders **without** requiring you to author per-object textures.

By leveraging ubershaders and Unity's `unity_RendererUserValue` (RSUV), B2U achieves high levels of draw-call batching while maintaining per-object customization. This ensures visual consistency across materials and provides major VRAM savings (the default setup uses less than 90 MB of texture memory).

This package was developed by a single person. I wanted to make the art side of game development less overwhelming and time-consuming for everyone—whether you are a new developer, someone who doesn't feel confident creating textures, someone unfamiliar with shader programming, or a solo dev who simply can't afford the time that full artistic direction takes.

The entire pipeline is free to use, modify, and distribute! If you choose to use it in your project, please feel free to share your work—I would love to see what people create with the addon. Because I work a full-time job alongside developing B2U, I apologize in advance that I cannot guarantee immediate fixes for all issues.

If you would like to support development, please consider becoming a GitHub Sponsor. Thank you!

---

## **Installation**

> B2U is planned to be submitted to the **Unity Asset Store** in the future. Initially, releases are distributed via **GitHub** and **Gumroad**. For the complete walkthrough, see the [Unity Setup Guide](https://dnart.ca/b2u/learn/unity/setup/).

1. Open **Window -> Package Manager** in Unity.
2. Click **`+`** in the top-left corner and choose one of the following:
   - **Add package from git URL...** and enter `https://github.com/CalebDNArt/B2U-Unity.git`
   - **Add package from disk...** and select `package.json` from a local download
   - **Add package from tarball...** and select the downloaded `.tgz` archive
3. *(Optional)* In Package Manager, select **B2U Material Master -> Samples** and click **Import** on **B2U Sample Scene** for a working interactive demo.

---

## **Prerequisites & Project Setup**

- **Render Pipeline:** Universal Render Pipeline (**URP**) with **Forward** or **Forward+** rendering.
  - Ensure **Depth Texture** and **Opaque Texture** are enabled on your active URP Pipeline Asset.
- **Required Unity Packages:** `Newtonsoft Json`, `Unity.Burst`, `Unity.Mathematics`, and `Unity.Collections` (resolved automatically via UPM).
- **Project Layers & Tags** (**Edit -> Project Settings -> Tags and Layers**):
  - Add a dedicated User Layer (for example, **`B2UVolumetric`**) for Crystals and Volumetric Liquids, then assign it to **Volumetric Layer** on your `B2UShaderProfile`.
  - Add a tag named **`B2ULiquid`**. The Auto-Applier automatically assigns this tag to liquid meshes so dynamic wave ripples and Mesh IDs can track them at runtime.
- **Blender Addon:** To author palettes and bake B2U vertex/UV data onto your 3D models, install the companion **[B2U Blender Addon](https://github.com/CalebDNArt/B2U-Blender)**.

---

## **Features**

### **Materials**
<details><summary>Click to view material features</summary>

- **SRP Batcher & GPU Resident Drawer Compatible:** Batches objects of a given workflow into minimal draw calls while retaining per-instance overrides.
- **Dynamic World-Space Biome Blending:** All material workflows support dynamic 8-biome and 7-subtype color blending driven by two global biome weight textures.
- **Solids (`Opaque`, `Opaque Double-Sided`, `Transparent`)**
  - Triplanar normal mapping, curvature mapping, and heightmaps using a 64-slice `Texture2DArray` detail atlas.
  - 8 independent color variants per material slot, or dynamic biome-driven coloring (including dual-row `INDEP_DYN` support).
  - Physical edge wear and cavity masking (Soft Edges, Hard Toon Edges, or Colored Hard Edges).
  - Emission painting with vertex color masks and animated emission pattern maps.
  - Gradient painting (Vertex Red) to shift between base and secondary colors (`Opaque` / `Opaque DS`) or control opacity (`Transparent`).
  - Per-instance paint masking (Vertex Blue) using a 16-slice Paint Palette `Texture2DArray`.
- **Crystals**
  - Dual-surface volumetric refraction and backface thickness absorption.
  - Internal reflection flares (Ambient, Main Light, or All Lights).
  - Viewing-angle spectral dispersion / prismatic color shifting.
- **Liquids**
  - Opaque or transmissive refractive liquids with optional distance-based GPU hardware tessellation.
  - Volumetric refraction and chromatic aberration.
  - Projected procedural caustics (Main Light or All Lights) with shadow cascade attenuation.
  - Baked object-space flow mapping with dual-phase advection and upward bubble advection.
  - Dynamic interactive wave ripple system (up to 64 simultaneous ripples with analytical normal derivatives).
  - Procedural 3D Voronoi/Simplex noise or baked `Texture3D` noise evaluation.
- **Renderer Set User Values (RSUVs)**
  - Enables zero-overhead per-object control without breaking batching:
    - Paint Palette slice & color index (`Solids`)
    - 3D Noise offset randomization (`Solids`)
    - Emission Strength multiplier (`Solids`, `Crystals`, `Liquids`)
    - Procedural damage cracks (`Solids` & `Crystals`)
    - Transmission & Absorption multipliers (`Crystals`)
    - Flow Strength & Wave Height multipliers (`Liquids`)
</details>

### **Rendering & Lighting**
<details><summary>Click to view rendering features</summary>

- **8 Global Lighting Models**
  - **URP:** Standard physically based URP rendering.
  - **Lightweight:** Streamlined Blinn-Phong lighting for lower GPU overhead.
  - **Toon:** Posterizes albedo, diffuse bands, specular cones, and rim lighting for cel-shaded visuals.
  - **Anti-Light:** Inverts illumination so light casts darkness while shadows remain bright.
  - **Retro:** 4×4 ordered Bayer dithered shading.
  - **Gooch:** Tints shadows using the albedo's complementary color with adjustable shadow darkness.
  - **Faceted:** Procedurally reconstructs flat triangle normals in screen space for a low-poly/origami look on any mesh.
  - **Pop-Art:** Resolution-independent halftone Ben-Day dot shading.
- **[Volumetric Refraction (`B2UVolumetricRefraction`)](https://dnart.ca/b2u/learn/general/render-features/volumetric-refraction/)**
  - Uses a custom backface depth-normal buffer and intermediate screen-color copy passes (`Low`, `Medium`, and `High` quality modes) to composite crystals, standard liquids, and volumetric liquids through one another without raytracing.
- **[Edge Detection (`B2UEdgeDetection`)](https://dnart.ca/b2u/learn/general/render-features/edge-detection/)**
  - Generates clean outlines around solid geometry and liquid intersections.
  - Supports **Edge Buffer Mode** (renders a custom per-object color ID buffer for crisp silhouette and object-boundary lines) or **Depth Buffer Mode**.
  - Configurable outline color, resolution, and **Screen-Space** vs. **World-Space** thickness scaling.
</details>

### **Fullscreen Effects**
<details><summary>Click to view fullscreen features</summary>

- Driven by the [`B2UFullscreenEffects`](https://dnart.ca/b2u/learn/general/render-features/fullscreen-effects/) Scriptable Renderer Feature with a drag-and-drop reorderable draw list in the Shader Profile Inspector.
- **Liquid Immersion**
  - Automatically submerges the camera when dipping below the active liquid surface height.
  - Pulls surface/depth colors, viscosity, IOR, and absorption directly from the liquid's LUT row.
  - Features a customizable **Surface Meniscus & Wave Transition** that sweeps across the lens when entering or exiting the water, hiding near-plane mesh clipping.
- **[Recoloring](https://dnart.ca/b2u/learn/unity/scriptable-objects/recolor-preset/)**
  - Remaps screen pixels through a custom color gradient driven by:
    - `Scene Luminance`, `Scene Red`, `Scene Green`, `Scene Blue`, `Depth`, or `Normal`.
  - Supports multiple screen transition/blend modes:
    - `Alpha`, `Radial`, `Distorted Radial`, `Horizontal Scan`, `Vertical Scan`, `Horizontal Double Scan`, and `Vertical Double Scan`.
  - Optional world-space **Sonar Pulse** rings emanating from the camera.
- **[Stylization](https://dnart.ca/b2u/learn/unity/scriptable-objects/stylize-preset/)**
  - Overlays artistic base textures and multi-band luminance shadow hatching (16 built-in styles for hand-drawn, painted, or comic-book aesthetics).
  - Supports **Rotated** or **Translated** shadow band offsets and **Circular**, **Rectangular**, or **Noisy Rectangular** screen matting frames.
- **[Status Effects & Injury](https://dnart.ca/b2u/learn/unity/scriptable-objects/status-effect-preset/)**
  - **Injury Overlay:** Independent pulsing border vignette and scene darkening control (`0 = Uninjured`, `1 = Gravely Injured`).
  - **8 Gradient-Driven Status Effect Styles:**
    - **Burning:** Rising flames with heat-haze screen distortion.
    - **Freezing:** Voronoi ice crystals and frost growing inward from the screen edges with screen-space specular lighting.
    - **Shocked:** Radial procedural lightning bolts surging inward from the edges of the viewport.
    - **Poisoned:** Organic perimeter distortion, color tinting, and peripheral darkening.
    - **Hexed:** Animated procedural geometric mandala seal with background darkening.
    - **Blessed:** Animated corner sunbursts with god rays.
    - **Glitched:** Digital block displacement, procedural alien glyph overlays, static noise, and edge chromatic aberration.
    - **Shielded:** Bulging hexagonal sci-fi energy grid that expands from the center and settles along the screen perimeter.
</details>

### **Animation & Gameplay API**
<details><summary>Click to view animation & gameplay features</summary>

- **[`B2UAnimationTrigger`](https://dnart.ca/b2u/learn/unity/components/animation-trigger/) Component**
  - Stores a list of reusable animation jobs that can be triggered via script, `UnityEvent`, or standard Unity `Animator` timeline events (`TriggerEvent(int)`).
  - **Animatable Targets:**
    - **Global Floats:** Animate any float in the active `B2UShaderProfile` (e.g., Global Emission Strength, Solid Edge Thickness).
    - **Palette Values:** Dynamically animate material properties or LUT colors for specific materials in the active palette.
    - **RSUV Values:** Animate per-object properties (e.g., Crack Intensity, Emission Strength, Flow Strength) on a `B2URSUVMono` component.
    - **Fullscreen Triggers:** Smoothly fade fullscreen Recolor, Stylize, or Status Effect presets in and out.
    - **Data Swaps:** Instantaneously swap the active Shader Profile, Sister Palette, or Fullscreen Preset Collection from animation timelines.
  - **Interpolation Modes:**
    - **Aperiodic (One-Shot):** `Linear`, `Sigmoidal (Smoothstep)`, `Ease-In`, `Ease-Out`.
    - **Periodic (Continuous with Optional Decay):** `Sine`, `Sawtooth`, `Ping-Pong (Triangle)`, `Square`, `Parabolic`.
    - **Custom `AnimationCurve`**.
- **Gameplay & Physics Helpers ([`B2URuntimeUtility`](https://dnart.ca/b2u/api/v1.0/b2u-runtime-utility/))**
  - **Zero-Allocation Biome Queries:** Sample all 8 biome weights and active subtypes at any world position in `Update()` via `GetBiomeData(Vector3)`.
  - **Raycast Material & Variant Detection:** Identify the exact `B2UMaterial` and color variant (`0-7`) hit by a `Physics.Raycast` on `MeshCollider` triangles or primitive colliders via `TryGetMaterialAtHit()`—ideal for footsteps, bullet impacts, and surface audio.
  - **World-Space Color Evaluation:** Sample the true biome-blended color of any material at a world coordinate via `GetEvaluatedMaterialColor()` to tint particles, debris, or decals.
  - **Burst-Accelerated Liquid Currents & Ripples:** Query world-space river/liquid push vectors via `GetLiquidPush()` and spawn interactive 3D shader ripples via `InstantiateWave()`.
</details>

---

## **Getting Started**

For a working example, import the **B2U Sample Scene** from Package Manager's **Samples** tab, or follow the full [Unity Setup Guide](https://dnart.ca/b2u/learn/unity/setup/).

To author your own assets for B2U, first install the **[B2U Blender Addon](https://github.com/CalebDNArt/B2U-Blender)**. The addon allows you to build a material palette, export the `.json` and `.png` LUT textures, and bake B2U vertex/UV data onto your meshes.

---

### **1. B2U Naming Conventions**

Because B2U automatically configures texture import settings, slices `Texture2DArray` / `Texture3D` flipbooks, and repacks Blender UV channels into 4D UV streams on model import, assets must follow these [Naming Conventions](https://dnart.ca/b2u/learn/unity/importing/naming-conventions/) (see [Asset Postprocessing](https://dnart.ca/b2u/learn/unity/importing/asset-postprocessing/) for exact importer rules):

<details><summary>Custom Textures & Palette LUTs</summary>

- **General Rule:** All B2U textures **must BEGIN with `"B2U"`**.
  - *Tip:* If you want a texture starting with `"B2U"` to bypass `B2U_TexturePostprocessor`, include **`"NAPP"`** (No Asset Post Processing) anywhere in its filename.
- **Auxiliary Textures:**
  - **Iridescence LUT:** Must contain **`"Iridescence"`** (e.g., `B2U_IridescenceLUT_Default.png`).
    - *Format:* `Texture2D`, 8×8 resolution.
  - **Biome Weight Textures:** Must contain **`"BiomeTexture1"`** or **`"BiomeTexture2"`** (e.g., `B2U_BiomeTexture1.png`).
    - *Format:* `Texture2D`. Higher resolutions provide smoother biome transitions at the cost of memory.
  - **Paint Palette:** Must contain **`"PaintPalette"`** (e.g., `B2U_PaintPalette.png`).
    - *Format:* `Texture2DArray`, 1 column × 16 rows (16×8 pixels per slice, 16×128 total resolution).
  - **Detail Map Atlas:** Must contain **`"Detail"`** (e.g., `B2U_Detail_Atlas_Default.png`).
    - *Format:* `Texture2DArray`, 8×8 grid (64 slices). Recommended 512×512 per slice (4096×4096 total). Channels: `RG` = Normal shift, `B` = Curvature, `A` = Height.
  - **Emission Patterns:** Must contain **`"Patterns"`** (e.g., `B2U_Emission_Patterns_Default.png`).
    - *Format:* `Texture2D`, square aspect ratio, 4 grayscale patterns packed into `R`, `G`, `B`, and `A`.
  - **3D Noise Texture:** Must contain **`"3DNoise"`** (e.g., `B2U_64x64x64_3DNoise.png`).
    - *Format:* `Texture3D`, 8×8 flipbook grid. Recommended 64×64×64 resolution (`RGB` = 3D Simplex noise, `A` = 3D Voronoi noise).
- **Palette LUTs (Automatically named by the Blender Addon):**
  - **Color & Emission LUT:** Must begin with `"B2U"`, contain the **Palette Name**, and contain **`"ColorEmission"`** (e.g., `B2U_Test_Palette_ColorEmissionLUT_32x256.png`).
  - **Property LUT:** Must begin with `"B2U"`, contain the **Palette Name**, and contain **`"Property"`** (e.g., `B2U_Test_Palette_PropertyLUT_4x256.png`).
</details>

- **Meshes & 3D Models:**
  - Model filenames exported from Blender **must BEGIN with `"B2U"`** so `B2UMeshPostprocessor` can repack their UV channels on import.
  - In your Unity Scene, the root GameObject (or the mesh GameObject itself) **must BEGIN with `"B2U"`** for the Control Panel's Auto-Applier to process it and its child hierarchy.

---

### **2. Creating the ScriptableObject Assets**

Once your palette JSON and LUTs are exported from Blender, right-click in your Unity Project window to create the core [ScriptableObject](https://dnart.ca/b2u/learn/unity/scriptable-objects/) assets:

1. **`Create -> B2U -> Palette Data`** ([Guide](https://dnart.ca/b2u/learn/unity/scriptable-objects/palette-data/) · [Importing Palettes](https://dnart.ca/b2u/learn/unity/importing/importing-palettes/))
   - Stores the material definitions, schema ranges, biome names, and LUT texture references for a palette.
   - Click **Browse** next to **JSON Path** and select the `.json` file exported from Blender.
   - Click **Load From JSON**. This populates your material slots, locates the matching LUT and auxiliary textures, sets the **LUT Output Folder** automatically, and initializes the biome and subtype name arrays.
   - Assign your shared **3D Noise Texture** and **Paint Palette**, and customize your **Biome & Subtype Names** in the Inspector.
2. **`Create -> B2U -> Shader Profile`** ([Guide](https://dnart.ca/b2u/learn/unity/scriptable-objects/shader-profile/))
   - Stores all global rendering settings, quality modes, lighting models, workflow parameters, and fullscreen configurations, along with a real-time **Current Profile Cost (%)** estimator.
   - Assign your `B2UPaletteData` asset to the **Palette for Profile** slot.
   - If your scene does not yet have a [`B2URuntimeManager`](https://dnart.ca/b2u/learn/unity/components/runtime-manager/), click **Auto Setup Runtime Manager** in the Inspector.
   - When enabling **Volumetric Refraction**, **Edge Detection**, or **Fullscreen Effects**, if the corresponding URP Renderer Feature is not yet attached to your active URP Renderer, click the **Auto Setup Feature** button that appears to configure it automatically.

<details><summary>Optional: Creating Custom Fullscreen Presets & Collections</summary>

Default preset collections are included with the package, or you can author custom [Preset Collections](https://dnart.ca/b2u/learn/unity/scriptable-objects/preset-collection/) via the **Create -> B2U -> Fullscreen** menu:
- **Recolor Presets:** `Create -> B2U -> Fullscreen -> Recolor Preset` (grouped inside a `Recolor Preset Collection`).
- **Stylize Presets:** `Create -> B2U -> Fullscreen -> Stylize Preset` (grouped inside a `Stylize Preset Collection`).
- **Status Effect Presets:** `Create -> B2U -> Fullscreen -> Status Effect Preset` (grouped inside a `Status Effect Preset Collection`).
</details>

---

### **3. Using the Editor Windows**

Open the B2U Editor tools from Unity's top menu bar under **`B2U`**:

#### **[Control Panel](https://dnart.ca/b2u/learn/unity/editor-windows/control-panel/) (`B2U -> Control Panel`)**
- The central hub for managing your active `B2UShaderProfile` and scene materials.
- **Scene Material Auto-Applier:**
  - Scans all `B2U`-prefixed hierarchies in the active scene, inspects each mesh's baked vertex/UV data against the Property LUT, assigns the appropriate B2U material and Volumetric layer, tags liquid meshes with `"B2ULiquid"`, and attaches/updates `B2URSUVMono` components.
  - **Use Ubershader:** Uses the universal master materials (`Opaque_U`, `Liquid_U`, etc.) to maximize SRP Batcher draw-call batching (Recommended). Disabling this assigns keyword-specialized materials.
  - **Ignore Tessellation:** Forces liquid meshes marked as tessellated in Blender to use standard non-tessellated liquid materials.
- **Active Profile Properties:**
  - Embeds the full `B2UShaderProfile` Inspector for live scene tuning, along with **Force Push Globals** and **Reset Profile to Defaults** utilities.

#### **[Palette Tweaker](https://dnart.ca/b2u/learn/unity/editor-windows/palette-tweaker/) (`B2U -> Palette Tweaker`)**
- Allows you to inspect, add, remove, and live-edit palette materials, schema properties, and linear LUT colors directly in Unity while keeping your `.png` LUTs and Blender `.json` file synchronized.
- **Top Toolbar:**
  - **Palette Dropdown:** Switch between palettes in your project. Palettes with matching row layouts and workflows are automatically badged as `(Sister Palette)` and can be hot-swapped at runtime.
  - **Refresh List:** Rescans the project for `B2UPaletteData` assets.
  - **Build HLSL:** Generates `B2U_Constants.hlsl` from the active palette's schema `Min/Max` bounds (only required if you customize schema ranges in Blender).
  - **Recover from LUTs:** Reconstructs material slots by reverse-engineering the Color and Property LUT textures if the `.json` file is lost.
  - **Save Changes:** Writes all modified properties and colors back to the `.png` LUT assets and the Blender `.json` file.

#### **[Material Changer](https://dnart.ca/b2u/learn/unity/editor-windows/material-changer/) (`B2U -> Material Changer`)**
- A destructive mesh utility for permanently replacing baked Material IDs, Solid Color Variants, and Liquid Flags on a selected mesh asset.
- **For Solids & Crystals:**
  - Lists every `(Material ID, Variant)` combination detected on the mesh alongside its vertex count and linear LUT color swatch.
  - Select which slots to replace, choose a **Target Material** (and **Variant 1–8** via the cascading popout menu or quick slider for Biome-Independent solids), and click **Apply Destructive Changes to Mesh**.
- **For Liquids:**
  - Replace the liquid Material ID and toggle mesh-wide liquid behavior flags packed in `UV1.y` (`Is Tessellated`, `Volumetric Refraction`, `Use Displacement`, `Is Transmissive`, `Ignore Gravity`, `Use Caustics`, `Invert Height`, `Height-Based Transmission`, and `Height-Based Emission`).
- *Note:* For non-destructive runtime material changes, use `B2URuntimeUtility.SetB2UMaterial()` in the [API Reference](https://dnart.ca/b2u/api/v1.0/b2u-runtime-utility/).

---

### **4. Component Reference**

#### **[`B2URSUVMono`](https://dnart.ca/b2u/learn/unity/components/rsuv-mono/)**
Controls the renderer's 32-bit `unity_RendererUserValue` (RSUV), passing per-object overrides to the GPU without breaking SRP Batcher compatibility:
- **Solid Properties (`Opaque`, `Opaque DS`, `Transparent`):**
  - `Is Painted`, `Palette Index (0–15)`, `Paint Index (0–127)`, `Emission Strength`, `Noise Offset (0–63)`, and `Crack Intensity`.
- **Crystal Properties:**
  - `Emission Strength`, `Transmission Multiplier`, `Absorption Multiplier`, and `Crack Intensity`.
- **Liquid Properties:**
  - **Apply to Immersion Shader:** Binds this liquid mesh's surface height and material properties to the fullscreen underwater immersion pass.
  - **Refresh Material ID & Flow Cache:** Rebuilds the baked UV flow cache used by `B2URuntimeUtility.GetLiquidPush()` to push rigidbodies or characters along liquid currents.
  - `Emission Strength`, `Flow Strength (-1 to +1)`, and `Wave Multiplier (-1 to +1)`.

#### **[`B2UAnimationTrigger`](https://dnart.ca/b2u/learn/unity/components/animation-trigger/)**
- Dispatches procedural shader, palette, RSUV, and fullscreen animations from code, `UnityEvent` callbacks, or Unity `Animator` clips (`TriggerEvent(int)`).

#### **[`B2URuntimeManager`](https://dnart.ca/b2u/learn/unity/components/runtime-manager/)**
- Scene singleton that initializes your default `B2UShaderProfile`, drives `B2URuntimeAnimator`, hosts fullscreen effect coroutines, and updates dynamic liquid wave ripples.

---

## **Roadmap**

- See the [`Future Plans`](https://dnart.ca/b2u/learn/future-plans) page on the B2U website to see planned changes.