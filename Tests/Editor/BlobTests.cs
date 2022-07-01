using System;
using System.Collections.Generic;

using NUnit.Framework;

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

public class BlobTests
{
    #region Public Fields

    public static int[] dataset = new int[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97, 101, 103, 107, 109, 113, 127, 131, 137, 139, 149, 151, 157, 163, 167, 173, 179, 181, 191, 193, 197, 199, 211, 223, 227, 229, 233, 239, 241, 251, 257, 263, 269, 271, 277, 281, 283, 293, 307, 311, 313, 317, 331, 337, 347, 349, 353, 359, 367, 373, 379, 383, 389, 397, 401, 409, 419, 421, 431, 433, 439, 443, 449, 457, 461, 463, 467, 479, 487, 491, 499, 503, 509, 521, 523, 541 };

    #endregion Public Fields

    #region Public Methods

    public static NativeParallelMultiHashMap<TKey, TValue> GenerateData<TKey, TValue>(int size, TKey[] keys, TValue[] values, Allocator allocator = Allocator.Temp)
        where TKey : struct, IEquatable<TKey>, IComparable<TKey>
        where TValue : struct
    {
        NativeParallelMultiHashMap<TKey, TValue> result = new NativeParallelMultiHashMap<TKey, TValue>(size, allocator);

        int2 min;
        min.x = 0;
        min.y = 0;
        int2 max;
        max.x = keys.Length;
        max.y = values.Length;

        Unity.Mathematics.Random r = new Unity.Mathematics.Random(10);
        for (int i = 0; i < size; i++)
        {
            int2 indexes = r.NextInt2(min, max);
            result.Add(keys[indexes.x], values[indexes.y]);
        }
        return result;
    }

