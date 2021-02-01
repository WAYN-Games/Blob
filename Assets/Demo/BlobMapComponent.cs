using Unity.Entities;

public struct BlobMapComponent : IComponentData
{
    public BlobAssetReference<BlobMultiHashMap<int, int>> map;
    public BlobAssetReference<BlobArray<int>> keys;
}
