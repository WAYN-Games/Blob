using System;

using Unity.Entities;

public struct BlobHashMapBucketArray<TKey, TValue>
    where TKey : struct, IEquatable<TKey>
    where TValue : struct
{
    public BlobArray<BlobAssetReference<BlobHashMapBucket<TKey, TValue>>> Buckets;
}
