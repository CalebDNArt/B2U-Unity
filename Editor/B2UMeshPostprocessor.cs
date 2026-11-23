#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace B2U
{
    /// <summary>
    /// Automatically configures ModelImporter settings for B2U-prefixed 3D models and packs the 6 Blender 2D UV channels
    /// into 3 4D UV channels (UV0: GrainAndScale + EdgeIntensities, UV1: EdgeDistances XY+ZW, UV2: SmoothNormals + Thickness).
    /// </summary>
    public class B2UMeshPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion()
        {
            return 1;
        }

        private void OnPreprocessModel()
        {
            string filename = Path.GetFileNameWithoutExtension(assetPath);
            if (filename.StartsWith("B2U", StringComparison.OrdinalIgnoreCase))
            {
                ModelImporter importer = (ModelImporter)assetImporter;
                if (!importer.isReadable)
                {
                    importer.isReadable = true;
                }

                if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
                {
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                }

                if (importer.importTangents != ModelImporterTangents.None)
                {
                    importer.importTangents = ModelImporterTangents.None;
                }
            }
        }

        private void OnPostprocessModel(GameObject g)
        {
            string filename = Path.GetFileNameWithoutExtension(assetPath);
            if (!filename.StartsWith("B2U", StringComparison.OrdinalIgnoreCase)) return;

            HashSet<Mesh> processedMeshes = new HashSet<Mesh>();

            MeshFilter[] meshFilters = g.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                Mesh mesh = meshFilters[i].sharedMesh;
                if (mesh != null && processedMeshes.Add(mesh))
                {
                    ProcessMesh(mesh);
                }
            }

            SkinnedMeshRenderer[] skinnedRenderers = g.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinnedRenderers.Length; i++)
            {
                Mesh mesh = skinnedRenderers[i].sharedMesh;
                if (mesh != null && processedMeshes.Add(mesh))
                {
                    ProcessMesh(mesh);
                }
            }
        }

        private Vector4 UnpackLiquidUV(Vector2 uv)
        {
            uint packedXY = (uint)uv.x;
            uint flowX_int = (packedXY >> 12) & 0xFFFu;
            uint flowY_int = packedXY & 0xFFFu;

            Vector2 flowVector = new Vector2(
                ((half)flowX_int / 4095.0f) * 2.0f - 1.0f,
                ((half)flowY_int / 4095.0f) * 2.0f - 1.0f
            );

            uint packedZW = (uint)uv.y;
            uint dist_int = (packedZW >> 8) & 0xFFFFu;

            float absoluteDistance = ((half)dist_int / 65535.0f) * 100.0f;
            float materialID = (half)(packedZW & 0xFFu) / 255.0f;

            return new Vector4(flowVector.x, flowVector.y, absoluteDistance, materialID);
        }

        private void WeldMesh(Mesh mesh)
        {
            Vector3[] srcVertices = mesh.vertices;
            Vector3[] srcNormals = mesh.normals;
            Vector4[] srcTangents = mesh.tangents;
            Color32[] srcColors = mesh.colors32;

            List<Vector4> srcUV0 = new List<Vector4>();
            List<Vector4> srcUV1 = new List<Vector4>();
            List<Vector4> srcUV2 = new List<Vector4>();
            mesh.GetUVs(0, srcUV0);
            mesh.GetUVs(1, srcUV1);
            mesh.GetUVs(2, srcUV2);

            bool hasUV0 = srcUV0.Count == srcVertices.Length;
            bool hasUV1 = srcUV1.Count == srcVertices.Length;
            bool hasUV2 = srcUV2.Count == srcVertices.Length;
            bool hasColors = srcColors != null && srcColors.Length == srcVertices.Length;

            List<Vector3> newVertices = new List<Vector3>(srcVertices.Length);
            List<Vector3> newNormals = new List<Vector3>(srcVertices.Length);
            List<Vector4> newTangents = new List<Vector4>(srcVertices.Length);
            List<Color32> newColors = new List<Color32>(srcVertices.Length);
            List<Vector4> newUV0 = new List<Vector4>(srcVertices.Length);
            List<Vector4> newUV1 = new List<Vector4>(srcVertices.Length);
            List<Vector4> newUV2 = new List<Vector4>(srcVertices.Length);

            Dictionary<Vector3, int> uniqueVertexMap = new Dictionary<Vector3, int>(srcVertices.Length);

            int[] srcTriangles = mesh.triangles;
            int[] newTriangles = new int[srcTriangles.Length];

            for (int i = 0; i < srcVertices.Length; i++)
            {
                Vector3 pos = srcVertices[i];

                if (!uniqueVertexMap.TryGetValue(pos, out int newIndex))
                {
                    newIndex = newVertices.Count;
                    uniqueVertexMap.Add(pos, newIndex);

                    newVertices.Add(pos);
                    if (srcNormals.Length > i) newNormals.Add(srcNormals[i]);
                    if (srcTangents.Length > i) newTangents.Add(srcTangents[i]);
                    if (hasColors) newColors.Add(srcColors[i]);
                    if (hasUV0) newUV0.Add(srcUV0[i]);
                    if (hasUV1) newUV1.Add(srcUV1[i]);
                    if (hasUV2) newUV2.Add(srcUV2[i]);
                }
            }

            for (int t = 0; t < srcTriangles.Length; t++)
            {
                Vector3 originalPos = srcVertices[srcTriangles[t]];
                newTriangles[t] = uniqueVertexMap[originalPos];
            }

            mesh.Clear();
            mesh.SetVertices(newVertices);
            mesh.SetTriangles(newTriangles, 0);

            if (newNormals.Count > 0) mesh.SetNormals(newNormals);
            if (newTangents.Count > 0) mesh.SetTangents(newTangents);
            if (hasColors) mesh.SetColors(newColors);
            if (hasUV0) mesh.SetUVs(0, newUV0);
            if (hasUV1) mesh.SetUVs(1, newUV1);
            if (hasUV2) mesh.SetUVs(2, newUV2);

            mesh.RecalculateBounds();
        }

        private void ProcessMesh(Mesh mesh)
        {
            int vertCount = mesh.vertexCount;
            if (vertCount == 0) return;

            List<Vector2> uv0 = new List<Vector2>(vertCount);
            List<Vector2> uv1 = new List<Vector2>(vertCount);
            List<Vector2> uv2 = new List<Vector2>(vertCount);
            List<Vector2> uv3 = new List<Vector2>(vertCount);
            List<Vector2> uv4 = new List<Vector2>(vertCount);
            List<Vector2> uv5 = new List<Vector2>(vertCount);

            mesh.GetUVs(0, uv0);
            mesh.GetUVs(1, uv1);
            mesh.GetUVs(2, uv2);
            mesh.GetUVs(3, uv3);
            mesh.GetUVs(4, uv4);
            mesh.GetUVs(5, uv5);

            // 1. Solid Workflow (6 2D UV channels -> 3 4D UV channels)
            if (uv5.Count == vertCount && uv4.Count == vertCount &&
                uv3.Count == vertCount && uv2.Count == vertCount &&
                uv1.Count == vertCount && uv0.Count == vertCount)
            {
                List<Vector4> mergedUV0 = new List<Vector4>(vertCount);
                List<Vector4> mergedUV1 = new List<Vector4>(vertCount);
                List<Vector4> mergedUV2 = new List<Vector4>(vertCount);

                for (int i = 0; i < vertCount; i++)
                {
                    mergedUV0.Add(new Vector4(uv0[i].x, uv0[i].y, uv1[i].x, uv1[i].y));
                    mergedUV1.Add(new Vector4(uv2[i].x, uv2[i].y, uv3[i].x, uv3[i].y));

                    float decX = (uv4[i].x * 2f) - 1f;
                    float decY = (uv4[i].y * 2f) - 1f;
                    float decZ = (uv5[i].x * 2f) - 1f;
                    float thickness = uv5[i].y;

                    Vector3 decodedNormal = new Vector3(decX, decY, decZ).normalized;
                    mergedUV2.Add(new Vector4(decodedNormal.x, decodedNormal.y, decodedNormal.z, thickness));
                }

                mesh.SetUVs(0, mergedUV0);
                mesh.SetUVs(1, mergedUV1);
                mesh.SetUVs(2, mergedUV2);

                List<Vector2> emptyUV = new List<Vector2>(0);
                mesh.SetUVs(3, emptyUV);
                mesh.SetUVs(4, emptyUV);
                mesh.SetUVs(5, emptyUV);

                mesh.SetTangents(new List<Vector4>(0));
                return;
            }

            // 2. Liquid Workflow (3 2D UV channels: UV0=LiquidFlow, UV1=LiquidCurvature, UV2=LiquidData)
            // Packs UV0 = (Flow.x, Flow.y, Curvature.x, Curvature.y) and keeps UV1 = (Distance, PackedFlagsAndID)
            if (uv5.Count == 0 && uv3.Count == 0 &&
                uv0.Count == vertCount && uv1.Count == vertCount && uv2.Count == vertCount)
            {
                List<Vector4> mergedLiquidUV0 = new List<Vector4>(vertCount);

                for (int i = 0; i < vertCount; i++)
                {
                    mergedLiquidUV0.Add(new Vector4(uv0[i].x, uv0[i].y, uv1[i].x, uv1[i].y));
                }

                mesh.SetUVs(0, mergedLiquidUV0);
                mesh.SetUVs(1, uv2);
                mesh.SetUVs(2, new List<Vector2>(0));
                mesh.SetTangents(new List<Vector4>(0));
            }
        }
    }

    /// <summary>
    /// Automatically configures TextureImporter settings, 2DArray/3D flipbook slicing, and CPU readability
    /// for all B2U-prefixed textures (unless tagged with 'NAPP').
    /// </summary>
    public class B2U_TexturePostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            string fileName = Path.GetFileNameWithoutExtension(assetPath);

            if (!fileName.StartsWith("B2U", StringComparison.OrdinalIgnoreCase) ||
                fileName.IndexOf("NAPP", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }

            TextureImporter importer = (TextureImporter)assetImporter;

            bool useSRGB = false;
            bool useMipmaps = false;
            TextureImporterCompression compression = TextureImporterCompression.Uncompressed;
            FilterMode filter = FilterMode.Bilinear;
            TextureWrapMode wrap = TextureWrapMode.Clamp;

            TextureImporterShape shape = TextureImporterShape.Texture2D;
            int flipColumns = 1;
            int flipRows = 1;
            bool readable = false;

            if (fileName.Contains("Iridescence"))
            {
                // Uses default linear, clamped, bilinear 2D settings
            }
            else if (fileName.Contains("ColorEmission"))
            {
                readable = true;
                //useSRGB = true;
            }
            else if (fileName.Contains("BiomeTexture1") ||
                     fileName.Contains("BiomeTexture2"))
            {
                readable = true;
            }
            else if (fileName.Contains("Property"))
            {
                filter = FilterMode.Point;
                readable = true;
            }
            else if (fileName.Contains("PaintPalette"))
            {
                filter = FilterMode.Point;
                shape = TextureImporterShape.Texture2DArray;
                flipColumns = 1;
                flipRows = 16;
                readable = true;
            }
            else if (fileName.Contains("Detail"))
            {
                useMipmaps = true;
                compression = TextureImporterCompression.CompressedHQ;
                wrap = TextureWrapMode.Repeat;
                shape = TextureImporterShape.Texture2DArray;
                flipColumns = 8;
                flipRows = 8;
            }
            else if (fileName.Contains("Patterns"))
            {
                useMipmaps = true;
                compression = TextureImporterCompression.CompressedHQ;
                wrap = TextureWrapMode.Repeat;
            }
            else if (fileName.Contains("3DNoise"))
            {
                useMipmaps = true;
                wrap = TextureWrapMode.Repeat;
                shape = TextureImporterShape.Texture3D;
                flipColumns = 8;
                flipRows = 8;
            }

            importer.sRGBTexture = useSRGB;
            importer.mipmapEnabled = useMipmaps;
            importer.textureCompression = compression;
            importer.filterMode = filter;
            importer.wrapMode = wrap;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            settings.textureShape = shape;
            settings.readable = readable;
            if (shape != TextureImporterShape.Texture2D)
            {
                settings.flipbookColumns = flipColumns;
                settings.flipbookRows = flipRows;
            }

            importer.SetTextureSettings(settings);
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool needsRefresh = false;

            for (int i = 0; i < importedAssets.Length; i++)
            {
                string fileName = Path.GetFileNameWithoutExtension(importedAssets[i]);

                if (fileName.StartsWith("B2U", StringComparison.OrdinalIgnoreCase) &&
                   (fileName.Contains("BiomeTexture1") || fileName.Contains("BiomeTexture2") ||
                    fileName.Contains("ColorEmission") || fileName.Contains("Property") ||
                    fileName.Contains("Iridescence") || fileName.Contains("Patterns") ||
                    fileName.Contains("Detail") || fileName.Contains("3DNoise") ||
                    fileName.Contains("PaintPalette")))
                {
                    needsRefresh = true;
                    break;
                }
            }

            if (needsRefresh && B2URuntimeUtility.ActiveProfile != null)
            {
                B2URuntimeUtility.PushAllGlobals();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
        }
    }
}
#endif