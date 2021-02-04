
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

using UnityEngine;

[UpdateInGroup(typeof(TestGroup))]
[BurstCompile]
public struct BurstBlobISystem : ISystemBase
{
    private EntityQuery _singleton;

    public void OnCreate(ref SystemState state)
    {
        _singleton = state.GetEntityQuery(new ComponentType[] { ComponentType.ReadOnly(typeof(BlobMapComponent)) });
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobMapComponent component = _singleton.GetSingleton<BlobMapComponent>();


        ref BlobArray<int> localUniqueKeys = ref component.keys.Value;

        ref BlobMultiHashMap<int, int> map = ref component.map.Value;

        int sum = 0;
        int elementCount = 0;

        for (int i = 0; i < localUniqueKeys.Length; i++)
        {

            NativeArray<int> values = map.GetValuesForKey(localUniqueKeys[i]);
            for (int j = 0; j < values.Length; j++)
            {
                sum += values[j] % int.MaxValue;
                elementCount++;
            }

        }
        Debug.Log($" Blob BurstBlobISystem { elementCount} element summing to {sum} ");

    }

    public void OnDestroy(ref SystemState state)
    {
    }
}
