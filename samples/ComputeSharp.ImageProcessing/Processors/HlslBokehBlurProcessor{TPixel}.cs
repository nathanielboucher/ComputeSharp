﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing.Processors;
using ImageSharpRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace ComputeSharp.BokehBlur.Processors;

/// <inheritdoc/>
public sealed partial class HlslBokehBlurProcessor
{
    /// <summary>
    /// Applies bokeh blur processing to the image.
    /// </summary>
    /// <remarks>This processor is based on the code from Mike Pound, see <a href="https://github.com/mikepound/convolve">github.com/mikepound/convolve</a>.</remarks>
    public sealed partial class Implementation : ImageProcessor<ImageSharpRgba32>
    {
        /// <summary>
        /// The <see cref="ComputeSharp.GraphicsDevice"/> instance in use.
        /// </summary>
        private readonly GraphicsDevice GraphicsDevice;

        /// <summary>
        /// The kernel radius.
        /// </summary>
        private readonly int Radius;

        /// <summary>
        /// The maximum size of the kernel in either direction.
        /// </summary>
        private readonly int KernelSize;

        /// <summary>
        /// The number of components to use when applying the bokeh blur.
        /// </summary>
        private readonly int ComponentsCount;

        /// <summary>
        /// The kernel parameters to use for the current instance (a: X, b: Y, A: Z, B: W)
        /// </summary>
        private readonly Vector4[] KernelParameters;

        /// <summary>
        /// The kernel components for the current instance.
        /// </summary>
        private readonly Complex64[][] Kernels;

        /// <summary>
        /// The scaling factor for kernel values.
        /// </summary>
        private readonly float KernelsScale;

        /// <summary>
        /// The mapping of initialized complex kernels and parameters, to speed up the initialization of new <see cref="HlslBokehBlurProcessor"/> instances.
        /// </summary>
        private static readonly ConcurrentDictionary<(int Radius, int ComponentsCount), (Vector4[] Parameters, float Scale, Complex64[][] Kernels)> Cache = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="Implementation"/> class.
        /// </summary>
        /// <param name="definition">The <see cref="HlslBokehBlurProcessor"/> defining the processor parameters.</param>
        /// <param name="configuration">The configuration which allows altering default behaviour or extending the library.</param>
        /// <param name="source">The source <see cref="Image{TPixel}"/> instance to modify.</param>
        /// <param name="sourceRectangle">The source <see cref="Rectangle"/> that indicates the area to edit.</param>
        public Implementation(HlslBokehBlurProcessor definition, Configuration configuration, Image<ImageSharpRgba32> source, Rectangle sourceRectangle)
            : base(configuration, source, sourceRectangle)
        {
            GraphicsDevice = definition.GraphicsDevice;
            Radius = definition.Radius;
            KernelSize = Radius * 2 + 1;
            ComponentsCount = definition.Components;

            // Reuse the initialized values from the cache, if possible
            var parameters = (Radius, ComponentsCount);
            if (Cache.TryGetValue(parameters, out var info))
            {
                KernelParameters = info.Parameters;
                KernelsScale = info.Scale;
                Kernels = info.Kernels;
            }
            else
            {
                // Initialize the complex kernels and parameters with the current arguments
                (KernelParameters, KernelsScale) = GetParameters();
                Kernels = CreateComplexKernels();

                NormalizeKernels();

                // Store them in the cache for future use
                Cache.TryAdd(parameters, (KernelParameters, KernelsScale, Kernels));
            }
        }

        /// <summary>
        /// Gets the kernel scales to adjust the component values in each kernel.
        /// </summary>
        private static IReadOnlyList<float> KernelScales { get; } = new[] { 1.4f, 1.2f, 1.2f, 1.2f, 1.2f, 1.2f };

