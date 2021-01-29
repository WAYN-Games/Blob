using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;

namespace Unity.Transforms
{
    public abstract class ParentSystem : JobComponentSystem
    {
        EntityQuery m_NewParentsQuery;
        EntityQuery m_RemovedParentsQuery;
        EntityQuery m_ExistingParentsQuery;
        EntityQuery m_DeletedParentsQuery;

        static readonly ProfilerMarker k_ProfileDeletedParents = new ProfilerMarker("ParentSystem.DeletedParents");
        static readonly ProfilerMarker k_ProfileRemoveParents = new ProfilerMarker("ParentSystem.RemoveParents");
        static readonly ProfilerMarker k_ProfileChangeParents = new ProfilerMarker("ParentSystem.ChangeParents");
        static readonly ProfilerMarker k_ProfileNewParents = new ProfilerMarker("ParentSystem.NewParents");

        int FindChildIndex(DynamicBuffer<Child> children, Entity entity)
        {
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].Value == entity)
                    return i;
            }

            throw new InvalidOperationException("Child entity not in parent");
        }

        void RemoveChildFromParent(Entity childEntity, Entity parentEntity)
        {
            if (!EntityManager.HasComponent<Child>(parentEntity))
                return;

            var children = EntityManager.GetBuffer<Child>(parentEntity);
            var childIndex = FindChildIndex(children, childEntity);
            children.RemoveAt(childIndex);
            if (children.Length == 0)
            {
                EntityManager.RemoveComponent(parentEntity, typeof(Child));
            }
        }

        [BurstCompile]
        struct GatherChangedParents : IJobEntityBatch
        {
            public NativeMultiHashMap<Entity, Entity>.ParallelWriter ParentChildrenToAdd;
            public NativeMultiHashMap<Entity, Entity>.ParallelWriter ParentChildrenToRemove;
            public NativeHashMap<Entity, int>.ParallelWriter UniqueParents;
            public ComponentTypeHandle<PreviousParent> PreviousParentTypeHandle;

            [ReadOnly] public ComponentTypeHandle<Parent> ParentTypeHandle;
            [ReadOnly] public EntityTypeHandle EntityTypeHandle;
            public uint LastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                if (batchInChunk.DidChange(ParentTypeHandle, LastSystemVersion))
                {
                    var chunkPreviousParents = batchInChunk.GetNativeArray(PreviousParentTypeHandle);
                    var chunkParents = batchInChunk.GetNativeArray(ParentTypeHandle);
                    var chunkEntities = batchInChunk.GetNativeArray(EntityTypeHandle);

                    for (int j = 0; j < batchInChunk.Count; j++)
                    {
                        if (chunkParents[j].Value != chunkPreviousParents[j].Value)
                        {
                            var childEntity = chunkEntities[j];
                            var parentEntity = chunkParents[j].Value;
                            var previousParentEntity = chunkPreviousParents[j].Value;

                            ParentChildrenToAdd.Add(parentEntity, childEntity);
                            UniqueParents.TryAdd(parentEntity, 0);

                            if (previousParentEntity != Entity.Null)
                            {
                                ParentChildrenToRemove.Add(previousParentEntity, childEntity);
                                UniqueParents.TryAdd(previousParentEntity, 0);
                            }

                            chunkPreviousParents[j] = new PreviousParent
                            {
                                Value = parentEntity
                            };
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct FindMissingChild : IJob
        {
            [ReadOnly] public NativeHashMap<Entity, int> UniqueParents;
            [ReadOnly] public BufferFromEntity<Child> ChildFromEntity;
            public NativeList<Entity> ParentsMissingChild;

            public void Execute()
            {
                var parents = UniqueParents.GetKeyArray(Allocator.Temp);
                for (int i = 0; i < parents.Length; i++)
                {
                    var parent = parents[i];
                    if (!ChildFromEntity.HasComponent(parent))
                    {
                        ParentsMissingChild.Add(parent);
                    }
                }
            }
        }

        [BurstCompile]
        struct FixupChangedChildren : IJob
        {
            [ReadOnly] public NativeMultiHashMap<Entity, Entity> ParentChildrenToAdd;
            [ReadOnly] public NativeMultiHashMap<Entity, Entity> ParentChildrenToRemove;
            [ReadOnly] public NativeHashMap<Entity, int> UniqueParents;

            public BufferFromEntity<Child> ChildFromEntity;

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void ThrowChildEntityNotInParent()
            {
                throw new InvalidOperationException("Child entity not in parent");
            }

            int FindChildIndex(DynamicBuffer<Child> children, Entity entity)
            {
                for (int i = 0; i < children.Length; i++)
                {
                    if (children[i].Value == entity)
                        return i;
                }

                ThrowChildEntityNotInParent();
                return -1;
            }

            void RemoveChildrenFromParent(Entity parent, DynamicBuffer<Child> children)
            {
                if (ParentChildrenToRemove.TryGetFirstValue(parent, out var child, out var it))
                {
                    do
                    {
                        var childIndex = FindChildIndex(children, child);
                        children.RemoveAt(childIndex);
                    }
                    while (ParentChildrenToRemove.TryGetNextValue(out child, ref it));
                }
            }

            void AddChildrenToParent(Entity parent, DynamicBuffer<Child> children)
            {
                if (ParentChildrenToAdd.TryGetFirstValue(parent, out var child, out var it))
                {
                    do
                    {
                        children.Add(new Child() { Value = child });
                    }
                    while (ParentChildrenToAdd.TryGetNextValue(out child, ref it));
                }
            }

            public void Execute()
            {
                var parents = UniqueParents.GetKeyArray(Allocator.Temp);
                for (int i = 0; i < parents.Length; i++)
                {
                    var parent = parents[i];
                    var children = ChildFromEntity[parent];

                    RemoveChildrenFromParent(parent, children);
                    AddChildrenToParent(parent, children);
                }
            }
        }

        protected override void OnCreate()
        {
            m_NewParentsQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Parent>(),
                    ComponentType.ReadOnly<LocalToWorld>(),
                    ComponentType.ReadOnly<LocalToParent>()
                },
                None = new ComponentType[]
                {
                    typeof(PreviousParent)
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
            m_RemovedParentsQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    typeof(PreviousParent)
                },
                None = new ComponentType[]
                {
                    typeof(Parent)
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
            m_ExistingParentsQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Parent>(),
                    ComponentType.ReadOnly<LocalToWorld>(),
                    ComponentType.ReadOnly<LocalToParent>(),
                    typeof(PreviousParent)
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
            m_ExistingParentsQuery.SetChangedVersionFilter(typeof(Parent));
            m_DeletedParentsQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    typeof(Child)
                },
                None = new ComponentType[]
                {
                    typeof(LocalToWorld)
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
        }

        void UpdateNewParents()
        {
            if (m_NewParentsQuery.IsEmptyIgnoreFilter)
                return;

            EntityManager.AddComponent(m_NewParentsQuery, typeof(PreviousParent));
        }

        void UpdateRemoveParents()
        {
            if (m_RemovedParentsQuery.IsEmptyIgnoreFilter)
                return;

            var childEntities = m_RemovedParentsQuery.ToEntityArray(Allocator.TempJob);
            var previousParents = m_RemovedParentsQuery.ToComponentDataArray<PreviousParent>(Allocator.TempJob);

            for (int i = 0; i < childEntities.Length; i++)
            {
                var childEntity = childEntities[i];
                var previousParentEntity = previousParents[i].Value;

                RemoveChildFromParent(childEntity, previousParentEntity);
            }

            EntityManager.RemoveComponent(m_RemovedParentsQuery, typeof(PreviousParent));
            childEntities.Dispose();
            previousParents.Dispose();
        }

        void UpdateChangeParents()
        {
            if (m_ExistingParentsQuery.IsEmptyIgnoreFilter)
                return;

            var count = m_ExistingParentsQuery.CalculateEntityCount() * 2; // Potentially 2x changed: current and previous
            if (count == 0)
                return;

            // 1. Get (Parent,Child) to remove
            // 2. Get (Parent,Child) to add
            // 3. Get unique Parent change list
            // 4. Set PreviousParent to new Parent
            var parentChildrenToAdd = new NativeMultiHashMap<Entity, Entity>(count, Allocator.TempJob);
            var parentChildrenToRemove = new NativeMultiHashMap<Entity, Entity>(count, Allocator.TempJob);
            var uniqueParents = new NativeHashMap<Entity, int>(count, Allocator.TempJob);
            var gatherChangedParentsJob = new GatherChangedParents
            {
                ParentChildrenToAdd = parentChildrenToAdd.AsParallelWriter(),
                ParentChildrenToRemove = parentChildrenToRemove.AsParallelWriter(),
                UniqueParents = uniqueParents.AsParallelWriter(),
                PreviousParentTypeHandle = GetComponentTypeHandle<PreviousParent>(false),
                ParentTypeHandle = GetComponentTypeHandle<Parent>(true),
                EntityTypeHandle = GetEntityTypeHandle(),
                LastSystemVersion = LastSystemVersion
            };
            var gatherChangedParentsJobHandle = gatherChangedParentsJob.ScheduleParallel(m_ExistingParentsQuery, 1, default(JobHandle));
            gatherChangedParentsJobHandle.Complete();

            // 5. (Structural change) Add any missing Child to Parents
            var parentsMissingChild = new NativeList<Entity>(Allocator.TempJob);
            var findMissingChildJob = new FindMissingChild
            {
                UniqueParents = uniqueParents,
                ChildFromEntity = GetBufferFromEntity<Child>(),
                ParentsMissingChild = parentsMissingChild
            };
            var findMissingChildJobHandle = findMissingChildJob.Schedule();
            findMissingChildJobHandle.Complete();

            EntityManager.AddComponent(parentsMissingChild.AsArray(), typeof(Child));

            // 6. Get Child[] for each unique Parent
            // 7. Update Child[] for each unique Parent
            var fixupChangedChildrenJob = new FixupChangedChildren
            {
                ParentChildrenToAdd = parentChildrenToAdd,
                ParentChildrenToRemove = parentChildrenToRemove,
                UniqueParents = uniqueParents,
                ChildFromEntity = GetBufferFromEntity<Child>()
            };

            var fixupChangedChildrenJobHandle = fixupChangedChildrenJob.Schedule();
            fixupChangedChildrenJobHandle.Complete();

            parentChildrenToAdd.Dispose();
            parentChildrenToRemove.Dispose();
            uniqueParents.Dispose();
            parentsMissingChild.Dispose();
        }

        [BurstCompile]
        struct GatherChildEntities : IJob
        {
            public NativeArray<Entity> Parents;
            public NativeList<Entity> Children;
            public BufferFromEntity<Child> ChildFromEntity;

            public void Execute()
            {
                for (int i = 0; i < Parents.Length; i++)
                {
                    var parentEntity = Parents[i];
                    var childEntitiesSource = ChildFromEntity[parentEntity].AsNativeArray();
                    for (int j = 0; j < childEntitiesSource.Length; j++)
                        Children.Add(childEntitiesSource[j].Value);
                }
            }
        }

        void UpdateDeletedParents()
        {
            if (m_DeletedParentsQuery.IsEmptyIgnoreFilter)
                return;

            var previousParents = m_DeletedParentsQuery.ToEntityArray(Allocator.TempJob);
            var childEntities = new NativeList<Entity>(Allocator.TempJob);
            var gatherChildEntitiesJob = new GatherChildEntities
            {
                Parents = previousParents,
                Children = childEntities,
                ChildFromEntity = GetBufferFromEntity<Child>()
            };
            var gatherChildEntitiesJobHandle = gatherChildEntitiesJob.Schedule();
            gatherChildEntitiesJobHandle.Complete();

            EntityManager.RemoveComponent(childEntities, typeof(Parent));
            EntityManager.RemoveComponent(childEntities, typeof(PreviousParent));
            EntityManager.RemoveComponent(childEntities, typeof(LocalToParent));
            EntityManager.RemoveComponent(m_DeletedParentsQuery, typeof(Child));

            childEntities.Dispose();
            previousParents.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            inputDeps.Complete(); // #todo

            k_ProfileDeletedParents.Begin();
            UpdateDeletedParents();
            k_ProfileDeletedParents.End();

            k_ProfileRemoveParents.Begin();
            UpdateRemoveParents();
            k_ProfileRemoveParents.End();

            k_ProfileNewParents.Begin();
            UpdateNewParents();
            k_ProfileNewParents.End();

            k_ProfileChangeParents.Begin();
            UpdateChangeParents();
            k_ProfileChangeParents.End();

            return new JobHandle();
        }
    }
}
