using System;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
#if !NET_DOTS
using System.Text.RegularExpressions;
#endif

namespace Unity.Entities.Tests
{
    class IJobChunkTests : ECSTestsFixture
    {
        struct ProcessChunks : IJobChunk
        {
            public ComponentTypeHandle<EcsTestData> EcsTestTypeHandle;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int entityOffset)
            {
                var testDataArray = chunk.GetNativeArray(EcsTestTypeHandle);
                testDataArray[0] = new EcsTestData
                {
                    value = 5
                };
            }
        }

        [Test]
        public void IJobChunkProcess()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var group = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                Any = Array.Empty<ComponentType>(),
                None = Array.Empty<ComponentType>(),
                All = new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) }
            });

            var entity = m_Manager.CreateEntity(archetype);
            var job = new ProcessChunks
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Run(group);

            Assert.AreEqual(5, m_Manager.GetComponentData<EcsTestData>(entity).value);
        }

        [Test]
        public void IJobChunk_Run_WorksWithMultipleChunks()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var group = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                Any = Array.Empty<ComponentType>(),
                None = Array.Empty<ComponentType>(),
                All = new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) }
            });

            const int entityCount = 10;
            var entities = new NativeArray<Entity>(entityCount, Allocator.Temp);

            m_Manager.CreateEntity(archetype, entities);

            for (int i = 0; i < entityCount; ++i)
                m_Manager.SetSharedComponentData(entities[i], new EcsTestSharedComp(i));

            var job = new ProcessChunks
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Run(group);

            for (int i = 0; i < entityCount; ++i)
                Assert.AreEqual(5, m_Manager.GetComponentData<EcsTestData>(entities[i]).value);

            entities.Dispose();
        }

        [Test]
        public void IJobChunkProcessFiltered()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));

            var entity1 = m_Manager.CreateEntity(archetype);
            var entity2 = m_Manager.CreateEntity(archetype);

            m_Manager.SetSharedComponentData<SharedData1>(entity1, new SharedData1 { value = 10 });
            m_Manager.SetComponentData<EcsTestData>(entity1, new EcsTestData { value = 10 });

            m_Manager.SetSharedComponentData<SharedData1>(entity2, new SharedData1 { value = 20 });
            m_Manager.SetComponentData<EcsTestData>(entity2, new EcsTestData { value = 20 });

            group.SetSharedComponentFilter(new SharedData1 { value = 20 });

            var job = new ProcessChunks
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Schedule(group).Complete();

            Assert.AreEqual(10, m_Manager.GetComponentData<EcsTestData>(entity1).value);
            Assert.AreEqual(5,  m_Manager.GetComponentData<EcsTestData>(entity2).value);

            group.Dispose();
        }

        [Test]
        public void IJobChunkWithEntityOffsetCopy()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2));

            var entities = new NativeArray<Entity>(50000, Allocator.Temp);
            m_Manager.CreateEntity(archetype, entities);

            for (int i = 0; i < 50000; ++i)
                m_Manager.SetComponentData(entities[i], new EcsTestData { value = i });

            entities.Dispose();

            var copyIndices = group.ToComponentDataArray<EcsTestData>(Allocator.TempJob);

            for (int i = 0; i < 50000; ++i)
                Assert.AreEqual(copyIndices[i].value, i);

            copyIndices.Dispose();
        }

        struct ProcessChunkIndex : IJobChunk
        {
            public ComponentTypeHandle<EcsTestData> EcsTestTypeHandle;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int entityOffset)
            {
                var testDataArray = chunk.GetNativeArray(EcsTestTypeHandle);
                testDataArray[0] = new EcsTestData
                {
                    value = chunkIndex
                };
            }
        }

        struct ProcessEntityOffset : IJobChunk
        {
            public ComponentTypeHandle<EcsTestData> EcsTestTypeHandle;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int entityOffset)
            {
                var testDataArray = chunk.GetNativeArray(EcsTestTypeHandle);
                for (int i = 0; i < chunk.Count; ++i)
                {
                    testDataArray[i] = new EcsTestData
                    {
                        value = entityOffset
                    };
                }
            }
        }

        [Test]
        public void IJobChunkProcessChunkIndexWithFilter()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));

            var entity1 = m_Manager.CreateEntity(archetype);
            var entity2 = m_Manager.CreateEntity(archetype);

            m_Manager.SetSharedComponentData<SharedData1>(entity1, new SharedData1 { value = 10 });
            m_Manager.SetComponentData<EcsTestData>(entity1, new EcsTestData { value = 10 });

            m_Manager.SetSharedComponentData<SharedData1>(entity2, new SharedData1 { value = 20 });
            m_Manager.SetComponentData<EcsTestData>(entity2, new EcsTestData { value = 20 });

            group.SetSharedComponentFilter(new SharedData1 { value = 10 });

            var job = new ProcessChunkIndex
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Schedule(group).Complete();

            group.SetSharedComponentFilter(new SharedData1 { value = 20 });
            job.Schedule(group).Complete();

            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity1).value);
            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity2).value);

            group.Dispose();
        }

        [Test]
        public void IJobChunkProcessChunkIndexWithFilterRun()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));

            var entity1 = m_Manager.CreateEntity(archetype);
            var entity2 = m_Manager.CreateEntity(archetype);

            m_Manager.SetSharedComponentData<SharedData1>(entity1, new SharedData1 { value = 10 });
            m_Manager.SetComponentData<EcsTestData>(entity1, new EcsTestData { value = 10 });

            m_Manager.SetSharedComponentData<SharedData1>(entity2, new SharedData1 { value = 20 });
            m_Manager.SetComponentData<EcsTestData>(entity2, new EcsTestData { value = 20 });

            group.SetSharedComponentFilter(new SharedData1 { value = 10 });

            var job = new ProcessChunkIndex
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Run(group);

            group.SetSharedComponentFilter(new SharedData1 { value = 20 });
            job.Run(group);

            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity1).value);
            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity2).value);

            group.Dispose();
        }

        [Test]
        public void IJobChunkProcessChunkIndex()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));

            var entity1 = m_Manager.CreateEntity(archetype);
            var entity2 = m_Manager.CreateEntity(archetype);

            m_Manager.SetSharedComponentData<SharedData1>(entity1, new SharedData1 { value = 10 });
            m_Manager.SetComponentData<EcsTestData>(entity1, new EcsTestData { value = 10 });

            m_Manager.SetSharedComponentData<SharedData1>(entity2, new SharedData1 { value = 20 });
            m_Manager.SetComponentData<EcsTestData>(entity2, new EcsTestData { value = 20 });

            var job = new ProcessChunkIndex
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            // ScheduleSingle forces all chunks to run on a single thread, so the for loop in IJobChunk.ExecuteInternal() has >1 iteration.
            job.ScheduleSingle(group).Complete();

            int[] values =
            {
                m_Manager.GetComponentData<EcsTestData>(entity1).value,
                m_Manager.GetComponentData<EcsTestData>(entity2).value,
            };
            CollectionAssert.AreEquivalent(values, new int[] {0, 1});

            group.Dispose();
        }

        [Test]
        public void IJobChunkProcessEntityOffset()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));

            var entity1 = m_Manager.CreateEntity(archetype);
            var entity2 = m_Manager.CreateEntity(archetype);

            m_Manager.SetSharedComponentData<SharedData1>(entity1, new SharedData1 { value = 10 });
            m_Manager.SetComponentData<EcsTestData>(entity1, new EcsTestData { value = 10 });

            m_Manager.SetSharedComponentData<SharedData1>(entity2, new SharedData1 { value = 20 });
            m_Manager.SetComponentData<EcsTestData>(entity2, new EcsTestData { value = 20 });

            group.SetSharedComponentFilter(new SharedData1 { value = 10 });

            var job = new ProcessEntityOffset
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Schedule(group).Complete();

            group.SetSharedComponentFilter(new SharedData1 { value = 20 });
            job.Schedule(group).Complete();

            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity1).value);
            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity2).value);

            group.Dispose();
        }

        [Test]
        public void IJobChunkProcessChunkMultiArchetype()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var archetypeC = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3));

            var entity1A = m_Manager.CreateEntity(archetypeA);
            var entity2A = m_Manager.CreateEntity(archetypeA);
            var entityB = m_Manager.CreateEntity(archetypeB);
            var entityC = m_Manager.CreateEntity(archetypeC);

            EntityQuery query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            m_Manager.SetComponentData<EcsTestData>(entity1A, new EcsTestData { value = -1 });
            m_Manager.SetComponentData<EcsTestData>(entity2A, new EcsTestData { value = -1 });
            m_Manager.SetComponentData<EcsTestData>(entityB,  new EcsTestData { value = -1 });
            m_Manager.SetComponentData<EcsTestData>(entityC,  new EcsTestData { value = -1 });

            var job = new ProcessEntityOffset
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Schedule(query).Complete();

            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity1A).value);
            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity2A).value);
            Assert.AreEqual(2,  m_Manager.GetComponentData<EcsTestData>(entityB).value);
            Assert.AreEqual(3,  m_Manager.GetComponentData<EcsTestData>(entityC).value);

            query.Dispose();
        }

        struct ProcessChunkWriteIndex : IJobChunk
        {
            public ComponentTypeHandle<EcsTestData> EcsTestTypeHandle;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int entityOffset)
            {
                var testDataArray = chunk.GetNativeArray(EcsTestTypeHandle);
                for (int i = 0; i < chunk.Count; ++i)
                {
                    testDataArray[i] = new EcsTestData
                    {
                        value = entityOffset + i
                    };
                }
            }
        }

        struct WriteToArray : IJobChunk
        {
            public NativeArray<int> MyArray;
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                for (int i = 0; i < MyArray.Length; i++)
                {
                    MyArray[i] = chunkIndex + firstEntityIndex;
                }
            }
        }

