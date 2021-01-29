#if UNITY_2020_2_OR_NEWER
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Unity.Entities
{
    [UpdateInGroup(typeof(ConversionSetupGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.GameObjectConversion)]
    class GameObjectEnableDisableIncrementalConversionSystem : SystemBase
    {
        readonly HashSet<int> _disabledGameObjects = new HashSet<int>();
        readonly HashSet<int> _visitedInstances = new HashSet<int>();
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
                _disabledGameObjects.Remove(deletions[i]);

            // Update information for all changed instances.
            var changes = _incremental.IncomingChanges.ChangedGameObjects;
            using (var activeChildren = new NativeList<int>(Allocator.TempJob))
            {
                _visitedInstances.Clear();
                for (int i = 0; i < changes.Count; i++)
                {
                    var go = changes[i];
                    var instanceId = go.GetInstanceID();
                    if (!go.activeInHierarchy)
                    {
                        if (_disabledGameObjects.Add(instanceId))
                        {
                            _incremental.AddHierarchyConversionRequest(instanceId);
                            ReconvertAllDisabledParents(go.transform.parent);
                        }
                    }
                    else if (_disabledGameObjects.Remove(instanceId))
                    {
                        // in this case, we know that the game object was previously disabled. This means that all of its
                        // children might also have changed from inactive to active.
                        FindActiveChildren(go, _visitedInstances, activeChildren);
                    }
                }

                for (int i = 0; i < activeChildren.Length; i++)
                    _disabledGameObjects.Remove(activeChildren[i]);
                _incremental.AddConversionRequest(activeChildren);
            }

            // for each GameObjects that has changed parent, we have to also reconvert all of its parents:
            //  (a) if it moved from a disabled parent, we need to remove it from the linked entity group there
            //  (b) if it moved to a disabled parent, we need to add it to the linked entity group there
            var moved = _incremental.IncomingChanges.ParentChanges;
            for (int i = 0; i < moved.Length; i++)
            {
                var current = Resources.InstanceIDToObject(moved[i].NewParentInstanceId) as GameObject;
                var previous = Resources.InstanceIDToObject(moved[i].PreviousParentInstanceId) as GameObject;
                if (current != null)
                    ReconvertAllDisabledParents(current.transform);

                if (previous != null)
                    ReconvertAllDisabledParents(previous.transform);
            }
        }

        // TODO: Instead of reconverting the parents, we could reasonably well patch the linked entity groups
        void ReconvertAllDisabledParents(Transform t)
        {
            if (t == null)
                return;
            var current = t.gameObject;
            while (!current.activeInHierarchy)
            {
                _incremental.AddConversionRequest(current.GetInstanceID());
                var p = current.transform.parent;
                if (p == null)
                    break;
                current = p.gameObject;
            }
        }

        void FindActiveChildren(GameObject gameObject, HashSet<int> visitedInstances, NativeList<int> conversionRequests)
        {
            int instanceId = gameObject.GetInstanceID();
            if (!visitedInstances.Add(instanceId) || !gameObject.activeInHierarchy)
                return;
            conversionRequests.Add(instanceId);
            var t = gameObject.transform;
            int n = t.childCount;
            for (int c = 0; c < n; c++)
                FindActiveChildren(t.GetChild(c).gameObject, visitedInstances, conversionRequests);
        }
    }
}
#endif
