using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace B2U
{
    /// <summary>
    /// URP ScriptableRendererFeature that executes an ordered ping-pong chain of active B2U fullscreen effects
    /// (Liquid Immersion, Recolor, Stylize, and Status/Injury overlays) before post-processing.
    /// </summary>
    /// <remarks>This class is used to inject new passes into the render pipeline. The settings are controlled by the active B2UShaderProfile. Don't change settings directly.</remarks>
    public class B2UFullscreenEffects : ScriptableRendererFeature
    {
        public B2UFullscreenEffectsSettings settings = new B2UFullscreenEffectsSettings();
        private B2UFullscreenEffectsPass m_ScriptablePass;

        private static readonly int liquidYID = Shader.PropertyToID("_B2U_Immersion_Liquid_Surface_Y");
        private static readonly int recolorBlendID = Shader.PropertyToID("_B2U_Recolor_Blend");
        private static readonly int stylizeBlendID = Shader.PropertyToID("_B2U_Stylize_Blend");
        private static readonly int injuryStrengthID = Shader.PropertyToID("_B2U_Status_Effect_Injury_Strength");
        private static readonly int statusStrengthID = Shader.PropertyToID("_B2U_Status_Effect_Strength");

        public override void Create()
        {
            B2URuntimeUtility.RegisterFeature(this);
            if (settings == null) settings = new B2UFullscreenEffectsSettings();

            m_ScriptablePass = new B2UFullscreenEffectsPass(settings)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings == null) return;

            CameraType camType = renderingData.cameraData.cameraType;
            if (camType == CameraType.Preview || camType == CameraType.Reflection)
            {
                return;
            }

            if (m_ScriptablePass == null)
            {
                Create();
            }

            float camY = renderingData.cameraData.camera.transform.position.y;
            float liquidY = Shader.GetGlobalFloat(liquidYID);

            bool immersionActive = settings.immersionEnabled && settings.immersionMat != null && (camY < liquidY + settings.liquidTransitionDistance);
            bool recolorActive = settings.recolorEnabled && settings.recolorMat != null && (Shader.GetGlobalFloat(recolorBlendID) > 0f);
            bool stylizeActive = settings.stylizeEnabled && settings.stylizeMat != null && (Shader.GetGlobalFloat(stylizeBlendID) > 0f);
            bool statusActive = settings.statusEnabled && settings.statusMat != null &&
                                (Shader.GetGlobalFloat(injuryStrengthID) > 0f || Shader.GetGlobalFloat(statusStrengthID) > 0f);

            if (!immersionActive && !recolorActive && !stylizeActive && !statusActive)
            {
                return;
            }

            // Dynamically configure only the camera buffers required by the effects active this frame
            ScriptableRenderPassInput requiredInputs = ScriptableRenderPassInput.Color;
            if (recolorActive)
            {
                requiredInputs |= ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
            }
            else if (immersionActive)
            {
                requiredInputs |= ScriptableRenderPassInput.Depth;
            }

            m_ScriptablePass.ConfigureInput(requiredInputs);
            renderer.EnqueuePass(m_ScriptablePass);
        }

        [Serializable]
        public class B2UFullscreenEffectsSettings
        {
            public bool immersionEnabled;
            public Material immersionMat;
            public int immersionDrawOrder = 0;
            public float liquidTransitionDistance = 1f;

            public bool recolorEnabled;
            public Material recolorMat;
            public int recolorDrawOrder = 1;

            public bool stylizeEnabled;
            public Material stylizeMat;
            public int stylizeDrawOrder = 2;

            public bool statusEnabled;
            public Material statusMat;
            public int statusDrawOrder = 3;
        }

        private class B2UFullscreenEffectsPass : ScriptableRenderPass
        {
            private readonly B2UFullscreenEffectsSettings settings;

            private enum EffectType { Immersion, Recolor, Stylize, Status }

            private struct EffectDefinition : IComparable<EffectDefinition>
            {
                public EffectType type;
                public int drawOrder;

                public int CompareTo(EffectDefinition other)
                {
                    return drawOrder.CompareTo(other.drawOrder);
                }
            }

            private struct EffectInfo
            {
                public string passName;
                public Material mat;
            }

            private class PassData
            {
                public Material material;
                public TextureHandle source;
            }

            private class CopyPassData
            {
                public TextureHandle source;
            }

            private readonly EffectDefinition[] m_SortedDefinitions = new EffectDefinition[4];
            private readonly EffectInfo[] m_ActiveEffects = new EffectInfo[4];

            public B2UFullscreenEffectsPass(B2UFullscreenEffectsSettings settings)
            {
                this.settings = settings;

                m_SortedDefinitions[0] = new EffectDefinition { type = EffectType.Immersion };
                m_SortedDefinitions[1] = new EffectDefinition { type = EffectType.Recolor };
                m_SortedDefinitions[2] = new EffectDefinition { type = EffectType.Stylize };
                m_SortedDefinitions[3] = new EffectDefinition { type = EffectType.Status };
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer) return;

                for (int i = 0; i < 4; i++)
                {
                    switch (m_SortedDefinitions[i].type)
                    {
                        case EffectType.Immersion: m_SortedDefinitions[i].drawOrder = settings.immersionDrawOrder; break;
                        case EffectType.Recolor: m_SortedDefinitions[i].drawOrder = settings.recolorDrawOrder; break;
                        case EffectType.Stylize: m_SortedDefinitions[i].drawOrder = settings.stylizeDrawOrder; break;
                        case EffectType.Status: m_SortedDefinitions[i].drawOrder = settings.statusDrawOrder; break;
                    }
                }

                Array.Sort(m_SortedDefinitions);

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                float camY = cameraData.camera.transform.position.y;
                float liquidY = Shader.GetGlobalFloat(liquidYID);

                bool liquidActive = camY < liquidY + settings.liquidTransitionDistance;
                bool recolorActive = Shader.GetGlobalFloat(recolorBlendID) > 0f;
                bool stylizeActive = Shader.GetGlobalFloat(stylizeBlendID) > 0f;
                bool statusActive = Shader.GetGlobalFloat(injuryStrengthID) > 0f || Shader.GetGlobalFloat(statusStrengthID) > 0f;

                int activeCount = 0;

                for (int i = 0; i < 4; i++)
                {
                    switch (m_SortedDefinitions[i].type)
                    {
                        case EffectType.Immersion:
                            if (settings.immersionEnabled && settings.immersionMat != null && liquidActive)
                                m_ActiveEffects[activeCount++] = new EffectInfo { passName = "B2U Immersion Pass", mat = settings.immersionMat };
                            break;
                        case EffectType.Recolor:
                            if (settings.recolorEnabled && settings.recolorMat != null && recolorActive)
                                m_ActiveEffects[activeCount++] = new EffectInfo { passName = "B2U Recolor Pass", mat = settings.recolorMat };
                            break;
                        case EffectType.Stylize:
                            if (settings.stylizeEnabled && settings.stylizeMat != null && stylizeActive)
                                m_ActiveEffects[activeCount++] = new EffectInfo { passName = "B2U Stylize Pass", mat = settings.stylizeMat };
                            break;
                        case EffectType.Status:
                            if (settings.statusEnabled && settings.statusMat != null && statusActive)
                                m_ActiveEffects[activeCount++] = new EffectInfo { passName = "B2U Status Effect Pass", mat = settings.statusMat };
                            break;
                    }
                }

                if (activeCount == 0) return;

                TextureHandle activeColor = resourceData.activeColorTexture;

                TextureDesc desc = activeColor.GetDescriptor(renderGraph);
                desc.clearBuffer = false;
                desc.depthBufferBits = DepthBits.None;
                desc.msaaSamples = MSAASamples.None;
                desc.name = "_B2U_PingPong_0";

                TextureHandle rt0 = renderGraph.CreateTexture(desc);
                TextureHandle rt1 = TextureHandle.nullHandle;

                if (activeCount > 2)
                {
                    desc.name = "_B2U_PingPong_1";
                    rt1 = renderGraph.CreateTexture(desc);
                }

                TextureHandle source = activeColor;
                TextureHandle dest = rt0;

                for (int i = 0; i < activeCount; i++)
                {
                    EffectInfo effect = m_ActiveEffects[i];
                    bool isLastActive = (i == activeCount - 1);

                    if (isLastActive && !source.Equals(activeColor))
                    {
                        dest = activeColor;
                    }

                    using (var builder = renderGraph.AddRasterRenderPass<PassData>(effect.passName, out var passData))
                    {
                        passData.material = effect.mat;
                        passData.source = source;

                        builder.UseTexture(source, AccessFlags.Read);

                        if (resourceData.cameraDepthTexture.IsValid())
                        {
                            builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                        }
                        if (resourceData.cameraNormalsTexture.IsValid())
                        {
                            builder.UseTexture(resourceData.cameraNormalsTexture, AccessFlags.Read);
                        }

                        builder.SetRenderAttachment(dest, 0);

                        builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                        {
                            Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                        });
                    }

                    source = dest;
                    if (activeCount > 2)
                    {
                        dest = dest.Equals(rt0) ? rt1 : rt0;
                    }
                }

                // When exactly 1 effect runs, it blits activeColor -> rt0; copy rt0 back into activeColor
                if (activeCount == 1)
                {
                    using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>("B2U Copy Final Pass", out var passData))
                    {
                        passData.source = rt0;

                        builder.UseTexture(rt0, AccessFlags.Read);
                        builder.SetRenderAttachment(activeColor, 0);

                        builder.SetRenderFunc(static (CopyPassData data, RasterGraphContext context) =>
                        {
                            Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), 0.0f, false);
                        });
                    }
                }
            }
        }
    }
}