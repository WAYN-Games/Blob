using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Entities.Tests
{
    partial class JobSafetyTests : ECSTestsFixture
    {
        struct TestIncrementJob : IJob
        {
            public NativeArray<Entity> entities;
            public ComponentDataFromEntity<EcsTestData> data;
            public void Execute()
            {
                for (int i = 0; i != entities.Length; i++)
                {
                    var entity = entities[i];

                    var d = data[entity];
                    d.value++;
                    data[entity] = d;
                }
            }
        }

        [Test]
        public void ComponentAccessAfterScheduledJobThrows()
        {
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));

            var job = new TestIncrementJob();
            job.entities = group.ToEntityArray(Allocator.TempJob);
            job.data = m_Manager.GetComponentDataFromEntity<EcsTestData>();

            Assert.AreEqual(42, job.data[job.entities[0]].value);

            var fence = job.Schedule();

            Assert.Throws<System.InvalidOperationException>(() =>
            {
                var f = job.data[job.entities[0]].value;
                // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                f.GetHashCode();
            });

            fence.Complete();
            Assert.AreEqual(43, job.data[job.entities[0]].value);

            job.entities.Dispose();
        }

        // These tests require:
        // - JobsDebugger support for static safety IDs (added in 2020.1)
        // - Asserting throws
#if !UNITY_DOTSRUNTIME
        struct UseComponentDataFromEntity : IJob
        {
            public ComponentDataFromEntity<EcsTestData> data;
            public void Execute()
            {
            }
        }

        [Test,DotsRuntimeFixme]
        public void ComponentDataFromEntity_UseAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            var testDatas = m_Manager.GetComponentDataFromEntity<EcsTestData>();

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates ComponentDataFromEntity

            Assert.That(() => { var f = testDatas[entity].value; },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<ObjectDisposedException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains(
                        "ComponentDataFromEntity<Unity.Entities.Tests.EcsTestData> which has been invalidated by a structural change"));
        }

        [Test,DotsRuntimeFixme]
        public void ComponentDataFromEntity_UseFromJobAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            var testDatas = m_Manager.GetComponentDataFromEntity<EcsTestData>();

            var job = new UseComponentDataFromEntity();
            job.data = testDatas;

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates ComponentDataFromEntity

            Assert.That(() => { job.Schedule().Complete(); },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<InvalidOperationException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains(
                        "ComponentDataFromEntity<Unity.Entities.Tests.EcsTestData> UseComponentDataFromEntity.data which has been invalidated by a structural change."));
        }

        struct UseBufferFromEntity : IJob
        {
            public BufferFromEntity<EcsIntElement> data;
            public void Execute()
            {
            }
        }

        [Test,DotsRuntimeFixme]
        public void BufferFromEntity_UseAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsIntElement));
            var testDatas = m_Manager.GetBufferFromEntity<EcsIntElement>();

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates BufferFromEntity

            Assert.That(() => { var f = testDatas[entity]; },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<ObjectDisposedException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains(
                        "BufferFromEntity<Unity.Entities.Tests.EcsIntElement> which has been invalidated by a structural change."));
        }

        [Test,DotsRuntimeFixme]
        public void BufferFromEntity_UseFromJobAfterStructuralChange_ThrowsCustomErrorMessage()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsIntElement));
            var testDatas = m_Manager.GetBufferFromEntity<EcsIntElement>();

            var job = new UseBufferFromEntity();
            job.data = testDatas;

            m_Manager.AddComponent<EcsTestData2>(entity); // invalidates BufferFromEntity

            Assert.That(() => { job.Schedule().Complete(); },
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<InvalidOperationException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains(
                        "BufferFromEntity<Unity.Entities.Tests.EcsIntElement> UseBufferFromEntity.data which has been invalidated by a structural change."));
        }

