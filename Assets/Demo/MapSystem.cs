using Unity.Collections;
using Unity.Entities;

using UnityEngine;

[UpdateInGroup(typeof(TestGroup))]
public class MapSystem : SystemBase
{
    private NativeMultiHashMap<int, int> _map;

    public void SetMap(NativeMultiHashMap<int, int> map)
    {
        _map = map;
    }
    private BlobAssetReference<BlobArray<int>> _uniqueKeys;

    public void SetUniqueKeys(BlobAssetReference<BlobArray<int>> uniqueKeys)
    {
        _uniqueKeys = uniqueKeys;
    }


    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (_uniqueKeys.IsCreated) _uniqueKeys.Dispose();
        if (_map.IsCreated) _map.Dispose();
    }

    protected override void OnUpdate()
    {
        ref BlobArray<int> localUniqueKeys = ref _uniqueKeys.Value;
        NativeMultiHashMap<int, int> localMap = _map;

        int sum = 0;
        int elementCount = 0;

        for (int i = 0; i < localUniqueKeys.Length; i++)
        {

            var values = localMap.GetValuesForKey(localUniqueKeys[i]);
            while (values.MoveNext())
            {
                sum += values.Current % int.MaxValue;
                elementCount++;
            }
        }
        Debug.Log($" Map System { elementCount} element summing to {sum} ");

    }
}
