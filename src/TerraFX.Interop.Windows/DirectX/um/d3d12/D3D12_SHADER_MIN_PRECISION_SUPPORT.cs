// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// Ported from um/d3d12.h in the Windows SDK for Windows 10.0.20348.0
// Original source is Copyright © Microsoft. All rights reserved.

using System;

namespace TerraFX.Interop.DirectX
{
    [Flags]
    internal enum D3D12_SHADER_MIN_PRECISION_SUPPORT
    {
        D3D12_SHADER_MIN_PRECISION_SUPPORT_NONE = 0,
        D3D12_SHADER_MIN_PRECISION_SUPPORT_10_BIT = 0x1,
        D3D12_SHADER_MIN_PRECISION_SUPPORT_16_BIT = 0x2,
    }
}