#endif

        [Test]
        public void GetComponentCompletesJob()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var job = new TestIncrementJob();
            job.entities = group.ToEntityArray(Allocator.TempJob);
            job.data = m_Manager.GetComponentDataFromEntity<EcsTestData>();
            group.AddDependency(job.Schedule());

            // Implicit Wait for job, returns value after job has completed.
            Assert.AreEqual(1, m_Manager.GetComponentData<EcsTestData>(entity).value);

            job.entities.Dispose();
        }

        [Test]
        public void DestroyEntityCompletesScheduledJobs()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            /*var entity2 =*/ m_Manager.CreateEntity(typeof(EcsTestData));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var job = new TestIncrementJob();
            job.entities = group.ToEntityArray(Allocator.TempJob);
            job.data = m_Manager.GetComponentDataFromEntity<EcsTestData>();
            group.AddDependency(job.Schedule());

            m_Manager.DestroyEntity(entity);

            var componentData = group.ToComponentDataArray<EcsTestData>(Allocator.TempJob);

            // @TODO: This is maybe a little bit dodgy way of determining if the job has been completed...
            //        Probably should expose api to inspector job debugger state...
            Assert.AreEqual(1, componentData.Length);
            Assert.AreEqual(1, componentData[0].value);

            job.entities.Dispose();
            componentData.Dispose();
        }

        // This does what normal TearDown does, minus shutting down engine subsystems
        private void CleanupWorld()
        {
            if (World != null && World.IsCreated)
            {
                // Clean up systems before calling CheckInternalConsistency because we might have filters etc
                // holding on SharedComponentData making checks fail
                while (World.Systems.Count > 0)
                {
                    World.DestroySystem(World.Systems[0]);
                }

                m_ManagerDebug.CheckInternalConsistency();

                World.Dispose();
                World = null;

                World.DefaultGameObjectInjectionWorld = m_PreviousWorld;
                m_PreviousWorld = null;
                m_Manager = default;
            }
        }

        [Test]
        public void EntityManagerDestructionDetectsUnregisteredJob()
        {
#if !NET_DOTS
#if UNITY_DOTSRUNTIME
            LogAssert.ExpectReset();
#endif
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("job is still running"));
#endif

            /*var entity =*/ m_Manager.CreateEntity(typeof(EcsTestData));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var job = new TestIncrementJob();
            job.entities = group.ToEntityArray(Allocator.TempJob);
            job.data = m_Manager.GetComponentDataFromEntity<EcsTestData>();
            var jobHandle = job.Schedule();

            // This should detect the unregistered running job & emit the expected error message
            CleanupWorld();

            // Manually complete the job before cleaning up for real
            jobHandle.Complete();
            CleanupWorld();
            job.entities.Dispose();
#if !NET_DOTS
            LogAssert.NoUnexpectedReceived();
