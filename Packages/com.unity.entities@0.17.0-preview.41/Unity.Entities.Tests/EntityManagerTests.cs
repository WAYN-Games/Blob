using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities.Tests
{
    interface IEcsFooInterface
    {
        int value { get; set; }
    }
    public struct EcsFooTest : IComponentData, IEcsFooInterface
    {
        public int value { get; set; }

        public EcsFooTest(int inValue) { value = inValue; }
    }

    interface IEcsNotUsedInterface
    {
        int value { get; set; }
    }

    class EntityManagerTests : ECSTestsFixture
    {
#if UNITY_EDITOR
        [Test]
        public void NameEntities()
        {
            WordStorage.Setup();
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var count = 1024;
            var array = new NativeArray<Entity>(count, Allocator.Temp);
            m_Manager.CreateEntity(archetype, array);
            for (int i = 0; i < count; i++)
            {
                m_Manager.SetName(array[i], "Name" + i);
            }

            for (int i = 0; i < count; ++i)
            {
                Assert.AreEqual(m_Manager.GetName(array[i]), "Name" + i);
            }

            // even though we've made 1024 entities, the string table should contain only two entries:
            // "", and "Name"
            Assert.IsTrue(WordStorage.Instance.Entries == 2);
            array.Dispose();
        }

        [Test]
        public void InstantiateKeepsName()
        {
            WordStorage.Setup();
            var entity = m_Manager.CreateEntity();
            m_Manager.SetName(entity, "Blah");

            var instance = m_Manager.Instantiate(entity);
            Assert.AreEqual("Blah", m_Manager.GetName(instance));
        }

#endif

        [Test]
        public void IncreaseEntityCapacity()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var count = 1024;
            var array = new NativeArray<Entity>(count, Allocator.Temp);
            m_Manager.CreateEntity(archetype, array);
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(i, array[i].Index);
            }

            array.Dispose();
        }

        [Test]
        public void AddComponentEmptyNativeArray()
        {
            var array = new NativeArray<Entity>(0, Allocator.Temp);
            m_Manager.AddComponent(array, typeof(EcsTestData));
            array.Dispose();
        }

        unsafe public bool IndexInChunkIsValid(Entity entity)
        {
            var entityInChunk = m_Manager.GetCheckedEntityDataAccess()->EntityComponentStore->GetEntityInChunk(entity);
            return entityInChunk.IndexInChunk < entityInChunk.Chunk->Count;
        }

        [Test]
        unsafe public void AddComponentNativeArrayCorrectChunkIndexAfterPacking()
        {
            // This test checks for the bug revealed here https://github.com/Unity-Technologies/dots/issues/2133
            // When packing was done, it was possible for the packed entities to have an incorrect
            // EntityInChunk.IndexInChunk.  A catastrophic case was when IndexInChunk was larger than Chunk.Count.
            var types = new[] { ComponentType.ReadWrite<EcsTestData>()};
            var archetype = m_Manager.CreateArchetype(types);
            var entities = new NativeArray<Entity>(2, Allocator.TempJob);

            // Create four entities so that we create two holes in the chunk when we add a new
            // component to two of them.
            entities[0] = m_Manager.CreateEntity(archetype);
            var checkEntity1 = m_Manager.CreateEntity(archetype);
            entities[1] = m_Manager.CreateEntity(archetype);
            var checkEntity2 = m_Manager.CreateEntity(archetype);

            m_Manager.AddComponent(entities, typeof(EcsTestData2));

            Assert.IsTrue(IndexInChunkIsValid(checkEntity1));
            Assert.IsTrue(IndexInChunkIsValid(checkEntity2));
            entities.Dispose();
        }

#if !NET_DOTS
        // https://unity3d.atlassian.net/browse/DOTSR-1432
        // In IL2CPP this segfaults at runtime. There is a crash in GetAssignableComponentTypes()

        [Test]
        public void FoundComponentInterface()
        {
            var fooTypes = m_Manager.GetAssignableComponentTypes(typeof(IEcsFooInterface));
            Assert.AreEqual(1, fooTypes.Count);
            Assert.AreEqual(typeof(EcsFooTest), fooTypes[0]);

            var barTypes = m_Manager.GetAssignableComponentTypes(typeof(IEcsNotUsedInterface));
            Assert.AreEqual(0, barTypes.Count);
        }

        [Test]
        public void FoundComponentInterfaceNonAllocating()
        {
            var list = new List<Type>();
            var fooTypes = m_Manager.GetAssignableComponentTypes(typeof(IEcsFooInterface), list);
            Assert.AreEqual(1, fooTypes.Count);
            Assert.AreEqual(typeof(EcsFooTest), fooTypes[0]);

            list.Clear();
            var barTypes = m_Manager.GetAssignableComponentTypes(typeof(IEcsNotUsedInterface), list);
            Assert.AreEqual(0, barTypes.Count);
        }

