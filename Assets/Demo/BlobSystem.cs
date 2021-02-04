using Unity.Collections;
using Unity.Entities;

using UnityEngine;

public class TestGroup : ComponentSystemGroup
{

}

[UpdateInGroup(typeof(TestGroup))]
public class BlobSystem : SystemBase
{
    private BlobAssetReference<BlobArray<int>> _uniqueKeys;

    public void SetUniqueKeys(BlobAssetReference<BlobArray<int>> uniqueKeys)
    {
        _uniqueKeys = uniqueKeys;
    }

    public void SetBlob(BlobAssetReference<BlobMultiHashMap<int, int>> mapBlob)
    {
        _mapBlob = mapBlob;
    }

    private BlobAssetReference<BlobMultiHashMap<int, int>> _mapBlob;


    protected override void OnUpdate()
    {

        ref BlobArray<int> localUniqueKeys = ref _uniqueKeys.Value;

        int sum = 0;
        int elementCount = 0;

        ref BlobMultiHashMap<int, int> map = ref _mapBlob.Value;
        for (int i = 0; i < localUniqueKeys.Length; i++)
        {

            NativeArray<int> values = map.GetValuesForKey(localUniqueKeys[i]);
            for (int j = 0; j < values.Length; j++)
            {
                sum += values[j] % int.MaxValue;
                elementCount++;
            }
        }
        Debug.Log($" Blob System { elementCount} element summing to {sum} ");
    }
}
