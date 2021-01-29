using System.Runtime.InteropServices;

namespace Unity.Entities.Hybrid.CodeGen.Tests
{
    [StructLayout(LayoutKind.Explicit, Size = 10)]
    [GenerateAuthoringComponent]
    public struct BufferElementWithExplicitLayout : IBufferElementData
    {
        [FieldOffset(3)] public byte Value;
    }
}