#if !NET_DOTS // DOTS Runtimes does not support regex
        [Test]
        public void ParallelArrayWriteTriggersSafetySystem()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entitiesA = new NativeArray<Entity>(archetypeA.ChunkCapacity, Allocator.Temp);
            m_Manager.CreateEntity(archetypeA, entitiesA);
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            using (var local = new NativeArray<int>(archetypeA.ChunkCapacity * 2, Allocator.TempJob))
            {
                LogAssert.Expect(LogType.Exception, new Regex("IndexOutOfRangeException: *"));

                new WriteToArray
                {
                    MyArray = local
                }.ScheduleParallel(query).Complete();
            }
        }

        [Test]
        public void SingleArrayWriteDoesNotTriggerSafetySystem()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entitiesA = new NativeArray<Entity>(archetypeA.ChunkCapacity, Allocator.Temp);
            m_Manager.CreateEntity(archetypeA, entitiesA);
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            using (var local = new NativeArray<int>(archetypeA.ChunkCapacity * 2, Allocator.TempJob))
            {
                new WriteToArray
                {
                    MyArray = local
                }.ScheduleSingle(query).Complete();
            }
        }
#endif

#if !UNITY_DOTSRUNTIME // IJobForEach is deprecated
#pragma warning disable 618
        struct ForEachComponentData : IJobForEachWithEntity<EcsTestData>
        {
            public void Execute(Entity entity, int index, ref EcsTestData c0)
            {
                c0 = new EcsTestData { value = index };
            }
        }