        /// <summary>
        /// Gets the available bokeh blur kernel parameters.
        /// </summary>
        private static IReadOnlyList<Vector4[]> KernelComponents { get; } = new[]
        {
            // 1 component
            new[] { new Vector4(0.862325f, 1.624835f, 0.767583f, 1.862321f) },

            // 2 components
            new[]
            {
                new Vector4(0.886528f, 5.268909f, 0.411259f, -0.548794f),
                new Vector4(1.960518f, 1.558213f, 0.513282f, 4.56111f)
            },

            // 3 components
            new[]
            {
                new Vector4(2.17649f, 5.043495f, 1.621035f, -2.105439f),
                new Vector4(1.019306f, 9.027613f, -0.28086f, -0.162882f),
                new Vector4(2.81511f, 1.597273f, -0.366471f, 10.300301f)
            },

            // 4 components
            new[]
            {
                new Vector4(4.338459f, 1.553635f, -5.767909f, 46.164397f),
                new Vector4(3.839993f, 4.693183f, 9.795391f, -15.227561f),
                new Vector4(2.791880f, 8.178137f, -3.048324f, 0.302959f),
                new Vector4(1.342190f, 12.328289f, 0.010001f, 0.244650f)
            },

            // 5 components
            new[]
            {
                new Vector4(4.892608f, 1.685979f, -22.356787f, 85.91246f),
                new Vector4(4.71187f, 4.998496f, 35.918936f, -28.875618f),
                new Vector4(4.052795f, 8.244168f, -13.212253f, -1.578428f),
                new Vector4(2.929212f, 11.900859f, 0.507991f, 1.816328f),
                new Vector4(1.512961f, 16.116382f, 0.138051f, -0.01f)
            },

            // 6 components
            new[]
            {
                new Vector4(5.143778f, 2.079813f, -82.326596f, 111.231024f),
                new Vector4(5.612426f, 6.153387f, 113.878661f, 58.004879f),
                new Vector4(5.982921f, 9.802895f, 39.479083f, -162.028887f),
                new Vector4(6.505167f, 11.059237f, -71.286026f, 95.027069f),
                new Vector4(3.869579f, 14.81052f, 1.405746f, -3.704914f),
                new Vector4(2.201904f, 19.032909f, -0.152784f, -0.107988f)
            }
        };

        /// <summary>
        /// Gets the kernel parameters and scaling factor for the current count value in the current instance.
        /// </summary>
        private (Vector4[] Parameters, float Scale) GetParameters()
        {
            // Prepare the kernel components
            int index = Math.Max(0, Math.Min(ComponentsCount - 1, KernelComponents.Count));

            return (KernelComponents[index], KernelScales[index]);
        }

        /// <summary>
        /// Creates the collection of complex 1D kernels with the specified parameters.
        /// </summary>
        private Complex64[][] CreateComplexKernels()
        {
            var kernels = new Complex64[KernelParameters.Length][];
            ref Vector4 baseRef = ref MemoryMarshal.GetReference(KernelParameters.AsSpan());

            for (int i = 0; i < KernelParameters.Length; i++)
            {
                ref Vector4 paramsRef = ref Unsafe.Add(ref baseRef, i);

                kernels[i] = CreateComplex1DKernel(paramsRef.X, paramsRef.Y);
            }

            return kernels;
        }

        /// <summary>
        /// Creates a complex 1D kernel with the specified parameters.
        /// </summary>
        /// <param name="a">The exponential parameter for each complex component.</param>
        /// <param name="b">The angle component for each complex component.</param>
        private Complex64[] CreateComplex1DKernel(float a, float b)
        {
            var kernel = new Complex64[KernelSize];
            ref Complex64 baseRef = ref MemoryMarshal.GetReference(kernel.AsSpan());
            int r = Radius;
            int n = -r;

            for (int i = 0; i < KernelSize; i++, n++)
            {
                // Incrementally compute the range values
                float value = n * KernelsScale * (1f / r);

                value *= value;

                // Fill in the complex kernel values
                Unsafe.Add(ref baseRef, i) = new Complex64(
                    (float)(Math.Exp(-a * value) * Math.Cos(b * value)),
                    (float)(Math.Exp(-a * value) * Math.Sin(b * value)));
            }

            return kernel;
        }

