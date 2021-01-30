using System;
using System.Collections.Generic;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;


public class BlobHashMapBuilder<TKey, TValue>
    where TKey : struct, IEquatable<TKey>, IComparable<TKey>
    where TValue : struct
{
    private BlobBuilder _bb;
    private int _bucketCount;
    private float _loadFactor;
    private Dictionary<int, SortedDictionary<TKey, (int, KeyIndex, List<TValue>)>> _data;
    private List<KeyValuePair<TKey, TValue>> _rehashList;
    private HashSet<TKey> _keySet;
    private bool IsRehashing = false;

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
        _data = new Dictionary<int, SortedDictionary<TKey, (int, KeyIndex, List<TValue>)>>();
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
        IsRehashing = true;
        _bucketCount *= 2;
        _data.Clear();
        AddAll(_rehashList);

        IsRehashing = false;
    }

    public BlobHashMapBuilder<TKey, TValue> Add(TKey key, TValue value)
    {
        if (!IsRehashing)
        {
            if (_bucketCount * _loadFactor < _keySet.Count) Rehash();
            _keySet.Add(key);
            _rehashList.Add(new KeyValuePair<TKey, TValue>(key, value));
        }
        (int bucketIndex, int keyHash) = BlobHashMapUtils.ComputeBucketIndex(key, _bucketCount);
        SortedDictionary<TKey, (int, KeyIndex, List<TValue>)> bucket;

        if (_data.ContainsKey(bucketIndex))
        {
            bucket = _data[bucketIndex];
        }
        else
        {
            bucket = new SortedDictionary<TKey, (int, KeyIndex, List<TValue>)>();
            _data.Add(bucketIndex, bucket);
        }

        AddDataToBucket(bucket, keyHash, key, value);

        return this;
    }

    private void AddDataToBucket(SortedDictionary<TKey, (int, KeyIndex, List<TValue>)> bucket, int keyHash, TKey key, TValue value)
    {
        if (bucket.ContainsKey(key))
        {
            (int keyHash, KeyIndex keyIndex, List<TValue> values) bucketData = bucket[key];
            bucketData.values.Add(value);
        }
        else
        {
            List<TValue> values = new List<TValue>();
            values.Add(value);
            bucket.Add(key, (keyHash, new KeyIndex() { KeyHash = keyHash }, values));
        }
    }

    public BlobAssetReference<BlobMultiHashMap<TKey, TValue>> CreateBlobAssetReference(Allocator allocator = Allocator.Temp)
    {
        ref BlobMultiHashMap<TKey, TValue> blobMap = ref _bb.ConstructRoot<BlobMultiHashMap<TKey, TValue>>();

        SetTotalCount(ref blobMap);

        BlobBuilderArray<BlobHashMapBucket<TKey, TValue>> buckets = _bb.Allocate(ref blobMap.BucketArray, _bucketCount);

        for (int i = 0; i < _bucketCount; i++)
        {
            //  Debug.Log($"Processing bucket {i}");
            if (_data.TryGetValue(i, out SortedDictionary<TKey, (int, KeyIndex, List<TValue>)> bucketData))
            {
                ref BlobHashMapBucket<TKey, TValue> bucket = ref buckets[i];
                // Sort the bucket by key to make sure all values for the same key are one after another.
                int bucketKeyCount = bucketData.Keys.Count;
                int bucketValueCount = 0;
                foreach ((int keyHash, KeyIndex keyIndex, List<TValue> values) bucketDataValue in bucketData.Values)
                {
                    bucketValueCount += bucketDataValue.values.Count;
                }
                BlobBuilderArray<TKey> bucketKeys = _bb.Allocate(ref bucket.KeysArray, bucketKeyCount);
                BlobBuilderArray<KeyIndex> bucketKeyIndexes = _bb.Allocate(ref bucket.KeyIndexes, bucketKeyCount);
                BlobBuilderArray<TValue> bucketValues = _bb.Allocate(ref bucket.ValuesArray, bucketValueCount);
                int firstIndex = 0;
                SortedDictionary<TKey, (int, KeyIndex, List<TValue>)>.Enumerator enumerator = bucketData.GetEnumerator();
                int j = 0;
                while (enumerator.MoveNext())
                {
                    KeyValuePair<TKey, (int keyHash, KeyIndex keyIndex, List<TValue> valueList)> current = enumerator.Current;
                    bucketKeys[j] = current.Key;
                    KeyIndex keyIndex = current.Value.keyIndex;
                    keyIndex.FirstIndex = firstIndex;
                    var values = current.Value.valueList;
                    keyIndex.ElementCount = values.Count;
                    bucketKeyIndexes[j] = keyIndex;

                    for (int k = 0; k < values.Count; k++)
                    {
                        bucketValues[firstIndex + k] = values[k];
                    }
                    firstIndex += keyIndex.ElementCount;
                    j++;
                }
            }
        }
        return _bb.CreateBlobAssetReference<BlobMultiHashMap<TKey, TValue>>(allocator);
    }


    private void SetTotalCount(ref BlobMultiHashMap<TKey, TValue> blobMap)
    {
        ref int ValueCount = ref _bb.Allocate(ref blobMap.ValueCount);
        int valueCount = 0;
        var e = _data.Values.GetEnumerator();
        while (e.MoveNext())
        {
            var bucket = e.Current;
            foreach ((int keyHash, KeyIndex keyIndex, List<TValue> values) key in bucket.Values)
            {
                valueCount += key.values.Count;
            }
        }
        ValueCount = valueCount;
    }

}