#endif

        [Test]
        public void VersionIsConsistent()
        {
            Assert.AreEqual(0, m_Manager.Version);

            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            Assert.AreEqual(1, m_Manager.Version);

            m_Manager.AddComponentData(entity, new EcsTestData2(0));
            Assert.AreEqual(3, m_Manager.Version);

            m_Manager.SetComponentData(entity, new EcsTestData2(5));
            Assert.AreEqual(3, m_Manager.Version); // Shouldn't change when just setting data

            m_Manager.RemoveComponent<EcsTestData2>(entity);
            Assert.AreEqual(5, m_Manager.Version);

            m_Manager.DestroyEntity(entity);
            Assert.AreEqual(6, m_Manager.Version);
        }

        [Test]
        public void GetChunkVersions_ReflectsChange()
        {
            m_Manager.Debug.SetGlobalSystemVersion(1);

            var entity = m_Manager.CreateEntity(typeof(EcsTestData));

            m_Manager.Debug.SetGlobalSystemVersion(2);

            var version = m_Manager.GetChunkVersionHash(entity);

            m_Manager.SetComponentData(entity, new EcsTestData());

            var version2 = m_Manager.GetChunkVersionHash(entity);

            Assert.AreNotEqual(version, version2);
        }

        interface TestInterface
        {
        }

        struct TestInterfaceComponent : TestInterface, IComponentData
        {
            public int Value;
        }

        [Test]
        [DotsRuntimeFixme]
        public void GetComponentBoxedSupportsInterface()
        {
            var entity = m_Manager.CreateEntity();

            m_Manager.AddComponentData(entity, new TestInterfaceComponent { Value = 5});
            var obj = m_Manager.Debug.GetComponentBoxed(entity, typeof(TestInterface));

            Assert.AreEqual(typeof(TestInterfaceComponent), obj.GetType());
            Assert.AreEqual(5, ((TestInterfaceComponent)obj).Value);
        }

        [Test]
        [DotsRuntimeFixme]
        public void GetComponentBoxedThrowsWhenInterfaceNotFound()
        {
            var entity = m_Manager.CreateEntity();
            Assert.Throws<ArgumentException>(() => m_Manager.Debug.GetComponentBoxed(entity, typeof(TestInterface)));
        }

        [Test]
        public void EntityArchetypeFromDifferentEntityManagerThrows()
        {
            using (var world = new World("Temp"))
            {
                var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
                var nullArchetype = default(EntityArchetype);
                Assert.Throws<ArgumentException>(() => world.EntityManager.CreateEntity(archetype));
                Assert.Throws<ArgumentException>(() => world.EntityManager.CreateEntity(nullArchetype));
            }
        }

        // test for DOTS-3479: changing a chunk's archetype in-place temporarily leaves the new archetype's chunks-with-empty-slots
        // list in an inconsistent state.
        [Test]
        public void ChangeArchetypeInPlace_WithSharedComponents_ChunkIsInFreeSlotList()
        {
            var srcArchetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp), typeof(EcsTestTag));
            var dstArchetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));

            // Create two chunks in srcArchetype with different shared component values
            var ent0 = m_Manager.CreateEntity(srcArchetype);
            m_Manager.SetSharedComponentData(ent0, new EcsTestSharedComp {value = 17});
            var ent1 = m_Manager.CreateEntity(srcArchetype);
            m_Manager.SetSharedComponentData(ent1, new EcsTestSharedComp {value = 42});

            // move ent1's chunk to dstArchetype
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestTag), typeof(EcsTestSharedComp)))
            {
                query.SetSharedComponentFilter(new EcsTestSharedComp {value = 42});
                m_Manager.RemoveComponent<EcsTestTag>(query);
            }
            var archetypeChunk = m_Manager.GetChunk(ent1);
            Assert.AreEqual(dstArchetype, archetypeChunk.Archetype);

            // entity's chunk should have plenty of empty slots available
            Assert.AreEqual(1, archetypeChunk.Count);
            Assert.IsFalse(archetypeChunk.Full);

            var ent2 = m_Manager.CreateEntity(dstArchetype);
            // Changing ent2 to the same shared component values as ent1 should move ent2 to the same chunk as ent1.
            // If these asserts fail, it means ent1's chunk wasn't correctly added to dstArchetype's
            // FreeChunksBySharedComponents list (specifically, it was added under a hash of the wrong shared component
            // values), and a new chunk was created for ent2 instead.
            m_Manager.SetSharedComponentData(ent2, new EcsTestSharedComp {value = 42});
            Assert.AreEqual(2, archetypeChunk.Count); // ent1's chunk should contain both entities
            Assert.AreEqual(archetypeChunk, m_Manager.GetChunk(ent2)); // ent1 and ent2 should have the same chunk
        }

        [Test]
        public unsafe void ComponentsWithBool()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestComponentWithBool));
            var count = 128;
            var array = new NativeArray<Entity>(count, Allocator.Temp);
            m_Manager.CreateEntity(archetype, array);

            var hash = new NativeHashMap<Entity, bool>(count, Allocator.Temp);

            var cg = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestComponentWithBool>());
            using (var chunks = cg.CreateArchetypeChunkArray(Allocator.TempJob))
            {
                var boolsType = m_Manager.GetComponentTypeHandle<EcsTestComponentWithBool>(false);
                var entsType = m_Manager.GetEntityTypeHandle();

                foreach (var chunk in chunks)
                {
                    var bools = chunk.GetNativeArray(boolsType);
                    var entities = chunk.GetNativeArray(entsType);

                    for (var i = 0; i < chunk.Count; ++i)
                    {
                        bools[i] = new EcsTestComponentWithBool {value = (entities[i].Index & 1) == 1};
                        Assert.IsTrue(hash.TryAdd(entities[i], bools[i].value));
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                var data = m_Manager.GetComponentData<EcsTestComponentWithBool>(array[i]);
                Assert.AreEqual((array[i].Index & 1) == 1, data.value);
            }

            array.Dispose();
            hash.Dispose();
        }

        [TypeManager.ForcedMemoryOrderingAttribute(1)]
        struct BigComponentWithAlign1 : IComponentData
        {
            unsafe fixed byte val[1027];
        }

        [TypeManager.ForcedMemoryOrderingAttribute(2)]
        struct ComponentWithAlign8 : IComponentData
        {
            double val;
        }

        [MaximumChunkCapacity(1)]
        struct MaxCapacityTag1 : IComponentData {}

        [MaximumChunkCapacity(2)]
        struct MaxCapacityTag2 : IComponentData {}

        [MaximumChunkCapacity(3)]
        struct MaxCapacityTag3 : IComponentData {}

#if !UNITY_PORTABLE_TEST_RUNNER
        // https://unity3d.atlassian.net/browse/DOTSR-1432
        // TODO: IL2CPP_TEST_RUNNER doesn't support TestCase(typeof)

        [TestCase(typeof(MaxCapacityTag1))]
        [TestCase(typeof(MaxCapacityTag2))]
        [TestCase(typeof(MaxCapacityTag3))]
        [TestCase(typeof(EcsTestTag))]
        public unsafe void ChunkComponentRunIsAligned(Type maxCapacityTagType)
        {
            // Create an entity
            var archetype = m_Manager.CreateArchetype(typeof(BigComponentWithAlign1), typeof(ComponentWithAlign8), maxCapacityTagType);
            var entity = m_Manager.CreateEntity(archetype);
            // Get a pointer to the first bigger-aligned component
            var p0 = m_Manager.GetComponentDataRawRW(entity, TypeManager.GetTypeIndex<Entity>());
            var p1 = m_Manager.GetComponentDataRawRW(entity, TypeManager.GetTypeIndex<BigComponentWithAlign1>());
            var p2 = m_Manager.GetComponentDataRawRW(entity, TypeManager.GetTypeIndex<ComponentWithAlign8>());
            Assert.IsTrue(p0 < p1 && p1 < p2, "Order of components in memory is not as expected");

            // all component arrays need to be cache line aligned
            Assert.True(CollectionHelper.IsAligned(p0, CollectionHelper.CacheLineSize));
            Assert.True(CollectionHelper.IsAligned(p1, CollectionHelper.CacheLineSize));
            Assert.True(CollectionHelper.IsAligned(p2, CollectionHelper.CacheLineSize));
        }

#endif

        struct WillFitWithAlign : IComponentData
        {
            // sizeof(T) is not a constant
            public const int kSizeOfEntity = 8;
            public const int kSizeOfBigComponentWithAlign1 = 1027;

            public const int kAlignmentOverhead = 2 * CollectionHelper.CacheLineSize - kSizeOfEntity -
                (kSizeOfBigComponentWithAlign1 & (CollectionHelper.CacheLineSize - 1));
            public const int kWillFitSize =
                Chunk.kBufferSize - kSizeOfEntity - kSizeOfBigComponentWithAlign1 - kAlignmentOverhead;
            unsafe fixed byte val[kWillFitSize];
        }

        struct WontFitWithAlign : IComponentData
        {
            // Make component one byte larger than would fit in chunk
            unsafe fixed byte val[WillFitWithAlign.kWillFitSize + 1];
        }


        [Test]
        public unsafe void CreatingArchetypeWithToLargeEntityThrows()
        {
            Assert.DoesNotThrow(() => m_Manager.CreateArchetype(typeof(BigComponentWithAlign1), typeof(WillFitWithAlign)));
            Assert.Throws<ArgumentException>(() => m_Manager.CreateArchetype(typeof(BigComponentWithAlign1), typeof(WontFitWithAlign)));
        }

        void CreateEntitiesWithDataToRemove(NativeArray<Entity> entities)
        {
            var count = entities.Length;
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3), typeof(EcsTestDataEntity), typeof(EcsIntElement));

            m_Manager.CreateEntity(archetype, entities);

            for (int i = 0; i < count; i++)
            {
                m_Manager.SetComponentData(entities[i], new EcsTestData { value = i});
                m_Manager.SetComponentData(entities[i], new EcsTestData2 { value0 = 20000 + i, value1 = 40000 + i});
                m_Manager.SetComponentData(entities[i],
                    new EcsTestData3 { value0 = 50000 + i, value1 = 60000 + i, value2 = 70000 + i});
                m_Manager.SetComponentData(entities[i], new EcsTestDataEntity { value0 = i, value1 = entities[i] });
                var buffer = m_Manager.GetBuffer<EcsIntElement>(entities[i]);
                buffer.Add(new EcsIntElement {Value = i});
            }
        }

        void ValidateRemoveComponents(NativeArray<Entity> entities, NativeArray<Entity> entitiesToRemoveData)
        {
            var count = entities.Length;
            var group0 = m_Manager.CreateEntityQuery(typeof(EcsTestDataEntity));
            var entities0 = group0.ToEntityArray(Allocator.Persistent);

            Assert.AreEqual(entities0.Length, count);

            for (int i = 0; i < count; i++)
            {
                var entity = entities[i];
                var testDataEntity = m_Manager.GetComponentData<EcsTestDataEntity>(entity);
                var testBuffer = m_Manager.GetBuffer<EcsIntElement>(entity);
                Assert.AreEqual(testDataEntity.value1, entity);
                Assert.AreEqual(testDataEntity.value0, testBuffer[0].Value);
            }

            entities0.Dispose();
            group0.Dispose();

            for (int i = 0; i < entitiesToRemoveData.Length; i++)
            {
                Assert.IsFalse(m_Manager.HasComponent<EcsTestData>(entitiesToRemoveData[i]));
                Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(entitiesToRemoveData[i]));
            }
        }

        [Test]
        public void BatchRemoveComponents()
        {
            var count = 1024;
            var entities = new NativeArray<Entity>(count, Allocator.Persistent);

            CreateEntitiesWithDataToRemove(entities);

            var entitiesToRemoveData = new NativeArray<Entity>(count / 2, Allocator.Persistent);
            for (int i = 0; i < count / 2; i++)
                entitiesToRemoveData[i] = entities[i * 2];

            m_Manager.RemoveComponent(entitiesToRemoveData, typeof(EcsTestData));

            var group0 = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            var group1 = m_Manager.CreateEntityQuery(typeof(EcsTestData2), typeof(EcsTestData3));

            var entities0 = group0.ToEntityArray(Allocator.Persistent);
            var entities1 = group1.ToEntityArray(Allocator.Persistent);

            Assert.AreEqual(entities0.Length, count / 2);
            Assert.AreEqual(entities1.Length, count);

            entities0.Dispose();
            entities1.Dispose();

            m_Manager.RemoveComponent(entitiesToRemoveData, typeof(EcsTestData2));

            var entities1b = group1.ToEntityArray(Allocator.Persistent);

            Assert.AreEqual(entities1b.Length, count / 2);

            entities1b.Dispose();

            entities.Dispose();
            entitiesToRemoveData.Dispose();

            group0.Dispose();
            group1.Dispose();
        }

        [Test]
        public void BatchRemoveComponentsValuesSimplified()
        {
            var count = 4;
            var entities = new NativeArray<Entity>(count, Allocator.Persistent);

            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestDataEntity), typeof(EcsIntElement));

            m_Manager.CreateEntity(archetype, entities);

            for (int i = 0; i < count; i++)
            {
                m_Manager.SetComponentData(entities[i], new EcsTestData { value = i});
                m_Manager.SetComponentData(entities[i], new EcsTestDataEntity { value0 = i, value1 = entities[i] });
                var buffer = m_Manager.GetBuffer<EcsIntElement>(entities[i]);
                buffer.Add(new EcsIntElement {Value = i});
            }

            var entitiesToRemoveData = new NativeArray<Entity>(1, Allocator.Persistent);
            entitiesToRemoveData[0] = entities[0];

            m_Manager.RemoveComponent(entitiesToRemoveData, typeof(EcsTestData));

            ValidateRemoveComponents(entities, entitiesToRemoveData);

            entities.Dispose();
            entitiesToRemoveData.Dispose();
        }

        [Test]
        public void BatchRemoveComponentsValues()
        {
            var count = 1024;
            var entities = new NativeArray<Entity>(count, Allocator.Persistent);

            CreateEntitiesWithDataToRemove(entities);

            var entitiesToRemoveData = new NativeArray<Entity>(count / 2, Allocator.Persistent);
            for (int i = 0; i < count / 2; i++)
            {
                entitiesToRemoveData[i] = entities[i * 2];
            }

            m_Manager.RemoveComponent(entitiesToRemoveData, typeof(EcsTestData));
            m_Manager.RemoveComponent(entitiesToRemoveData, typeof(EcsTestData2));

            ValidateRemoveComponents(entities, entitiesToRemoveData);

            entities.Dispose();
            entitiesToRemoveData.Dispose();
        }

        [Test]
        public void BatchRemoveComponentsValuesWholeChunks()
        {
            var count = 1024;
            var entities = new NativeArray<Entity>(count, Allocator.Persistent);

            CreateEntitiesWithDataToRemove(entities);

            var entitiesToRemoveData = new NativeArray<Entity>(count / 2, Allocator.Persistent);
            for (int i = 0; i < count / 2; i++)
                entitiesToRemoveData[i] = entities[i];

            m_Manager.RemoveComponent(entitiesToRemoveData, typeof(EcsTestData));
            m_Manager.RemoveComponent(entitiesToRemoveData, typeof(EcsTestData2));

            ValidateRemoveComponents(entities, entitiesToRemoveData);

            entities.Dispose();
            entitiesToRemoveData.Dispose();
        }

        [Test]
        public void BatchRemoveComponentsValuesDuplicates()
        {
            var count = 1024;
            var entities = new NativeArray<Entity>(count, Allocator.Persistent);

            CreateEntitiesWithDataToRemove(entities);

            var entitiesToRemoveData = new NativeArray<Entity>(count / 2, Allocator.Persistent);
            for (int i = 0; i < count / 2; i++)
                entitiesToRemoveData[i] = entities[i % 16];

            m_Manager.RemoveComponent(entitiesToRemoveData, typeof(EcsTestData));
            m_Manager.RemoveComponent(entitiesToRemoveData, typeof(EcsTestData2));

            ValidateRemoveComponents(entities, entitiesToRemoveData);

            entities.Dispose();
            entitiesToRemoveData.Dispose();
        }

        [Test]
        public void BatchRemoveComponentsValuesBatches()
        {
            var count = 1024;
            var entities = new NativeArray<Entity>(count, Allocator.Persistent);

            CreateEntitiesWithDataToRemove(entities);

            var entitiesToRemoveData = new NativeArray<Entity>(count / 2, Allocator.Persistent);
            for (int i = 0; i < count / 2; i++)
            {
                var skipOffset = (i >> 5) * 40;
                var offset = i & 0x1f;
                entitiesToRemoveData[i] = entities[skipOffset + offset];
            }

            m_Manager.RemoveComponent(entitiesToRemoveData, typeof(EcsTestData));
            m_Manager.RemoveComponent(entitiesToRemoveData, typeof(EcsTestData2));

            ValidateRemoveComponents(entities, entitiesToRemoveData);

            for (int i = 0; i < entitiesToRemoveData.Length; i++)
            {
                Assert.IsFalse(m_Manager.HasComponent<EcsTestData>(entitiesToRemoveData[i]));
                Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(entitiesToRemoveData[i]));
            }

            entities.Dispose();
            entitiesToRemoveData.Dispose();
        }

        [Test]
        public void BatchAddComponents([Values(1,5,10,100,1000)] int count)
        {
            var entities = new NativeArray<Entity>(count, Allocator.Persistent);
            var entitiesToAddData = new NativeArray<Entity>((count+1) / 2, Allocator.Persistent);

            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            m_Manager.CreateEntity(archetype, entities);

            for (int i = 0; i < entitiesToAddData.Length; i++)
                entitiesToAddData[i] = entities[i*2];

            m_Manager.AddComponent(entitiesToAddData, typeof(EcsTestData2));

            for (int i = 0; i < count; ++i)
            {
                // even-numbered entities should have the component, odds shouldn't
                Assert.AreEqual((i % 2) == 0, m_Manager.HasComponent<EcsTestData2>(entities[i]));
            }

            entitiesToAddData.Dispose();
            entities.Dispose();
        }

        [Test]
        public void AddComponentQueryWithArray()
        {
            m_Manager.CreateEntity(typeof(EcsTestData2));
            m_Manager.CreateEntity();
            m_Manager.CreateEntity();
            m_Manager.CreateEntity();

            var entities = m_Manager.UniversalQuery.ToEntityArray(Allocator.TempJob);
            var data = new NativeArray<EcsTestData>(entities.Length, Allocator.Temp);
            for (int i = 0; i != data.Length; i++)
                data[i] = new EcsTestData(entities[i].Index);

            m_Manager.AddComponentData(m_Manager.UniversalQuery, data);

            for (int i = 0; i != data.Length; i++)
                Assert.AreEqual(entities[i].Index, data[i].value);
            Assert.AreEqual(4, entities.Length);

            data.Dispose();
            entities.Dispose();
        }

        [Test]
        public void AddSharedComponentData_WithEntityQuery_ThatHasNoMatch_WillNotCorruptInternalState()
        {
            var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            m_Manager.AddSharedComponentData(entityQuery, new EcsTestSharedComp(1));
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void SetSharedComponentData_WithEntityQuery_ThatHasNoMatch_WillNotCorruptInternalState()
        {
            var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            m_Manager.SetSharedComponentData(entityQuery, new EcsTestSharedComp(1));
            m_Manager.Debug.CheckInternalConsistency();
        }

        [StructLayout(LayoutKind.Explicit, Size = 3)]
        public struct ComponentDataByteThen2PaddingBytes : IComponentData
        {
            [FieldOffset(0)]
            public byte mByte;
        }

        [StructLayout(LayoutKind.Explicit, Size = 3)]
        public struct ComponentPaddingByteThenDataByteThenPaddingByte: IComponentData
        {
            [FieldOffset(1)]
            public byte mByte;
        }

        /// <summary>
        /// This test is to ensure we can rely on padding bytes being zero-initialized such that memory comparisons
        /// of chunks written to using SetComponent or by storing structs via chunk pointers is fine. A user will
        /// only encounter problems where default(c) != default(c) if they explicitly alter the padding bytes of the struct
        /// they copy into chunk memory, to which we say, they must intend for this struct to be different than one that
        /// didn't have it's padding bytes explicitly altered.
        ///
        /// Relevant part of the standard that ensures types are by default zero initialized:
        /// ECMA-335 I.12.6.8
        /// All memory allocated for static variables (other than those assigned RVAs within a PE
        /// file, see Partition II) and objects shall be zeroed before they are made visible to any user code.
        /// </summary>
        [Test]
        public unsafe void ValidatePaddingBytesAreOverwrittenWhenReusingChunks()
        {
            const byte kPaddingValue = 0xFF;
            const byte kMemoryInitPattern = 0xAA;
            Assertions.Assert.AreNotEqual(kMemoryInitPattern, kPaddingValue);
            Assertions.Assert.AreNotEqual(0, kPaddingValue);

            m_ManagerDebug.UseMemoryInitPattern = true;
            m_ManagerDebug.MemoryInitPattern = kMemoryInitPattern;

            NativeHashSet<ulong> seenChunkBuffers = new NativeHashSet<ulong>(8, Allocator.TempJob);
            var dppArchetype = m_Manager.CreateArchetype(typeof(ComponentDataByteThen2PaddingBytes));
            var pdpArchetype = m_Manager.CreateArchetype(typeof(ComponentPaddingByteThenDataByteThenPaddingByte));
            Assert.AreEqual(dppArchetype.ChunkCapacity, pdpArchetype.ChunkCapacity);

            // Allocate entities to fill multiple chunks with components with known "bad" padding bytes
            var entityCount = dppArchetype.ChunkCapacity * 2;
            var entities = new NativeArray<Entity>(entityCount, Allocator.TempJob);
            for (int i = 0; i < entityCount; ++i)
            {
                var entity = m_Manager.CreateEntity(dppArchetype);
                entities[i] = entity;

                var component = new ComponentDataByteThen2PaddingBytes();
                var pData = (byte*) UnsafeUtility.AddressOf(ref component);
                pData[0] = (byte) entity.Index; // data byte
                pData[1] = kPaddingValue;       // padding byte
                pData[2] = kPaddingValue;       // padding byte

                m_Manager.SetComponentData(entity, component);
            }

            Assert.AreEqual(2, dppArchetype.ChunkCount);
            for (int i = 0; i < dppArchetype.Archetype->Chunks.Count; ++i)
                seenChunkBuffers.Add((ulong)dppArchetype.Archetype->Chunks[i]->Buffer);

            // Validate the components have the byte pattern we expect
            foreach (var entity in entities)
            {
                var component = m_Manager.GetComponentData<ComponentDataByteThen2PaddingBytes>(entity);
                var pData = (byte*) UnsafeUtility.AddressOf(ref component);
                Assert.AreEqual((byte) entity.Index, pData[0]);
                Assert.AreEqual(kPaddingValue, pData[1] );
                Assert.AreEqual(kPaddingValue, pData[2]);
            }

            // Delete all entities in the second chunk, pushing the chunk back on the freelist
            for (int i = dppArchetype.ChunkCapacity; i < entityCount; ++i)
                m_Manager.DestroyEntity(entities[i]);
            Assert.AreEqual(1, dppArchetype.ChunkCount);

            // Allocate a new archetype with the opposite stripping pattern
            // and ensure the chunk retrieved is a chunk we've seen already
            var newEntityCount = pdpArchetype.ChunkCapacity;
            var newEntities = new NativeArray<Entity>(newEntityCount, Allocator.TempJob);
            for (int i = 0; i < newEntityCount; ++i)
            {
                var entity = m_Manager.CreateEntity(pdpArchetype);
                newEntities[i] = entity;

                var component = new ComponentPaddingByteThenDataByteThenPaddingByte();
                var pData = (byte*) UnsafeUtility.AddressOf(ref component);
                Assertions.Assert.AreEqual(0, pData[0]); // pData[0] -- padding byte left uninitialized
                pData[1] = (byte) entity.Index; // data byte
                Assertions.Assert.AreEqual(0, pData[0]); // pData[2] -- padding byte left uninitialized

                m_Manager.SetComponentData(entity, component);
            }
            Assert.AreEqual(1, pdpArchetype.ChunkCount);
            Assert.IsTrue(seenChunkBuffers.Contains((ulong)pdpArchetype.Archetype->Chunks[0]->Buffer));

            // Validate the components have zero initialized padding bytes and not the poisoned padding
            // i.e. what you store to chunk memory is what you get. You are not affected by the
            // non-zero-initialized chunk memory state
            foreach (var entity in newEntities)
            {
                var component = m_Manager.GetComponentData<ComponentPaddingByteThenDataByteThenPaddingByte>(entity);
                var pData = (byte*) UnsafeUtility.AddressOf(ref component);
                Assert.AreEqual((byte)0, pData[0] );
                Assert.AreEqual((byte) entity.Index, pData[1]);
                Assert.AreEqual((byte)0, pData[2]);
            }

            newEntities.Dispose();
            entities.Dispose();
            seenChunkBuffers.Dispose();
        }

        [Test]
        [Ignore("Fix had to be reverted, check issue #2996")]
        public void Fix1602()
        {
            var startArchetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var startGroup = m_Manager.CreateEntityQuery(typeof(EcsTestData), ComponentType.Exclude<EcsTestTag>());
            var endGroup = m_Manager.CreateEntityQuery(typeof(EcsTestTag));
            for (int i = 0; i < 100; i++)
            {
                var entity = m_Manager.CreateEntity(startArchetype);
                m_Manager.AddComponent<EcsTestTag>(startGroup);
            }
            Assert.AreEqual(startGroup.CalculateChunkCount(), 0);
            Assert.AreEqual(endGroup.CalculateChunkCount(), 1);
            startGroup.Dispose();
            endGroup.Dispose();
        }

        // These tests require:
        // - JobsDebugger support for static safety IDs (added in 2020.1)
        // - Asserting throws
#if !UNITY_DOTSRUNTIME
        [Test,DotsRuntimeFixme]
        public void EntityManager_DoubleDispose_UsesCustomOwnerTypeName()
        {
            World tempWorld = new World("TestWorld");
            var entityManager = tempWorld.EntityManager;
            tempWorld.Dispose();
            Assert.That(() => entityManager.DestroyInstance(),
#if UNITY_2020_2_OR_NEWER
                Throws.Exception.TypeOf<ObjectDisposedException>()
#else
                Throws.InvalidOperationException
#endif
                    .With.Message.Contains("EntityManager"));
        }

#endif

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        class TestInterfaceManagedComponent : TestInterface, IComponentData
        {
            public int Value;
        }

        [Test]
        public void VersionIsConsistent_ManagedComponents()
        {
            Assert.AreEqual(0, m_Manager.Version);

            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            Assert.AreEqual(1, m_Manager.Version);

            m_Manager.AddComponentData(entity, new EcsTestData2(0));
            Assert.AreEqual(3, m_Manager.Version);

            m_Manager.SetComponentData(entity, new EcsTestData2(5));
            Assert.AreEqual(3, m_Manager.Version); // Shouldn't change when just setting data

            m_Manager.AddComponentData(entity, new EcsTestManagedComponent());
            Assert.AreEqual(5, m_Manager.Version);

            m_Manager.SetComponentData(entity, new EcsTestManagedComponent() { value = "new" });
            Assert.AreEqual(5, m_Manager.Version); // Shouldn't change when just setting data

            m_Manager.RemoveComponent<EcsTestData2>(entity);
            Assert.AreEqual(7, m_Manager.Version);

            m_Manager.DestroyEntity(entity);
            Assert.AreEqual(8, m_Manager.Version);
        }

        [Test]
        [DotsRuntimeFixme]  // Would need UnsafeUtility.PinSystemObjectAndGetAddress
        public void GetComponentBoxedSupportsInterface_ManagedComponent()
        {
            var entity = m_Manager.CreateEntity();

            m_Manager.AddComponentData(entity, new TestInterfaceManagedComponent { Value = 5 });
            var obj = m_Manager.Debug.GetComponentBoxed(entity, typeof(TestInterface));

            Assert.AreEqual(typeof(TestInterfaceManagedComponent), obj.GetType());
            Assert.AreEqual(5, ((TestInterfaceManagedComponent)obj).Value);
        }

        [Test]
        public unsafe void ManagedComponents()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestManagedComponent));
            var count = 128;
            var array = new NativeArray<Entity>(count, Allocator.Temp);
            m_Manager.CreateEntity(archetype, array);

            var hash = new NativeHashMap<Entity, FixedString64>(count, Allocator.Temp);

            var cg = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestManagedComponent>());
            using (var chunks = cg.CreateArchetypeChunkArray(Allocator.TempJob))
            {
                var ManagedComponentType = m_Manager.GetComponentTypeHandle<EcsTestManagedComponent>(false);
                var entsType = m_Manager.GetEntityTypeHandle();

                foreach (var chunk in chunks)
                {
                    var components = chunk.GetManagedComponentAccessor(ManagedComponentType, m_Manager);
                    var entities = chunk.GetNativeArray(entsType);

                    Assert.AreEqual(chunk.Count, components.Length);

                    for (var i = 0; i < chunk.Count; ++i)
                    {
                        components[i] = new EcsTestManagedComponent { value = entities[i].Index.ToString() };
                        Assert.IsTrue(hash.TryAdd(entities[i], new FixedString64(components[i].value)));
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                var data = m_Manager.GetComponentData<EcsTestManagedComponent>(array[i]);
                Assert.AreEqual(array[i].Index.ToString(), data.value);
            }

            array.Dispose();
            hash.Dispose();
        }

        [Test]
        public void GetManagedComponentAccessor_ReturnsEmptyArray_IfTypeIsMissing()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData4), typeof(EcsTestData5));
            var count = 128;
            using(var array = new NativeArray<Entity>(count, Allocator.Temp))
            {
                m_Manager.CreateEntity(archetype, array);

                var cg = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestData4>());
                using (var chunks = cg.CreateArchetypeChunkArray(Allocator.TempJob))
                {
                    var managedComponentType = m_Manager.GetComponentTypeHandle<EcsTestManagedComponent>(false);

                    foreach (var chunk in chunks)
                    {
                        var components = chunk.GetManagedComponentAccessor(managedComponentType, m_Manager);
                        Assert.AreEqual(0, components.Length);
                    }
                }
            }
        }

        public class ComplexManagedComponent : IComponentData
        {
            public class MyClass
            {
                public string data;
            }

            public ComplexManagedComponent()
            {
                String = "default";
                Class = new MyClass() { data = "default" };
                List = new List<MyClass>();
            }

            public string String;
            public List<MyClass> List;
            public MyClass Class;
        }

        // https://unity3d.atlassian.net/browse/DOTSR-1432
        // TODO the il2cpp test runner doesn't Assert.AreSame/AreNotSame
        [Test]
        [DotsRuntimeFixme] // Unity.Properties support
        public void Instantiate_DeepClone_ManagedComponents()
        {
            var entity = m_Manager.CreateEntity();

            var originalString = "SomeString";
            var originalClassData = "SomeData";
            var originalClass = new ComplexManagedComponent.MyClass() { data = originalClassData };
            var originalListClassData = "SomeListData";
            var originalListClass = new ComplexManagedComponent.MyClass() { data = originalListClassData };
            var originalList = new List<ComplexManagedComponent.MyClass>();
            originalList.Add(originalListClass);
            var originalManagedComponent = new ComplexManagedComponent()
            {
                String = originalString,
                List = originalList,
                Class = originalClass
            };
            m_Manager.AddComponentData(entity, originalManagedComponent);

            void ValidateInstance(ComplexManagedComponent inst)
            {
                Assert.AreEqual("SomeString", inst.String);
                Assert.AreEqual("SomeData", inst.Class.data);
                Assert.AreEqual(1, inst.List.Count);
                Assert.AreEqual("SomeListData", inst.List[0].data);
            }

            var instance = m_Manager.Instantiate(entity);
            var instanceComponent = m_Manager.GetComponentData<ComplexManagedComponent>(instance);
            ValidateInstance(instanceComponent);

            // We change our managed component but our new instance should be unaffected
            originalString = "SomeOtherString";
            originalClassData = "SomeOtherData";
            originalListClassData = "SomeOtherListData";
            originalList.Clear();

            Assert.AreEqual("SomeString", instanceComponent.String);
            Assert.AreEqual("SomeData", instanceComponent.Class.data);
            Assert.AreEqual(1, instanceComponent.List.Count);
            Assert.AreEqual("SomeListData", instanceComponent.List[0].data);
            ValidateInstance(instanceComponent);

            Assert.AreSame(m_Manager.GetComponentData<ComplexManagedComponent>(entity), originalManagedComponent);
            Assert.AreNotSame(instanceComponent, originalManagedComponent);
        }

        class ManagedComponentWithArray : IComponentData
        {
            public int[] IntArray;
        }

