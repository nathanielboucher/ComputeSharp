﻿using ComputeSharp.Core.Interop;
using ComputeSharp.Graphics.Extensions;
using System;
using System.Runtime.CompilerServices;
using TerraFX.Interop;

namespace ComputeSharp.Graphics.Buffers.Abstract
{
    /// <summary>
    /// An abstract <see langword="class"/> that represents a graphics resource object.
    /// </summary>
    public abstract unsafe class GraphicsResource2 : NativeObject
    {
        /// <summary>
        /// The <see cref="ID3D12Resource"/> instance currently mapped.
        /// </summary>
        protected internal readonly ID3D12Resource* D3D12Resource;

        /// <summary>
        /// Creates a new <see cref="GraphicsResource"/> instance with the specified parameters.
        /// </summary>
        /// <param name="device">The <see cref="GraphicsDevice"/> associated with the current instance.</param>
        protected GraphicsResource2(GraphicsDevice2 device, ID3D12Resource* d3d12resource)
        {
            D3D12Resource = d3d12resource;

            GraphicsDevice = device;
        }

        /// <summary>
        /// Gets the <see cref="GraphicsDevice"/> associated with the current instance.
        /// </summary>
        public GraphicsDevice2 GraphicsDevice { get; }

        /// <summary>
        /// Maps the current resource to a specified subresource.
        /// </summary>
        /// <returns>A <see cref="MappedResource"/> instance representing the mapped resource.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal MappedResource MapResource()
        {
            return new MappedResource(D3D12Resource);
        }

        /// <inheritdoc/>
        protected override void OnDispose()
        {
            D3D12Resource->Release();
        }

        /// <summary>
        /// A type representing a mapped memory resource.
        /// </summary>
        internal readonly ref struct MappedResource
        {
            /// <summary>
            /// The target <see cref="ID3D12Resource"/> to map.
            /// </summary>
            private readonly ID3D12Resource* d3D12Resource;

            /// <summary>
            /// The pointer to the mapped resource.
            /// </summary>
            public readonly IntPtr Pointer;

            /// <summary>
            /// Creates a new <see cref="MappedResource"/> instance for a given <see cref="ID3D12Resource"/> value.
            /// </summary>
            /// <param name="resource">The input <see cref="ID3D12Resource"/> instance to map.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public MappedResource(ID3D12Resource* d3d12resource)
            {
                d3D12Resource = d3d12resource;

                Pointer = d3d12resource->Map();
            }

            /// <inheritdoc cref="IDisposable.Dispose"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                d3D12Resource->Unmap();
            }
        }
    }
}
