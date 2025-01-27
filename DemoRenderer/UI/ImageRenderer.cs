﻿using BepuUtilities;
using BepuUtilities.Memory;
using DemoContentLoader;
using DemoRenderer.Attributes;
using SharpDX.Direct3D11;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DemoRenderer.UI
{
    /// <summary>
    /// GPU-relevant information for the rendering of a single image instance.
    /// </summary>
    public struct ImageInstance
    {
        /// <summary>
        /// Packed location of the minimum corner of the glyph. Lower 16 bits is X, upper 16 bits is Y. Should be scaled by PackedToScreen.
        /// </summary>
        public uint PackedMinimum;
        /// <summary>
        /// Packed horizontal axis used by the glyph. Lower 16 bits is X, upper 16 bits is Y. UNORM packed across a range from -1.0 at 0 to 1.0 at 65534.
        /// </summary>
        public uint PackedHorizontalAxis;
        /// <summary>
        /// Packed width and height. Width is in the lower 16 bits, height is in the upper 16 bits.
        /// </summary>
        public uint PackedSize;
        /// <summary>
        /// RGBA color, packed in a UNORM manner such that bits 0 through 7 are R, bits 8 through 15 are G, bits 16 through 23 are B, and bits 24 through 31 are A.
        /// </summary>
        public uint PackedColor;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ImageInstance(in Vector2 start, in Vector2 horizontalAxis, in Vector2 size, in Vector4 color, in Vector2 screenToPackedScale)
        {
            //Note that this can do some weird stuff if the position is outside of the target range. For the sake of the demos, we just assume everything's in frame.
            //If you want to use this for a game where you can't guarantee that everything's in frame, this packing range would need to be modified.
            //One simple option is to just set the mapped region to extend beyond the rendered target. It reduces the precision density a bit, but that's not too important.
            PackedMinimum = (uint)(start.X * screenToPackedScale.X) | ((uint)(start.Y * screenToPackedScale.Y) << 16);
            var scaledAxisX = (uint)(horizontalAxis.X * 32767f + 32767f);
            var scaledAxisY = (uint)(horizontalAxis.Y * 32767f + 32767f);
            Debug.Assert(scaledAxisX <= 65534);
            Debug.Assert(scaledAxisY <= 65534);
            PackedHorizontalAxis = scaledAxisX | (scaledAxisY << 16);
            const float sizeScale = 65535f / 4096f;
            var scaledSize = size * sizeScale;
            var clampedSize = Vector2.Max(Vector2.Zero, Vector2.Min(new Vector2(65535f), scaledSize));
            PackedSize = (uint)clampedSize.X | (((uint)clampedSize.Y) << 16);
            PackedColor = Helpers.PackColor(color);
        }
    }

    public class ImageRenderer : IDisposable
    {
        struct VertexConstants
        {
            public Vector2 PackedToScreenScale;
            public Vector2 ScreenToNDCScale;
        }

        const int maximumGlyphsPerDraw = 2048;
#pragma warning disable 0649
        ConstantsBuffer<VertexConstants> vertexConstants;

        [InitialCapacity(1)]
        StructuredBuffer<ImageInstance> instances;
        [QuadIndices(maximumGlyphsPerDraw)]
        IndexBuffer indices;

        [SamplerStateDescription]
        SamplerState sampler;
        [Resource(@"UI\RenderImages.hlsl.vshader")]
        VertexShader vertexShader;
        [Resource(@"UI\RenderImages.hlsl.pshader")]
        PixelShader pixelShader;
#pragma warning restore 0649
        public ImageRenderer()
        {
        }

        /// <summary>
        /// Sets up the rendering pipeline with any glyph rendering specific render state that can be shared across all glyph batches drawn using the GlyphRenderer.Render function.
        /// </summary>
        /// <param name="context">Context to configure.</param>
        public void PreparePipeline(DeviceContext context)
        {
            //This assumes that rasterizer, blend, and depth states have been set appropriately for screenspace transparent rendering.
            context.InputAssembler.InputLayout = null;
            context.InputAssembler.SetIndexBuffer(indices);
            context.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            context.VertexShader.Set(vertexShader);
            context.VertexShader.SetConstantBuffer(0, vertexConstants.Buffer);
            context.VertexShader.SetShaderResource(0, instances.SRV);
            context.PixelShader.Set(pixelShader);
            context.PixelShader.SetSampler(0, sampler);
        }

        public void Render(DeviceContext context, RenderableImage image, Int2 screenResolution, Span<ImageInstance> instances)
        {
            var vertexConstantsData = new VertexConstants
            {
                //These first two scales could be uploaded once, but it would require another buffer. Not important enough.
                //The packed minimum must permit subpixel locations. So, distribute the range 0 to 65535 over the pixel range 0 to resolution.
                PackedToScreenScale = new Vector2(screenResolution.X / 65535f, screenResolution.Y / 65535f),
                ScreenToNDCScale = new Vector2(2f / screenResolution.X, -2f / screenResolution.Y)
            };
            vertexConstants.Update(context, ref vertexConstantsData);
            context.PixelShader.SetShaderResource(0, image.SRV);

            var count = instances.Length;
            var start = 0;
            while (count > 0)
            {
                var batchCount = Math.Min(this.instances.Capacity, count);
                this.instances.Update(context, instances.Slice(start, batchCount));
                context.DrawIndexedInstanced(6, batchCount, 0, 0, 0);
                count -= batchCount;
                start += batchCount;
            }
        }
        bool disposed;
        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                vertexShader.Dispose();
                pixelShader.Dispose();
                instances.Dispose();
                indices.Dispose();
                sampler.Dispose();
                vertexConstants.Dispose();
            }
        }

#if DEBUG
        ~ImageRenderer()
        {
            Helpers.CheckForUndisposed(disposed, this);
        }
#endif
    }
}