        /// <summary>
        /// Normalizes the kernels with respect to A * real + B * imaginary.
        /// </summary>
        private void NormalizeKernels()
        {
            // Calculate the complex weighted sum
            float total = 0;
            Span<Complex64[]> kernelsSpan = Kernels.AsSpan();
            ref Complex64[] baseKernelsRef = ref MemoryMarshal.GetReference(kernelsSpan);
            ref Vector4 baseParamsRef = ref MemoryMarshal.GetReference(KernelParameters.AsSpan());

            for (int i = 0; i < KernelParameters.Length; i++)
            {
                ref Complex64[] kernelRef = ref Unsafe.Add(ref baseKernelsRef, i);
                int length = kernelRef.Length;
                ref Complex64 valueRef = ref kernelRef[0];
                ref Vector4 paramsRef = ref Unsafe.Add(ref baseParamsRef, i);

                for (int j = 0; j < length; j++)
                {
                    for (int k = 0; k < length; k++)
                    {
                        ref Complex64 jRef = ref Unsafe.Add(ref valueRef, j);
                        ref Complex64 kRef = ref Unsafe.Add(ref valueRef, k);

                        total +=
                            paramsRef.Z * (jRef.Real * kRef.Real - jRef.Imaginary * kRef.Imaginary)
                            + paramsRef.W * (jRef.Real * kRef.Imaginary + jRef.Imaginary * kRef.Real);
                    }
                }
            }

            // Normalize the kernels
            float scalar = 1f / (float)Math.Sqrt(total);

            for (int i = 0; i < kernelsSpan.Length; i++)
            {
                ref Complex64[] kernelsRef = ref Unsafe.Add(ref baseKernelsRef, i);
                int length = kernelsRef.Length;
                ref Complex64 valueRef = ref kernelsRef[0];

                for (int j = 0; j < length; j++)
                {
                    Unsafe.Add(ref valueRef, j) *= scalar;
                }
            }
        }

        /// <inheritdoc/>
        protected override void OnFrameApply(ImageFrame<ImageSharpRgba32> source)
        {
            if (!source.DangerousTryGetSinglePixelMemory(out Memory<ImageSharpRgba32> pixelMemory))
            {
                ThrowHelper.ThrowInvalidOperationException("Cannot process image frames wrapping discontiguous memory.");
            }

            Span<Rgba32> span = MemoryMarshal.Cast<ImageSharpRgba32, Rgba32>(pixelMemory.Span);

            using ReadWriteTexture2D<Rgba32, float4> texture = GraphicsDevice.AllocateReadWriteTexture2D<Rgba32, float4>(span, source.Width, source.Height);
            using ReadWriteTexture2D<float4> temporary = GraphicsDevice.AllocateReadWriteTexture2D<float4>(source.Width, source.Height, AllocationMode.Clear);
            using ReadWriteTexture2D<float4> reals = GraphicsDevice.AllocateReadWriteTexture2D<float4>(source.Width, source.Height);
            using ReadWriteTexture2D<float4> imaginaries = GraphicsDevice.AllocateReadWriteTexture2D<float4>(source.Width, source.Height);
            using ReadOnlyBuffer<Complex64> kernel = GraphicsDevice.AllocateReadOnlyBuffer<Complex64>(KernelSize);

            // Preliminary gamma highlight pass
            GraphicsDevice.For<GammaHighlightProcessor>(source.Height, new(texture));

            // Perform two 1D convolutions for each component in the current instance
            for (int j = 0; j < Kernels.Length; j++)
            {
                Vector4 parameters = KernelParameters[j];

                kernel.CopyFrom(Kernels[j]);

                using var context = GraphicsDevice.CreateComputeContext();

                context.For<VerticalConvolutionProcessor>(
                    source.Width,
                    source.Height,
                    new(texture, reals, imaginaries, kernel));

                context.Barrier(reals);
                context.Barrier(imaginaries);

                context.For<HorizontalConvolutionAndAccumulatePartialsProcessor>(
                    source.Width,
                    source.Height,
                    new(parameters.Z, parameters.W, reals, imaginaries, temporary, kernel));
            }

            // Apply the inverse gamma exposure pass
            GraphicsDevice.For<InverseGammaHighlightProcessor>(source.Height, new(temporary, texture));

            // Write the final pixel data
            texture.CopyTo(span);
        }

