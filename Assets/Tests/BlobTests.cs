using NUnit.Framework;

using Unity.Collections;
using Unity.Entities;

using UnityEngine;

public class BlobTests
{
    // A Test behaves as an ordinary method
    [Test]
    public void BlobTestsSimplePasses()
    {
        NativeMultiHashMap<int, int> initialData = new NativeMultiHashMap<int, int>(5, Allocator.Temp);
        initialData.Add(1, 10);
        initialData.Add(3, 30);
        initialData.Add(2, 20);
        initialData.Add(4, 40);
        initialData.Add(2, 21);
        using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            BlobAssetReference<BlobMultiHashMap<int, int>> mapref = new BlobHashMapBuilder<int, int>(blobBuilder).AddAll(initialData.GetEnumerator()).CreateBlobAssetReference(Allocator.Temp);
            ref var map = ref mapref.Value;
            ref int count = ref map.KeyCount.Value;
            Assert.AreEqual(4, count);
            ref var buckets = ref map.BucketArray;
            Assert.AreEqual(16, buckets.Length);
            DebugLog(ref buckets);
            AssertForKey(1, initialData, ref map);
            AssertForKey(2, initialData, ref map);
            AssertForKey(3, initialData, ref map);
            AssertForKey(4, initialData, ref map);
        }


    }

    private static void DebugLog(ref BlobArray<BlobHashMapBucket<int, int>> buckets)
    {
        for (int i = 0; i < buckets.Length; i++)
        {
            ref var keys = ref buckets[i].KeysArray;
            for (int j = 0; j < keys.Length; j++)
            {
                ref var key = ref keys[j];

                Debug.Log($"Bucket {i} contains key {key}");
            }
            ref var values = ref buckets[i].ValuesArray;
            for (int k = 0; k < values.Length; k++)
            {
                Debug.Log($"Bucket {i} contains value {values[k]} ");
            }
        }
    }

    private void AssertForKey(int key, NativeMultiHashMap<int, int> initialData, ref BlobMultiHashMap<int, int> map)
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

}