#endif
        }

        [Test]
        public void DestroyEntityDetectsUnregisteredJob()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var job = new TestIncrementJob();
            job.entities = group.ToEntityArray(Allocator.TempJob);
            job.data = m_Manager.GetComponentDataFromEntity<EcsTestData>();
            var fence = job.Schedule();

            Assert.Throws<System.InvalidOperationException>(() => { m_Manager.DestroyEntity(entity); });

            fence.Complete();
            job.entities.Dispose();
        }

        [Test]
        public void GetComponentDetectsUnregisteredJob()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var job = new TestIncrementJob();
            job.entities = group.ToEntityArray(Allocator.TempJob);
            job.data = m_Manager.GetComponentDataFromEntity<EcsTestData>();
            var jobHandle = job.Schedule();

            Assert.Throws<System.InvalidOperationException>(() => { m_Manager.GetComponentData<EcsTestData>(entity); });

            jobHandle.Complete();

            job.entities.Dispose();
        }

        struct EntityOnlyDependencyJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle EntityTypeHandle;
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
            }
        }

        struct NoDependenciesJob : IJobChunk
        {
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
            }
        }

        class EntityOnlyDependencySystem : JobComponentSystem
        {
            public JobHandle JobHandle;
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                EntityManager.CreateEntity(typeof(EcsTestData));
                var group = GetEntityQuery(new ComponentType[] {});
                var job = new EntityOnlyDependencyJob
                {
                    EntityTypeHandle = EntityManager.GetEntityTypeHandle()
                };
                JobHandle = job.Schedule(group, inputDeps);
                return JobHandle;
            }
        }

        class NoComponentDependenciesSystem : JobComponentSystem
        {
            public JobHandle JobHandle;
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                EntityManager.CreateEntity(typeof(EcsTestData));
                var group = GetEntityQuery(new ComponentType[] {});
                var job = new NoDependenciesJob {};

                JobHandle = job.Schedule(group, inputDeps);
                return JobHandle;
            }
        }

        class DestroyAllEntitiesSystem : JobComponentSystem
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                var allEntities = EntityManager.GetAllEntities();
                EntityManager.DestroyEntity(allEntities);
                allEntities.Dispose();
                return inputDeps;
            }
        }

        [Test]
        public void StructuralChangeCompletesEntityOnlyDependencyJob()
        {
            var system = World.GetOrCreateSystem<EntityOnlyDependencySystem>();
            system.Update();
            World.GetOrCreateSystem<DestroyAllEntitiesSystem>().Update();
            Assert.IsTrue(JobHandle.CheckFenceIsDependencyOrDidSyncFence(system.JobHandle, new JobHandle()));
        }

        [Test]
        public void StructuralChangeCompletesNoComponentDependenciesJob()
        {
            var system = World.GetOrCreateSystem<NoComponentDependenciesSystem>();
            system.Update();
            World.GetOrCreateSystem<DestroyAllEntitiesSystem>().Update();
            Assert.IsTrue(JobHandle.CheckFenceIsDependencyOrDidSyncFence(system.JobHandle, new JobHandle()));
        }

        [Test]
        public void StructuralChangeAfterSchedulingNoDependenciesJobThrows()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entity = m_Manager.CreateEntity(archetype);
            var group = EmptySystem.GetEntityQuery(typeof(EcsTestData));
            var handle = new NoDependenciesJob().Schedule(group);
            Assert.Throws<InvalidOperationException>(() => m_Manager.DestroyEntity(entity));
            handle.Complete();
        }

        [Test]
        public void StructuralChangeAfterSchedulingEntityOnlyDependencyJobThrows()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entity = m_Manager.CreateEntity(archetype);
            var group = EmptySystem.GetEntityQuery(typeof(EcsTestData));
            var handle = new EntityOnlyDependencyJob {EntityTypeHandle = m_Manager.GetEntityTypeHandle()}.Schedule(group);
            Assert.Throws<InvalidOperationException>(() => m_Manager.DestroyEntity(entity));
            handle.Complete();
        }

        class SharedComponentSystem : JobComponentSystem
        {
            EntityQuery group;
            protected override void OnCreate()
            {
                group = GetEntityQuery(ComponentType.ReadOnly<EcsTestSharedComp>());
            }

            struct SharedComponentJobChunk : IJobChunk
            {
                [ReadOnly] public SharedComponentTypeHandle<EcsTestSharedComp> EcsTestSharedCompTypeHandle;
                public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
                {
                }
            }

            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return new SharedComponentJobChunk
                {
                    EcsTestSharedCompTypeHandle = GetSharedComponentTypeHandle<EcsTestSharedComp>()
                }.Schedule(group, inputDeps);
            }
        }

        [Test]
        public void JobsUsingArchetypeChunkSharedComponentTypeSyncOnStructuralChange()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var entity = m_Manager.CreateEntity(archetype);

            var sharedComponentSystem = World.CreateSystem<SharedComponentSystem>();

            sharedComponentSystem.Update();
            // DestroyEntity should sync the job and not cause any safety error
            m_Manager.DestroyEntity(entity);
        }