        /// <summary>
        /// Kernel for the vertical convolution pass.
        /// </summary>
        [AutoConstructor]
        internal partial struct VerticalConvolutionProcessor : IComputeShader
        {
            public IReadWriteNormalizedTexture2D<float4> source;
            public ReadWriteTexture2D<float4> reals;
            public ReadWriteTexture2D<float4> imaginaries;
            public ReadOnlyBuffer<Complex64> kernel;

            /// <inheritdoc/>
            public void Execute()
            {
                float4 real = float4.Zero;
                float4 imaginary = float4.Zero;
                int maxY = source.Height;
                int maxX = source.Width;
                int kernelLength = kernel.Length;
                int radiusY = kernelLength >> 1;

                for (int i = 0; i < kernelLength; i++)
                {
                    int offsetY = Hlsl.Clamp(ThreadIds.Y + i - radiusY, 0, maxY);
                    int offsetX = Hlsl.Clamp(ThreadIds.X, 0, maxX);
                    float4 color = source[offsetX, offsetY];
                    Complex64 factors = kernel[i];

                    real += factors.Real * color;
                    imaginary += factors.Imaginary * color;
                }

                reals[ThreadIds.XY] = real;
                imaginaries[ThreadIds.XY] = imaginary;
            }
        }

        /// <summary>
        /// Kernel for the horizontal convolution pass.
        /// </summary>
        [AutoConstructor]
        internal partial struct HorizontalConvolutionAndAccumulatePartialsProcessor : IComputeShader
        {
            public float z;
            public float w;

            public ReadWriteTexture2D<float4> reals;
            public ReadWriteTexture2D<float4> imaginaries;
            public ReadWriteTexture2D<float4> target;
            public ReadOnlyBuffer<Complex64> kernel;

            /// <inheritdoc/>
            public void Execute()
            {
                float4 real = float4.Zero;
                float4 imaginary = float4.Zero;
                int maxY = target.Height;
                int maxX = target.Width;
                int kernelLength = kernel.Length;
                int radiusX = kernelLength >> 1;
                int offsetY = Hlsl.Clamp(ThreadIds.Y, 0, maxY);

                for (int i = 0; i < kernelLength; i++)
                {
                    int offsetX = Hlsl.Clamp(ThreadIds.X + i - radiusX, 0, maxX);
                    float4 sourceReal = reals[offsetX, offsetY];
                    float4 sourceImaginary = imaginaries[offsetX, offsetY];
                    Complex64 factors = kernel[i];

                    real += factors.Real * sourceReal - factors.Imaginary * sourceImaginary;
                    imaginary += factors.Real * sourceImaginary + factors.Imaginary * sourceReal;
                }

                target[ThreadIds.XY] += real * z + imaginary * w;
            }
        }

        /// <summary>
        /// Kernel for the gamma highlight pass.
        /// </summary>
        [AutoConstructor]
        internal readonly partial struct GammaHighlightProcessor : IComputeShader
        {
            public readonly IReadWriteNormalizedTexture2D<float4> source;

            /// <inheritdoc/>
            public void Execute()
            {
                int width = source.Width;

                for (int i = 0; i < width; i++)
                {
                    float4 v = source[i, ThreadIds.X];

                    v.XYZ = v.XYZ * v.XYZ * v.XYZ;

                    source[i, ThreadIds.X] = v;
                }
            }
        }

        /// <summary>
        /// Kernel for the inverse gamma highlight pass.
        /// </summary>
        [AutoConstructor]
        internal readonly partial struct InverseGammaHighlightProcessor : IComputeShader
        {
            public readonly ReadWriteTexture2D<float4> source;
            public readonly IReadWriteNormalizedTexture2D<float4> target;

            /// <inheritdoc/>
            public void Execute()
            {
                int width = source.Width;

                for (int i = 0; i < width; i++)
                {
                    float4 v = source[i, ThreadIds.X];

                    v = Hlsl.Clamp(v, 0, float.MaxValue);
                    v.XYZ = Hlsl.Pow(v.XYZ, 1 / 3f);

                    target[i, ThreadIds.X] = v;
                }
            }
        }
    }
}
