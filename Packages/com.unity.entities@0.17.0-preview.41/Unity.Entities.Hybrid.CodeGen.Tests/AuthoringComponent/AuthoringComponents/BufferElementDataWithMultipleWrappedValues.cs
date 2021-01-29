namespace Unity.Entities.Hybrid.CodeGen.Tests
{
    [GenerateAuthoringComponent]
    public struct BufferElementDataWithMultipleWrappedValues : IBufferElementData
    {
#pragma warning disable 649
        public int Value1;
        public int Value2;
#pragma warning restore 649
    }
}
