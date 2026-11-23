using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace B2U
{
    /// <summary>
    /// URP ScriptableRendererFeature that renders screen-space or world-space outlines using either
    /// the camera depth buffer or a custom Object ID color buffer.
    /// </summary>
    /// <remarks>This class is used to inject new passes into the render pipeline. The settings are controlled by the active B2UShaderProfile. Don't change settings directly.</remarks>
    public class B2UEdgeDetection : ScriptableRendererFeature
    {
        public B2UEdgeDetectionSettings settings = new B2UEdgeDetectionSettings();
        private B2UEdgeDetectionPass m_BufferPass;
        private B2UEdgeFullscreenPass m_FullscreenPass;

        public override void Create()
        {
            B2URuntimeUtility.RegisterFeature(this);
            if (settings == null) settings = new B2UEdgeDetectionSettings();

            m_BufferPass = new B2UEdgeDetectionPass(settings)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques
            };

            m_FullscreenPass = new B2UEdgeFullscreenPass(settings)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingSkybox
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings == null || !settings.active || settings.fullscreenMaterial == null)
            {
                return;
            }

            // In custom Object ID buffer mode, an override material is required to populate _B2UEdgeBuffer
            if (!settings.depthMode && settings.overrideMaterial == null)
            {
                return;
            }

            CameraType camType = renderingData.cameraData.cameraType;
            if (camType == CameraType.Preview || camType == CameraType.Reflection)
            {
                return;
            }

            if (m_BufferPass == null || m_FullscreenPass == null)
            {
                Create();
            }

            if (!settings.depthMode)
            {
                renderer.EnqueuePass(m_BufferPass);
            }

            // Ensure URP generates the camera depth texture for depth-mode edge detection and world-space sizing
            m_FullscreenPass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(m_FullscreenPass);
        }

        [Serializable]
        public class B2UEdgeDetectionSettings
        {
            public bool active = true;
            public bool depthMode = true;
            public B2UVolumetricRefraction.TextureDownsample edgeBufferResolution = B2UVolumetricRefraction.TextureDownsample.Half;

            [Tooltip("The material that renders meshes into solid colors based on Object ID.")]
            public Material overrideMaterial;

            [Tooltip("The Shader Graph material containing the B2U_EdgeDetection logic.")]
            public Material fullscreenMaterial;

            public LayerMask layerMask = -1;
        }

        /// <summary>
        /// Frame-scoped ContextItem used to pass the transient Object ID edge buffer handle
        /// from B2UEdgeDetectionPass to B2UEdgeFullscreenPass inside the RenderGraph.
        /// </summary>
        private class EdgeBufferFrameData : ContextItem
        {
            public TextureHandle edgeBufferHandle = TextureHandle.nullHandle;

            public override void Reset()
            {
                edgeBufferHandle = TextureHandle.nullHandle;
            }
        }

        private class B2UEdgeDetectionPass : ScriptableRenderPass
        {
            private readonly B2UEdgeDetectionSettings settings;

            private static readonly int EdgeBufferTexId = Shader.PropertyToID("_B2UEdgeBuffer");
            private static readonly ShaderTagId UniversalForwardTag = new ShaderTagId("UniversalForward");
            private static readonly ShaderTagId UniversalForwardOnlyTag = new ShaderTagId("UniversalForwardOnly");
            private static readonly ShaderTagId LightweightForwardTag = new ShaderTagId("LightweightForward");
            private static readonly ShaderTagId SRPDefaultUnlitTag = new ShaderTagId("SRPDefaultUnlit");

            public B2UEdgeDetectionPass(B2UEdgeDetectionSettings settings)
            {
                this.settings = settings;
            }

            private class PassData
            {
                public RendererListHandle rendererList;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer) return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();

                int div = Mathf.Max(1, (int)settings.edgeBufferResolution);
                int width = Mathf.Max(1, cameraData.cameraTargetDescriptor.width / div);
                int height = Mathf.Max(1, cameraData.cameraTargetDescriptor.height / div);

                TextureDesc colorDesc = new TextureDesc(width, height)
                {
                    name = "_B2UEdgeColorTarget",
                    colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    depthBufferBits = DepthBits.None,
                    msaaSamples = MSAASamples.None,
                    clearBuffer = true,
                    clearColor = Color.black,
                    filterMode = div > 1 ? FilterMode.Bilinear : FilterMode.Point
                };

                TextureDesc depthDesc = new TextureDesc(width, height)
                {
                    name = "_B2UEdgeDepthTarget",
                    colorFormat = GraphicsFormat.None,
                    depthBufferBits = DepthBits.Depth32,
                    msaaSamples = MSAASamples.None,
                    clearBuffer = true,
                    filterMode = FilterMode.Point
                };

                TextureHandle edgeColorTarget = renderGraph.CreateTexture(colorDesc);
                TextureHandle edgeDepthTarget = renderGraph.CreateTexture(depthDesc);

                // Store the handle in frameData so the fullscreen pass can declare a read dependency on it
                EdgeBufferFrameData edgeFrameData = frameData.GetOrCreate<EdgeBufferFrameData>();
                edgeFrameData.edgeBufferHandle = edgeColorTarget;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("B2U Edge Detection Buffer", out var passData))
                {
                    SortingSettings sorting = new SortingSettings(cameraData.camera)
                    {
                        criteria = SortingCriteria.CommonOpaque
                    };

                    DrawingSettings drawing = new DrawingSettings(UniversalForwardTag, sorting)
                    {
                        overrideMaterial = settings.overrideMaterial,
                        overrideMaterialPassIndex = 0
                    };

                    drawing.SetShaderPassName(1, UniversalForwardOnlyTag);
                    drawing.SetShaderPassName(2, LightweightForwardTag);
                    drawing.SetShaderPassName(3, SRPDefaultUnlitTag);

                    FilteringSettings filtering = new FilteringSettings(new RenderQueueRange(0, 3050), settings.layerMask);

                    passData.rendererList = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));
                    builder.UseRendererList(passData.rendererList);

                    builder.SetRenderAttachment(edgeColorTarget, 0);
                    builder.SetRenderAttachmentDepth(edgeDepthTarget, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(edgeColorTarget, EdgeBufferTexId);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        context.cmd.DrawRendererList(data.rendererList);
                    });
                }
            }
        }

        private class B2UEdgeFullscreenPass : ScriptableRenderPass
        {
            private readonly B2UEdgeDetectionSettings settings;

            public B2UEdgeFullscreenPass(B2UEdgeDetectionSettings settings)
            {
                this.settings = settings;
            }

            private class CopyPassData
            {
                public TextureHandle source;
            }

            private class BlitPassData
            {
                public Material material;
                public TextureHandle source;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer) return;

                TextureHandle activeColor = resourceData.activeColorTexture;

                // 1. Allocate temporary color target for safe read/write blitting
                TextureDesc destDesc = renderGraph.GetTextureDesc(activeColor);
                destDesc.name = "_B2UEdgeTempColor";
                destDesc.depthBufferBits = DepthBits.None;
                destDesc.msaaSamples = MSAASamples.None;
                destDesc.clearBuffer = false;
                TextureHandle tempColor = renderGraph.CreateTexture(destDesc);

                // 2. Copy Pass: Active Color -> Temp Color
                using (var builder = renderGraph.AddRasterRenderPass<CopyPassData>("B2U Edge Copy Pass", out var passData))
                {
                    passData.source = activeColor;
                    builder.UseTexture(passData.source, AccessFlags.Read);
                    builder.SetRenderAttachment(tempColor, 0);

                    builder.SetRenderFunc(static (CopyPassData data, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), 0.0f, false);
                    });
                }

                // 3. Process Pass: Temp Color -> Active Color with Edge Detection Material
                using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>("B2U Edge Fullscreen Blit", out var passData))
                {
                    passData.material = settings.fullscreenMaterial;
                    passData.source = tempColor;

                    builder.UseTexture(passData.source, AccessFlags.Read);

                    if (resourceData.cameraDepthTexture.IsValid())
                    {
                        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                    }

                    if (!settings.depthMode && frameData.Contains<EdgeBufferFrameData>())
                    {
                        TextureHandle edgeBuf = frameData.Get<EdgeBufferFrameData>().edgeBufferHandle;
                        if (edgeBuf.IsValid())
                        {
                            builder.UseTexture(edgeBuf, AccessFlags.Read);
                        }
                    }

                    builder.SetRenderAttachment(activeColor, 0);

                    builder.SetRenderFunc(static (BlitPassData data, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                    });
                }
            }
        }
    }
}