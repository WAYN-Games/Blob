using System;
using System.Collections.Generic;

using NUnit.Framework;

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public class BlobTests
{

    [Test]
    public void BlobMultiHashMapNonExistingKeyTest([Values(0, 10, 100, 1000)] int size)
    {
        NativeMultiHashMap<int, int> initialData = GenerateData(size, dataset, dataset);
        using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            BlobAssetReference<BlobMultiHashMap<int, int>> mapref = new BlobHashMapBuilder<int, int>(blobBuilder).AddAll(initialData.GetEnumerator()).CreateBlobAssetReference(Allocator.Temp);
            ref var map = ref mapref.Value;
            Assert.False(map.ContainsKey(4));
        }
    }

    [Test]
    public void BlobMultiHashMapExistingKeyTest()
    {
        NativeMultiHashMap<int, int> initialData = GenerateData(1, dataset, dataset);
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
    public void BlobMultiHashMapTest([Values(10, 100, 1000)] int size)
    {
        NativeMultiHashMap<int, int> initialData = GenerateData(size, dataset, dataset);
        using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            BlobAssetReference<BlobMultiHashMap<int, int>> mapref = new BlobHashMapBuilder<int, int>(blobBuilder).AddAll(initialData.GetEnumerator()).CreateBlobAssetReference(Allocator.Temp);
            ref var map = ref mapref.Value;

            foreach (var key in initialData.GetUniqueKeyArray(Allocator.Temp).Item1)
            {
                AssertForKey(key, initialData, ref map);
            }
        }
    }



    [Test]
    public void BlobMultiHashMapWithColidingKeyTest([Values(10, 100, 1000)] int size)
    {
        List<CollidingKey> keys = new List<CollidingKey>();
        foreach (int i in dataset)
        {
            keys.Add(new CollidingKey() { value = i });
        }

        NativeMultiHashMap<CollidingKey, int> initialData = GenerateData(size, keys.ToArray(), dataset);
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


    private struct CollidingKey : IEquatable<CollidingKey>, IComparable<CollidingKey>
    {
        public int value;

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
    }

    static int[] dataset = new int[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97, 101, 103, 107, 109, 113, 127, 131, 137, 139, 149, 151, 157, 163, 167, 173, 179, 181, 191, 193, 197, 199, 211, 223, 227, 229, 233, 239, 241, 251, 257, 263, 269, 271, 277, 281, 283, 293, 307, 311, 313, 317, 331, 337, 347, 349, 353, 359, 367, 373, 379, 383, 389, 397, 401, 409, 419, 421, 431, 433, 439, 443, 449, 457, 461, 463, 467, 479, 487, 491, 499, 503, 509, 521, 523, 541 };

    private void AssertForKey<TKey>(TKey key, NativeMultiHashMap<TKey, int> initialData, ref BlobMultiHashMap<TKey, int> map)
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

    private NativeMultiHashMap<TKey, TValue> GenerateData<TKey, TValue>(int size, TKey[] keys, TValue[] values, Allocator allocator = Allocator.Temp)
        where TKey : struct, IEquatable<TKey>, IComparable<TKey>
        where TValue : struct
    {
        NativeMultiHashMap<TKey, TValue> result = new NativeMultiHashMap<TKey, TValue>(size, allocator);

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


}