#pragma warning restore 618

        [Test]
        public void FilteredIJobChunkProcessesSameChunksAsFilteredJobForEach()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));

            var entitiesA = new NativeArray<Entity>(5000, Allocator.Temp);
            m_Manager.CreateEntity(archetypeA, entitiesA);

            var entitiesB = new NativeArray<Entity>(5000, Allocator.Temp);
            // TODO this looks like a test bug. Shouldn't it be (archetypeB, entitiesB)?
            m_Manager.CreateEntity(archetypeA, entitiesB);

            for (int i = 0; i < 5000; ++i)
            {
                m_Manager.SetSharedComponentData(entitiesA[i], new EcsTestSharedComp { value = i % 8 });
                m_Manager.SetSharedComponentData(entitiesB[i], new EcsTestSharedComp { value = i % 8 });
            }

            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));

            group.SetSharedComponentFilter(new EcsTestSharedComp { value = 1 });

            var jobChunk = new ProcessChunkWriteIndex
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            jobChunk.Schedule(group).Complete();

            var componentArrayA = group.ToComponentDataArray<EcsTestData>(Allocator.TempJob);

            var jobProcess = new ForEachComponentData
            {};
            jobProcess.Schedule(group).Complete();

            var componentArrayB = group.ToComponentDataArray<EcsTestData>(Allocator.TempJob);

            CollectionAssert.AreEqual(componentArrayA.ToArray(), componentArrayB.ToArray());

            componentArrayA.Dispose();
            componentArrayB.Dispose();
            group.Dispose();
        }

#endif // !UNITY_DOTSRUNTIME

#if !UNITY_2020_2_OR_NEWER
        struct InitializedAsSingleAndParallelJob : IJobChunk
        {
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int entityOffset) {}
        }
        [Test]
        public void IJobChunkInitializedAsSingleAndParallel_CreatesDifferentScheduleData()
        {
            var jobReflectionDataParallel = JobChunkExtensions.JobChunkProducer<InitializedAsSingleAndParallelJob>.InitializeParallel();
            var jobReflectionDataSingle = JobChunkExtensions.JobChunkProducer<InitializedAsSingleAndParallelJob>.InitializeSingle();

            Assert.AreNotEqual(jobReflectionDataParallel, jobReflectionDataSingle);
        }
#endif
    }
}
