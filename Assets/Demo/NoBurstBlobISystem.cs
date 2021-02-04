using Unity.Entities;

using UnityEngine;

[UpdateInGroup(typeof(TestGroup))]
public struct NoBurstBlobISystem : ISystemBase
{

    public void OnCreate(ref SystemState state)
    {

    }

    public void OnUpdate(ref SystemState state)
    {

        BlobMapComponent component = state.GetSingleton<BlobMapComponent>();

        ref BlobArray<int> localUniqueKeys = ref component.keys.Value;
        ref BlobMultiHashMap<int, int> map = ref component.map.Value;

        int sum = 0;
        int elementCount = 0;

        for (int i = 0; i < localUniqueKeys.Length; i++)
        {

            var values = map.GetValuesForKey(localUniqueKeys[i]);
            for (int j = 0; j < values.Length; j++)
            {
                sum += values[j] % int.MaxValue;
                elementCount++;
            }
        }
        Debug.Log($" Blob NoBurstBlobISystem { elementCount} element summing to {sum} ");

    }

    public void OnDestroy(ref SystemState state)
    {
    }
}