#if !UNITY_PORTABLE_TEST_RUNNER
        // https://unity3d.atlassian.net/browse/DOTSR-1432
        // TODO: IL2CPP_TEST_RUNNER doesn't broadly support the That / Constraint Model. Note this test case is also flagged DotsRuntimeFixme.

        [Test]
        [DotsRuntimeFixme] // Requires Unity.Properties support
        public void Instantiate_DeepClone_ManagedComponentWithZeroSizedArray()
        {
            var originalEntity = m_Manager.CreateEntity();
            var originalComponent = new ManagedComponentWithArray { IntArray = new int[0] };

            m_Manager.AddComponentData(originalEntity, originalComponent);

            var instance = m_Manager.Instantiate(originalEntity);
            var instanceComponent = m_Manager.GetComponentData<ManagedComponentWithArray>(instance);

            Assert.That(originalComponent.IntArray, Is.Not.SameAs(instanceComponent.IntArray));
            Assert.That(instanceComponent.IntArray.Length, Is.EqualTo(0));
        }

        [Test]
        public void Instantiate_DeepClone_ManagedComponentWithNullArray()
        {
            var originalEntity = m_Manager.CreateEntity();
            var originalComponent = new ManagedComponentWithArray { IntArray = null };

            m_Manager.AddComponentData(originalEntity, originalComponent);

            var instance = m_Manager.Instantiate(originalEntity);
            var instanceComponent = m_Manager.GetComponentData<ManagedComponentWithArray>(instance);

            Assert.That(instanceComponent.IntArray, Is.Null);
        }

