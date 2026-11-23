using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace B2U
{
    /// <summary>
    /// URP ScriptableRendererFeature that manages volumetric backface depth/normal rendering
    /// and interleaved screen-color copy passes across Crystal, Liquid, Volumetric, and Transparent render queues.
    /// </summary>
    /// <remarks>This class is used to inject new passes into the render pipeline. The settings are controlled by the active B2UShaderProfile. Don't change settings directly.</remarks>
    public class B2UVolumetricRefraction : ScriptableRendererFeature
    {
        public B2UVolumetricRefractionSettings settings = new B2UVolumetricRefractionSettings();
        private B2UMultiQueueRefractionPass m_ScriptablePass;

        /// <summary>
        /// The resolution of the texture relative to the screen resolution.
        /// </summary>
        /// <remarks>I put this here as this was the first feature, but it is used by others and will be moved eventually.</remarks>
        public enum TextureDownsample 
        { 
            /// <summary>
            /// Matches the screen resolution.
            /// </summary>
            Full = 1, 
            /// <summary>
            /// Half the screen resolution.
            /// </summary>
            Half = 2, 
            /// <summary>
            /// A quarter of the screen resolution.
            /// </summary>
            Quarter = 4, 
            /// <summary>
            /// An eighth of the screen resolution.
            /// </summary>
            Eighth = 8 
        }
        /// <summary>
        /// The copy pass mode to use. This is set automatically based on the refraction quality mode.
        /// </summary>
        public enum CopyPassMode 
        { 
            /// <summary>
            /// No copy pass is executed. Any object with volumetric refraction will be erased if another transparent is drawn over it.
            /// </summary>
            None, 
            /// <summary>
            /// A copy pass is executed after crystals are drawn. Crystals can be seen while inside other transparents.
            /// </summary>
            SingleCopyAfterCrystals, 
            /// <summary>
            /// Two copy passes are executed, one after crystals and one after liquids. Crystals can be seen through transparents, and non-volumetric liquids can be seen through volumetric liquids.
            /// </summary>
            DoubleCopyAfterCrystalsAndLiquids 
        }

        public override void Create()
        {
            B2URuntimeUtility.RegisterFeature(this);
            if (settings == null) settings = new B2UVolumetricRefractionSettings();

            m_ScriptablePass = new B2UMultiQueueRefractionPass(settings)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_ScriptablePass == null)
            {
                Create();
            }

            CameraType camType = renderingData.cameraData.cameraType;
            if (camType == CameraType.Preview || camType == CameraType.Reflection)
            {
                return;
            }

            ScriptableRenderPassInput requiredInputs = ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth;
            m_ScriptablePass.ConfigureInput(requiredInputs);

            renderer.EnqueuePass(m_ScriptablePass);
        }

        [Serializable]
        public class B2UVolumetricRefractionSettings
        {
            public Material overrideMaterial;
            public bool drawVolumetric = false;
            public LayerMask volumetricLayer;
            public TextureDownsample volumetricResolution = TextureDownsample.Half;
            public TextureDownsample colorCopyResolution = TextureDownsample.Half;
            public CopyPassMode copyMode = CopyPassMode.None;
        }

        private class B2UMultiQueueRefractionPass : ScriptableRenderPass
        {
            private readonly B2UVolumetricRefractionSettings settings;

            private static readonly int VolumetricRefractionTexId = Shader.PropertyToID("_B2UVolumetricRefractionTexture");
            private static readonly int ColorCopyTexId = Shader.PropertyToID("_B2UColorCopy");
            private static readonly ShaderTagId UniversalForwardTag = new ShaderTagId("UniversalForward");

            private static readonly List<ShaderTagId> TransparentShaderTags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("LightweightForward"),
                new ShaderTagId("SRPDefaultUnlit")
            };

            private struct QueueBand
            {
                public string passName;
                public string copyPassName;
                public RenderQueueRange range;
                public bool doCopy;
            }

            private static readonly QueueBand[] BandsNone =
            {
                new QueueBand
                {
                    passName = "B2U Draw All Transparents (3000-5000)",
                    copyPassName = string.Empty,
                    range = new RenderQueueRange(3000, 5000),
                    doCopy = false
                }
            };

            private static readonly QueueBand[] BandsSingleCopy =
            {
                new QueueBand
                {
                    passName = "B2U Draw Crystals (3000-3049)",
                    copyPassName = string.Empty,
                    range = new RenderQueueRange(3000, 3049),
                    doCopy = false
                },
                new QueueBand
                {
                    passName = "B2U Draw Liquids & Transparents (3050-5000)",
                    copyPassName = "B2U Copy before Liquids & Transparents (3050-5000)",
                    range = new RenderQueueRange(3050, 5000),
                    doCopy = true
                }
            };

            private static readonly QueueBand[] BandsDoubleCopy =
            {
                new QueueBand
                {
                    passName = "B2U Draw Crystals (3000-3049)",
                    copyPassName = string.Empty,
                    range = new RenderQueueRange(3000, 3049),
                    doCopy = false
                },
                new QueueBand
                {
                    passName = "B2U Draw Environmental Liquids (3050-3099)",
                    copyPassName = "B2U Copy before Environmental Liquids (3050-3099)",
                    range = new RenderQueueRange(3050, 3099),
                    doCopy = true
                },
                new QueueBand
                {
                    passName = "B2U Draw Volumetrics (3100-3149)",
                    copyPassName = "B2U Copy before Volumetrics (3100-3149)",
                    range = new RenderQueueRange(3100, 3149),
                    doCopy = true
                },
                new QueueBand
                {
                    passName = "B2U Draw Standard Transparents (3150-5000)",
                    copyPassName = string.Empty,
                    range = new RenderQueueRange(3150, 5000),
                    doCopy = false
                }
            };

            public B2UMultiQueueRefractionPass(B2UVolumetricRefractionSettings settings)
            {
                this.settings = settings;
            }

            private class BackfaceData
            {
                public RendererListHandle rendererList;
            }

            private class CopyData
            {
                public TextureHandle sourceTexture;
            }

            private class DrawData
            {
                public RendererListHandle rendererList;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer) return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                TextureHandle volumetricColorTarget = TextureHandle.nullHandle;

                // 1. Volumetric Backface Pass (renders backface normals and depth for volumetric refraction)
                if (settings.drawVolumetric && settings.overrideMaterial != null)
                {
                    using (var builder = renderGraph.AddRasterRenderPass<BackfaceData>("B2U Volumetric Backface", out var passData))
                    {
                        int downsample = Mathf.Max(1, (int)settings.volumetricResolution);

                        TextureDesc colorDesc = resourceData.activeColorTexture.GetDescriptor(renderGraph);
                        colorDesc.name = "_B2UVolumetricColor";
                        colorDesc.width = Mathf.Max(1, colorDesc.width / downsample);
                        colorDesc.height = Mathf.Max(1, colorDesc.height / downsample);
                        colorDesc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                        colorDesc.depthBufferBits = DepthBits.None;
                        colorDesc.msaaSamples = MSAASamples.None;
                        colorDesc.clearBuffer = true;
                        colorDesc.clearColor = Color.clear;
                        colorDesc.filterMode = downsample > 1 ? FilterMode.Bilinear : FilterMode.Point;
                        volumetricColorTarget = renderGraph.CreateTexture(colorDesc);

                        TextureDesc depthDesc = resourceData.activeColorTexture.GetDescriptor(renderGraph);
                        depthDesc.name = "_B2UVolumetricDepth";
                        depthDesc.width = Mathf.Max(1, depthDesc.width / downsample);
                        depthDesc.height = Mathf.Max(1, depthDesc.height / downsample);
                        depthDesc.colorFormat = GraphicsFormat.None;
                        depthDesc.depthBufferBits = DepthBits.Depth16;
                        depthDesc.msaaSamples = MSAASamples.None;
                        depthDesc.clearBuffer = true;
                        depthDesc.filterMode = FilterMode.Point;
                        TextureHandle depthTarget = renderGraph.CreateTexture(depthDesc);

                        SortingSettings sorting = new SortingSettings(cameraData.camera)
                        {
                            criteria = cameraData.defaultOpaqueSortFlags
                        };

                        DrawingSettings drawing = new DrawingSettings(UniversalForwardTag, sorting)
                        {
                            overrideMaterial = settings.overrideMaterial,
                            overrideMaterialPassIndex = 0
                        };

                        FilteringSettings filtering = new FilteringSettings(RenderQueueRange.all, settings.volumetricLayer);
                        passData.rendererList = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));

                        builder.UseRendererList(passData.rendererList);
                        if (resourceData.cameraDepthTexture.IsValid())
                        {
                            builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                        }
                        builder.SetRenderAttachment(volumetricColorTarget, 0);
                        builder.SetRenderAttachmentDepth(depthTarget, AccessFlags.Write);
                        builder.SetGlobalTextureAfterPass(volumetricColorTarget, VolumetricRefractionTexId);

                        builder.SetRenderFunc(static (BackfaceData data, RasterGraphContext context) =>
                        {
                            context.cmd.DrawRendererList(data.rendererList);
                        });
                    }
                }

                // 2. Select pre-allocated queue bands based on active refraction copy mode
                QueueBand[] bands = settings.copyMode switch
                {
                    CopyPassMode.SingleCopyAfterCrystals => BandsSingleCopy,
                    CopyPassMode.DoubleCopyAfterCrystalsAndLiquids => BandsDoubleCopy,
                    _ => BandsNone
                };

                TextureHandle copyTarget = TextureHandle.nullHandle;
                if (settings.copyMode != CopyPassMode.None)
                {
                    int copyDownsample = Mathf.Max(1, (int)settings.colorCopyResolution);
                    TextureDesc copyDesc = resourceData.activeColorTexture.GetDescriptor(renderGraph);
                    copyDesc.name = "_B2USharedColorCopy";
                    copyDesc.width = Mathf.Max(1, copyDesc.width / copyDownsample);
                    copyDesc.height = Mathf.Max(1, copyDesc.height / copyDownsample);
                    copyDesc.depthBufferBits = DepthBits.None;
                    copyDesc.msaaSamples = MSAASamples.None;
                    copyDesc.clearBuffer = false;
                    copyDesc.filterMode = copyDownsample > 1 ? FilterMode.Bilinear : FilterMode.Point;

                    copyTarget = renderGraph.CreateTexture(copyDesc);
                }

                // 3. Execute Interleaved Screen Copies and Transparent Queue Draws
                bool hasCopiedAtLeastOnce = false;

                for (int i = 0; i < bands.Length; i++)
                {
                    QueueBand band = bands[i];

                    if (band.doCopy && copyTarget.IsValid())
                    {
                        using (var builder = renderGraph.AddRasterRenderPass<CopyData>(band.copyPassName, out var passData))
                        {
                            passData.sourceTexture = resourceData.activeColorTexture;
                            builder.UseTexture(passData.sourceTexture, AccessFlags.Read);
                            builder.SetRenderAttachment(copyTarget, 0);
                            builder.SetGlobalTextureAfterPass(copyTarget, ColorCopyTexId);

                            builder.SetRenderFunc(static (CopyData data, RasterGraphContext context) =>
                            {
                                Blitter.BlitTexture(context.cmd, data.sourceTexture, new Vector4(1f, 1f, 0f, 0f), 0.0f, false);
                            });
                        }
                        hasCopiedAtLeastOnce = true;
                    }

                    using (var builder = renderGraph.AddRasterRenderPass<DrawData>(band.passName, out var passData))
                    {
                        DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                            TransparentShaderTags,
                            renderingData,
                            cameraData,
                            lightData,
                            SortingCriteria.CommonTransparent
                        );

                        FilteringSettings filtering = new FilteringSettings(band.range, cameraData.camera.cullingMask);
                        passData.rendererList = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));

                        builder.UseRendererList(passData.rendererList);

                        // Declare read dependencies so RenderGraph keeps transient textures alive across all transparent bands
                        if (volumetricColorTarget.IsValid())
                        {
                            builder.UseTexture(volumetricColorTarget, AccessFlags.Read);
                        }
                        if (hasCopiedAtLeastOnce && copyTarget.IsValid())
                        {
                            builder.UseTexture(copyTarget, AccessFlags.Read);
                        }

                        builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                        builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);

                        builder.SetRenderFunc(static (DrawData data, RasterGraphContext context) =>
                        {
                            context.cmd.DrawRendererList(data.rendererList);
                        });
                    }
                }
            }
        }
    }
}