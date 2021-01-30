using System;

using Unity.Collections;
using Unity.Entities;

/// <summary>
/// An immutable map of key/values stored in a blob asset.
/// It supports multiple values per key.
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public struct BlobMultiHashMap<TKey, TValue>
    where TKey : struct, IEquatable<TKey>
    where TValue : struct
{

    /// <summary>
    /// List of buckets in the map.
    /// </summary>
    internal BlobArray<BlobHashMapBucket<TKey, TValue>> BucketArray;
    /// <summary>
    /// The total number of element in the Map.
    /// </summary>
    public BlobPtr<int> ValueCount;

    public bool ContainsKey(TKey key)
    {
        int bucketCount = BucketArray.Length;
        (int bucketIndex, int keyHash) = BlobHashMapUtils.ComputeBucketIndex(key, bucketCount);
        return BucketArray[bucketIndex].ContainsKey(key);
    }

    public NativeArray<TValue> GetValuesForKey(TKey key, Allocator allocator = Allocator.Temp)
    {
        // Find the bucket containing the values for the TKey
        int bucketCount = BucketArray.Length;
        (int bucketIndex, int keyHash) = BlobHashMapUtils.ComputeBucketIndex(key, bucketCount);
        // Retrieve the values for that key from the bucket.
        return BucketArray[bucketIndex].GetValuesForKey(key, allocator);
    }
}
