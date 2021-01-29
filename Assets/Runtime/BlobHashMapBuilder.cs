using System;
using System.Collections.Generic;
using System.Linq;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

using UnityEngine;

public class BlobHashMapBuilder<TKey, TValue>
    where TKey : struct, IEquatable<TKey>, IComparable<TKey>
    where TValue : struct
{
    private BlobBuilder _bb;
    private int _bucketCount;
    private float _loadFactor;
    private Dictionary<int, Dictionary<int, (TKey, KeyIndex, List<TValue>)>> _data;
    private List<KeyValuePair<TKey, TValue>> _rehashList;
    private HashSet<TKey> _keySet;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="bb"></param>
    /// <param name="bucketCount"></param>
    /// <param name="loadFactor"></param>
    public BlobHashMapBuilder(BlobBuilder bb, int bucketCount = -1, float loadFactor = .75f)
    {
        _bb = bb;
        _bucketCount = bucketCount == -1 ? 16 : bucketCount;
        _loadFactor = loadFactor;
        _data = new Dictionary<int, Dictionary<int, (TKey, KeyIndex, List<TValue>)>>();
        _rehashList = new List<KeyValuePair<TKey, TValue>>();
        _keySet = new HashSet<TKey>();
    }

    public BlobHashMapBuilder<TKey, TValue> AddAll(IEnumerator<KeyValue<TKey, TValue>> elements)
    {
        while (elements.MoveNext())
        {
            Add(elements.Current.Key, elements.Current.Value);
        }
        return this;
    }

    public BlobHashMapBuilder<TKey, TValue> AddAll(IEnumerable<KeyValuePair<TKey, TValue>> elements)
    {
        foreach (var element in elements)
        {
            Add(element.Key, element.Value);
        }
        return this;
    }

    private void Rehash()
    {
        _bucketCount *= 2;
        _data.Clear();
        AddAll(_rehashList);
    }

    public BlobHashMapBuilder<TKey, TValue> Add(TKey key, TValue value)
    {
        if (_bucketCount * _loadFactor < _keySet.Count) Rehash();
        _keySet.Add(key);
        _rehashList.Add(new KeyValuePair<TKey, TValue>(key, value));
        (int bucketIndex, int keyHash) = BlobHashMapUtils.ComputeBucketIndex(key, _bucketCount);
        Dictionary<int, (TKey, KeyIndex, List<TValue>)> bucket;

        if (_data.ContainsKey(bucketIndex))
        {
            bucket = _data[bucketIndex];
        }
        else
        {
            bucket = new Dictionary<int, (TKey, KeyIndex, List<TValue>)>();
            _data.Add(bucketIndex, bucket);
        }

        AddDataToBucket(bucket, keyHash, key, value);

        return this;
    }

    private void AddDataToBucket(Dictionary<int, (TKey, KeyIndex, List<TValue>)> bucket, int keyHash, TKey key, TValue value)
    {
        if (bucket.ContainsKey(keyHash))
        {
            (TKey key, KeyIndex keyIndex, List<TValue> values) bucketData = bucket[keyHash];
            bucketData.values.Add(value);
        }
        else
        {
            List<TValue> values = new List<TValue>();
            values.Add(value);
            bucket.Add(keyHash, (key, new KeyIndex() { KeyHash = keyHash }, values));
        }
    }

    public BlobAssetReference<BlobMultiHashMap<TKey, TValue>> CreateBlobAssetReference(Allocator allocator = Allocator.Temp)
    {
        ref BlobMultiHashMap<TKey, TValue> blobMap = ref _bb.ConstructRoot<BlobMultiHashMap<TKey, TValue>>();

        SetTotalCount(ref blobMap);
        SetTotalKeyCount(ref blobMap);

        BlobBuilderArray<BlobHashMapBucket<TKey, TValue>> buckets = _bb.Allocate(ref blobMap.BucketArray, _bucketCount);

        for (int i = 0; i < _bucketCount; i++)
        {
            Debug.Log($"Processing bucket {i}");
            if (_data.TryGetValue(i, out Dictionary<int, (TKey, KeyIndex, List<TValue>)> bucketData))
            {
                ref BlobHashMapBucket<TKey, TValue> bucket = ref buckets[i];
                // Sort the bucket by key to make sure all values for the same key are one after another.
                KeyValuePair<int, (TKey, KeyIndex, List<TValue>)>[] orderedData = bucketData.OrderBy((KeyValuePair<int, (TKey key, KeyIndex, List<TValue>)> pair) => pair.Value.key).ToArray();
                int bucketKeyCount = bucketData.Keys.Count;
                int bucketValueCount = 0;
                foreach ((TKey key, KeyIndex keyIndex, List<TValue> values) bucketDataValue in bucketData.Values)
                {
                    bucketValueCount += bucketDataValue.values.Count;
                }
                BlobBuilderArray<TKey> bucketKeys = _bb.Allocate(ref bucket.KeysArray, bucketKeyCount);
                BlobBuilderArray<KeyIndex> bucketKeyIndexes = _bb.Allocate(ref bucket.KeyIndexes, bucketKeyCount);
                BlobBuilderArray<TValue> bucketValues = _bb.Allocate(ref bucket.ValuesArray, bucketValueCount);
                int firstIndex = 0;
                for (int j = 0; j < orderedData.Length; j++)
                {

                    (TKey key, KeyIndex keyIndex, List<TValue> values) = orderedData[j].Value;
                    bucketKeys[j] = key;
                    keyIndex.FirstIndex = firstIndex;
                    keyIndex.ElementCount = values.Count;
                    bucketKeyIndexes[j] = keyIndex;

                    for (int k = 0; k < values.Count; k++)
                    {
                        Debug.Log($"{key} => {values[k]} ");
                        bucketValues[firstIndex + k] = values[k];
                    }
                    firstIndex += keyIndex.ElementCount;
                }
            }
        }
        return _bb.CreateBlobAssetReference<BlobMultiHashMap<TKey, TValue>>(allocator);
    }


    private void SetTotalCount(ref BlobMultiHashMap<TKey, TValue> blobMap)
    {
        ref int ValueCount = ref _bb.Allocate(ref blobMap.ValueCount);
        int valueCount = 0;
        foreach (Dictionary<int, (TKey key, KeyIndex keyIndex, List<TValue> values)> bucket in _data.Values)
        {
            foreach ((TKey key, KeyIndex keyIndex, List<TValue> values) key in bucket.Values)
            {
                valueCount += key.values.Count;
            }
        }
        ValueCount = valueCount;
    }
    private void SetTotalKeyCount(ref BlobMultiHashMap<TKey, TValue> blobMap)
    {
        ref int KeyCount = ref _bb.Allocate(ref blobMap.KeyCount);
        int keyCount = 0;
        foreach (Dictionary<int, (TKey, KeyIndex, List<TValue>)> bucket in _data.Values)
        {
            keyCount += bucket.Values.Count;
        }
        KeyCount = keyCount;
    }

}
