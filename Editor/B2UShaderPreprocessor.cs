#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace B2U
{
    /// <summary>
    /// Pre-build modal checklist window and shader variant stripper for B2U Master shaders.
    /// </summary>
    public class B2UShaderPreprocessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport, IPreprocessShaders
    {
        // Run after URP's built-in ShaderPreprocessor (callbackOrder 0) so URP strips unused pipeline keywords first
        public int callbackOrder => 100;

        public const long UrpBuildLightingMultiplier = 256L;
        public const double UrpInspectorTheoreticalMultiplier = 40576.0;

        internal struct BoolRule
        {
            public B2UShaderProperty Property;
            public string Label;
            public string Keyword;
            public bool AffectsGeometryStages;

            public BoolRule(B2UShaderProperty prop, string label, string keyword, bool affectsGeometryStages = false)
            {
                Property = prop;
                Label = label;
                Keyword = keyword;
                AffectsGeometryStages = affectsGeometryStages;
            }
        }

        internal struct EnumRule
        {
            public B2UShaderProperty Property;
            public string Label;
            public string[] FallbackOptions;

            public EnumRule(B2UShaderProperty prop, string label, params string[] fallbackOptions)
            {
                Property = prop;
                Label = label;
                FallbackOptions = fallbackOptions;
            }
        }

        internal static readonly BoolRule[] s_BoolRules =
        {
            new BoolRule(B2UShaderProperty.GlobalCracksUseEmissionColor, "Global Cracks Use Emission Color", "_B2U_CRACKS_USE_EMISSION_COLOR"),
            new BoolRule(B2UShaderProperty.UseNoiseForSolids, "Use Noise For Solids", "_B2U_USE_WORLD_SPACE_NOISE"),
            new BoolRule(B2UShaderProperty.UseProceduralNoiseForSolids, "Use Procedural Noise For Solids", "_B2U_USE_PROCEDURAL_NOISE_SOLID"),
            new BoolRule(B2UShaderProperty.UseProceduralNoiseForLiquids, "Use Procedural Noise For Liquids", "_B2U_USE_PROCEDURAL_NOISE_LIQUID", affectsGeometryStages: true),
            new BoolRule(B2UShaderProperty.UseBubbleAdvection, "Use Bubble Advection", "_B2U_USE_BUBBLE_ADVECTION", affectsGeometryStages: true),
            new BoolRule(B2UShaderProperty.UseAngleOffset, "Use Angle Offset", "_B2U_ANGLE_OFFSET", affectsGeometryStages: true),
            new BoolRule(B2UShaderProperty.ChromaticAbberationForLiquids, "Chromatic Aberration For Liquids", "_B2U_LIQUID_CHROMATIC_ABBERATION"),
            new BoolRule(B2UShaderProperty.LiquidsHaveVertexDisplacement, "Liquids Have Vertex Displacement", "_B2U_USE_DISPLACEMENT", affectsGeometryStages: true),
            new BoolRule(B2UShaderProperty.TessellatedLiquidsResampleInFragment, "Tessellated Liquids Resample In Fragment", "_B2U_TESS_RESAMPLE_FRAGMENT"),
            new BoolRule(B2UShaderProperty.LiquidEmissionHasFlow, "Liquid Emission Has Flow", "_B2U_EMISSION_HAS_FLOW"),
            new BoolRule(B2UShaderProperty.SolidsHaveSmoothEdges, "Solids Have Smooth Edges", "_B2U_SMOOTH_EDGES"),
            new BoolRule(B2UShaderProperty.DetailMapAffectsHardEdges, "Detail Map Affects Hard Edges", "_B2U_HARD_EDGE_DETAIL"),
            new BoolRule(B2UShaderProperty.EdgeDetectionEnabled, "Edge Detection Enabled", "_B2U_EDGE_DETECTION"),
            new BoolRule(B2UShaderProperty.EdgeDetectionMode, "Edge Detection Depth Mode", "_B2U_EDGE_DETECTION_DEPTH"),
            new BoolRule(B2UShaderProperty.EdgeDetectionSpace, "Edge Detection World Space", "_B2U_EDGE_DETECTION_WORLD_SPACE"),
            new BoolRule(B2UShaderProperty.VolumetricRefractionEnabled, "Volumetric Refraction Pass", "_B2U_VREF_ENABLED"),
            new BoolRule(B2UShaderProperty.VertexRGamma, "Vertex Red Gamma", "_B2U_GRADIENT_GAMMA"),
            new BoolRule(B2UShaderProperty.VertexGGamma, "Vertex Green Gamma", "_B2U_EMISSION_GAMMA"),
            new BoolRule(B2UShaderProperty.VertexBGamma, "Vertex Blue Gamma", "_B2U_PAINT_GAMMA")
        };

        internal static readonly EnumRule[] s_EnumRules =
        {
            new EnumRule(B2UShaderProperty.LightModel, "Light Model (_B2U_LIGHT_MODEL)",
                "_B2U_LIGHT_MODEL_URP", "_B2U_LIGHT_MODEL_LIGHTWEIGHT", "_B2U_LIGHT_MODEL_TOON", "_B2U_LIGHT_MODEL_ANTILIGHT",
                "_B2U_LIGHT_MODEL_RETRO", "_B2U_LIGHT_MODEL_GOOCH", "_B2U_LIGHT_MODEL_FACETED", "_B2U_LIGHT_MODEL_POPART"),
            new EnumRule(B2UShaderProperty.RefractionQualityMode, "Refraction Quality (_B2U_REFRACTION_QUALITY)",
                "_B2U_REFRACTION_QUALITY_LOW", "_B2U_REFRACTION_QUALITY_MEDIUM", "_B2U_REFRACTION_QUALITY_HIGH", "_B2U_REFRACTION_QUALITY_ULTRA"),
            new EnumRule(B2UShaderProperty.CausticMode, "Caustic Mode (_B2U_CAUSTICS)",
                "_B2U_CAUSTICS_OFF", "_B2U_CAUSTICS_MAIN_LIGHT", "_B2U_CAUSTICS_ALL_LIGHTS"),
            new EnumRule(B2UShaderProperty.IridescenceMode, "Iridescence Mode (_B2U_IRIDESCENCE)",
                "_B2U_IRIDESCENCE_AMBIENT", "_B2U_IRIDESCENCE_MAIN_LIGHT", "_B2U_IRIDESCENCE_ALL_LIGHTS"),
            new EnumRule(B2UShaderProperty.CrystalFlareMode, "Crystal Flare Mode (_B2U_CRYSTAL_FLARES)",
                "_B2U_CRYSTAL_FLARES_AMBIENT", "_B2U_CRYSTAL_FLARES_MAIN_LIGHT", "_B2U_CRYSTAL_FLARES_ALL_LIGHTS"),
            new EnumRule(B2UShaderProperty.EdgeMode, "Solid Edge Mode (_B2U_EDGE_MODE)",
                "_B2U_EDGE_MODE_BOTH", "_B2U_EDGE_MODE_CONVEX_ONLY", "_B2U_EDGE_MODE_CONCAVE_ONLY"),
            new EnumRule(B2UShaderProperty.EdgeStyle, "Solid Edge Style (_B2U_EDGE_STYLE)",
                "_B2U_EDGE_STYLE_SOFT_EDGE", "_B2U_EDGE_STYLE_HARD_EDGE", "_B2U_EDGE_STYLE_COLORED_HARD_EDGE")
        };

        private static readonly HashSet<string> s_GeometryOnlyPasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ShadowCaster",
            "DepthOnly",
            "DepthNormals",
            "DepthNormalsOnly",
            "MotionVectors",
            "SceneSelectionPass",
            "ScenePickingPass",
            "Meta"
        };

        internal class ProfileSnapshot
        {
            public string ProfileName;
            public HashSet<string> EnabledKeywords = new HashSet<string>(StringComparer.Ordinal);
        }

        private class SnippetStats
        {
            public int Chunks;
            public long TotalIn;
            public long TotalKept;
            public string LastZeroMatchDetail;
        }

        private static readonly List<ProfileSnapshot> s_SelectedProfileSnapshots = new List<ProfileSnapshot>();
        private static readonly HashSet<string> s_ManualAllowedOnKeywords = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> s_ManualAllowedOffBooleans = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, SnippetStats> s_SnippetStats = new Dictionary<string, SnippetStats>(StringComparer.Ordinal);
        private static B2UShaderProfile s_ReferenceProfile = null;
        private static bool s_StripNonFragmentStages = true;
        private static bool s_BuildConfigured = false;

        private struct KeywordCheck
        {
            public string Name;
            public bool IsBoolean;
            public bool AffectsGeometryStages;
            public int EnumGroupIndex;
            public ShaderKeyword GlobalKw;
            public ShaderKeyword LocalKw;

            public KeywordCheck(Shader shader, string name, bool isBoolean, bool affectsGeometryStages, int enumGroupIndex = -1)
            {
                Name = name;
                IsBoolean = isBoolean;
                AffectsGeometryStages = affectsGeometryStages;
                EnumGroupIndex = enumGroupIndex;
                GlobalKw = new ShaderKeyword(name);
                LocalKw = new ShaderKeyword(shader, name);
            }

            public bool IsEnabled(ShaderKeywordSet set)
            {
                return set.IsEnabled(GlobalKw) || set.IsEnabled(LocalKw);
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            s_BuildConfigured = false;
            s_SnippetStats.Clear();

            if (!Application.isBatchMode)
            {
                bool confirmed = B2UShaderVariantChecklistWindow.ShowModalForBuild();
                if (!confirmed)
                {
                    throw new BuildFailedException("[B2UShaderPreprocessor] Build cancelled by user in the B2U Shader Variant Checklist.");
                }
            }
            else
            {
                B2UShaderVariantChecklistWindow.ApplySavedSettingsForBuild();
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            foreach (var kvp in s_SnippetStats.OrderBy(k => k.Key))
            {
                SnippetStats st = kvp.Value;
                if (st.TotalKept == 0 && st.TotalIn > 0)
                {
                    Debug.LogError(
                        $"[B2UShaderPreprocessor] ZERO VARIANTS KEPT across all {st.Chunks} chunk(s) for {kvp.Key} (total input: {st.TotalIn:N0})! " +
                        $"This pass will render magenta unless its blocking keyword is allowed.\n{st.LastZeroMatchDetail}");
                }
                else
                {
                    Debug.Log($"[B2UShaderPreprocessor] Summary — {kvp.Key}: Kept {st.TotalKept:N0} / {st.TotalIn:N0} across {st.Chunks} chunk(s).");
                }
            }

            s_SnippetStats.Clear();
            s_BuildConfigured = false;
            s_SelectedProfileSnapshots.Clear();
            s_ManualAllowedOnKeywords.Clear();
            s_ManualAllowedOffBooleans.Clear();
            s_ReferenceProfile = null;
        }

        internal static void SetBuildConfiguration(
            List<ProfileSnapshot> profileSnapshots,
            HashSet<string> manualOnKeywords,
            HashSet<string> manualOffBooleans,
            B2UShaderProfile referenceProfile,
            bool stripNonFragmentStages)
        {
            s_SelectedProfileSnapshots.Clear();
            s_SelectedProfileSnapshots.AddRange(profileSnapshots);

            s_ManualAllowedOnKeywords.Clear();
            foreach (string kw in manualOnKeywords) s_ManualAllowedOnKeywords.Add(kw);

            s_ManualAllowedOffBooleans.Clear();
            foreach (string kw in manualOffBooleans) s_ManualAllowedOffBooleans.Add(kw);

            s_ReferenceProfile = referenceProfile;
            s_StripNonFragmentStages = stripNonFragmentStages;
            s_BuildConfigured = true;

            string names = s_SelectedProfileSnapshots.Count > 0
                ? string.Join(", ", s_SelectedProfileSnapshots.Select(p => p.ProfileName))
                : "None (Manual Only)";
            Debug.Log($"[B2UShaderPreprocessor] Build config active — Profiles ({s_SelectedProfileSnapshots.Count}): [{names}] | Manual ON: {s_ManualAllowedOnKeywords.Count} | Manual OFF: {s_ManualAllowedOffBooleans.Count} | Strip Non-Fragment Stages: {s_StripNonFragmentStages}");
        }

        internal static string[] GetEnumOptions(B2UShaderProfile referenceProfile, EnumRule rule)
        {
            if (referenceProfile != null &&
                referenceProfile.propertyBlocks != null &&
                referenceProfile.propertyBlocks.TryGetValue(rule.Property, out var block) &&
                block.enumOptions != null &&
                block.enumOptions.Length > 0)
            {
                return block.enumOptions;
            }
            return rule.FallbackOptions;
        }

        internal static ProfileSnapshot CreateSnapshotFromProfile(B2UShaderProfile profile)
        {
            profile.InitializeDictionaries();
            profile.RestoreSavedValues();

            ProfileSnapshot snapshot = new ProfileSnapshot { ProfileName = profile.name };

            for (int i = 0; i < s_BoolRules.Length; i++)
            {
                if (profile.propertyBlocks.TryGetValue(s_BoolRules[i].Property, out var block) &&
                    block.value is bool b && b)
                {
                    snapshot.EnabledKeywords.Add(s_BoolRules[i].Keyword);
                }
            }

            for (int i = 0; i < s_EnumRules.Length; i++)
            {
                string[] options = GetEnumOptions(profile, s_EnumRules[i]);
                int index = 0;
                if (profile.propertyBlocks.TryGetValue(s_EnumRules[i].Property, out var block) && block.value != null)
                {
                    index = Mathf.Clamp(Convert.ToInt32(block.value), 0, options.Length - 1);
                }
                snapshot.EnabledKeywords.Add(options[index]);
            }

            return snapshot;
        }

        private static bool IsSeparatedStagePlatform(ShaderCompilerPlatform platform)
        {
            return platform == ShaderCompilerPlatform.D3D ||
                   platform == ShaderCompilerPlatform.Metal ||
                   platform == ShaderCompilerPlatform.PS4 ||
                   platform == ShaderCompilerPlatform.XboxOneD3D11 ||
                   platform == ShaderCompilerPlatform.XboxOneD3D12;
        }

        /// <summary>
        /// Discovers all checklist keywords that appear ON in at least one variant of this chunk in a single pass.
        /// Unlike checking modulation, this catches keywords that are 100% ON across an entire chunk.
        /// </summary>
        private static List<KeywordCheck> DiscoverPresentKeywordsInChunk(Shader shader, IList<ShaderCompilerData> data)
        {
            List<KeywordCheck> remaining = new List<KeywordCheck>(48);

            for (int i = 0; i < s_BoolRules.Length; i++)
            {
                remaining.Add(new KeywordCheck(shader, s_BoolRules[i].Keyword, true, s_BoolRules[i].AffectsGeometryStages));
            }

            for (int i = 0; i < s_EnumRules.Length; i++)
            {
                string[] opts = GetEnumOptions(s_ReferenceProfile, s_EnumRules[i]);
                for (int o = 0; o < opts.Length; o++)
                {
                    remaining.Add(new KeywordCheck(shader, opts[o], false, false, i));
                }
            }

            List<KeywordCheck> present = new List<KeywordCheck>(remaining.Count);
            int count = data.Count;

            for (int i = 0; i < count && remaining.Count > 0; i++)
            {
                ShaderKeywordSet kwSet = data[i].shaderKeywordSet;
                for (int r = remaining.Count - 1; r >= 0; r--)
                {
                    if (remaining[r].IsEnabled(kwSet))
                    {
                        present.Add(remaining[r]);
                        remaining.RemoveAt(r);
                    }
                }
            }

            return present;
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (data == null || data.Count == 0)
            {
                return;
            }

            string shaderName = shader.name;
            if (!shaderName.Contains("B2U Master", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!s_BuildConfigured)
            {
                B2UShaderVariantChecklistWindow.ApplySavedSettingsForBuild();
            }

            string snippetKey = $"{shaderName} ({snippet.passName} - {snippet.shaderType})";
            if (!s_SnippetStats.TryGetValue(snippetKey, out SnippetStats stats))
            {
                stats = new SnippetStats();
                s_SnippetStats[snippetKey] = stats;
            }
            stats.Chunks++;
            stats.TotalIn += data.Count;

            List<KeywordCheck> activeChecks = DiscoverPresentKeywordsInChunk(shader, data);
            if (activeChecks.Count == 0)
            {
                stats.TotalKept += data.Count;
                return;
            }

            int initialCount = data.Count;
            ulong[] variantMasks = new ulong[initialCount];
            bool[] isLowRefractionVref = new bool[initialCount];

            KeywordCheck lowRefractionCheck = new KeywordCheck(shader, "_B2U_REFRACTION_QUALITY_LOW", false, false);
            KeywordCheck vrefEnabledCheck = new KeywordCheck(shader, "_B2U_VREF_ENABLED", true, false);

            for (int i = 0; i < initialCount; i++)
            {
                ShaderKeywordSet kwSet = data[i].shaderKeywordSet;
                isLowRefractionVref[i] = lowRefractionCheck.IsEnabled(kwSet) && vrefEnabledCheck.IsEnabled(kwSet);

                ulong vm = 0UL;
                for (int b = 0; b < activeChecks.Count; b++)
                {
                    if (activeChecks[b].IsEnabled(kwSet))
                    {
                        vm |= (1UL << b);
                    }
                }
                variantMasks[i] = vm;
            }

            bool isGeometryOnlyPass = s_GeometryOnlyPasses.Contains(snippet.passName);
            bool isNonFragmentStage = snippet.shaderType != ShaderType.Fragment &&
                                      IsSeparatedStagePlatform(data[0].shaderCompilerPlatform);
            bool collapseFragmentKeywords = s_StripNonFragmentStages && (isGeometryOnlyPass || isNonFragmentStage);

            List<ShaderCompilerData> keptVariants = FilterVariants(
                data,
                variantMasks,
                isLowRefractionVref,
                activeChecks,
                collapseFragmentKeywords,
                out ulong firstProfileAllowed,
                out ulong firstProfileRequired,
                out ulong prunedRequiredBits);

            if (keptVariants.Count == 0)
            {
                // Chunk has 0 matching variants — strip this chunk completely instead of dumping it into the compiler
                stats.LastZeroMatchDetail = BuildZeroMatchDetail(variantMasks, activeChecks, firstProfileAllowed, firstProfileRequired);
                data.Clear();

                string stageMode = collapseFragmentKeywords ? " [Non-Frag Collapsed]" : "";
                Debug.Log($"[B2UShaderPreprocessor] {snippetKey} [Chunk #{stats.Chunks}]{stageMode}: Stripped all {initialCount} / {initialCount} variants (Kept 0). {stats.LastZeroMatchDetail}");
                return;
            }

            stats.TotalKept += keptVariants.Count;

            if (keptVariants.Count < initialCount)
            {
                data.Clear();
                for (int i = 0; i < keptVariants.Count; i++)
                {
                    data.Add(keptVariants[i]);
                }

                string stageMode = collapseFragmentKeywords ? " [Non-Frag Collapsed]" : "";
                string prunedNote = "";
                if (prunedRequiredBits != 0UL)
                {
                    List<string> prunedNames = new List<string>();
                    for (int b = 0; b < activeChecks.Count; b++)
                    {
                        if ((prunedRequiredBits & (1UL << b)) != 0UL) prunedNames.Add(activeChecks[b].Name);
                    }
                    prunedNote = $" (Graph-pruned fallback for: {string.Join(", ", prunedNames)})";
                }

                Debug.Log($"[B2UShaderPreprocessor] {snippetKey} [Chunk #{stats.Chunks}]{stageMode}: Stripped {initialCount - data.Count} / {initialCount} variants (Kept {data.Count}).{prunedNote}");
            }

            if (data.Count > 1000)
            {
                LogUnlistedKeywordsDiagnostic(shaderName, snippet, data, activeChecks);
            }
        }

        private static List<ShaderCompilerData> FilterVariants(
            IList<ShaderCompilerData> data,
            ulong[] variantMasks,
            bool[] isLowRefractionVref,
            List<KeywordCheck> activeChecks,
            bool collapseFragmentKeywords,
            out ulong firstProfileAllowed,
            out ulong firstProfileRequired,
            out ulong prunedRequiredBits)
        {
            List<ProfileSnapshot> profilesToEvaluate = s_SelectedProfileSnapshots.Count > 0
                ? s_SelectedProfileSnapshots
                : new List<ProfileSnapshot> { new ProfileSnapshot { ProfileName = "ManualOnly" } };

            ProfileSnapshot canonicalProfile = profilesToEvaluate[0];

            // Pick each enum's canonical option globally from Profile[0] (independent of which options appear in this chunk)
            string[] canonicalEnumOptionName = new string[s_EnumRules.Length];
            for (int e = 0; e < s_EnumRules.Length; e++)
            {
                string[] opts = GetEnumOptions(s_ReferenceProfile, s_EnumRules[e]);
                string chosen = opts[0];
                for (int o = 0; o < opts.Length; o++)
                {
                    if (canonicalProfile.EnabledKeywords.Contains(opts[o]) || s_ManualAllowedOnKeywords.Contains(opts[o]))
                    {
                        chosen = opts[o];
                        break;
                    }
                }
                canonicalEnumOptionName[e] = chosen;
            }

            int profileCount = profilesToEvaluate.Count;
            ulong[] allowedOneMasks = new ulong[profileCount];
            ulong[] requiredOneMasks = new ulong[profileCount];

            for (int p = 0; p < profileCount; p++)
            {
                ProfileSnapshot snap = profilesToEvaluate[p];
                ulong allowedOne = 0UL;
                ulong requiredOne = 0UL;

                for (int b = 0; b < activeChecks.Count; b++)
                {
                    KeywordCheck check = activeChecks[b];
                    string kwName = check.Name;

                    if (collapseFragmentKeywords && !check.AffectsGeometryStages)
                    {
                        // Collapse fragment-only booleans to OFF (0) and fragment-only enums to 1 global canonical option
                        if (!check.IsBoolean && check.EnumGroupIndex >= 0 &&
                            string.Equals(kwName, canonicalEnumOptionName[check.EnumGroupIndex], StringComparison.Ordinal))
                        {
                            allowedOne |= (1UL << b);
                        }
                        continue;
                    }

                    bool inProfile = snap.EnabledKeywords.Contains(kwName);
                    bool manualOn = s_ManualAllowedOnKeywords.Contains(kwName);

                    if (inProfile || manualOn)
                    {
                        allowedOne |= (1UL << b);
                    }

                    if (check.IsBoolean)
                    {
                        bool isVolumetricPassKw = kwName == "_B2U_VREF_ENABLED";
                        bool allowedOff = (!inProfile && s_SelectedProfileSnapshots.Count > 0) ||
                                          s_ManualAllowedOffBooleans.Contains(kwName) ||
                                          isVolumetricPassKw;

                        if (!allowedOff && (inProfile || manualOn))
                        {
                            requiredOne |= (1UL << b);
                        }
                    }
                }

                // Only apply default-option fallback if neither this profile nor manual checkboxes selected ANY option for enum e
                if (!collapseFragmentKeywords)
                {
                    for (int e = 0; e < s_EnumRules.Length; e++)
                    {
                        string[] opts = GetEnumOptions(s_ReferenceProfile, s_EnumRules[e]);
                        bool configuredAny = false;
                        for (int o = 0; o < opts.Length; o++)
                        {
                            if (snap.EnabledKeywords.Contains(opts[o]) || s_ManualAllowedOnKeywords.Contains(opts[o]))
                            {
                                configuredAny = true;
                                break;
                            }
                        }

                        if (!configuredAny)
                        {
                            for (int b = 0; b < activeChecks.Count; b++)
                            {
                                if (activeChecks[b].EnumGroupIndex == e &&
                                    string.Equals(activeChecks[b].Name, opts[0], StringComparison.Ordinal))
                                {
                                    allowedOne |= (1UL << b);
                                    break;
                                }
                            }
                        }
                    }
                }

                allowedOneMasks[p] = allowedOne;
                requiredOneMasks[p] = requiredOne;
            }

            firstProfileAllowed = allowedOneMasks[0];
            firstProfileRequired = requiredOneMasks[0];
            prunedRequiredBits = 0UL;

            int initialCount = data.Count;
            bool[] keepVariant = new bool[initialCount];

            for (int p = 0; p < profileCount; p++)
            {
                ulong allowedOne = allowedOneMasks[p];
                ulong requiredOne = requiredOneMasks[p];

                Dictionary<ulong, HashSet<ulong>> flexToReqMasks = new Dictionary<ulong, HashSet<ulong>>(16);

                for (int i = 0; i < initialCount; i++)
                {
                    if (!collapseFragmentKeywords && isLowRefractionVref[i]) continue;

                    ulong vm = variantMasks[i];
                    if ((vm & ~allowedOne) != 0UL) continue;

                    ulong flexKey = vm & ~requiredOne;
                    ulong reqBits = vm & requiredOne;

                    if (!flexToReqMasks.TryGetValue(flexKey, out HashSet<ulong> reqSet))
                    {
                        reqSet = new HashSet<ulong>();
                        flexToReqMasks[flexKey] = reqSet;
                    }
                    reqSet.Add(reqBits);
                }

                if (flexToReqMasks.Count == 0) continue;

                Dictionary<ulong, HashSet<ulong>> flexToMaximal = new Dictionary<ulong, HashSet<ulong>>(flexToReqMasks.Count);
                foreach (var kvp in flexToReqMasks)
                {
                    List<ulong> reqList = new List<ulong>(kvp.Value);
                    HashSet<ulong> maximal = new HashSet<ulong>();

                    for (int a = 0; a < reqList.Count; a++)
                    {
                        ulong m = reqList[a];
                        bool isStrictSubset = false;
                        for (int b = 0; b < reqList.Count; b++)
                        {
                            if (a == b) continue;
                            ulong other = reqList[b];
                            if ((m & other) == m && other != m)
                            {
                                isStrictSubset = true;
                                break;
                            }
                        }

                        if (!isStrictSubset)
                        {
                            maximal.Add(m);
                            if (m != requiredOne)
                            {
                                prunedRequiredBits |= (requiredOne & ~m);
                            }
                        }
                    }

                    flexToMaximal[kvp.Key] = maximal;
                }

                for (int i = 0; i < initialCount; i++)
                {
                    if (keepVariant[i] || (!collapseFragmentKeywords && isLowRefractionVref[i])) continue;

                    ulong vm = variantMasks[i];
                    if ((vm & ~allowedOne) != 0UL) continue;

                    ulong flexKey = vm & ~requiredOne;
                    ulong reqBits = vm & requiredOne;

                    if (flexToMaximal.TryGetValue(flexKey, out HashSet<ulong> maxSet) && maxSet.Contains(reqBits))
                    {
                        keepVariant[i] = true;
                    }
                }
            }

            List<ShaderCompilerData> keptVariants = new List<ShaderCompilerData>(64);
            for (int i = 0; i < initialCount; i++)
            {
                if (keepVariant[i]) keptVariants.Add(data[i]);
            }

            return keptVariants;
        }

        private static string BuildZeroMatchDetail(
            ulong[] variantMasks,
            List<KeywordCheck> activeChecks,
            ulong allowedOne0,
            ulong requiredOne0)
        {
            int bestIdx = 0;
            int minDisallowed = int.MaxValue;
            for (int i = 0; i < variantMasks.Length; i++)
            {
                ulong bad = variantMasks[i] & ~allowedOne0;
                int count = 0;
                while (bad != 0UL) { bad &= (bad - 1UL); count++; }
                if (count < minDisallowed)
                {
                    minDisallowed = count;
                    bestIdx = i;
                    if (count <= 1) break;
                }
            }

            List<string> blockingNames = new List<string>();
            ulong bestBad = variantMasks[bestIdx] & ~allowedOne0;
            int firstBlockBit = -1;
            for (int b = 0; b < activeChecks.Count; b++)
            {
                if ((bestBad & (1UL << b)) != 0UL)
                {
                    if (firstBlockBit == -1) firstBlockBit = b;
                    blockingNames.Add(activeChecks[b].Name);
                }
            }

            string offCountNote = "";
            if (firstBlockBit != -1)
            {
                int offCount = 0;
                ulong bitMask = 1UL << firstBlockBit;
                for (int i = 0; i < variantMasks.Length; i++)
                {
                    if ((variantMasks[i] & bitMask) == 0UL) offCount++;
                }
                offCountNote = $" ({activeChecks[firstBlockBit].Name} was OFF in {offCount:N0}/{variantMasks.Length:N0} variants of this chunk)";
            }

            return $"Closest had {minDisallowed} disallowed ON: [{string.Join(", ", blockingNames)}]{offCountNote}";
        }

        private static void LogUnlistedKeywordsDiagnostic(
            string shaderName,
            ShaderSnippetData snippet,
            IList<ShaderCompilerData> data,
            List<KeywordCheck> activeChecks)
        {
            HashSet<string> knownNames = new HashSet<string>(activeChecks.Select(c => c.Name), StringComparer.Ordinal);
            HashSet<string> seenAll = new HashSet<string>(StringComparer.Ordinal);

            int sampleCount = Math.Min(data.Count, 256);
            int step = Math.Max(1, data.Count / sampleCount);

            for (int i = 0; i < data.Count; i += step)
            {
                ShaderKeyword[] kws = data[i].shaderKeywordSet.GetShaderKeywords();
                for (int k = 0; k < kws.Length; k++)
                {
                    string n = kws[k].name;
                    if (!knownNames.Contains(n) &&
                        (n.StartsWith("_B2U_", StringComparison.Ordinal) || n.StartsWith("_IS_", StringComparison.Ordinal)))
                    {
                        seenAll.Add(n);
                    }
                }
            }

            if (seenAll.Count > 0)
            {
                Debug.LogWarning(
                    $"[B2UShaderPreprocessor] {shaderName} ({snippet.passName} - {snippet.shaderType}) still has {data.Count} variants. " +
                    $"Unlisted B2U/custom keywords present in variants: [{string.Join(", ", seenAll.OrderBy(x => x))}]");
            }
        }
    }

    /// <summary>
    /// Modal checklist window allowing users to choose which B2UShaderProfiles and runtime keyword overrides
    /// are compiled into the build, with a live per-shader variant estimator.
    /// </summary>
    public class B2UShaderVariantChecklistWindow : EditorWindow
    {
        private const string PrefPrefix = "B2U_BuildChecklist_";

        private class MasterShaderEntry
        {
            public string DisplayName;
            public HashSet<string> DeclaredKeywords = new HashSet<string>(StringComparer.Ordinal);
            public bool IsTessellated;
        }

        private List<B2UShaderProfile> m_AllProfiles = new List<B2UShaderProfile>();
        private List<MasterShaderEntry> m_MasterShaders = new List<MasterShaderEntry>();
        private HashSet<string> m_SelectedProfileGuids = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> m_ManualOnKeywords = new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> m_ManualOffBooleans = new HashSet<string>(StringComparer.Ordinal);

        private bool m_StripNonFragmentStages = true;
        private Vector2 m_ScrollPos;
        private bool m_IsModalBuild = false;
        private bool m_BuildConfirmed = false;
        private bool m_ShowPerShaderBreakdown = true;

        [MenuItem("B2U/Shader Variant Build Checklist...")]
        public static void OpenFromMenu()
        {
            B2UShaderVariantChecklistWindow window = GetWindow<B2UShaderVariantChecklistWindow>(true, "B2U Shader Variant Build Checklist", true);
            window.minSize = new Vector2(640f, 800f);
            window.m_IsModalBuild = false;
            window.InitializeData();
            window.ShowUtility();
        }

        public static bool ShowModalForBuild()
        {
            B2UShaderVariantChecklistWindow window = CreateInstance<B2UShaderVariantChecklistWindow>();
            window.titleContent = new GUIContent("B2U Shader Variant Build Checklist");
            window.minSize = new Vector2(640f, 800f);
            window.m_IsModalBuild = true;
            window.m_BuildConfirmed = false;
            window.InitializeData();
            window.ShowModalUtility();
            return window.m_BuildConfirmed;
        }

        public static void ApplySavedSettingsForBuild()
        {
            B2UShaderVariantChecklistWindow temp = CreateInstance<B2UShaderVariantChecklistWindow>();
            temp.InitializeData();
            temp.PushToPreprocessor();
            DestroyImmediate(temp);
        }

        private void DiscoverMasterShaders()
        {
            m_MasterShaders.Clear();

            List<string> candidateTokens = new List<string>(64);
            for (int i = 0; i < B2UShaderPreprocessor.s_BoolRules.Length; i++)
                candidateTokens.Add(B2UShaderPreprocessor.s_BoolRules[i].Keyword);

            B2UShaderProfile refProf = m_AllProfiles.Count > 0 ? m_AllProfiles[0] : null;
            for (int i = 0; i < B2UShaderPreprocessor.s_EnumRules.Length; i++)
            {
                string[] opts = B2UShaderPreprocessor.GetEnumOptions(refProf, B2UShaderPreprocessor.s_EnumRules[i]);
                for (int o = 0; o < opts.Length; o++) candidateTokens.Add(opts[o]);
            }

            ShaderInfo[] allShaderInfos = ShaderUtil.GetAllShaderInfo();

            for (int i = 0; i < allShaderInfos.Length; i++)
            {
                string sName = allShaderInfos[i].name;
                if (!sName.Contains("B2U Master", StringComparison.OrdinalIgnoreCase)) continue;

                Shader shader = Shader.Find(sName);
                if (shader == null) continue;

                MasterShaderEntry entry = new MasterShaderEntry
                {
                    DisplayName = sName.Replace("Shader Graphs/", ""),
                    IsTessellated = sName.Contains("Tessellated", StringComparison.OrdinalIgnoreCase)
                };

                LocalKeyword[] kws = shader.keywordSpace.keywords;
                for (int k = 0; k < kws.Length; k++)
                {
                    entry.DeclaredKeywords.Add(kws[k].name);
                }

                string shaderAssetPath = AssetDatabase.GetAssetPath(shader);
                if (!string.IsNullOrEmpty(shaderAssetPath))
                {
                    string[] deps = AssetDatabase.GetDependencies(shaderAssetPath, true);
                    for (int d = 0; d < deps.Length; d++)
                    {
                        string depPath = deps[d];
                        if (!depPath.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase) &&
                            !depPath.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase) &&
                            !depPath.EndsWith(".shadersubgraph", StringComparison.OrdinalIgnoreCase) &&
                            !depPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string fullPath = Path.GetFullPath(depPath);
                        if (!File.Exists(fullPath)) continue;

                        string fileContent = File.ReadAllText(fullPath);
                        for (int t = 0; t < candidateTokens.Count; t++)
                        {
                            string token = candidateTokens[t];
                            if (!entry.DeclaredKeywords.Contains(token) &&
                                fileContent.Contains(token, StringComparison.Ordinal))
                            {
                                entry.DeclaredKeywords.Add(token);
                            }
                        }
                    }
                }

                m_MasterShaders.Add(entry);
            }

            m_MasterShaders.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
        }

        private void InitializeData()
        {
            m_AllProfiles.Clear();
            m_SelectedProfileGuids.Clear();
            m_ManualOnKeywords.Clear();
            m_ManualOffBooleans.Clear();

            string[] guids = AssetDatabase.FindAssets("t:B2UShaderProfile");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                B2UShaderProfile profile = AssetDatabase.LoadAssetAtPath<B2UShaderProfile>(path);
                if (profile != null)
                {
                    m_AllProfiles.Add(profile);
                }
            }

            DiscoverMasterShaders();

            HashSet<B2UShaderProfile> sceneProfiles = FindSceneReferencedProfiles();
            bool hasSavedProfilePrefs = EditorPrefs.GetBool(PrefPrefix + "Initialized", false);
            m_StripNonFragmentStages = EditorPrefs.GetBool(PrefPrefix + "StripNonFragmentStages", true);

            for (int i = 0; i < m_AllProfiles.Count; i++)
            {
                B2UShaderProfile profile = m_AllProfiles[i];
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
                bool defaultChecked = sceneProfiles.Contains(profile) || sceneProfiles.Count == 0;

                bool isChecked = hasSavedProfilePrefs
                    ? EditorPrefs.GetBool(PrefPrefix + "Profile_" + guid, defaultChecked)
                    : defaultChecked;

                if (isChecked)
                {
                    m_SelectedProfileGuids.Add(guid);
                }
            }

            for (int i = 0; i < B2UShaderPreprocessor.s_BoolRules.Length; i++)
            {
                string kw = B2UShaderPreprocessor.s_BoolRules[i].Keyword;
                if (EditorPrefs.GetBool(PrefPrefix + "BoolOn_" + kw, false))
                    m_ManualOnKeywords.Add(kw);
                if (EditorPrefs.GetBool(PrefPrefix + "BoolOff_" + kw, false))
                    m_ManualOffBooleans.Add(kw);
            }

            B2UShaderProfile refProfile = m_AllProfiles.Count > 0 ? m_AllProfiles[0] : null;
            for (int i = 0; i < B2UShaderPreprocessor.s_EnumRules.Length; i++)
            {
                string[] options = B2UShaderPreprocessor.GetEnumOptions(refProfile, B2UShaderPreprocessor.s_EnumRules[i]);
                for (int o = 0; o < options.Length; o++)
                {
                    string opt = options[o];
                    if (EditorPrefs.GetBool(PrefPrefix + "EnumOpt_" + opt, false))
                    {
                        m_ManualOnKeywords.Add(opt);
                    }
                }
            }
        }

        private void SavePreferences()
        {
            EditorPrefs.SetBool(PrefPrefix + "Initialized", true);
            EditorPrefs.SetBool(PrefPrefix + "StripNonFragmentStages", m_StripNonFragmentStages);

            for (int i = 0; i < m_AllProfiles.Count; i++)
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_AllProfiles[i]));
                EditorPrefs.SetBool(PrefPrefix + "Profile_" + guid, m_SelectedProfileGuids.Contains(guid));
            }

            for (int i = 0; i < B2UShaderPreprocessor.s_BoolRules.Length; i++)
            {
                string kw = B2UShaderPreprocessor.s_BoolRules[i].Keyword;
                EditorPrefs.SetBool(PrefPrefix + "BoolOn_" + kw, m_ManualOnKeywords.Contains(kw));
                EditorPrefs.SetBool(PrefPrefix + "BoolOff_" + kw, m_ManualOffBooleans.Contains(kw));
            }

            B2UShaderProfile refProfile = m_AllProfiles.Count > 0 ? m_AllProfiles[0] : null;
            for (int i = 0; i < B2UShaderPreprocessor.s_EnumRules.Length; i++)
            {
                string[] options = B2UShaderPreprocessor.GetEnumOptions(refProfile, B2UShaderPreprocessor.s_EnumRules[i]);
                for (int o = 0; o < options.Length; o++)
                {
                    string opt = options[o];
                    EditorPrefs.SetBool(PrefPrefix + "EnumOpt_" + opt, m_ManualOnKeywords.Contains(opt));
                }
            }
        }

        private static HashSet<B2UShaderProfile> FindSceneReferencedProfiles()
        {
            HashSet<B2UShaderProfile> profiles = new HashSet<B2UShaderProfile>();
            if (B2URuntimeUtility.ActiveProfile != null)
            {
                profiles.Add(B2URuntimeUtility.ActiveProfile);
            }

            string[] buildScenes = EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();

            if (buildScenes.Length > 0)
            {
                string[] dependencies = AssetDatabase.GetDependencies(buildScenes, true);
                for (int i = 0; i < dependencies.Length; i++)
                {
                    if (dependencies[i].EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    {
                        B2UShaderProfile p = AssetDatabase.LoadAssetAtPath<B2UShaderProfile>(dependencies[i]);
                        if (p != null) profiles.Add(p);
                    }
                }
            }

            return profiles;
        }

        private void PushToPreprocessor()
        {
            List<B2UShaderPreprocessor.ProfileSnapshot> snapshots = GetSelectedSnapshots();
            B2UShaderProfile refProfile = m_AllProfiles.Count > 0 ? m_AllProfiles[0] : null;
            B2UShaderPreprocessor.SetBuildConfiguration(snapshots, m_ManualOnKeywords, m_ManualOffBooleans, refProfile, m_StripNonFragmentStages);
        }

        private List<B2UShaderPreprocessor.ProfileSnapshot> GetSelectedSnapshots()
        {
            List<B2UShaderPreprocessor.ProfileSnapshot> snapshots = new List<B2UShaderPreprocessor.ProfileSnapshot>();
            for (int i = 0; i < m_AllProfiles.Count; i++)
            {
                B2UShaderProfile profile = m_AllProfiles[i];
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
                if (m_SelectedProfileGuids.Contains(guid))
                {
                    snapshots.Add(B2UShaderPreprocessor.CreateSnapshotFromProfile(profile));
                }
            }
            return snapshots;
        }

        private long CalculateB2UCombinationsForShader(
            MasterShaderEntry shaderEntry,
            List<B2UShaderPreprocessor.ProfileSnapshot> selectedSnapshots,
            B2UShaderProfile refProfile,
            bool geometryOnly = false)
        {
            List<B2UShaderPreprocessor.ProfileSnapshot> profiles = selectedSnapshots.Count > 0
                ? selectedSnapshots
                : new List<B2UShaderPreprocessor.ProfileSnapshot> { new B2UShaderPreprocessor.ProfileSnapshot { ProfileName = "ManualOnly" } };

            int pCount = Mathf.Min(profiles.Count, 20);
            List<uint[]> groupMasksPerProfile = new List<uint[]>(24);

            for (int b = 0; b < B2UShaderPreprocessor.s_BoolRules.Length; b++)
            {
                var rule = B2UShaderPreprocessor.s_BoolRules[b];
                if (geometryOnly && !rule.AffectsGeometryStages) continue;
                if (!shaderEntry.DeclaredKeywords.Contains(rule.Keyword)) continue;

                uint[] masks = new uint[pCount];
                for (int p = 0; p < pCount; p++)
                {
                    bool inProfile = profiles[p].EnabledKeywords.Contains(rule.Keyword);
                    bool allowOff = (!inProfile && selectedSnapshots.Count > 0) || m_ManualOffBooleans.Contains(rule.Keyword) || rule.Keyword == "_B2U_VREF_ENABLED";
                    bool allowOn = inProfile || m_ManualOnKeywords.Contains(rule.Keyword);

                    uint m = 0u;
                    if (allowOff) m |= 1u;
                    if (allowOn) m |= 2u;
                    masks[p] = m;
                }
                groupMasksPerProfile.Add(masks);
            }

            if (!geometryOnly)
            {
                for (int e = 0; e < B2UShaderPreprocessor.s_EnumRules.Length; e++)
                {
                    var rule = B2UShaderPreprocessor.s_EnumRules[e];
                    string[] opts = B2UShaderPreprocessor.GetEnumOptions(refProfile, rule);

                    bool existsOnShader = false;
                    for (int o = 0; o < opts.Length; o++)
                    {
                        if (shaderEntry.DeclaredKeywords.Contains(opts[o]))
                        {
                            existsOnShader = true;
                            break;
                        }
                    }
                    if (!existsOnShader) continue;

                    uint[] masks = new uint[pCount];
                    for (int p = 0; p < pCount; p++)
                    {
                        uint m = 0u;
                        for (int o = 0; o < opts.Length; o++)
                        {
                            if (profiles[p].EnabledKeywords.Contains(opts[o]) || m_ManualOnKeywords.Contains(opts[o]))
                            {
                                m |= (1u << o);
                            }
                        }
                        if (m == 0u) m = 1u;
                        masks[p] = m;
                    }
                    groupMasksPerProfile.Add(masks);
                }
            }

            if (groupMasksPerProfile.Count == 0) return 1L;

            long totalUnionCombos = 0L;
            int totalSubsets = (1 << pCount);

            for (int subset = 1; subset < totalSubsets; subset++)
            {
                int bits = CountBits((uint)subset);
                long intersectionProduct = 1L;

                for (int g = 0; g < groupMasksPerProfile.Count; g++)
                {
                    uint[] masks = groupMasksPerProfile[g];
                    uint commonMask = ~0u;

                    for (int p = 0; p < pCount; p++)
                    {
                        if ((subset & (1 << p)) != 0)
                        {
                            commonMask &= masks[p];
                        }
                    }

                    int commonCount = CountBits(commonMask);
                    if (commonCount == 0)
                    {
                        intersectionProduct = 0L;
                        break;
                    }
                    intersectionProduct *= commonCount;
                }

                if ((bits & 1) == 1)
                    totalUnionCombos += intersectionProduct;
                else
                    totalUnionCombos -= intersectionProduct;
            }

            return Math.Max(0L, totalUnionCombos);
        }

        private static int CountBits(uint v)
        {
            int c = 0;
            while (v != 0u)
            {
                v &= (v - 1u);
                c++;
            }
            return c;
        }

        private static string FormatCompactNumber(double num)
        {
            if (num >= 1_000_000_000d) return $"{(num / 1_000_000_000d):F2}B";
            if (num >= 1_000_000d) return $"{(num / 1_000_000d):F2}M";
            if (num >= 10_000d) return $"{(num / 1_000d):F1}k";
            return num.ToString("N0");
        }

        private static string FormatDuration(int totalMinutes)
        {
            if (totalMinutes < 60) return $"{totalMinutes} mins";
            int hours = totalMinutes / 60;
            int mins = totalMinutes % 60;
            return mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6f);

            EditorGUILayout.HelpBox(
                "IMPORTANT — RUNTIME KEYWORD COMPILATION:\n" +
                "Checking a B2UShaderProfile below automatically compiles all keyword states stored in that profile.\n\n" +
                "If you change any individual keyword or enum value at runtime (via SetB2UShaderPropertyValue, Shader.EnableKeyword, or in-game graphics menus), " +
                "you MUST check those options below to mark them for compilation. Requesting an uncompiled variant at runtime will cause Unity to return a null (magenta error) shader.",
                MessageType.Warning);

            EditorGUILayout.Space(4f);

            List<B2UShaderPreprocessor.ProfileSnapshot> selectedSnapshots = GetSelectedSnapshots();
            HashSet<string> profileOnKeywords = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> profileOffBooleans = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < selectedSnapshots.Count; i++)
            {
                B2UShaderPreprocessor.ProfileSnapshot snap = selectedSnapshots[i];
                foreach (string kw in snap.EnabledKeywords)
                {
                    profileOnKeywords.Add(kw);
                }

                for (int b = 0; b < B2UShaderPreprocessor.s_BoolRules.Length; b++)
                {
                    string bKw = B2UShaderPreprocessor.s_BoolRules[b].Keyword;
                    if (!snap.EnabledKeywords.Contains(bKw))
                    {
                        profileOffBooleans.Add(bKw);
                    }
                }
            }

            m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos);

            // --- SECTION 1: SHADER PROFILES ---
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("1. B2U Shader Profiles to Include", EditorStyles.boldLabel);
            if (GUILayout.Button("All", GUILayout.Width(50f)))
            {
                foreach (var p in m_AllProfiles)
                    m_SelectedProfileGuids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p)));
            }
            if (GUILayout.Button("Scene Only", GUILayout.Width(80f)))
            {
                m_SelectedProfileGuids.Clear();
                foreach (var p in FindSceneReferencedProfiles())
                    m_SelectedProfileGuids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p)));
            }
            if (GUILayout.Button("None", GUILayout.Width(50f)))
            {
                m_SelectedProfileGuids.Clear();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2f);

            if (m_AllProfiles.Count == 0)
            {
                EditorGUILayout.LabelField("No B2UShaderProfile assets found in project.", EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0; i < m_AllProfiles.Count; i++)
                {
                    B2UShaderProfile profile = m_AllProfiles[i];
                    string path = AssetDatabase.GetAssetPath(profile);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    bool isSelected = m_SelectedProfileGuids.Contains(guid);

                    EditorGUILayout.BeginHorizontal();
                    bool newSelected = EditorGUILayout.ToggleLeft($"{profile.name}  ({path})", isSelected);
                    if (newSelected != isSelected)
                    {
                        if (newSelected) m_SelectedProfileGuids.Add(guid);
                        else m_SelectedProfileGuids.Remove(guid);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);

            // --- SECTION 2: BOOLEAN KEYWORDS ---
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("2. Boolean Keywords (Check to Allow Runtime Toggling)", EditorStyles.boldLabel);
            if (GUILayout.Button("Clear Overrides", GUILayout.Width(105f)))
            {
                for (int i = 0; i < B2UShaderPreprocessor.s_BoolRules.Length; i++)
                {
                    string kw = B2UShaderPreprocessor.s_BoolRules[i].Keyword;
                    m_ManualOnKeywords.Remove(kw);
                    m_ManualOffBooleans.Remove(kw);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2f);

            for (int i = 0; i < B2UShaderPreprocessor.s_BoolRules.Length; i++)
            {
                var rule = B2UShaderPreprocessor.s_BoolRules[i];
                string kw = rule.Keyword;

                bool profHasOff = profileOffBooleans.Contains(kw);
                bool profHasOn = profileOnKeywords.Contains(kw);

                bool manualOff = m_ManualOffBooleans.Contains(kw);
                bool manualOn = m_ManualOnKeywords.Contains(kw);

                bool manualBoth = manualOff && manualOn;

                EditorGUILayout.BeginHorizontal();

                bool newManualBoth = EditorGUILayout.ToggleLeft($"{rule.Label}  ({kw})", manualBoth);
                if (newManualBoth != manualBoth)
                {
                    if (newManualBoth)
                    {
                        m_ManualOffBooleans.Add(kw);
                        m_ManualOnKeywords.Add(kw);
                    }
                    else
                    {
                        m_ManualOffBooleans.Remove(kw);
                        m_ManualOnKeywords.Remove(kw);
                    }
                }

                string profileStateLabel = (profHasOff && profHasOn) ? "Profiles: Off & On"
                    : profHasOn ? "Profiles: On"
                    : profHasOff ? "Profiles: Off"
                    : "Profiles: None";

                EditorGUILayout.LabelField(profileStateLabel, EditorStyles.miniLabel, GUILayout.Width(105f));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6f);

            // --- SECTION 3: ENUM KEYWORDS ---
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("3. Enum Keywords", EditorStyles.boldLabel);
            if (GUILayout.Button("Clear Overrides", GUILayout.Width(105f)))
            {
                B2UShaderProfile refProf = m_AllProfiles.Count > 0 ? m_AllProfiles[0] : null;
                for (int i = 0; i < B2UShaderPreprocessor.s_EnumRules.Length; i++)
                {
                    string[] opts = B2UShaderPreprocessor.GetEnumOptions(refProf, B2UShaderPreprocessor.s_EnumRules[i]);
                    for (int o = 0; o < opts.Length; o++) m_ManualOnKeywords.Remove(opts[o]);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2f);

            B2UShaderProfile referenceProfile = m_AllProfiles.Count > 0 ? m_AllProfiles[0] : null;
            for (int i = 0; i < B2UShaderPreprocessor.s_EnumRules.Length; i++)
            {
                var enumRule = B2UShaderPreprocessor.s_EnumRules[i];
                string[] options = B2UShaderPreprocessor.GetEnumOptions(referenceProfile, enumRule);

                EditorGUILayout.LabelField(enumRule.Label, EditorStyles.boldLabel);
                EditorGUI.indentLevel++;

                for (int o = 0; o < options.Length; o++)
                {
                    string opt = options[o];
                    bool fromProfile = profileOnKeywords.Contains(opt);
                    bool manualChecked = m_ManualOnKeywords.Contains(opt);
                    bool effectiveChecked = fromProfile || manualChecked;

                    string displayLabel = fromProfile ? $"{opt}  [From Profile]" : opt;
                    bool nextChecked = EditorGUILayout.ToggleLeft(displayLabel, effectiveChecked);
                    if (nextChecked != effectiveChecked)
                    {
                        if (nextChecked) m_ManualOnKeywords.Add(opt);
                        else m_ManualOnKeywords.Remove(opt);
                    }
                }

                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4f);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6f);

            // --- SECTION 4: LIVE VARIANT CALCULATOR FOOTER ---
            DrawVariantEstimatePanel(selectedSnapshots, referenceProfile);

            EditorGUILayout.Space(6f);

            // --- FOOTER BUTTONS ---
            EditorGUILayout.BeginHorizontal();
            if (m_IsModalBuild)
            {
                if (GUILayout.Button("Cancel Build", GUILayout.Height(30f)))
                {
                    m_BuildConfirmed = false;
                    Close();
                }

                GUI.backgroundColor = new Color(0.4f, 0.85f, 0.45f);
                if (GUILayout.Button("Confirm & Build", GUILayout.Height(30f)))
                {
                    SavePreferences();
                    PushToPreprocessor();
                    m_BuildConfirmed = true;
                    Close();
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                if (GUILayout.Button("Save Checklist Settings", GUILayout.Height(30f)))
                {
                    SavePreferences();
                    PushToPreprocessor();
                    Close();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
        }

        private void DrawVariantEstimatePanel(
            List<B2UShaderPreprocessor.ProfileSnapshot> selectedSnapshots,
            B2UShaderProfile referenceProfile)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            m_StripNonFragmentStages = EditorGUILayout.ToggleLeft(
                "Strip fragment-only keywords from Vertex/Hull/Domain & Shadow/Depth passes (Recommended — cuts build time ~45–50%)",
                m_StripNonFragmentStages);

            EditorGUILayout.Space(2f);

            long totalFragVariants = 0L;
            long totalOtherStageVariants = 0L;
            double totalInspectorTheoretical = 0d;

            m_ShowPerShaderBreakdown = EditorGUILayout.Foldout(
                m_ShowPerShaderBreakdown,
                "Estimated Shader Variant Count (Includes URP Lighting & Pass/Stage Multipliers)",
                true,
                EditorStyles.foldoutHeader);

            for (int i = 0; i < m_MasterShaders.Count; i++)
            {
                MasterShaderEntry shaderEntry = m_MasterShaders[i];
                long fragCombos = CalculateB2UCombinationsForShader(shaderEntry, selectedSnapshots, referenceProfile, geometryOnly: false);
                long geomCombos = m_StripNonFragmentStages
                    ? CalculateB2UCombinationsForShader(shaderEntry, selectedSnapshots, referenceProfile, geometryOnly: true)
                    : fragCombos;

                long mainFragVariants = fragCombos * B2UShaderPreprocessor.UrpBuildLightingMultiplier;
                long otherStagesMultiplier = shaderEntry.IsTessellated ? 9L : 5L;
                long otherStagesUrpAvg = m_StripNonFragmentStages ? 64L : B2UShaderPreprocessor.UrpBuildLightingMultiplier;
                long otherStageVariants = geomCombos * otherStagesUrpAvg * otherStagesMultiplier;
                long shaderTotalCompilations = mainFragVariants + otherStageVariants;

                double theoreticalVariants = fragCombos * B2UShaderPreprocessor.UrpInspectorTheoreticalMultiplier;

                totalFragVariants += mainFragVariants;
                totalOtherStageVariants += otherStageVariants;
                totalInspectorTheoretical += theoreticalVariants;

                if (m_ShowPerShaderBreakdown)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"• {shaderEntry.DisplayName}", EditorStyles.miniLabel, GUILayout.Width(240f));
                    EditorGUILayout.LabelField(
                        $"{fragCombos:N0} frag / {geomCombos:N0} geom combos  →  ~{mainFragVariants:N0} fp (~{shaderTotalCompilations:N0} total)",
                        EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space(3f);

            long totalAcrossPassesAndStages = totalFragVariants + totalOtherStageVariants;

            // Calibrated against real-world clean builds on a 24-core desktop CPU:
            // ~215 complex B2U fragment programs/min, ~1,140 non-fragment stage programs/min, + ~2 min fixed overhead.
            int logicalCores = Mathf.Max(1, SystemInfo.processorCount);
            float coreScale = Mathf.Clamp(logicalCores / 24f, 0.2f, 1.5f);

            float fragRatePerMin = 215f * coreScale;
            float otherRatePerMin = 1140f * coreScale;
            float baseCompileMins = (totalFragVariants / fragRatePerMin) + (totalOtherStageVariants / otherRatePerMin);

            int estMinutesLow = Mathf.Max(2, Mathf.RoundToInt(2f + (baseCompileMins * 0.85f)));
            int estMinutesHigh = Mathf.Max(estMinutesLow + 2, Mathf.RoundToInt(2f + (baseCompileMins * 1.30f)));

            EditorGUILayout.LabelField(
                $"Est. Total Stage Compilations: ~{totalAcrossPassesAndStages:N0} ({totalFragVariants:N0} fp + {totalOtherStageVariants:N0} vp/hp/dp/depth)   |   Inspector Eq: {FormatCompactNumber(totalInspectorTheoretical)}",
                EditorStyles.boldLabel);

            Color prevColor = GUI.contentColor;
            if (estMinutesHigh >= 180)
                GUI.contentColor = new Color(1f, 0.45f, 0.45f);
            else if (estMinutesHigh >= 60)
                GUI.contentColor = new Color(1f, 0.82f, 0.35f);

            EditorGUILayout.LabelField(
                $"Estimated Clean Build Time ({logicalCores} logical cores): ~{FormatDuration(estMinutesLow)} – {FormatDuration(estMinutesHigh)}  (Incremental builds compile only changed shaders)",
                EditorStyles.miniBoldLabel);

            GUI.contentColor = prevColor;

            EditorGUILayout.EndVertical();
        }
    }
}
#endif