#if !UNITY_DOTSRUNTIME  // IJobForEach is deprecated
#pragma warning disable 618
        struct BufferSafetyJobA : IJobChunk
        {
            public BufferTypeHandle<EcsIntElement> BufferTypeHandleRO;

            [DeallocateOnJobCompletion]
            public NativeArray<int> MyArray;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var accessor = chunk.GetBufferAccessor(BufferTypeHandleRO);
                for (int bufferIndex = 0; bufferIndex < chunk.Count; ++bufferIndex)
                {
                    var buffer = accessor[bufferIndex];
                    MyArray[0] = new EcsIntElement
                    {
                        Value = buffer[0].Value + buffer[1].Value + buffer[2].Value,
                    };
                }
            }
        }

        struct BufferSafetyJobB : IJobParallelFor
        {
            [ReadOnly]
            [DeallocateOnJobCompletion]
            public NativeArray<Entity> Entities;

            [ReadOnly]
            public BufferFromEntity<EcsIntElement> BuffersFromEntity;

            public void Execute(int index)
            {
                var buffer = BuffersFromEntity[Entities[index]];
                var total = buffer[3].Value;
            }
        }

        struct BufferSafetyJobC : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle EntityTypeHandle;

            [ReadOnly]
            public BufferFromEntity<EcsIntElement> BufferFromEntityRO;

            [DeallocateOnJobCompletion]
            public NativeArray<int> MyArray;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var entities = chunk.GetNativeArray(EntityTypeHandle);
                for (int entityIndex = 0; entityIndex < chunk.Count; ++entityIndex)
                {
                    var buffer = BufferFromEntityRO[entities[entityIndex]];
                    MyArray[0] = new EcsIntElement
                    {
                        Value = buffer[0].Value + buffer[1].Value + buffer[2].Value,
                    };
                }
            }
        }
#pragma warning restore 618

        public void SetupDynamicBufferJobTestEnvironment()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsIntElement), typeof(EcsTestData), typeof(EcsTestData2));
            var entity = m_Manager.CreateEntity(archetype);
            var buffer = m_Manager.GetBuffer<EcsIntElement>(entity);
            buffer.Add(new EcsIntElement { Value = 1 });
            buffer.Add(new EcsIntElement { Value = 10 });
            buffer.Add(new EcsIntElement { Value = 100 });
            buffer.Add(new EcsIntElement { Value = 0 });
        }

        [Test]
        public void TwoJobsUsingDynamicBuffersDontCauseSafetySystemFalsePositiveErrors()
        {
            SetupDynamicBufferJobTestEnvironment();

            var query = EmptySystem.GetEntityQuery(typeof(EcsIntElement));

            var jobA = new BufferSafetyJobA{BufferTypeHandleRO = m_Manager.GetBufferTypeHandle<EcsIntElement>(true), MyArray = new NativeArray<int>(1, Allocator.TempJob)};
            var jobAHandle = jobA.Schedule(query, default(JobHandle));

            var jobB = new BufferSafetyJobB
            {
                Entities = query.ToEntityArray(Allocator.TempJob),
                BuffersFromEntity = EmptySystem.GetBufferFromEntity<EcsIntElement>(true)
            };
            var jobBHandle = jobB.Schedule(jobB.Entities.Length, 1, default(JobHandle));
            jobBHandle.Complete();
            jobAHandle.Complete();
        }

        [Test]
        public void TwoJobsUsingReadOnlyDynamicBuffersCanRunInParallel_BufferFromEntity()
        {
            SetupDynamicBufferJobTestEnvironment();

            var query = EmptySystem.GetEntityQuery(typeof(EcsIntElement));

            var jobA = new BufferSafetyJobC{EntityTypeHandle = m_Manager.GetEntityTypeHandle(), BufferFromEntityRO = m_Manager.GetBufferFromEntity<EcsIntElement>(true), MyArray = new NativeArray<int>(1, Allocator.TempJob)};
            var jobAHandle = jobA.Schedule(query, default(JobHandle));

            var jobB = new BufferSafetyJobB
            {
                Entities = query.ToEntityArray(Allocator.TempJob),
                BuffersFromEntity = EmptySystem.GetBufferFromEntity<EcsIntElement>(true)
            };
            var jobBHandle = jobB.Schedule(jobB.Entities.Length, 1, default(JobHandle));

            jobAHandle.Complete();
            jobBHandle.Complete();
        }

