#if UNITY_2020_2_OR_NEWER
using System.Collections.Generic;

namespace Unity.Entities
{
    [UpdateInGroup(typeof(ConversionSetupGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.GameObjectConversion)]
    class StaticOptimizeIncrementalConversionSystem : SystemBase
    {
        private HashSet<int> _staticTag = new HashSet<int>();
        IncrementalChangesSystem _incremental;
        protected override void OnCreate()
        {
            base.OnCreate();
            _incremental = World.GetExistingSystem<IncrementalChangesSystem>();
        }

        protected override void OnUpdate()
        {
            // delete information about all deleted instances
            var deletions = _incremental.IncomingChanges.RemovedGameObjectInstanceIds;
            for (int i = 0; i < deletions.Length; i++)
                _staticTag.Remove(deletions[i]);

            // Update information for all changed instances.
            var changes = _incremental.IncomingChanges.ChangedGameObjects;
            for (int i = 0; i < changes.Count; i++)
            {
                var go = changes[i];
                var instanceId = go.GetInstanceID();
                if (go.TryGetComponent<StaticOptimizeEntity>(out _))
                {
                    if (_staticTag.Add(instanceId))
                        _incremental.AddHierarchyConversionRequest(instanceId);
                }
                else if (_staticTag.Remove(instanceId))
                    _incremental.AddHierarchyConversionRequest(instanceId);
            }
        }
    }
}
#endif
