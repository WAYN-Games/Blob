
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

using UnityEngine;

public class TestGroup : ComponentSystemGroup
{

}


[UpdateInGroup(typeof(TestGroup))]
public struct NoBurstBlobISystem : ISystemBase
{

    private BlobAssetReference<BlobArray<int>> _uniqueKeys;

    private BlobAssetReference<BlobMultiHashMap<int, int>> _mapBlob;

    public void OnCreate(ref SystemState state)
    {

    }

    private SystemState Setup(SystemState state)
    {
        var entities = state.EntityManager.GetAllEntities();
        for (int i = 0; i < entities.Length; i++)
        {
            if (state.EntityManager.HasComponent<BlobMapComponent>(entities[i]))
            {
                BlobMapComponent component = state.EntityManager.GetComponentData<BlobMapComponent>(entities[i]);
                _uniqueKeys = component.keys;
                _mapBlob = component.map;
            }
        }

        return state;
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!_uniqueKeys.IsCreated || !_mapBlob.IsCreated)
        {
            state = Setup(state);
        }

        ref BlobArray<int> localUniqueKeys = ref _uniqueKeys.Value;

        int sum = 0;
        int elementCount = 0;

        ref BlobMultiHashMap<int, int> map = ref _mapBlob.Value;
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


[UpdateInGroup(typeof(TestGroup))]
[BurstCompile]
public struct BurstBlobISystem : ISystemBase
{

    private BlobAssetReference<BlobArray<int>> _uniqueKeys;

    private BlobAssetReference<BlobMultiHashMap<int, int>> _mapBlob;

    public void OnCreate(ref SystemState state)
    {

    }

    private SystemState Setup(SystemState state)
    {
        var entities = state.EntityManager.GetAllEntities();
        for (int i = 0; i < entities.Length; i++)
        {
            if (state.EntityManager.HasComponent<BlobMapComponent>(entities[i]))
            {
                BlobMapComponent component = state.EntityManager.GetComponentData<BlobMapComponent>(entities[i]);
                _uniqueKeys = component.keys;
                _mapBlob = component.map;
            }
        }

        return state;
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!_uniqueKeys.IsCreated || !_mapBlob.IsCreated)
        {
            state = Setup(state);
        }

        ref BlobArray<int> localUniqueKeys = ref _uniqueKeys.Value;

        int sum = 0;
        int elementCount = 0;

        ref BlobMultiHashMap<int, int> map = ref _mapBlob.Value;
        for (int i = 0; i < localUniqueKeys.Length; i++)
        {

            var values = map.GetValuesForKey(localUniqueKeys[i]);
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

            var values = map.GetValuesForKey(localUniqueKeys[i]);
            for (int j = 0; j < values.Length; j++)
            {
                sum += values[j] % int.MaxValue;
                elementCount++;
            }
        }
        Debug.Log($" Blob System { elementCount} element summing to {sum} ");
    }
}

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
        Debug.Log($" Blob System { elementCount} element summing to {sum} ");

    }
}