    [Test]
    public void SimpleExempleTest()
    {
        using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            // Populating the map
            BlobHashMapBuilder<int, int> blobHashMapBuilder = new BlobHashMapBuilder<int, int>(blobBuilder);
            blobHashMapBuilder.Add(1, 2);
            blobHashMapBuilder.Add(1, 3);
            blobHashMapBuilder.Add(3, 4);
            blobHashMapBuilder.Add(5, 6);

            // Creating the blob reference
            BlobAssetReference<BlobMultiHashMap<int, int>> blobAssetReference = blobHashMapBuilder.CreateBlobAssetReference(Allocator.Temp);

            // reading the data
            ref var map = ref blobAssetReference.Value;
            NativeArray<int> valuesForKey1 = map.GetValuesForKey(1);    // The blobmap can contain multiple values for the same key
            Assert.AreEqual(2, valuesForKey1[0]);                       // Check that the first value for the key is the expected one
            Assert.AreEqual(3, valuesForKey1[1]);                       // Check that the second value for the key is the expected one
            Assert.AreEqual(4, map.ValueCount.Value);                   // Check that the blob asset contains the expected number of values
            Assert.IsTrue(map.ContainsKey(5));                          // Check that the blob asset contains at least one value for key 5
        }
    }

    [Test]
    public void BlobMultiHashMapNonExistingKeyTest([Values(0, 10, 100, 1000)] int size)
    {
        if (!BurstCompiler.IsEnabled && size > 1000) return;
        NativeParallelMultiHashMap<int, int> initialData = GenerateData(size, dataset, dataset);
        using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            BlobAssetReference<BlobMultiHashMap<int, int>> mapref = new BlobHashMapBuilder<int, int>(blobBuilder).AddAll(initialData.GetEnumerator()).CreateBlobAssetReference(Allocator.Temp);
            ref var map = ref mapref.Value;
            Assert.False(map.ContainsKey(-4));
            Assert.AreEqual(0, map.GetValuesForKey(-4).Length);
            Assert.False(map.GetValuesForKey(-4).IsCreated);
        }
    }

    [Test]
    public void BlobMultiHashMapExistingKeyTest()
    {
        NativeParallelMultiHashMap<int, int> initialData = GenerateData(1, dataset, dataset);
        using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            BlobAssetReference<BlobMultiHashMap<int, int>> mapref = new BlobHashMapBuilder<int, int>(blobBuilder).AddAll(initialData.GetEnumerator()).CreateBlobAssetReference(Allocator.Temp);
            ref var map = ref mapref.Value;
            int key = initialData.GetKeyArray(Allocator.Temp)[0];
            Assert.True(map.ContainsKey(key));
            var e = initialData.GetValuesForKey(key);
            e.MoveNext();
            Assert.AreEqual(e.Current, map.GetValuesForKey(initialData.GetKeyArray(Allocator.Temp)[0])[0]);
        }
    }

    [Test]
    public void BlobMultiHashMapTest([Values(10, 100, 1000, 10000, 100000, 500000, 1000000)] int size)
    {
        if (!BurstCompiler.IsEnabled && size > 1000) return;
        NativeParallelMultiHashMap<int, int> initialData = GenerateData(size, dataset, dataset, Allocator.TempJob);
        using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            BlobAssetReference<BlobMultiHashMap<int, int>> mapref = new BlobHashMapBuilder<int, int>(blobBuilder).AddAll(initialData.GetEnumerator()).CreateBlobAssetReference(Allocator.TempJob);
            ref var map = ref mapref.Value;

            var uniqueKeys = initialData.GetUniqueKeyArray(Allocator.TempJob);

            NativeParallelHashMap<int, int> mapResults = new NativeParallelHashMap<int, int>(uniqueKeys.Item2, Allocator.TempJob);
            NativeParallelHashMap<int, int> blobResults = new NativeParallelHashMap<int, int>(uniqueKeys.Item2, Allocator.TempJob);
            NativeArray<int> ukeys = uniqueKeys.Item1.GetSubArray(0, uniqueKeys.Item2);

            var mapDep = new MapJob()
            {
                Keys = ukeys,
                map = initialData,
                results = mapResults.AsParallelWriter()
            }.Schedule(uniqueKeys.Item2, 1);

            var blobDep = new BlobJob()
            {
                Keys = ukeys,
                mapref = mapref,
                results = blobResults.AsParallelWriter()
            }.Schedule(uniqueKeys.Item2, 1);

            mapDep.Complete();
            blobDep.Complete();
            for (int i = 0; i < ukeys.Length; i++)
            {
                Assert.True(mapResults.ContainsKey(ukeys[i]));
                Assert.True(blobResults.ContainsKey(ukeys[i]));
                Assert.AreEqual(mapResults[ukeys[i]], blobResults[ukeys[i]]);
            }

            mapref.Dispose();
            mapResults.Dispose();
            blobResults.Dispose();
            uniqueKeys.Item1.Dispose();
            initialData.Dispose();
        }
    }

    [Test]
    public void BlobMultiHashMapWithColidingKeyTest([Values(10, 100, 1000)] int size)
    {
        if (!BurstCompiler.IsEnabled && size > 1000) return;
        List<CollidingKey> keys = new List<CollidingKey>();
        foreach (int i in dataset)
        {
            keys.Add(new CollidingKey() { value = i });
        }

        NativeParallelMultiHashMap<CollidingKey, int> initialData = GenerateData(size, keys.ToArray(), dataset);
        using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            BlobAssetReference<BlobMultiHashMap<CollidingKey, int>> mapref = new BlobHashMapBuilder<CollidingKey, int>(blobBuilder).AddAll(initialData.GetEnumerator()).CreateBlobAssetReference(Allocator.Temp);
            ref var map = ref mapref.Value;

            foreach (var key in initialData.GetUniqueKeyArray(Allocator.Temp).Item1)
            {
                AssertForKey(key, initialData, ref map);
            }
        }
    }

    #endregion Public Methods

    #region Private Methods

    private void AssertForKey<TKey>(TKey key, NativeParallelMultiHashMap<TKey, int> initialData, ref BlobMultiHashMap<TKey, int> map)
         where TKey : struct, IEquatable<TKey>, IComparable<TKey>
    {
        var e = initialData.GetValuesForKey(key);
        int intialValue = 0;
        while (e.MoveNext())
        {
            intialValue += e.Current;
        }

        int blobValue = 0;
        foreach (int i in map.GetValuesForKey(key))
        {
            blobValue += i;
        }

        Assert.AreEqual(intialValue, blobValue, $"Values are not equal for key [{key}] : {intialValue} != {blobValue}");
    }

    #endregion Private Methods

    #region Public Structs

    public struct MapJob : IJobParallelFor
    {
        #region Public Fields

        [ReadOnly] public NativeArray<int> Keys;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> map;
        public NativeParallelHashMap<int, int>.ParallelWriter results;

        #endregion Public Fields

        #region Public Methods

        public void Execute(int index)
        {
            int key = Keys[index];
            var e = map.GetValuesForKey(key);
            int sum = 0;

            while (e.MoveNext())
            {
                sum += e.Current;
            }

            results.TryAdd(key, sum);
        }

        #endregion Public Methods
    }

    public struct BlobJob : IJobParallelFor
    {
        #region Public Fields

        [ReadOnly] public NativeArray<int> Keys;
        [ReadOnly] public BlobAssetReference<BlobMultiHashMap<int, int>> mapref;
        public NativeParallelHashMap<int, int>.ParallelWriter results;

        #endregion Public Fields

        #region Public Methods

        public void Execute(int index)
        {
            ref var map = ref mapref.Value;
            int key = Keys[index];
            NativeArray<int> values = map.GetValuesForKey(key);
            int sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }

            results.TryAdd(key, sum);
        }

        #endregion Public Methods
    }

    #endregion Public Structs

    #region Private Structs

    private struct CollidingKey : IEquatable<CollidingKey>, IComparable<CollidingKey>
    {
        #region Public Fields

        public int value;

        #endregion Public Fields

        #region Public Methods

        public int CompareTo(CollidingKey other)
        {
            return value.CompareTo(other.value);
        }

        public bool Equals(CollidingKey other)
        {
            return value == other.value;
        }

        public override bool Equals(object obj)
        {
            return obj.Equals(value);
        }

        public override int GetHashCode()
        {
            return 1;
        }

        public override string ToString()
        {
            return $"{value}";
        }

        #endregion Public Methods
    }

    #endregion Private Structs
}