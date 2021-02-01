using System;

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

using UnityEngine;

[DisallowMultipleComponent]
public class BlobMapAuthoringComponent : MonoBehaviour, IConvertGameObjectToEntity
{
    public int Size;


    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        NativeMultiHashMap<int, int> initialData = GenerateData(Size, dataset, dataset, Allocator.Persistent);


        (NativeArray<int>, int) uniqueKeys = initialData.GetUniqueKeyArray(Allocator.Temp);

        BlobAssetReference<BlobMultiHashMap<int, int>> mapref;
        using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            mapref = new BlobHashMapBuilder<int, int>(blobBuilder).AddAll(initialData.GetEnumerator()).CreateBlobAssetReference(Allocator.Temp);


        }
        BlobAssetReference<BlobArray<int>> keyBlob;
        using (BlobBuilder blobBuilder2 = new BlobBuilder(Allocator.Temp))
        {

            ref var uniqueKeysBlob = ref blobBuilder2.ConstructRoot<BlobArray<int>>();
            var array = blobBuilder2.Allocate(ref uniqueKeysBlob, uniqueKeys.Item2);

            for (int i = 0; i < uniqueKeys.Item2; i++)
            {

                array[i] = uniqueKeys.Item1[i];
            }


            keyBlob = blobBuilder2.CreateBlobAssetReference<BlobArray<int>>(Allocator.Persistent);
            ref var t = ref keyBlob.Value;

        }

        dstManager.AddComponentData(entity, new BlobMapComponent() { keys = keyBlob, map = mapref });

        dstManager.World.GetOrCreateSystem<BlobSystem>().SetUniqueKeys(keyBlob);
        dstManager.World.GetOrCreateSystem<BlobSystem>().SetBlob(mapref);
        dstManager.World.GetOrCreateSystem<MapSystem>().SetUniqueKeys(keyBlob);
        dstManager.World.GetOrCreateSystem<MapSystem>().SetMap(initialData);


    }



    public static int[] dataset = new int[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97, 101, 103, 107, 109, 113, 127, 131, 137, 139, 149, 151, 157, 163, 167, 173, 179, 181, 191, 193, 197, 199, 211, 223, 227, 229, 233, 239, 241, 251, 257, 263, 269, 271, 277, 281, 283, 293, 307, 311, 313, 317, 331, 337, 347, 349, 353, 359, 367, 373, 379, 383, 389, 397, 401, 409, 419, 421, 431, 433, 439, 443, 449, 457, 461, 463, 467, 479, 487, 491, 499, 503, 509, 521, 523, 541 };

    public static NativeMultiHashMap<TKey, TValue> GenerateData<TKey, TValue>(int size, TKey[] keys, TValue[] values, Allocator allocator = Allocator.Temp)
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