#endif // UNITY_PORTABLE_TEST_RUNNER

        class ManagedComponentWithNestedClass : IComponentData
        {
#pragma warning disable 649
            public class NestedClass
            {
                public float x;
            }

            public NestedClass Nested;
        }
#pragma warning restore 649

#if !UNITY_PORTABLE_TEST_RUNNER
        // https://unity3d.atlassian.net/browse/DOTSR-1432
        // TODO: IL2CPP_TEST_RUNNER doesn't broadly support the That / Constraint Model. Note this is also flagged DotsRuntimeFixme.
        [Test]
        public void Instantiate_DeepClone_ManagedComponentWithNullReferenceType()
        {
            var originalEntity = m_Manager.CreateEntity();
            var originalComponent = new ManagedComponentWithNestedClass { Nested = null };

            m_Manager.AddComponentData(originalEntity, originalComponent);

            var instance = m_Manager.Instantiate(originalEntity);
            var instanceComponent = m_Manager.GetComponentData<ManagedComponentWithNestedClass>(instance);

            Assert.That(instanceComponent.Nested, Is.Null);
        }

        [Test]
        public void AddComponentKeepsObjectReference()
        {
            var entity = m_Manager.CreateEntity();
            var obj = new EcsTestManagedComponent();
            m_Manager.AddComponentData(entity, obj);
            m_Manager.AddComponentData(entity, new EcsTestData());
            m_Manager.AddComponentData(entity, new ComplexManagedComponent());
            Assert.AreSame(m_Manager.GetComponentData<EcsTestManagedComponent>(entity), obj);
        }

        [Test]
        public void ManagedAddComponentDefaultsToNull()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponent<EcsTestManagedComponent>(entity);
            Assert.AreEqual(null, m_Manager.GetComponentData<EcsTestManagedComponent>(entity));

            m_Manager.RemoveComponent<EcsTestManagedComponent>(entity);
            Assert.IsFalse(m_Manager.HasComponent<EcsTestManagedComponent>(entity));
        }

        [Test]
        public void AddComponentWithExplicitNull()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponentData<EcsTestManagedComponent>(entity, null);
            Assert.AreEqual(null, m_Manager.GetComponentData<EcsTestManagedComponent>(entity));
        }

        [Test]
        public void SetManagedComponentDataToNull()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponentData<EcsTestManagedComponent>(entity, new EcsTestManagedComponent());
            m_Manager.SetComponentData<EcsTestManagedComponent>(entity, null);
            Assert.AreEqual(null, m_Manager.GetComponentData<EcsTestManagedComponent>(entity));
        }

        [Test]
        public void AddMismatchedManagedComponentThrows()
        {
            var entity = m_Manager.CreateEntity();
            Assert.Throws<ArgumentException>(() =>
            {
                m_Manager.AddComponentData<EcsTestManagedComponent>(entity, new EcsTestManagedComponent2());
            });
        }

        [Test]
        public void CreateEntity_InputUsesAllocatorTemp_Works([Values(1, 10, 100, 1000)] int entityCount)
        {
            var archetype = m_Manager.CreateArchetype();
            var entities = new NativeArray<Entity>(entityCount, Allocator.Temp);
            m_Manager.CreateEntity(archetype, entities);
            foreach (var ent in entities)
                Assert.IsTrue(m_Manager.Exists(ent));
            entities.Dispose();
        }

        [Test]
        public void Instantiate_InputUsesAllocatorTemp_Works([Values(1, 10, 100, 1000)] int entityCount)
        {
            var prefabEnt = m_Manager.CreateEntity(typeof(EcsTestData));
            var entities = new NativeArray<Entity>(entityCount, Allocator.Temp);
            m_Manager.Instantiate(prefabEnt, entities);
            foreach (var ent in entities)
            {
                Assert.IsTrue(m_Manager.HasComponent<EcsTestData>(ent));
            }
            entities.Dispose();
        }

        [Test]
        public void CopyEntities_InputUsesAllocatorTemp_Works([Values(1, 10, 100, 1000)] int entityCount)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var srcEntities = new NativeArray<Entity>(entityCount, Allocator.Temp);
            for (int i = 0; i < entityCount; ++i)
            {
                srcEntities[i] = m_Manager.CreateEntity(archetype);
                m_Manager.SetComponentData(srcEntities[i], new EcsTestData(i));
            }

            var dstEntities = new NativeArray<Entity>(entityCount, Allocator.Temp);

            m_Manager.CopyEntities(srcEntities, dstEntities);

            for (int i = 0; i < srcEntities.Length; ++i)
            {
                Assert.AreEqual(archetype, m_Manager.GetChunk(dstEntities[i]).Archetype);
                Assert.AreEqual(m_Manager.GetComponentData<EcsTestData>(srcEntities[i]),
                    m_Manager.GetComponentData<EcsTestData>(dstEntities[i]));
            }

            srcEntities.Dispose();
            dstEntities.Dispose();
        }

        [Test]
        public void CopyEntitiesFrom_InputUsesAllocatorTemp_Works([Values(1, 10, 100, 1000)] int entityCount)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var srcEntities = new NativeArray<Entity>(entityCount, Allocator.Temp);
            for (int i = 0; i < entityCount; ++i)
            {
                srcEntities[i] = m_Manager.CreateEntity(archetype);
                m_Manager.SetComponentData(srcEntities[i], new EcsTestData(i));
            }

            var dstWorld = new World("Copy Destination World");
            var dstEntities = new NativeArray<Entity>(entityCount, Allocator.Temp);

            dstWorld.EntityManager.CopyEntitiesFrom(m_Manager, srcEntities, dstEntities);

            for (int i = 0; i < srcEntities.Length; ++i)
            {
                // Can't compare archetypes across Worlds?
                // Assert.AreEqual(archetype, dstWorld.EntityManager.GetChunk(dstEntities[i]).Archetype);
                Assert.AreEqual(m_Manager.GetComponentData<EcsTestData>(srcEntities[i]),
                    dstWorld.EntityManager.GetComponentData<EcsTestData>(dstEntities[i]));
            }

            srcEntities.Dispose();
            dstEntities.Dispose();
            dstWorld.Dispose();
        }

        [Test]
        public void DestroyEntity_InputUsesAllocatorTemp_Works([Values(1, 10, 100, 1000)] int entityCount)
        {
            var archetype = m_Manager.CreateArchetype();
            var entities = new NativeArray<Entity>(entityCount, Allocator.Temp);
            for (int i = 0; i < entityCount; ++i)
                entities[i] = m_Manager.CreateEntity(archetype);
            m_Manager.DestroyEntity(entities);
            foreach (var ent in entities)
                Assert.IsFalse(m_Manager.Exists(ent));
            entities.Dispose();
        }

        [Test]
        public void AddComponent_InputUsesAllocatorTemp_Works([Values(1, 10, 100, 1000)] int entityCount)
        {
            var archetype = m_Manager.CreateArchetype();
            var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Temp);
            m_Manager.AddComponent<EcsTestData>(entities);
            foreach (var ent in entities)
                Assert.IsTrue(m_Manager.HasComponent<EcsTestData>(ent));
            entities.Dispose();
        }

        [Test]
        public void RemoveComponent_InputUsesAllocatorTemp_Works([Values(1, 10, 100, 1000)] int entityCount)
        {
            var archetype = m_Manager.CreateArchetype();
            var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Temp);
            foreach (var ent in entities)
                m_Manager.AddComponent<EcsTestData>(ent);
            m_Manager.RemoveComponent<EcsTestData>(entities);
            foreach (var ent in entities)
                Assert.IsFalse(m_Manager.HasComponent<EcsTestData>(ent));
            entities.Dispose();
        }

        [Test]
        public void GetEntityInfo_InvalidEntity_ReturnsEntityInvalid()
        {
            var invalidEntity = new Entity {Index = m_Manager.EntityCapacity + 1, Version = 1};
            var info = m_Manager.Debug.GetEntityInfo(invalidEntity);
            Assert.AreEqual(info, "Entity.Invalid","Entity with Large Index failed test");

            var invalidEntity2 = new Entity {Index = -1, Version = 1};
            info = m_Manager.Debug.GetEntityInfo(invalidEntity2);
            Assert.AreEqual(info, "Entity.Invalid", "Entity with Negative Index failed test");

        }

#endif // UNITY_PORTABLE_TEST_RUNNER
#endif
    }
}
