﻿using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ComputeSharp.D2D1.__Internals;
using TerraFX.Interop.DirectX;

#pragma warning disable CS0618

namespace ComputeSharp.D2D1.Shaders.Loaders;

/// <summary>
/// A data loader for D2D1 pixel shaders dispatched via <see cref="ID2D1DrawInfo"/>.
/// </summary>
internal unsafe struct D2D1ByteBufferDispatchDataLoader : ID2D1DispatchDataLoader
{
    /// <summary>
    /// The target buffer to write data to.
    /// </summary>
    private readonly byte* buffer;

    /// <summary>
    /// The length of the buffer pointed to by <see cref="buffer"/>.
    /// </summary>
    private readonly int length;

    /// <summary>
    /// The number of written bytes.
    /// </summary>
    private int writtenBytes;

    /// <summary>
    /// Creates a new <see cref="D2D1ByteBufferDispatchDataLoader"/> instance.
    /// </summary>
    /// <param name="buffer">The target buffer to write data to.</param>
    /// <param name="length">The length of the buffer pointed to by <see cref="buffer"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal D2D1ByteBufferDispatchDataLoader(byte* buffer, int length)
    {
        this.buffer = buffer;
        this.length = length;
        this.writtenBytes = 0;
    }

    /// <summary>
    /// Gets the number of written bytes, or <c>0</c> in case of failure.
    /// </summary>
    /// <param name="writtenBytes">The number of written bytes, of <c>0</c> in case of failure.</param>
    /// <returns>Whether or not the constant buffer was retrieved successfully.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetWrittenBytes(out int writtenBytes)
    {
        return (writtenBytes = this.writtenBytes) != 0;
    }

    /// <inheritdoc/>
    void ID2D1DispatchDataLoader.LoadConstantBuffer(ReadOnlySpan<uint> data)
    {
        ReadOnlySpan<byte> constantBuffer = MemoryMarshal.Cast<uint, byte>(data);

        if (constantBuffer.TryCopyTo(new Span<byte>(this.buffer, this.length)))
        {
            this.writtenBytes = constantBuffer.Length;
        }
    }
}