#pragma warning disable 618
        struct BufferSafetyJob_TwoReadOnly : IJobForEachWithEntity_EB<EcsIntElement>
        {
            [ReadOnly]
            public BufferFromEntity<EcsIntElement> BuffersFromEntity;

            public void Execute(Entity e, int _, [ReadOnly] DynamicBuffer<EcsIntElement> bufferA)
            {
                var bufferB = BuffersFromEntity[e];

                var totalA = bufferA[0] + bufferA[1] + bufferA[2];
                var totalB = bufferB[0] + bufferB[1] + bufferB[2];
            }
        }
#pragma warning restore 618

        [Test]
        public void SingleJobUsingSameReadOnlyDynamicBuffer()
        {
            SetupDynamicBufferJobTestEnvironment();

            var query = EmptySystem.GetEntityQuery(typeof(EcsIntElement));

            var job = new BufferSafetyJob_TwoReadOnly
            {
                BuffersFromEntity = EmptySystem.GetBufferFromEntity<EcsIntElement>(true)
            };
            job.Run(query);
        }

#pragma warning disable 618
        struct BufferSafetyJob_OneRead_OneWrite : IJobForEachWithEntity_EB<EcsIntElement>
        {
            public BufferFromEntity<EcsIntElement> BuffersFromEntity;

            public void Execute(Entity e, int _, [ReadOnly] DynamicBuffer<EcsIntElement> bufferA)
            {
                var bufferB = BuffersFromEntity[e];

                var totalA = bufferA[0] + bufferA[1] + bufferA[2];
                var totalB = bufferB[0] + bufferB[1] + bufferB[2];
                bufferB[3] = new EcsIntElement {Value = totalB};
            }
        }
#pragma warning restore 618

        [Test]
        public void SingleJobUsingSameReadOnlyAndReadWriteDynamicBufferThrows()
        {
            SetupDynamicBufferJobTestEnvironment();

            var query = EmptySystem.GetEntityQuery(typeof(EcsIntElement));

            var job = new BufferSafetyJob_OneRead_OneWrite
            {
                BuffersFromEntity = EmptySystem.GetBufferFromEntity<EcsIntElement>(false)
            };
            Assert.Throws<InvalidOperationException>(() =>
            {
                job.Run(query);
            });
        }

#pragma warning disable 618
        unsafe struct BufferSafetyJob_GetUnsafePtrReadWrite : IJobForEach_BCC<EcsIntElement, EcsTestData, EcsTestData2>
        {
            public void Execute(DynamicBuffer<EcsIntElement> b0, [ReadOnly] ref EcsTestData c1, [ReadOnly] ref EcsTestData2 c2)
            {
                b0.GetUnsafePtr();
            }
        }
#pragma warning restore 618

        [Test]
        public void DynamicBuffer_UnsafePtr_DoesntThrowWhenReadWrite()
        {
            SetupDynamicBufferJobTestEnvironment();

            var job = new BufferSafetyJob_GetUnsafePtrReadWrite {};
            job.Run(EmptySystem);
        }
#endif // !UNITY_DOTSRUNTIME

        public partial class DynamicBufferReadOnlySystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .ForEach((
                    Entity e,
                    in DynamicBuffer<EcsIntElement> buffers) =>
                    {
                        unsafe
                        {
                            var ptr = buffers.GetUnsafeReadOnlyPtr();
                        }
                    }).Run();
            }
        }

        [Test]
        public void DynamicBuffer_UnsafeReadOnlyPtr_DoesntThrowWhenReadOnly()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsIntElement));
            var sys = World.CreateSystem<DynamicBufferReadOnlySystem>();
            sys.Update();
            m_Manager.DestroyEntity(ent);
            World.DestroySystem(sys);
        }
    }
}
