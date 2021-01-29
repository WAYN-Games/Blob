using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;

namespace Unity.Entities
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct BasicCommand
    {
        public int CommandType;
        public int TotalSize;
        public int SortKey;  /// Used to order command execution during playback
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CreateCommand
    {
        public BasicCommand Header;
        public EntityArchetype Archetype;
        public int IdentityIndex;
        public int BatchCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityCommand
    {
        public BasicCommand Header;
        public Entity Entity;
        public int IdentityIndex;
        public int BatchCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MultipleEntitiesCommand
    {
        public BasicCommand Header;
        public EntityNode Entities;
        public int EntitiesCount;
        public Allocator Allocator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MultipleEntitiesComponentCommand
    {
        public MultipleEntitiesCommand Header;
        public int ComponentTypeIndex;
        public int ComponentSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MultipleEntitiesAndComponentsCommand
    {
        public MultipleEntitiesCommand Header;
        public ComponentTypes Types;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityComponentCommand
    {
        public EntityCommand Header;
        public int ComponentTypeIndex;
        public int ComponentSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityMultipleComponentsCommand
    {
        public EntityCommand Header;
        public ComponentTypes Types;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityBufferCommand
    {
        public EntityCommand Header;
        public int ComponentTypeIndex;
        public int ComponentSize;
        public BufferHeaderNode BufferNode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityQueryCommand
    {
        public BasicCommand Header;
        public unsafe EntityQueryData* QueryData;
        public EntityQueryFilter EntityQueryFilter;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public unsafe EntityComponentStore* Store;
#endif
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityQueryComponentCommand
    {
        public EntityQueryCommand Header;
        public int ComponentTypeIndex;

        public int ComponentSize;
        // Data follows if command has an associated component payload
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityQueryMultipleComponentsCommand
    {
        public EntityQueryCommand Header;
        public ComponentTypes Types;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EntityQueryManagedComponentCommand
    {
        public EntityQueryCommand Header;
        public int ComponentTypeIndex;
        public EntityComponentGCNode GCNode;

        internal object GetBoxedObject()
        {
            if (GCNode.BoxedObject.IsAllocated)
                return GCNode.BoxedObject.Target;
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EntityQuerySharedComponentCommand
    {
        public EntityQueryCommand Header;
        public int ComponentTypeIndex;
        public int HashCode;
        public EntityComponentGCNode GCNode;

        internal object GetBoxedObject()
        {
            if (GCNode.BoxedObject.IsAllocated)
                return GCNode.BoxedObject.Target;
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EntityManagedComponentCommand
    {
        public EntityCommand Header;
        public int ComponentTypeIndex;
        public EntityComponentGCNode GCNode;

        internal object GetBoxedObject()
        {
            if (GCNode.BoxedObject.IsAllocated)
                return GCNode.BoxedObject.Target;
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EntitySharedComponentCommand
    {
        public EntityCommand Header;
        public int ComponentTypeIndex;
        public int HashCode;
        public EntityComponentGCNode GCNode;

        internal object GetBoxedObject()
        {
            if (GCNode.BoxedObject.IsAllocated)
                return GCNode.BoxedObject.Target;
            return null;
        }
    }

    internal unsafe struct EntityComponentGCNode
    {
        public GCHandle BoxedObject;
        public EntityComponentGCNode* Prev;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct BufferHeaderNode
    {
        public BufferHeaderNode* Prev;
        public BufferHeader TempBuffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EntityNode
    {
        public Entity* Ptr;
        public EntityNode* Prev;
    }


    [StructLayout(LayoutKind.Sequential, Size = 32)]
    internal unsafe struct ChainCleanup
    {
        public EntityNode* EntityArraysCleanupList;
        public BufferHeaderNode* BufferCleanupList;
        public EntityComponentGCNode* CleanupList;
    }

    [StructLayout(LayoutKind.Sequential, Size = (64 > JobsUtility.CacheLineSize) ? 64: JobsUtility.CacheLineSize)]
    internal unsafe struct EntityCommandBufferChain
    {
        public ECBChunk* m_Tail;
        public ECBChunk* m_Head;
        public ChainCleanup* m_Cleanup;
        public CreateCommand*                m_PrevCreateCommand;
        public EntityCommand*                m_PrevEntityCommand;
        public EntityCommandBufferChain* m_NextChain;
        public int m_LastSortKey;
        public bool m_CanBurstPlayback;

        internal static void InitChain(EntityCommandBufferChain* chain, Allocator allocator)
        {
            chain->m_Cleanup = (ChainCleanup*)Memory.Unmanaged.Allocate(sizeof(ChainCleanup), sizeof(ChainCleanup), allocator);
            chain->m_Cleanup->CleanupList = null;
            chain->m_Cleanup->BufferCleanupList = null;
            chain->m_Cleanup->EntityArraysCleanupList = null;

            chain->m_Tail = null;
            chain->m_Head = null;
            chain->m_PrevCreateCommand = null;
            chain->m_PrevEntityCommand = null;
            chain->m_LastSortKey = -1;
            chain->m_NextChain = null;
            chain->m_CanBurstPlayback = true;
        }
    }

    internal unsafe struct ECBSharedPlaybackState
    {
        public struct BufferWithFixUp
        {
            public EntityBufferCommand* cmd;
        }

        public Entity* CreateEntityBatch;
        public BufferWithFixUp* BuffersWithFixUp;
        public int LastBuffer;
    }

    internal unsafe struct ECBChainPlaybackState
    {
        public ECBChunk* Chunk;
        public int Offset;
        public int NextSortKey;
        public bool CanBurstPlayback;
    }

    internal unsafe struct ECBChainHeapElement
    {
        public int SortKey;
        public int ChainIndex;
    }
    internal unsafe struct ECBChainPriorityQueue : IDisposable
    {
        private readonly ECBChainHeapElement* m_Heap;
        private int m_Size;
        private readonly Allocator m_Allocator;
        private static readonly int BaseIndex = 1;
        public ECBChainPriorityQueue(NativeArray<ECBChainPlaybackState> chainStates, Allocator alloc)
        {
            m_Size = chainStates.Length;
            m_Allocator = alloc;
            m_Heap = (ECBChainHeapElement*)Memory.Unmanaged.Allocate((m_Size + BaseIndex) * sizeof(ECBChainHeapElement), 64, m_Allocator);
            for (int i = m_Size - 1; i >= m_Size / 2; --i)
            {
                m_Heap[BaseIndex + i].SortKey = chainStates[i].NextSortKey;
                m_Heap[BaseIndex + i].ChainIndex = i;
            }
            for (int i = m_Size / 2 - 1; i >= 0; --i)
            {
                m_Heap[BaseIndex + i].SortKey = chainStates[i].NextSortKey;
                m_Heap[BaseIndex + i].ChainIndex = i;
                Heapify(BaseIndex + i);
            }
        }

        public void Dispose()
        {
            Memory.Unmanaged.Free(m_Heap, m_Allocator);
        }

        public bool Empty { get { return m_Size <= 0; } }
        public ECBChainHeapElement Peek()
        {
            //Assert.IsTrue(!Empty, "Can't Peek() an empty heap");
            if (Empty)
            {
                return new ECBChainHeapElement { ChainIndex = -1, SortKey = -1};
            }
            return m_Heap[BaseIndex];
        }

        public ECBChainHeapElement Pop()
        {
            //Assert.IsTrue(!Empty, "Can't Pop() an empty heap");
            if (Empty)
            {
                return new ECBChainHeapElement { ChainIndex = -1, SortKey = -1};
            }
            ECBChainHeapElement top = Peek();
            m_Heap[BaseIndex] = m_Heap[m_Size--];
            if (!Empty)
            {
                Heapify(BaseIndex);
            }
            return top;
        }

        public void ReplaceTop(ECBChainHeapElement value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (Empty)
                Assert.IsTrue(false, "Can't ReplaceTop() an empty heap");
#endif
            m_Heap[BaseIndex] = value;
            Heapify(BaseIndex);
        }

        private void Heapify(int i)
        {
            // The index taken by this function is expected to be already biased by BaseIndex.
            // Thus, m_Heap[size] is a valid element (specifically, the final element in the heap)
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (i < BaseIndex || i > m_Size)
                Assert.IsTrue(false, "heap index " + i + " is out of range with size=" + m_Size);
#endif
            ECBChainHeapElement val = m_Heap[i];
            while (i <= m_Size / 2)
            {
                int child = 2 * i;
                if (child < m_Size && (m_Heap[child + 1].SortKey < m_Heap[child].SortKey))
                {
                    child++;
                }
                if (val.SortKey < m_Heap[child].SortKey)
                {
                    break;
                }
                m_Heap[i] = m_Heap[child];
                i = child;
            }
            m_Heap[i] = val;
        }
    }

    internal enum ECBCommand
    {
        InstantiateEntity,

        CreateEntity,
        DestroyEntity,

        AddComponent,
        AddMultipleComponents,
        AddComponentWithEntityFixUp,
        RemoveComponent,
        RemoveMultipleComponents,
        SetComponent,
        SetComponentWithEntityFixUp,

        AddBuffer,
        AddBufferWithEntityFixUp,
        SetBuffer,
        SetBufferWithEntityFixUp,
        AppendToBuffer,
        AppendToBufferWithEntityFixUp,

        AddManagedComponentData,
        SetManagedComponentData,

        AddSharedComponentData,
        SetSharedComponentData,

        AddComponentEntityQuery,
        AddMultipleComponentsEntityQuery,
        RemoveComponentEntityQuery,
        RemoveMultipleComponentsEntityQuery,
        DestroyEntitiesInEntityQuery,
        AddSharedComponentEntityQuery,

        AddComponentForMultipleEntities,
        AddMultipleComponentsForMultipleEntities,
        RemoveComponentForMultipleEntities,
        RemoveMultipleComponentsForMultipleEntities,
        DestroyMultipleEntities
    }

    /// <summary>
    /// Organized in memory like a single block with Chunk header followed by Size bytes of data.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ECBChunk
    {
        internal int Used;
        internal int Size;
        internal ECBChunk* Next;
        internal ECBChunk* Prev;

        internal int Capacity => Size - Used;

        internal int Bump(int size)
        {
            var off = Used;
            Used += size;
            return off;
        }

        internal int BaseSortKey
        {
            get
            {
                fixed(ECBChunk* pThis = &this)
                {
                    if (Used < sizeof(BasicCommand))
                    {
                        return -1;
                    }
                    var buf = (byte*)pThis + sizeof(ECBChunk);
                    var header = (BasicCommand*)(buf);
                    return header->SortKey;
                }
            }
        }
    }

    internal unsafe struct EntityCommandBufferData
    {
        public EntityCommandBufferChain m_MainThreadChain;

        public EntityCommandBufferChain* m_ThreadedChains;

        public int m_RecordedChainCount;

        public int m_MinimumChunkSize;

        public Allocator m_Allocator;

        public PlaybackPolicy m_PlaybackPolicy;

        public bool m_ShouldPlayback;

        public bool m_DidPlayback;

        public Entity m_Entity;

        public int m_BufferWithFixupsCount;
        public UnsafeAtomicCounter32 m_BufferWithFixups;

        private static readonly int ALIGN_64_BIT = 8;

        internal void InitConcurrentAccess()
        {
            if (m_ThreadedChains != null)
                return;

            // PERF: It's be great if we had a way to actually get the number of worst-case threads so we didn't have to allocate 128.
            int allocSize = sizeof(EntityCommandBufferChain) * JobsUtility.MaxJobThreadCount;

            m_ThreadedChains = (EntityCommandBufferChain*)Memory.Unmanaged.Allocate(allocSize, JobsUtility.CacheLineSize, m_Allocator);
            UnsafeUtility.MemClear(m_ThreadedChains, allocSize);

            for (var i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
            {
                EntityCommandBufferChain.InitChain(&m_ThreadedChains[i], m_Allocator);
            }
        }

        internal void DestroyConcurrentAccess()
        {
            if (m_ThreadedChains != null)
            {
                Memory.Unmanaged.Free(m_ThreadedChains, m_Allocator);
                m_ThreadedChains = null;
            }
        }

        private void ResetCreateCommandBatching(EntityCommandBufferChain* chain)
        {
            chain->m_PrevCreateCommand = null;
        }

        private void ResetEntityCommandBatching(EntityCommandBufferChain* chain)
        {
            chain->m_PrevEntityCommand = null;
        }

        internal void ResetCommandBatching(EntityCommandBufferChain* chain)
        {
            ResetCreateCommandBatching(chain);
            ResetEntityCommandBatching(chain);
        }

        internal void AddCreateCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, int index, EntityArchetype archetype, bool batchable)
        {
            if (batchable &&
                chain->m_PrevCreateCommand != null &&
                chain->m_PrevCreateCommand->Archetype == archetype)
            {
                ++chain->m_PrevCreateCommand->BatchCount;
            }
            else
            {
                ResetEntityCommandBatching(chain);
                var cmd = (CreateCommand*)Reserve(chain, sortKey, sizeof(CreateCommand));

                cmd->Header.CommandType = (int)op;
                cmd->Header.TotalSize = sizeof(CreateCommand);
                cmd->Header.SortKey = chain->m_LastSortKey;
                cmd->Archetype = archetype;
                cmd->IdentityIndex = index;
                cmd->BatchCount = 1;

                chain->m_PrevCreateCommand = cmd;
            }
        }

        internal void AddEntityCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, int index, Entity e, bool batchable)
        {
            if (batchable &&
                chain->m_PrevEntityCommand != null &&
                chain->m_PrevEntityCommand->Entity == e)
            {
                ++chain->m_PrevEntityCommand->BatchCount;
            }
            else
            {
                ResetCreateCommandBatching(chain);
                var cmd = (EntityCommand*)Reserve(chain, sortKey, sizeof(EntityCommand));

                cmd->Header.CommandType = (int)op;
                cmd->Header.TotalSize = sizeof(EntityCommand);
                cmd->Header.SortKey = chain->m_LastSortKey;
                cmd->Entity = e;
                cmd->IdentityIndex = index;
                cmd->BatchCount = 1;
                chain->m_PrevEntityCommand = cmd;
            }
        }

        internal bool RequiresEntityFixUp(byte* data, int typeIndex)
        {
            if (!TypeManager.HasEntityReferences(typeIndex))
                return false;

            var offsets = TypeManager.GetEntityOffsets(typeIndex, out var offsetCount);
            for (int i = 0; i < offsetCount; i++)
            {
                if (((Entity*)(data + offsets[i].Offset))->Index < 0)
                {
                    return true;
                }
            }
            return false;
        }

        internal void AddEntityComponentCommand<T>(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, Entity e, T component) where T : struct, IComponentData
        {
            var ctype = ComponentType.ReadWrite<T>();
            if (ctype.IsZeroSized)
            {
                AddEntityComponentTypeCommand(chain, sortKey, op, e, ctype);
                return;
            }

            // NOTE: This has to be sizeof not TypeManager.SizeInChunk since we use UnsafeUtility.CopyStructureToPtr
            //       even on zero size components.
            var typeSize = UnsafeUtility.SizeOf<T>();
            var sizeNeeded = Align(sizeof(EntityComponentCommand) + typeSize, ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var cmd = (EntityComponentCommand*)Reserve(chain, sortKey, sizeNeeded);

            cmd->Header.Header.CommandType = (int)op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->Header.Entity = e;
            cmd->ComponentTypeIndex = ctype.TypeIndex;
            cmd->ComponentSize = typeSize;

            byte* data = (byte*)(cmd + 1);
            UnsafeUtility.CopyStructureToPtr(ref component, data);

            if (RequiresEntityFixUp(data, ctype.TypeIndex))
            {
                if (op == ECBCommand.AddComponent)
                    cmd->Header.Header.CommandType = (int)ECBCommand.AddComponentWithEntityFixUp;
                else if (op == ECBCommand.SetComponent)
                    cmd->Header.Header.CommandType = (int)ECBCommand.SetComponentWithEntityFixUp;
            }
        }

        internal BufferHeader* AddEntityBufferCommand<T>(EntityCommandBufferChain* chain, int sortKey, ECBCommand op,
            Entity e, out int internalCapacity) where T : struct, IBufferElementData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            ref readonly var type = ref TypeManager.GetTypeInfo<T>();
            var sizeNeeded = Align(sizeof(EntityBufferCommand) + type.SizeInChunk, ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var cmd = (EntityBufferCommand*)Reserve(chain, sortKey, sizeNeeded);

            cmd->Header.Header.CommandType = (int)op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->Header.Entity = e;
            cmd->ComponentTypeIndex = typeIndex;
            cmd->ComponentSize = type.SizeInChunk;

            BufferHeader* header = &cmd->BufferNode.TempBuffer;
            BufferHeader.Initialize(header, type.BufferCapacity);

            cmd->BufferNode.Prev = chain->m_Cleanup->BufferCleanupList;
            chain->m_Cleanup->BufferCleanupList = &(cmd->BufferNode);

            internalCapacity = type.BufferCapacity;

            if (TypeManager.HasEntityReferences(typeIndex))
            {
                if (op == ECBCommand.AddBuffer)
                {
                    m_BufferWithFixups.Add(1);
                    cmd->Header.Header.CommandType = (int)ECBCommand.AddBufferWithEntityFixUp;
                }
                else if (op == ECBCommand.SetBuffer)
                {
                    m_BufferWithFixups.Add(1);
                    cmd->Header.Header.CommandType = (int)ECBCommand.SetBufferWithEntityFixUp;
                }
            }

            return header;
        }

        internal static int Align(int size, int alignmentPowerOfTwo)
        {
            return (size + alignmentPowerOfTwo - 1) & ~(alignmentPowerOfTwo - 1);
        }

        internal void AddEntityComponentTypeCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, Entity e, ComponentType t)
        {
            var sizeNeeded = Align(sizeof(EntityComponentCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var data = (EntityComponentCommand*)Reserve(chain, sortKey, sizeNeeded);

            data->Header.Header.CommandType = (int)op;
            data->Header.Header.TotalSize = sizeNeeded;
            data->Header.Header.SortKey = chain->m_LastSortKey;
            data->Header.Entity = e;
            data->ComponentTypeIndex = t.TypeIndex;
            data->ComponentSize = 0;
        }

        internal void AddEntityComponentTypesCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, Entity e, ComponentTypes t)
        {
            var sizeNeeded = Align(sizeof(EntityMultipleComponentsCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var data = (EntityMultipleComponentsCommand*)Reserve(chain, sortKey, sizeNeeded);

            data->Header.Header.CommandType = (int)op;
            data->Header.Header.TotalSize = sizeNeeded;
            data->Header.Header.SortKey = chain->m_LastSortKey;
            data->Header.Entity = e;
            data->Types = t;
        }

        internal void AddEntityQueryComponentCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, EntityQuery entityQuery, ComponentType t)
        {
            var sizeNeeded = Align(sizeof(EntityQueryComponentCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);

            var data = (EntityQueryComponentCommand*)Reserve(chain, sortKey, sizeNeeded);
            InitQueryHeader(&data->Header, op, chain, sizeNeeded, entityQuery);

            data->ComponentTypeIndex = t.TypeIndex;
        }

        internal void AddEntityQueryMultipleComponentsCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, EntityQuery entityQuery, ComponentTypes t)
        {
            var sizeNeeded = Align(sizeof(EntityQueryMultipleComponentsCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);

            var data = (EntityQueryMultipleComponentsCommand*)Reserve(chain, sortKey, sizeNeeded);
            InitQueryHeader(&data->Header, op, chain, sizeNeeded, entityQuery);

            data->Types = t;
        }

        internal bool AppendMultipleEntitiesCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, EntityQuery entityQuery)
        {
            var entities = entityQuery.ToEntityArray(m_Allocator);
            if (entities.Length == 0)
            {
                entities.Dispose();
                return false;
            }

            var sizeNeeded = Align(sizeof(MultipleEntitiesCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);

            var cmd = (MultipleEntitiesCommand*)Reserve(chain, sortKey, sizeNeeded);
            cmd->Entities.Ptr = (Entity*) entities.GetUnsafeReadOnlyPtr();
            cmd->Entities.Prev = chain->m_Cleanup->EntityArraysCleanupList;
            chain->m_Cleanup->EntityArraysCleanupList = &(cmd->Entities);

            cmd->EntitiesCount = entities.Length;
            cmd->Allocator = m_Allocator;

            cmd->Header.CommandType = (int)op;
            cmd->Header.TotalSize = sizeNeeded;
            cmd->Header.SortKey = chain->m_LastSortKey;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref entities.m_Safety, ref entities.m_DisposeSentinel); // dispose safety handle, but we'll dispose of the actual data in playback
#endif
            return true;
        }

        internal bool AppendMultipleEntitiesComponentCommandWithValue<T>(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, EntityQuery entityQuery, T component) where T : struct, IComponentData
        {
            var ctype = ComponentType.ReadWrite<T>();
            if (ctype.IsZeroSized)
                return AppendMultipleEntitiesComponentCommand(chain, sortKey, op, entityQuery, ctype);

            var entities = entityQuery.ToEntityArray(m_Allocator); // disposed in playback
            if (entities.Length == 0)
            {
                entities.Dispose();
                return false;
            }

            var typeSize = UnsafeUtility.SizeOf<T>();
            var sizeNeeded = Align(sizeof(MultipleEntitiesComponentCommand) + typeSize, ALIGN_64_BIT);

            ResetCommandBatching(chain);

            var cmd = (MultipleEntitiesComponentCommand*)Reserve(chain, sortKey, sizeNeeded);
            cmd->Header.Entities.Ptr = (Entity*) entities.GetUnsafeReadOnlyPtr();
            cmd->Header.Entities.Prev = chain->m_Cleanup->EntityArraysCleanupList;
            chain->m_Cleanup->EntityArraysCleanupList = &(cmd->Header.Entities);

            cmd->Header.EntitiesCount = entities.Length;
            cmd->Header.Allocator = m_Allocator;

            cmd->Header.Header.CommandType = (int)op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->ComponentTypeIndex = ctype.TypeIndex;
            cmd->ComponentSize = typeSize;

            byte* componentData = (byte*)(cmd + 1);
            UnsafeUtility.CopyStructureToPtr(ref component, componentData);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref entities.m_Safety, ref entities.m_DisposeSentinel); // dispose safety handle, but we'll dispose of the actual data in playback
#endif
            return true;
        }

        internal bool AppendMultipleEntitiesComponentCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, EntityQuery entityQuery, ComponentType t)
        {
            var entities = entityQuery.ToEntityArray(m_Allocator); // disposed in playback
            if (entities.Length == 0)
            {
                entities.Dispose();
                return false;
            }

            var sizeNeeded = Align(sizeof(MultipleEntitiesComponentCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);

            var cmd = (MultipleEntitiesComponentCommand*)Reserve(chain, sortKey, sizeNeeded);
            cmd->Header.Entities.Ptr = (Entity*) entities.GetUnsafeReadOnlyPtr();
            cmd->Header.Entities.Prev = chain->m_Cleanup->EntityArraysCleanupList;
            chain->m_Cleanup->EntityArraysCleanupList = &(cmd->Header.Entities);

            cmd->Header.EntitiesCount = entities.Length;
            cmd->Header.Allocator = m_Allocator;

            cmd->Header.Header.CommandType = (int)op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->ComponentTypeIndex = t.TypeIndex;
            cmd->ComponentSize = 0;   // signifies that the command doesn't include a value for the new component

    #if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref entities.m_Safety, ref entities.m_DisposeSentinel); // dispose safety handle, but we'll dispose of the actual data in playback
    #endif
            return true;
        }

        internal bool AppendMultipleEntitiesMultipleComponentsCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, EntityQuery entityQuery, ComponentTypes t)
        {
            var entities = entityQuery.ToEntityArray(m_Allocator);  // disposed in playback
            if (entities.Length == 0)
            {
                entities.Dispose();
                return false;
            }

            var sizeNeeded = Align(sizeof(MultipleEntitiesAndComponentsCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);

            var cmd = (MultipleEntitiesAndComponentsCommand*)Reserve(chain, sortKey, sizeNeeded);
            cmd->Header.Entities.Ptr = (Entity*) entities.GetUnsafeReadOnlyPtr();
            cmd->Header.Entities.Prev = chain->m_Cleanup->EntityArraysCleanupList;
            chain->m_Cleanup->EntityArraysCleanupList = &(cmd->Header.Entities);

            cmd->Header.EntitiesCount = entities.Length;
            cmd->Header.Allocator = m_Allocator;

            cmd->Header.Header.CommandType = (int)op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;

            cmd->Types = t;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref entities.m_Safety, ref entities.m_DisposeSentinel); // dispose safety handle, but we'll dispose of the actual data in playback
#endif
            return true;
        }

        internal void AddEntitySharedComponentCommand<T>(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, Entity e, int hashCode, object boxedObject)
            where T : struct
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var sizeNeeded = Align(sizeof(EntitySharedComponentCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);
            chain->m_CanBurstPlayback = false;
            var data = (EntitySharedComponentCommand*)Reserve(chain, sortKey, sizeNeeded);

            data->Header.Header.CommandType = (int)op;
            data->Header.Header.TotalSize = sizeNeeded;
            data->Header.Header.SortKey = chain->m_LastSortKey;
            data->Header.Entity = e;
            data->ComponentTypeIndex = typeIndex;
            data->HashCode = hashCode;

            if (boxedObject != null)
            {
                data->GCNode.BoxedObject = GCHandle.Alloc(boxedObject);
                // We need to store all GCHandles on a cleanup list so we can dispose them later, regardless of if playback occurs or not.
                data->GCNode.Prev = chain->m_Cleanup->CleanupList;
                chain->m_Cleanup->CleanupList = &(data->GCNode);
            }
            else
            {
                data->GCNode.BoxedObject = new GCHandle();
            }
        }

        internal void AddEntityQueryComponentCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, EntityQuery entityQuery)
        {
            var sizeNeeded = Align(sizeof(EntityQueryComponentCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var data = (EntityQueryCommand*)Reserve(chain, sortKey, sizeNeeded);

            InitQueryHeader(data, op, chain, sizeNeeded, entityQuery);
        }

        static void InitQueryHeader(EntityQueryCommand* data, ECBCommand op, EntityCommandBufferChain* chain, int size, EntityQuery entityQuery)
        {
            data->Header.CommandType = (int)op;
            data->Header.TotalSize = size;
            data->Header.SortKey = chain->m_LastSortKey;
            var impl = entityQuery._GetImpl();
            data->QueryData = impl->_QueryData;
            data->EntityQueryFilter = impl->_Filter;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            data->Store = impl->_Access->EntityComponentStore;
#endif
        }

        internal void AddEntitySharedComponentCommand<T>(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, EntityQuery entityQuery, int hashCode, object boxedObject)
            where T : struct
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var sizeNeeded = Align(sizeof(EntityQuerySharedComponentCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);
            chain->m_CanBurstPlayback = false;
            var data = (EntityQuerySharedComponentCommand*)Reserve(chain, sortKey, sizeNeeded);
            InitQueryHeader(&data->Header, op, chain, sizeNeeded, entityQuery);
            data->ComponentTypeIndex = typeIndex;
            data->HashCode = hashCode;

            if (boxedObject != null)
            {
                data->GCNode.BoxedObject = GCHandle.Alloc(boxedObject);
                // We need to store all GCHandles on a cleanup list so we can dispose them later, regardless of if playback occurs or not.
                data->GCNode.Prev = chain->m_Cleanup->CleanupList;
                chain->m_Cleanup->CleanupList = &(data->GCNode);
            }
            else
            {
                data->GCNode.BoxedObject = new GCHandle();
            }
        }

        internal byte* Reserve(EntityCommandBufferChain* chain, int sortKey, int size)
        {
            int newSortKey = sortKey;
            if (newSortKey < chain->m_LastSortKey)
            {
                EntityCommandBufferChain* archivedChain = (EntityCommandBufferChain*)Memory.Unmanaged.Allocate(sizeof(EntityCommandBufferChain), ALIGN_64_BIT, m_Allocator);
                *archivedChain = *chain;
                EntityCommandBufferChain.InitChain(chain, m_Allocator);
                chain->m_NextChain = archivedChain;
            }
            chain->m_LastSortKey = newSortKey;

            if (chain->m_Tail == null || chain->m_Tail->Capacity < size)
            {
                var chunkSize = math.max(m_MinimumChunkSize, size);

                var c = (ECBChunk*)Memory.Unmanaged.Allocate(sizeof(ECBChunk) + chunkSize, 16, m_Allocator);
                var prev = chain->m_Tail;
                c->Next = null;
                c->Prev = prev;
                c->Used = 0;
                c->Size = chunkSize;

                if (prev != null) prev->Next = c;

                if (chain->m_Head == null)
                {
                    chain->m_Head = c;
                    // This seems to be the best place to track the number of non-empty command buffer chunks
                    // during the recording process.
                    Interlocked.Increment(ref m_RecordedChainCount);
                }

                chain->m_Tail = c;
            }

            var offset = chain->m_Tail->Bump(size);
            var ptr = (byte*)chain->m_Tail + sizeof(ECBChunk) + offset;
            return ptr;
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public DynamicBuffer<T> CreateBufferCommand<T>(ECBCommand commandType, EntityCommandBufferChain* chain, int sortKey, Entity e, AtomicSafetyHandle bufferSafety, AtomicSafetyHandle arrayInvalidationSafety) where T : struct, IBufferElementData
#else
        public DynamicBuffer<T> CreateBufferCommand<T>(ECBCommand commandType, EntityCommandBufferChain* chain, int sortKey, Entity e) where T : struct, IBufferElementData
#endif
        {
            int internalCapacity;
            BufferHeader* header = AddEntityBufferCommand<T>(chain, sortKey, commandType, e, out internalCapacity);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safety = bufferSafety;
            AtomicSafetyHandle.UseSecondaryVersion(ref safety);
            var arraySafety = arrayInvalidationSafety;
            return new DynamicBuffer<T>(header, safety, arraySafety, false, false, 0, internalCapacity);
#else
            return new DynamicBuffer<T>(header, internalCapacity);
#endif
        }

        public void AppendToBufferCommand<T>(EntityCommandBufferChain* chain, int sortKey, Entity e, T element) where T : struct, IBufferElementData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            // NOTE: This has to be sizeof not TypeManager.SizeInChunk since we use UnsafeUtility.CopyStructureToPtr
            //       even on zero size components.
            var typeSize = UnsafeUtility.SizeOf<T>();
            var sizeNeeded = Align(sizeof(EntityComponentCommand) + typeSize, ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var cmd = (EntityComponentCommand*)Reserve(chain, sortKey, sizeNeeded);

            cmd->Header.Header.CommandType = (int)ECBCommand.AppendToBuffer;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->Header.Entity = e;
            cmd->ComponentTypeIndex = typeIndex;
            cmd->ComponentSize = typeSize;

            byte* data = (byte*)(cmd + 1);
            UnsafeUtility.CopyStructureToPtr(ref element, data);

            if (TypeManager.HasEntityReferences(typeIndex))
            {
                cmd->Header.Header.CommandType = (int)ECBCommand.AppendToBufferWithEntityFixUp;
            }
        }
    }

    /// <summary>
    /// Specifies if the <see cref="EntityCommandBuffer"/> can be played a single time or multiple times.
    /// </summary>
    public enum PlaybackPolicy
    {
        /// <summary>
        /// The <see cref="EntityCommandBuffer"/> can only be played once. After a first playback, the EntityCommandBuffer must be disposed.
        /// </summary>
        SinglePlayback,
        /// <summary>
        /// The <see cref="EntityCommandBuffer"/> can be played back more than once.
        /// </summary>
        /// <remarks>Even though the EntityCommandBuffer can be played back more than once, no commands can be added after the first playback.</remarks>
        MultiPlayback
    }

    /// <summary>
    ///     A thread-safe command buffer that can buffer commands that affect entities and components for later playback.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [NativeContainer]
    [BurstCompile]
    [GenerateBurstMonoInterop("EntityCommandBuffer")]
    public unsafe partial struct EntityCommandBuffer : IDisposable
    {
        /// <summary>
        ///     The minimum chunk size to allocate from the job allocator.
        /// </summary>
        /// We keep this relatively small as we don't want to overload the temp allocator in case people make a ton of command buffers.
        private const int kDefaultMinimumChunkSize = 4 * 1024;

        [NativeDisableUnsafePtrRestriction] internal EntityCommandBufferData* m_Data;

        internal int SystemID;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety0;
        private AtomicSafetyHandle m_BufferSafety;
        private AtomicSafetyHandle m_ArrayInvalidationSafety;
        private int m_SafetyReadOnlyCount;
        private int m_SafetyReadWriteCount;

        [NativeSetClassTypeToNullOnSchedule]
        private DisposeSentinel m_DisposeSentinel;

        internal void WaitForWriterJobs()
        {
            AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_Safety0);
            AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_BufferSafety);
            AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_ArrayInvalidationSafety);
        }

        private static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<EntityCommandBuffer>();
        [BurstDiscard]
        private static void CreateStaticSafetyId()
        {
            s_staticSafetyId.Data = AtomicSafetyHandle.NewStaticSafetyId<EntityCommandBuffer>();
        }
#endif
        static readonly ProfilerMarker k_ProfileEcbPlayback = new ProfilerMarker("EntityCommandBuffer.Playback");

        /// <summary>
        ///     Allows controlling the size of chunks allocated from the temp job allocator to back the command buffer.
        /// </summary>
        /// Larger sizes are more efficient, but create more waste in the allocator.
        public int MinimumChunkSize
        {
            get { return m_Data->m_MinimumChunkSize > 0 ? m_Data->m_MinimumChunkSize : kDefaultMinimumChunkSize; }
            set { m_Data->m_MinimumChunkSize = Math.Max(0, value); }
        }

        /// <summary>
        /// Controls whether this command buffer should play back.
        /// </summary>
        ///
        /// This property is normally true, but can be useful to prevent
        /// the buffer from playing back when the user code is not in control
        /// of the site of playback.
        ///
        /// For example, is a buffer has been acquired from an EntityCommandBufferSystem and partially
        /// filled in with data, but it is discovered that the work should be aborted,
        /// this property can be set to false to prevent the buffer from playing back.
        public bool ShouldPlayback
        {
            get { return m_Data != null ? m_Data->m_ShouldPlayback : false; }
            set { if (m_Data != null) m_Data->m_ShouldPlayback = value; }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void EnforceSingleThreadOwnership()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_Data == null)
                throw new NullReferenceException("The EntityCommandBuffer has not been initialized!");
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety0);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void AssertDidNotPlayback()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_Data != null && m_Data->m_DidPlayback)
                throw new InvalidOperationException("The EntityCommandBuffer has already been played back and no further commands can be added.");
#endif
        }

        /// <summary>
        ///  Creates a new command buffer.
        /// </summary>
        /// <param name="label">Memory allocator to use for chunks and data</param>
        public EntityCommandBuffer(Allocator label)
            : this(label, 1, PlaybackPolicy.SinglePlayback)
        {
        }

        /// <summary>
        ///  Creates a new command buffer.
        /// </summary>
        /// <param name="label">Memory allocator to use for chunks and data</param>
        /// <param name="playbackPolicy">Specifies if the EntityCommandBuffer can be played a single time or more than once.</param>
        public EntityCommandBuffer(Allocator label, PlaybackPolicy playbackPolicy)
            : this(label, 1, playbackPolicy)
        {
        }

        /// <summary>
        ///  Creates a new command buffer.
        /// </summary>
        /// <param name="label">Memory allocator to use for chunks and data</param>
        /// <param name="disposeSentinelStackDepth">
        /// Specify how many stack frames to skip when reporting memory leaks.
        /// -1 will disable leak detection
        /// 0 or positive values
        /// </param>
        /// <param name="playbackPolicy">Specifies if the EntityCommandBuffer can be played a single time or more than once.</param>
        internal EntityCommandBuffer(Allocator label, int disposeSentinelStackDepth, PlaybackPolicy playbackPolicy)
        {
            m_Data = (EntityCommandBufferData*)Memory.Unmanaged.Allocate(sizeof(EntityCommandBufferData), UnsafeUtility.AlignOf<EntityCommandBufferData>(), label);
            m_Data->m_Allocator = label;
            m_Data->m_PlaybackPolicy = playbackPolicy;
            m_Data->m_MinimumChunkSize = kDefaultMinimumChunkSize;
            m_Data->m_ShouldPlayback = true;
            m_Data->m_DidPlayback = false;
            m_Data->m_BufferWithFixupsCount = 0;
            m_Data->m_BufferWithFixups = new UnsafeAtomicCounter32(&m_Data->m_BufferWithFixupsCount);

            EntityCommandBufferChain.InitChain(&m_Data->m_MainThreadChain, label);

            m_Data->m_ThreadedChains = null;
            m_Data->m_RecordedChainCount = 0;

            SystemID = 0;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (disposeSentinelStackDepth >= 0)
            {
                DisposeSentinel.Create(out m_Safety0, out m_DisposeSentinel, disposeSentinelStackDepth, label);
            }
            else
            {
                m_DisposeSentinel = null;
                m_Safety0 = AtomicSafetyHandle.Create();
            }

            // Used for all buffers returned from the API, so we can invalidate them once Playback() has been called.
            m_BufferSafety = AtomicSafetyHandle.Create();
            // Used to invalidate array aliases to buffers
            m_ArrayInvalidationSafety = AtomicSafetyHandle.Create();

            m_SafetyReadOnlyCount = 0;
            m_SafetyReadWriteCount = 3;

            if (s_staticSafetyId.Data == 0)
            {
                CreateStaticSafetyId();
            }
            AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety0, s_staticSafetyId.Data);
            AtomicSafetyHandle.SetStaticSafetyId(ref m_BufferSafety, s_staticSafetyId.Data);
            AtomicSafetyHandle.SetStaticSafetyId(ref m_ArrayInvalidationSafety, s_staticSafetyId.Data);
#endif
            m_Data->m_Entity = new Entity();
            m_Data->m_BufferWithFixups.Reset();
        }

        public bool IsCreated   { get { return m_Data != null; } }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref m_Safety0, ref m_DisposeSentinel);
            AtomicSafetyHandle.Release(m_ArrayInvalidationSafety);
            AtomicSafetyHandle.Release(m_BufferSafety);
#endif

            if (m_Data != null)
            {
                FreeChain(&m_Data->m_MainThreadChain, m_Data->m_PlaybackPolicy, m_Data->m_DidPlayback);

                if (m_Data->m_ThreadedChains != null)
                {
                    for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
                    {
                        FreeChain(&m_Data->m_ThreadedChains[i], m_Data->m_PlaybackPolicy, m_Data->m_DidPlayback);
                    }

                    m_Data->DestroyConcurrentAccess();
                }

                Memory.Unmanaged.Free(m_Data, m_Data->m_Allocator);
                m_Data = null;
            }
        }

        private void FreeChain(EntityCommandBufferChain* chain, PlaybackPolicy playbackPolicy, bool didPlayback)
        {
            if (chain == null)
            {
                return;
            }

            var cleanup_list = chain->m_Cleanup->CleanupList;
            while (cleanup_list != null)
            {
                cleanup_list->BoxedObject.Free();
                cleanup_list = cleanup_list->Prev;
            }

            chain->m_Cleanup->CleanupList = null;

            // Buffers played in ecbs which can be played back more than once are always copied during playback.
            // Also, entity arrays in commands are only disposed in playback if ECB is single playback.
            if (playbackPolicy == PlaybackPolicy.MultiPlayback || !didPlayback)
            {
                var bufferCleanupList = chain->m_Cleanup->BufferCleanupList;
                while (bufferCleanupList != null)
                {
                    var prev = bufferCleanupList->Prev;
                    BufferHeader.Destroy(&bufferCleanupList->TempBuffer);
                    bufferCleanupList = prev;
                }

                var entityArraysCleanupList = chain->m_Cleanup->EntityArraysCleanupList;
                while (entityArraysCleanupList != null)
                {
                    var prev = entityArraysCleanupList->Prev;
                    UnsafeUtility.Free(entityArraysCleanupList->Ptr, m_Data->m_Allocator);
                    entityArraysCleanupList = prev;
                }
            }

            chain->m_Cleanup->BufferCleanupList = null;
            chain->m_Cleanup->EntityArraysCleanupList = null;
            Memory.Unmanaged.Free(chain->m_Cleanup, m_Data->m_Allocator);

            while (chain->m_Tail != null)
            {
                var prev = chain->m_Tail->Prev;
                Memory.Unmanaged.Free(chain->m_Tail, m_Data->m_Allocator);
                chain->m_Tail = prev;
            }

            chain->m_Head = null;
            if (chain->m_NextChain != null)
            {
                FreeChain(chain->m_NextChain, playbackPolicy, didPlayback);
                Memory.Unmanaged.Free(chain->m_NextChain, m_Data->m_Allocator);
                chain->m_NextChain = null;
            }
        }

        internal int MainThreadSortKey => Int32.MaxValue;
        private const bool kBatchableCommand = true;

        /// <summary>
        /// Create an entity with specified archetype.</summary>
        /// <param name="archetype">The archetype of the new entity.</param>
        public Entity CreateEntity(EntityArchetype archetype)
        {
            archetype.CheckValidEntityArchetype();
            return _CreateEntity(archetype);
        }

        /// <summary>
        /// Create an entity with no components.</summary>
        public Entity CreateEntity()
        {
            EntityArchetype archetype = new EntityArchetype();
            return _CreateEntity(archetype);
        }

        private Entity _CreateEntity(EntityArchetype archetype)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            int index = --m_Data->m_Entity.Index;
            m_Data->AddCreateCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.CreateEntity, index, archetype, kBatchableCommand);
            return m_Data->m_Entity;
        }

        public Entity Instantiate(Entity e)
        {
            CheckEntityNotNull(e);
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            int index = --m_Data->m_Entity.Index;
            m_Data->AddEntityCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.InstantiateEntity,
                index, e, kBatchableCommand);
            return m_Data->m_Entity;
        }

        public void DestroyEntity(Entity e)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.DestroyEntity, 0, e, false);
        }

        public DynamicBuffer<T> AddBuffer<T>(Entity e) where T : struct, IBufferElementData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return m_Data->CreateBufferCommand<T>(ECBCommand.AddBuffer, &m_Data->m_MainThreadChain, MainThreadSortKey, e, m_BufferSafety, m_ArrayInvalidationSafety);
#else
            return m_Data->CreateBufferCommand<T>(ECBCommand.AddBuffer, &m_Data->m_MainThreadChain, MainThreadSortKey, e);
#endif
        }

        public DynamicBuffer<T> SetBuffer<T>(Entity e) where T : struct, IBufferElementData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return m_Data->CreateBufferCommand<T>(ECBCommand.SetBuffer, &m_Data->m_MainThreadChain, MainThreadSortKey, e, m_BufferSafety, m_ArrayInvalidationSafety);
#else
            return m_Data->CreateBufferCommand<T>(ECBCommand.SetBuffer, &m_Data->m_MainThreadChain, MainThreadSortKey, e);
#endif
        }

        /// <summary>
        /// Appends a single element to the end of a dynamic buffer component.</summary>
        /// <remarks>
        /// At <see cref="Playback(EntityManager)"/>, this command throws an InvalidOperationException if the entity doesn't
        /// have a <see cref="DynamicBuffer{T}"/> component storing elements of type T.
        /// </remarks>
        /// <param name="e">The entity to which the dynamic buffer belongs.</param>
        /// <param name="element">The new element to add to the <see cref="DynamicBuffer{T}"/> component.</param>
        /// <typeparam name="T">The <see cref="IBufferElementData"/> type stored by the <see cref="DynamicBuffer{T}"/>.</typeparam>
        /// <exception cref="InvalidOperationException">Thrown if the entity does not have a <see cref="DynamicBuffer{T}"/>
        /// component storing elements of type T at the time the entity command buffer executes this append-to-buffer command.</exception>
        public void AppendToBuffer<T>(Entity e, T element) where T : struct, IBufferElementData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AppendToBufferCommand<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, e, element);
        }

        public void AddComponent<T>(Entity e, T component) where T : struct, IComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddComponent, e, component);
        }

        public void AddComponent<T>(Entity e) where T : struct, IComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentTypeCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddComponent, e, ComponentType.ReadWrite<T>());
        }

        public void AddComponent(Entity e, ComponentType componentType)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentTypeCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddComponent, e, componentType);
        }


        /// <summary> Records a command to add one or more components to an entity. </summary>
        /// <remarks></remarks>
        /// <param name="e"> The entity to get additional components. </param>
        /// <param name="componentTypes"> The types of components to add. </param>
        public void AddComponent(Entity e, ComponentTypes componentTypes)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentTypesCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddMultipleComponents, e, componentTypes);
        }

        public void SetComponent<T>(Entity e, T component) where T : struct, IComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.SetComponent, e, component);
        }

        public void RemoveComponent<T>(Entity e)
        {
            RemoveComponent(e, ComponentType.ReadWrite<T>());
        }

        public void RemoveComponent(Entity e, ComponentType componentType)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentTypeCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.RemoveComponent, e, componentType);
        }

        /// <summary> Records a command to remove one or more components from an entity. </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="e"> The entity to have components removed. </param>
        /// <param name="componentTypes"> The types of components to remove. </param>
        public void RemoveComponent(Entity e, ComponentTypes componentTypes)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentTypesCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.RemoveMultipleComponents, e, componentTypes);
        }

        /// <summary> Records a command to remove one or more components from all entities matching a query.</summary>
        /// <remarks>The query is performed at playback time, not when the method is called.</remarks>
        /// <param name="entityQuery"> The query specifying which entities to remove the components from. </param>
        /// <param name="componentTypes"> The types of components to remove. </param>
        public void RemoveComponent(EntityQuery entityQuery, ComponentTypes componentTypes)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityQueryMultipleComponentsCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.RemoveMultipleComponentsEntityQuery, entityQuery, componentTypes);
        }

        /// <summary> Records a command to add a component to all entities matching a query.</summary>
        /// <remarks>The query is performed at playback time, not when the method is called.</remarks>
        /// <param name="entityQuery"> The query specifying which entities to add the component to. </param>
        /// <param name="componentType"> The type of component to add.</param>
        public void AddComponent(EntityQuery entityQuery, ComponentType componentType)
        {
            AssertDidNotPlayback();
            m_Data->AddEntityQueryComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.AddComponentEntityQuery, entityQuery, componentType);
        }

        /// <summary> Records a command to add one or more components to all entities matching a query. </summary>
        /// <remarks>The query is performed at playback time, not when the method is called.</remarks>
        /// <param name="entityQuery"> The query specifying which entities get the added components. </param>
        /// <param name="componentTypes"> The types of components to add. </param>
        public void AddComponent(EntityQuery entityQuery, ComponentTypes types)
        {
            AssertDidNotPlayback();
            m_Data->AddEntityQueryMultipleComponentsCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.AddMultipleComponentsEntityQuery, entityQuery, types);
        }

        /// <summary> Records a command to add a component to all entities matching a query.</summary>
        /// <remarks>The query is performed at playback time, not when the method is called.</remarks>
        /// <param name="entityQuery"> The query specifying which entities get the added component. </param>
        /// <typeparam name="T"> The type of component to add. </typeparam>
        public void AddComponent<T>(EntityQuery entityQuery)
        {
            AddComponent(entityQuery, ComponentType.ReadWrite<T>());
        }

        /// <summary> Records a command to remove a component from all entities matching a query.</summary>
        /// <remarks>The query is performed at playback time, not when the method is called.</remarks>
        /// <param name="entityQuery"> The query specifying which entities from which the component is removed. </param>
        /// <param name="componentTypes"> The type of component to remove. </param>
        public void RemoveComponent(EntityQuery entityQuery, ComponentType componentType)
        {
            AssertDidNotPlayback();
            m_Data->AddEntityQueryComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.RemoveComponentEntityQuery, entityQuery, componentType);
        }

        /// <summary> Records a command to remove a component from all entities matching a query.</summary>
        /// <remarks>The query is performed at playback time, not when the method is called.</remarks>
        /// <param name="entityQuery"> The query specifying which entities from which the component is removed. </param>
        /// <typeparam name="T"> The type of component to remove. </typeparam>
        public void RemoveComponent<T>(EntityQuery entityQuery)
        {
            RemoveComponent(entityQuery, ComponentType.ReadWrite<T>());
        }

        /// <summary> Records a command to destroy all entities matching a query.</summary>
        /// <remarks>The query is performed at playback time, not when the method is called.</remarks>
        /// <param name="entityQuery"> The query specifying which entities from which the component is removed. </param>
        public void DestroyEntity(EntityQuery entityQuery)
        {
            AssertDidNotPlayback();
            m_Data->AddEntityQueryComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.DestroyEntitiesInEntityQuery, entityQuery);
        }

        /// <summary>Records a command to add a component to all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Does not affect entities which already have the component.
        ///
        /// If one of these entities is destroyed before playback, playback will fail. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities to which the component is added. </param>
        /// <param name="componentType">The type of component to add.</param>
        /// <returns>True if the query matches any entities. False if it matches none.</returns>
        public void AddComponentForEntityQuery(EntityQuery entityQuery, ComponentType componentType)
        {
            AssertDidNotPlayback();
            m_Data->AppendMultipleEntitiesComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.AddComponentForMultipleEntities, entityQuery, componentType);
        }

        /// <summary>Records a command to add a component to all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Does not affect entities which already have the component.
        ///
        /// If one of these entities is destroyed before playback, playback will fail. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities to which the component is added. </param>
        /// <typeparam name="T"> The type of component to add. </typeparam>
        /// <returns>True if the query matches any entities. False if it matches none.</returns>
        public void AddComponentForEntityQuery<T>(EntityQuery entityQuery)
        {
            AddComponentForEntityQuery(entityQuery, ComponentType.ReadWrite<T>());
        }

        /// <summary>Records a command to add a component to all entities matching a query. Also sets the value of this new component on all the matching entities.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Entities which already have the component type will have the component set to the value.
        ///
        /// If one of these entities is destroyed before playback, playback will fail. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities to which the component is added. </param>
        /// <param name="value">The value to set on the new component in playback for all entities matching the query.</param>
        /// <typeparam name="T"> The type of component to add. </typeparam>
        /// <returns>True if the query matches any entities. False if it matches none.</returns>
        public void AddComponentForEntityQuery<T>(EntityQuery entityQuery, T value) where T : struct, IComponentData
        {
            AssertDidNotPlayback();
            m_Data->AppendMultipleEntitiesComponentCommandWithValue(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.AddComponentForMultipleEntities, entityQuery, value);
        }

        /// <summary>Records a command to add multiple components to all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Some matching entities may already have some or all of the specified components. After this operation, all matching entities will have all of the components.
        ///
        /// If one of these entities is destroyed before playback, playback will fail. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities to which the components are added. </param>
        /// <param name="componentTypes">The types of components to add.</param>
        /// <returns>True if the query matches any entities. False if it matches none.</returns>
        public void AddComponentForEntityQuery(EntityQuery entityQuery, ComponentTypes componentTypes)
        {
            AssertDidNotPlayback();
            m_Data->AppendMultipleEntitiesMultipleComponentsCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.AddMultipleComponentsForMultipleEntities, entityQuery, componentTypes);
        }

        /// <summary>Records a command to remove a component from all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Does not affect entities already missing the component.
        ///
        /// If one of these entities is destroyed before playback, playback will fail. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities from which the component is removed. </param>
        /// <param name="componentType">The types of component to remove.</param>
        /// <returns>True if the query matches any entities. False if it matches none.</returns>
        public void RemoveComponentForEntityQuery(EntityQuery entityQuery, ComponentType componentType)
        {
            AssertDidNotPlayback();
            m_Data->AppendMultipleEntitiesComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.RemoveComponentForMultipleEntities, entityQuery, componentType);
        }

        /// <summary>Records a command to remove a component from all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Does not affect entities already missing the component.
        ///
        /// If one of these entities is destroyed before playback, playback will fail. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities from which the component is removed. </param>
        /// <typeparam name="T"> The type of component to remove. </typeparam>
        /// <returns>True if the query matches any entities. False if it matches none.</returns>
        public void RemoveComponentForEntityQuery<T>(EntityQuery entityQuery)
        {
            RemoveComponentForEntityQuery(entityQuery, ComponentType.ReadWrite<T>());
        }

        /// <summary>Records a command to remove multiple components from all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Some matching entities may already be missing some or all of the specified components. After this operation, all matching entities will have none of the components.
        ///
        /// If one of these entities is destroyed before playback, playback will fail. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities from which the components are removed. </param>
        /// <param name="componentTypes">The types of components to remove.</param>
        /// <returns>True if the query matches any entities. False if it matches none.</returns>
        public void RemoveComponentForEntityQuery(EntityQuery entityQuery, ComponentTypes componentTypes)
        {
            AssertDidNotPlayback();
            m_Data->AppendMultipleEntitiesMultipleComponentsCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.RemoveMultipleComponentsForMultipleEntities, entityQuery, componentTypes);
        }

        /// <summary>Records a command to destroy all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// If one of these entities is destroyed before playback, playback will fail. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities to destroy.</param>
        /// <returns>True if the query matches any entities. False if it matches none.</returns>
        public void DestroyEntitiesForEntityQuery(EntityQuery entityQuery)
        {
            AssertDidNotPlayback();
            m_Data->AppendMultipleEntitiesCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.DestroyMultipleEntities, entityQuery);
        }

        static bool IsDefaultObject<T>(ref T component, out int hashCode) where T : struct, ISharedComponentData
        {
            var defaultValue = default(T);

            hashCode = TypeManager.GetHashCode(ref component);
            return TypeManager.Equals(ref defaultValue, ref component);
        }

        public void AddSharedComponent<T>(Entity e, T component) where T : struct, ISharedComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();

            int hashCode;
            if (IsDefaultObject(ref component, out hashCode))
                m_Data->AddEntitySharedComponentCommand<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddSharedComponentData, e, hashCode, null);
            else
                m_Data->AddEntitySharedComponentCommand<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddSharedComponentData, e, hashCode, component);
        }

        /// <summary> Records a command to add a shared component to all entities matching a query.</summary>
        /// <remarks>The query is performed at playback time, not when the method is called. For entities matching the query which already have
        /// this component type, the value is updated.</remarks>
        /// <param name="entityQuery"> The query specifying which entities to add the component value to. </param>
        /// <param name="component"> The component value to add. </param>
        public void AddSharedComponent<T>(EntityQuery entityQuery, T component) where T : struct, ISharedComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();

            int hashCode;
            if (IsDefaultObject(ref component, out hashCode))
                m_Data->AddEntitySharedComponentCommand<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddSharedComponentEntityQuery, entityQuery, hashCode, null);
            else
                m_Data->AddEntitySharedComponentCommand<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddSharedComponentEntityQuery, entityQuery, hashCode, component);
        }

        public void SetSharedComponent<T>(Entity e, T component) where T : struct, ISharedComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();

            int hashCode;
            if (IsDefaultObject(ref component, out hashCode))
                m_Data->AddEntitySharedComponentCommand<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.SetSharedComponentData, e, hashCode, null);
            else
                m_Data->AddEntitySharedComponentCommand<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.SetSharedComponentData, e, hashCode, component);
        }

        /// <summary>
        /// Play back all recorded operations against an entity manager.
        /// </summary>
        /// <param name="mgr">The entity manager that will receive the operations</param>
        public void Playback(EntityManager mgr)
        {
            PlaybackInternal(mgr.GetCheckedEntityDataAccess());
        }

        /// <summary>
        /// Play back all recorded operations with an exclusive entity transaction.
        /// <seealso cref="EntityManager.BeginExclusiveEntityTransaction"/>.
        /// </summary>
        /// <param name="mgr">The exclusive entity transaction that will process the operations</param>
        public void Playback(ExclusiveEntityTransaction mgr)
        {
            PlaybackInternal(mgr.EntityManager.GetCheckedEntityDataAccess());
        }

        void PlaybackInternal(EntityDataAccess* mgr)
        {
            EnforceSingleThreadOwnership();

            if (!ShouldPlayback || m_Data == null)
                return;
            if (m_Data != null && m_Data->m_DidPlayback && m_Data->m_PlaybackPolicy == PlaybackPolicy.SinglePlayback)
            {
                throw new InvalidOperationException(
                    "Attempt to call Playback() on an EntityCommandBuffer that has already been played back. " +
                    "EntityCommandBuffers created with the SinglePlayback policy can only be played back once.");
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_BufferSafety);
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_ArrayInvalidationSafety);
#endif

            k_ProfileEcbPlayback.Begin();

            // Walk all chains (Main + Threaded) and build a NativeArray of PlaybackState objects.
            // Only chains with non-null Head pointers will be included.
            if (m_Data->m_RecordedChainCount > 0)
            {
                var archetypeChanges = new EntityComponentStore.ArchetypeChanges();
                var managedReferenceIndexRemovalCount = new NativeList<int>(10, Allocator.Temp);
                var managedListPointer =
                    (UnsafeList*)NativeListUnsafeUtility.GetInternalListDataPtrUnchecked(
                        ref managedReferenceIndexRemovalCount);
                StartTrackingChanges(mgr, managedListPointer, ref archetypeChanges);

                var chainStates = new NativeArray<ECBChainPlaybackState>(m_Data->m_RecordedChainCount, Allocator.Temp);
                using (chainStates)
                {
                    int initialChainCount = 0;
                    for (var chain = &m_Data->m_MainThreadChain; chain != null; chain = chain->m_NextChain)
                    {
                        if (chain->m_Head != null)
                        {
#pragma warning disable 728
                            chainStates[initialChainCount++] = new ECBChainPlaybackState
                            {
                                Chunk = chain->m_Head,
                                Offset = 0,
                                NextSortKey = chain->m_Head->BaseSortKey,
                                CanBurstPlayback = chain->m_CanBurstPlayback
                            };
#pragma warning restore 728
                        }
                    }
                    if (m_Data->m_ThreadedChains != null)
                    {
                        for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
                        {
                            for (var chain = &m_Data->m_ThreadedChains[i]; chain != null; chain = chain->m_NextChain)
                            {
                                if (chain->m_Head != null)
                                {
#pragma warning disable 728
                                    chainStates[initialChainCount++] = new ECBChainPlaybackState
                                    {
                                        Chunk = chain->m_Head,
                                        Offset = 0,
                                        NextSortKey = chain->m_Head->BaseSortKey,
                                        CanBurstPlayback = chain->m_CanBurstPlayback
                                    };
#pragma warning restore 728
                                }
                            }
                        }
                    }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    if (m_Data->m_RecordedChainCount != initialChainCount)
                        Assert.IsTrue(false, "RecordedChainCount (" + m_Data->m_RecordedChainCount + ") != initialChainCount (" + initialChainCount + ")");
#endif

                    // Play back the recorded commands in increasing sortKey order
                    const int kMaxStatesOnStack = 100000;
                    int entityCount = -m_Data->m_Entity.Index;
                    int bufferCount = *m_Data->m_BufferWithFixups.Counter;
                    int playbackStateSize = entityCount * sizeof(Entity) +
                        bufferCount * sizeof(ECBSharedPlaybackState.BufferWithFixUp);

                    Entity* createEntitiesBatch = null;
                    ECBSharedPlaybackState.BufferWithFixUp* buffersWithFixup = null;
                    if (playbackStateSize > kMaxStatesOnStack)
                    {
                        createEntitiesBatch = (Entity*)
                            Memory.Unmanaged.Allocate(entityCount * sizeof(Entity),
                            4, Allocator.Temp);
                        buffersWithFixup = (ECBSharedPlaybackState.BufferWithFixUp*)
                            Memory.Unmanaged.Allocate(bufferCount * sizeof(ECBSharedPlaybackState.BufferWithFixUp),
                            4, Allocator.Temp);
                    }
                    else
                    {
                        var stacke = stackalloc Entity[entityCount];
                        createEntitiesBatch = stacke;

                        var stackb = stackalloc ECBSharedPlaybackState.BufferWithFixUp[bufferCount];
                        buffersWithFixup = stackb;
                    }

                    ECBSharedPlaybackState playbackState = new ECBSharedPlaybackState
                    {
                        CreateEntityBatch = createEntitiesBatch,
                        BuffersWithFixUp = buffersWithFixup,
                        LastBuffer = 0,
                    };

                    using (ECBChainPriorityQueue chainQueue = new ECBChainPriorityQueue(chainStates, Allocator.Temp))
                    {
                        ECBChainHeapElement currentElem = chainQueue.Pop();


                        while (currentElem.ChainIndex != -1)
                        {
                            ECBChainHeapElement nextElem = chainQueue.Peek();


                            PlaybackChain(mgr, managedListPointer, ref archetypeChanges, ref playbackState, (ECBChainPlaybackState*)chainStates.GetUnsafePtr(),
                                currentElem.ChainIndex, nextElem.ChainIndex, !m_Data->m_DidPlayback,
                                m_Data->m_PlaybackPolicy);

                            if (chainStates[currentElem.ChainIndex].Chunk == null)
                            {
                                chainQueue.Pop(); // ignore return value; we already have it as nextElem
                            }
                            else
                            {
                                currentElem.SortKey = chainStates[currentElem.ChainIndex].NextSortKey;
                                chainQueue.ReplaceTop(currentElem);
                            }
                            currentElem = nextElem;
                        }
                    }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    if (bufferCount != playbackState.LastBuffer)
                        Assert.IsTrue(false, "bufferCount (" + bufferCount + ") != playbackState.LastBuffer (" + playbackState.LastBuffer + ")");
#endif
                    for (int i = 0; i < playbackState.LastBuffer; i++)
                    {
                        ECBSharedPlaybackState.BufferWithFixUp* fixup = playbackState.BuffersWithFixUp + i;
                        EntityBufferCommand* cmd = fixup->cmd;
                        var entity = SelectEntity(cmd->Header.Entity, playbackState);
                        if (mgr->Exists(entity) && mgr->HasComponent(entity, TypeManager.GetType(cmd->ComponentTypeIndex)))
                            FixupBufferContents(mgr, cmd, entity, playbackState);
                    }

                    if (playbackStateSize > kMaxStatesOnStack)
                    {
                        Memory.Unmanaged.Free(createEntitiesBatch, Allocator.Temp);
                        Memory.Unmanaged.Free(buffersWithFixup, Allocator.Temp);
                    }
                }

                ProcessTrackedChanges(mgr, managedListPointer , ref archetypeChanges);
            }


            m_Data->m_DidPlayback = true;
            k_ProfileEcbPlayback.End();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckEntityNotNull(Entity entity)
        {
            if (entity == Entity.Null)
                throw new InvalidOperationException("Invalid Entity.Null passed.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckEntityBatchNotNull(Entity* entity)
        {
            if (entity == null)
                throw new InvalidOperationException(
                    "playbackState.CreateEntityBatch passed to SelectEntity is null.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckEntityVersionValid(Entity entity)
        {
            if (entity.Version <= 0)
                throw new InvalidOperationException("Invalid Entity version");
        }

        private static unsafe Entity SelectEntity(Entity cmdEntity, ECBSharedPlaybackState playbackState)
        {
            CheckEntityNotNull(cmdEntity);
            if (cmdEntity.Index < 0)
            {
                int index = -cmdEntity.Index - 1;
                CheckEntityBatchNotNull(playbackState.CreateEntityBatch);
                Entity e = *(playbackState.CreateEntityBatch + index);
                CheckEntityVersionValid(e);
                return e;
            }
            return cmdEntity;
        }

        private static void FixupComponentData(byte* data, int typeIndex, ECBSharedPlaybackState playbackState)
        {
            FixupComponentData(data, 1, typeIndex, playbackState);
        }

        private static void FixupComponentData(byte* data, int count, int typeIndex, ECBSharedPlaybackState playbackState)
        {
            ref readonly var componentTypeInfo = ref TypeManager.GetTypeInfo(typeIndex);

            var offsets = TypeManager.GetEntityOffsets(componentTypeInfo);
            var offsetCount = componentTypeInfo.EntityOffsetCount;
            for (var componentCount = 0; componentCount < count; componentCount++, data += componentTypeInfo.ElementSize)
            {
                for (int i = 0; i < offsetCount; i++)
                {
                    // Need fix ups
                    Entity* e = (Entity*)(data + offsets[i].Offset);
                    if (e->Index < 0)
                    {
                        var index = -e->Index - 1;
                        Entity real = *(playbackState.CreateEntityBatch + index);
                        *e = real;
                    }
                }
            }
        }


#if !NET_DOTS
        class FixupManagedComponent : Unity.Properties.PropertyVisitor, Properties.Adapters.IVisit<Entity>
        {
            [ThreadStatic]
            public static FixupManagedComponent _CachedVisitor;

            ECBSharedPlaybackState PlaybackState;
            public FixupManagedComponent()
            {
                AddAdapter(this);
            }

            public static void FixUpComponent(object obj, in ECBSharedPlaybackState state)
            {
                var visitor = FixupManagedComponent._CachedVisitor;
                if (FixupManagedComponent._CachedVisitor == null)
                    FixupManagedComponent._CachedVisitor = visitor = new FixupManagedComponent();

                visitor.PlaybackState = state;
                Unity.Properties.PropertyContainer.Visit(ref obj, visitor);
            }

            Unity.Properties.VisitStatus Properties.Adapters.IVisit<Entity>.Visit<TContainer>(Unity.Properties.Property<TContainer, Entity> property, ref TContainer container, ref Entity value)
            {
                if (value.Index < 0)
                {
                    var index = -value.Index - 1;
                    Entity real = *(PlaybackState.CreateEntityBatch + index);
                    value = real;
                }

                return Unity.Properties.VisitStatus.Stop;
            }
        }
#endif

        static void SetCommandDataWithFixup(
            EntityComponentStore* mgr, EntityComponentCommand* cmd, Entity entity,
            ECBSharedPlaybackState playbackState)
        {
            byte* data = (byte*)mgr->GetComponentDataRawRW(entity, cmd->ComponentTypeIndex);
            UnsafeUtility.MemCpy(data, cmd + 1, cmd->ComponentSize);
            FixupComponentData(data, cmd->ComponentTypeIndex,
                playbackState);
        }

        private static unsafe void AddToPostPlaybackFixup(EntityBufferCommand* cmd, ref ECBSharedPlaybackState playbackState)
        {
            var entity = SelectEntity(cmd->Header.Entity, playbackState);
            ECBSharedPlaybackState.BufferWithFixUp* toFixup =
                playbackState.BuffersWithFixUp + playbackState.LastBuffer++;
            toFixup->cmd = cmd;
        }

        static void FixupBufferContents(
            EntityDataAccess* mgr, EntityBufferCommand* cmd, Entity entity,
            ECBSharedPlaybackState playbackState)
        {
            BufferHeader* bufferHeader = (BufferHeader*)mgr->EntityComponentStore->GetComponentDataWithTypeRW(entity, cmd->ComponentTypeIndex, mgr->EntityComponentStore->GlobalSystemVersion);
            FixupComponentData(BufferHeader.GetElementPointer(bufferHeader), bufferHeader->Length,
                cmd->ComponentTypeIndex, playbackState);
        }

        static void PlaybackChain(
            EntityDataAccess* mgr,
            UnsafeList* managedReferenceIndexRemovalCount,
            ref EntityComponentStore.ArchetypeChanges archetypeChanges,
            ref ECBSharedPlaybackState playbackState,
            ECBChainPlaybackState* chainStates,
            int currentChain,
            int nextChain,
            bool isFirstPlayback,
            PlaybackPolicy playbackPolicy)
        {
            var chunk = chainStates[currentChain].Chunk;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (chunk == null)
                Assert.IsTrue(false, "chainStates[" + currentChain + "].Chunk is null.");
#endif
            var off = chainStates[currentChain].Offset;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (off < 0 || off >= chunk->Used)
                Assert.IsTrue(false, "chainStates[" + currentChain + "].Offset is invalid: " + off + ". Should be between 0 and " + chunk->Used);
#endif

            if (chainStates[currentChain].CanBurstPlayback)
            {
                // Bursting PlaybackChain
                PlaybackChainChunk(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges, ref playbackState,
                    chainStates, currentChain, nextChain, isFirstPlayback, playbackPolicy);
            }
            else
            {
                // Non-Bursted PlaybackChain
                _PlaybackChainChunk(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges, ref playbackState,
                    chainStates, currentChain, nextChain, isFirstPlayback, playbackPolicy);
            }
        }

        [BurstMonoInteropMethod]
        static void _PlaybackChainChunk(EntityDataAccess* mgr,
            UnsafeList* managedReferenceIndexRemovalCount,
            ref EntityComponentStore.ArchetypeChanges archetypeChanges,
            ref ECBSharedPlaybackState playbackState,
            ECBChainPlaybackState* chainStates,
            int currentChain,
            int nextChain,
            bool isFirstPlayback,
            PlaybackPolicy playbackPolicy)
        {
            int nextChainSortKey = (nextChain != -1) ? chainStates[nextChain].NextSortKey : -1;
            var chunk = chainStates[currentChain].Chunk;
            var off = chainStates[currentChain].Offset;

            while (chunk != null)
            {
                var buf = (byte*)chunk + sizeof(ECBChunk);
                while (off < chunk->Used)
                {
                    var header = (BasicCommand*)(buf + off);
                    if (nextChain != -1 && header->SortKey > nextChainSortKey)
                    {
                        // early out because a different chain needs to playback
                        var state = chainStates[currentChain];
                        state.Chunk = chunk;
                        state.Offset = off;
                        state.NextSortKey = header->SortKey;
                        chainStates[currentChain] = state;
                        return;
                    }

                    AssertSinglePlayback((ECBCommand)header->CommandType, isFirstPlayback);


                    var foundCommand = PlaybackUnmanagedCommandInternal(mgr, header, ref playbackState, playbackPolicy,
                        managedReferenceIndexRemovalCount, ref archetypeChanges);

                    // foundCommand will be false if either:
                    // 1) We are inside of Burst and therefore need to call the non-Burst function pointer
                    // 2) It's a managed command and we are not inside of Burst
                    if (!foundCommand)
                    {
                        var didExecuteManagedPlayack = false;
                        PlaybackManagedCommand(mgr, header, ref playbackState, playbackPolicy, managedReferenceIndexRemovalCount, ref archetypeChanges, ref didExecuteManagedPlayack);
                        if (!didExecuteManagedPlayack)
                            throw new System.InvalidOperationException("PlaybackManagedCommand can't be used from a bursted command buffer playback");
                    }

                    off += header->TotalSize;
                }
                // Reached the end of a chunk; advance to the next one
                chunk = chunk->Next;
                off = 0;
            }

            // Reached the end of the chain; update its playback state to make sure it's ignored
            // for the remainder of playback.
            {
                var state = chainStates[currentChain];
                state.Chunk = null;
                state.Offset = 0;
                state.NextSortKey = Int32.MinValue;
                chainStates[currentChain] = state;
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckBufferExistsOnEntity(EntityComponentStore* mgr, Entity entity, EntityComponentCommand* cmd)
        {
            if (!mgr->HasComponent(entity, cmd->ComponentTypeIndex))
                throw new System.InvalidOperationException("Buffer does not exist on entity, cannot append element.");
        }

        internal static bool PlaybackUnmanagedCommandInternal(EntityDataAccess* mgr, BasicCommand* header, ref ECBSharedPlaybackState playbackState, PlaybackPolicy playbackPolicy, UnsafeList* managedReferenceIndexRemovalCount, ref EntityComponentStore.ArchetypeChanges archetypeChanges)
        {
            switch ((ECBCommand)header->CommandType)
            {
                case ECBCommand.DestroyEntity:
                {
                    var cmd = (EntityCommand*)header;
                    Entity entity = SelectEntity(cmd->Entity, playbackState);
                    mgr->EntityComponentStore->DestroyEntityWithValidation(entity);
                }
                    return true;

                case ECBCommand.RemoveComponent:
                {
                    var cmd = (EntityComponentCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    mgr->EntityComponentStore->RemoveComponentWithValidation(entity, ComponentType.FromTypeIndex(cmd->ComponentTypeIndex));
                }
                    return true;

                case ECBCommand.RemoveMultipleComponents:
                {
                    var cmd = (EntityMultipleComponentsCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    mgr->EntityComponentStore->RemoveMultipleComponentsWithValidation(entity, cmd->Types);
                }
                return true;

                case ECBCommand.CreateEntity:
                {
                    var cmd = (CreateCommand*)header;
                    EntityArchetype at = cmd->Archetype;

                    if (!at.Valid)
                    {
                        ComponentTypeInArchetype* typesInArchetype = stackalloc ComponentTypeInArchetype[1];

                        var cachedComponentCount = EntityDataAccess.FillSortedArchetypeArray(typesInArchetype, null, 0);

                        // Lookup existing archetype (cheap)
                        EntityArchetype entityArchetype;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        entityArchetype._DebugComponentStore = mgr->EntityComponentStore;
#endif

                        entityArchetype.Archetype = mgr->EntityComponentStore->GetExistingArchetype(typesInArchetype, cachedComponentCount);
                        if (entityArchetype.Archetype == null)
                        {
                            entityArchetype.Archetype =
                                mgr->EntityComponentStore->GetOrCreateArchetype(typesInArchetype, cachedComponentCount);
                        }

                        at = entityArchetype;
                    }

                    int index = -cmd->IdentityIndex - 1;

                    mgr->EntityComponentStore->CreateEntityWithValidation(at, playbackState.CreateEntityBatch + index, cmd->BatchCount);
                }
                    return true;

                case ECBCommand.InstantiateEntity:
                {
                    var cmd = (EntityCommand*)header;

                    var index = -cmd->IdentityIndex - 1;
                    Entity srcEntity = SelectEntity(cmd->Entity, playbackState);
                    mgr->EntityComponentStore->InstantiateWithValidation(srcEntity, playbackState.CreateEntityBatch + index,
                        cmd->BatchCount);
                }
                    return true;

                case ECBCommand.AddComponent:
                {
                    var cmd = (EntityComponentCommand*)header;
                    var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    mgr->EntityComponentStore->AddComponentWithValidation(entity, componentType);
                    if (cmd->ComponentSize != 0)
                        mgr->EntityComponentStore->SetComponentDataRawEntityHasComponent(entity, cmd->ComponentTypeIndex, cmd + 1,
                            cmd->ComponentSize);
                }
                    return true;

                case ECBCommand.AddMultipleComponents:
                {
                    var cmd = (EntityMultipleComponentsCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    mgr->EntityComponentStore->AddMultipleComponentsWithValidation(entity, cmd->Types);
                }
                    return true;

                case ECBCommand.AddComponentWithEntityFixUp:
                {
                    var cmd = (EntityComponentCommand*)header;
                    var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    mgr->EntityComponentStore->AddComponentWithValidation(entity, componentType);
                    SetCommandDataWithFixup(mgr->EntityComponentStore, cmd, entity, playbackState);
                }
                    return true;

                case ECBCommand.SetComponent:
                {
                    var cmd = (EntityComponentCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    mgr->EntityComponentStore->SetComponentDataRawEntityHasComponent(entity, cmd->ComponentTypeIndex, cmd + 1,
                        cmd->ComponentSize);
                }
                    return true;

                case ECBCommand.SetComponentWithEntityFixUp:
                {
                    var cmd = (EntityComponentCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    SetCommandDataWithFixup(mgr->EntityComponentStore, cmd, entity, playbackState);
                }
                    return true;

                case ECBCommand.AddBuffer:
                {
                    var cmd = (EntityBufferCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    mgr->EntityComponentStore->AddComponentWithValidation(entity, ComponentType.FromTypeIndex(cmd->ComponentTypeIndex));

                    if (playbackPolicy == PlaybackPolicy.SinglePlayback)
                        mgr->EntityComponentStore->SetBufferRawWithValidation(entity, cmd->ComponentTypeIndex,
                            &cmd->BufferNode.TempBuffer,
                            cmd->ComponentSize);
                    else
                    {
                        // copy the buffer to ensure that no two entities point to the same buffer from the ECB
                        // either in the same world or in different worlds
                        var buffer = CloneBuffer(&cmd->BufferNode.TempBuffer, cmd->ComponentTypeIndex);
                        mgr->EntityComponentStore->SetBufferRawWithValidation(entity, cmd->ComponentTypeIndex, &buffer,
                            cmd->ComponentSize);
                    }
                }
                    return true;
                case ECBCommand.AddBufferWithEntityFixUp:
                {
                    var cmd = (EntityBufferCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    mgr->EntityComponentStore->AddComponentWithValidation(entity, ComponentType.FromTypeIndex(cmd->ComponentTypeIndex));
                    mgr->EntityComponentStore->SetBufferRawWithValidation(entity, cmd->ComponentTypeIndex, &cmd->BufferNode.TempBuffer, cmd->ComponentSize);
                    AddToPostPlaybackFixup(cmd, ref playbackState);
                }
                    return true;

                case ECBCommand.SetBuffer:
                {
                    var cmd = (EntityBufferCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    if (playbackPolicy == PlaybackPolicy.SinglePlayback)
                        mgr->EntityComponentStore->SetBufferRawWithValidation(entity, cmd->ComponentTypeIndex, &cmd->BufferNode.TempBuffer,
                            cmd->ComponentSize);
                    else
                    {
                        // copy the buffer to ensure that no two entities point to the same buffer from the ECB
                        // either in the same world or in different worlds
                        var buffer = CloneBuffer(&cmd->BufferNode.TempBuffer, cmd->ComponentTypeIndex);
                        mgr->EntityComponentStore->SetBufferRawWithValidation(entity, cmd->ComponentTypeIndex, &buffer, cmd->ComponentSize);
                    }
                }
                    return true;

                case ECBCommand.SetBufferWithEntityFixUp:
                {
                    var cmd = (EntityBufferCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    mgr->EntityComponentStore->SetBufferRawWithValidation(entity, cmd->ComponentTypeIndex, &cmd->BufferNode.TempBuffer, cmd->ComponentSize);
                    AddToPostPlaybackFixup(cmd, ref playbackState);
                }
                    return true;

                case ECBCommand.RemoveMultipleComponentsEntityQuery:
                {
                    ProcessTrackedChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);
                    StartTrackingChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);

                    var cmd = (EntityQueryMultipleComponentsCommand*)header;
                    AssertValidEntityQuery(&cmd->Header, mgr->EntityComponentStore);
                    mgr->RemoveMultipleComponentsDuringStructuralChange(cmd->Header.QueryData->MatchingArchetypes, cmd->Header.EntityQueryFilter,
                        cmd->Types);
                }
                return true;

                case ECBCommand.AppendToBuffer:
                {
                    var cmd = (EntityComponentCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);

                    CheckBufferExistsOnEntity(mgr->EntityComponentStore, entity, cmd);

                    BufferHeader* bufferHeader = (BufferHeader*)mgr->EntityComponentStore->GetComponentDataWithTypeRW(entity, cmd->ComponentTypeIndex, mgr->EntityComponentStore->GlobalSystemVersion);

                    ref readonly var typeInfo = ref TypeManager.GetTypeInfo(cmd->ComponentTypeIndex);
                    var alignment = typeInfo.AlignmentInBytes;
                    var elementSize = typeInfo.ElementSize;

                    BufferHeader.EnsureCapacity(bufferHeader, bufferHeader->Length + 1, elementSize, alignment, BufferHeader.TrashMode.RetainOldData, false, 0);

                    var offset = bufferHeader->Length * elementSize;
                    UnsafeUtility.MemCpy(BufferHeader.GetElementPointer(bufferHeader) + offset, cmd + 1, (long)elementSize);
                    bufferHeader->Length += 1;
                }
                    return true;
                case ECBCommand.AppendToBufferWithEntityFixUp:
                {
                    var cmd = (EntityComponentCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);

                    CheckBufferExistsOnEntity(mgr->EntityComponentStore, entity, cmd);

                    BufferHeader* bufferHeader = (BufferHeader*)mgr->EntityComponentStore->GetComponentDataWithTypeRW(entity, cmd->ComponentTypeIndex, mgr->EntityComponentStore->GlobalSystemVersion);

                    ref readonly var typeInfo = ref TypeManager.GetTypeInfo(cmd->ComponentTypeIndex);
                    var alignment = typeInfo.AlignmentInBytes;
                    var elementSize = typeInfo.ElementSize;

                    BufferHeader.EnsureCapacity(bufferHeader, bufferHeader->Length + 1, elementSize, alignment, BufferHeader.TrashMode.RetainOldData, false, 0);

                    var offset = bufferHeader->Length * elementSize;
                    UnsafeUtility.MemCpy(BufferHeader.GetElementPointer(bufferHeader) + offset, cmd + 1, (long)elementSize);
                    bufferHeader->Length += 1;
                    FixupComponentData(BufferHeader.GetElementPointer(bufferHeader) + offset, typeInfo.TypeIndex, playbackState);
                }
                    return true;
                case ECBCommand.AddComponentEntityQuery:
                {
                    ProcessTrackedChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);
                    StartTrackingChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);

                    var cmd = (EntityQueryComponentCommand*)header;
                    AssertValidEntityQuery(&cmd->Header, mgr->EntityComponentStore);
                    var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                    mgr->AddComponentDuringStructuralChange(cmd->Header.QueryData->MatchingArchetypes, cmd->Header.EntityQueryFilter,
                        componentType);
                }
                return true;

                case ECBCommand.AddMultipleComponentsEntityQuery:
                {
                    ProcessTrackedChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);
                    StartTrackingChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);

                    var cmd = (EntityQueryMultipleComponentsCommand*)header;
                    AssertValidEntityQuery(&cmd->Header, mgr->EntityComponentStore);
                    mgr->AddComponentsDuringStructuralChange(cmd->Header.QueryData->MatchingArchetypes, cmd->Header.EntityQueryFilter,
                        cmd->Types);
                }
                return true;

                case ECBCommand.RemoveComponentEntityQuery:
                {
                    ProcessTrackedChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);
                    StartTrackingChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);

                    var cmd = (EntityQueryComponentCommand*)header;
                    AssertValidEntityQuery(&cmd->Header, mgr->EntityComponentStore);
                    var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                    mgr->RemoveComponentDuringStructuralChange(cmd->Header.QueryData->MatchingArchetypes, cmd->Header.EntityQueryFilter,
                        componentType);
                }
                    return true;

                case ECBCommand.DestroyEntitiesInEntityQuery:
                {
                    ProcessTrackedChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);
                    StartTrackingChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);

                    var cmd = (EntityQueryCommand*)header;
                    AssertValidEntityQuery(cmd, mgr->EntityComponentStore);
                    mgr->DestroyEntityDuringStructuralChange(cmd->QueryData->MatchingArchetypes, cmd->EntityQueryFilter);
                }
                    return true;

                case ECBCommand.AddComponentForMultipleEntities:
                {
                    var cmd = (MultipleEntitiesComponentCommand*)header;
                    var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                    var entities = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(cmd->Header.Entities.Ptr,
                        cmd->Header.EntitiesCount, cmd->Header.Allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref entities, AtomicSafetyHandle.Create());
#endif

                    mgr->AddComponentDuringStructuralChange(entities, componentType);
                    if (cmd->ComponentSize > 0)
                    {
                        for (int i = 0; i < entities.Length; i++)
                        {
                            mgr->EntityComponentStore->SetComponentDataRawEntityHasComponent(entities[i],
                                cmd->ComponentTypeIndex, cmd + 1, cmd->ComponentSize);
                        }
                    }

                    if (playbackPolicy == PlaybackPolicy.SinglePlayback)
                        entities.Dispose();
                }
                    return true;

                case ECBCommand.RemoveComponentForMultipleEntities:
                {
                    var cmd = (MultipleEntitiesComponentCommand*)header;
                    var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                    var entities = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(cmd->Header.Entities.Ptr,
                        cmd->Header.EntitiesCount, cmd->Header.Allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref entities, AtomicSafetyHandle.Create());
#endif

                    mgr->RemoveComponentDuringStructuralChange(entities, componentType);

                    if (playbackPolicy == PlaybackPolicy.SinglePlayback)
                        entities.Dispose();
                }
                    return true;

                case ECBCommand.AddMultipleComponentsForMultipleEntities:
                {
                    var cmd = (MultipleEntitiesAndComponentsCommand*)header;
                    var entities = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(cmd->Header.Entities.Ptr,
                        cmd->Header.EntitiesCount, cmd->Header.Allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref entities, AtomicSafetyHandle.Create());
#endif

                    mgr->AddMultipleComponentsDuringStructuralChange(entities, cmd->Types);

                    if (playbackPolicy == PlaybackPolicy.SinglePlayback)
                        entities.Dispose();
                }
                    return true;

                case ECBCommand.RemoveMultipleComponentsForMultipleEntities:
                {
                    var cmd = (MultipleEntitiesAndComponentsCommand*)header;
                    var entities = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(cmd->Header.Entities.Ptr,
                        cmd->Header.EntitiesCount, cmd->Header.Allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref entities, AtomicSafetyHandle.Create());
#endif

                    mgr->RemoveMultipleComponentsDuringStructuralChange(entities, cmd->Types);

                    if (playbackPolicy == PlaybackPolicy.SinglePlayback)
                        entities.Dispose();
                }
                    return true;

                case ECBCommand.DestroyMultipleEntities:
                {
                    var cmd = (MultipleEntitiesCommand*)header;
                    var entities = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(cmd->Entities.Ptr,
                        cmd->EntitiesCount, cmd->Allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref entities, AtomicSafetyHandle.Create());
#endif

                    mgr->DestroyEntityDuringStructuralChange(entities);

                    if (playbackPolicy == PlaybackPolicy.SinglePlayback)
                        entities.Dispose();
                }
                    return true;

            }

            return false;
        }

        [BurstDiscard]
        static void PlaybackManagedCommand(EntityDataAccess* mgr, BasicCommand* header, ref ECBSharedPlaybackState playbackState, PlaybackPolicy playbackPolicy, UnsafeList* managedReferenceIndexRemovalCount, ref EntityComponentStore.ArchetypeChanges archetypeChanges, ref bool didExecuteMethod)
        {
            didExecuteMethod = true;
            switch ((ECBCommand)header->CommandType)
            {
                case ECBCommand.AddManagedComponentData:
                {
                    var cmd = (EntityManagedComponentCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);

                    var addedManaged = mgr->AddComponentDuringStructuralChange(entity, ComponentType.FromTypeIndex(cmd->ComponentTypeIndex));
                    if (addedManaged)
                    {
                        ProcessTrackedChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);
                        StartTrackingChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);
                    }

                    var box = cmd->GetBoxedObject();
#if !NET_DOTS
                    if (box != null && TypeManager.HasEntityReferences(cmd->ComponentTypeIndex))
                        FixupManagedComponent.FixUpComponent(box, playbackState);
#endif

                    mgr->SetComponentObject(entity, ComponentType.FromTypeIndex(cmd->ComponentTypeIndex), box, mgr->ManagedComponentStore);
                }
                break;

                case ECBCommand.AddSharedComponentData:
                {
                    var cmd = (EntitySharedComponentCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    var addedShared = mgr->AddSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(entity, cmd->ComponentTypeIndex, cmd->HashCode,
                        cmd->GetBoxedObject(), managedReferenceIndexRemovalCount);
                    if (addedShared)
                    {
                        ProcessTrackedChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);
                        StartTrackingChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);
                    }
                }
                break;

                case ECBCommand.SetManagedComponentData:
                {
                    var cmd = (EntityManagedComponentCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    if (!mgr->EntityComponentStore->ManagedChangesTracker.Empty)
                    {
                        ProcessTrackedChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);
                        StartTrackingChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);
                    }

                    var box = cmd->GetBoxedObject();
#if !NET_DOTS
                    if (box != null && TypeManager.HasEntityReferences(cmd->ComponentTypeIndex))
                        FixupManagedComponent.FixUpComponent(box, playbackState);
#endif

                    mgr->SetComponentObject(entity, ComponentType.FromTypeIndex(cmd->ComponentTypeIndex), cmd->GetBoxedObject(), mgr->ManagedComponentStore);
                }
                break;

                case ECBCommand.SetSharedComponentData:
                {
                    var cmd = (EntitySharedComponentCommand*)header;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    mgr->SetSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(entity, cmd->ComponentTypeIndex, cmd->HashCode,
                        cmd->GetBoxedObject(), managedReferenceIndexRemovalCount);
                }
                break;

                case ECBCommand.AddSharedComponentEntityQuery:
                {
                    ProcessTrackedChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);
                    StartTrackingChanges(mgr, managedReferenceIndexRemovalCount, ref archetypeChanges);

                    var cmd = (EntityQuerySharedComponentCommand*)header;
                    AssertValidEntityQuery(&cmd->Header, mgr->EntityComponentStore);
                    mgr->AddSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(cmd->Header.QueryData->MatchingArchetypes,
                        cmd->Header.EntityQueryFilter, cmd->ComponentTypeIndex, cmd->HashCode,
                        cmd->GetBoxedObject(), managedReferenceIndexRemovalCount);
                }
                break;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                default:
                {
                    throw new InvalidOperationException("Invalid command not recognized for EntityCommandBuffer.");
                }
#endif
            }
        }

        static void StartTrackingChanges(EntityDataAccess* mgr, UnsafeList* managedReferenceIndexRemovalCount, ref EntityComponentStore.ArchetypeChanges archetypeChanges)
        {
            if (!mgr->IsInExclusiveTransaction)
                mgr->BeforeStructuralChange();

            archetypeChanges = mgr->EntityComponentStore->BeginArchetypeChangeTracking();
        }

        static void ProcessTrackedChanges(EntityDataAccess* mgr, UnsafeList* managedReferenceIndexRemovalCount, ref EntityComponentStore.ArchetypeChanges archetypeChanges)
        {
            mgr->PlaybackManagedChanges();
            var count = managedReferenceIndexRemovalCount->Length;
            ECBInterop.RemoveManagedReferences(mgr, (int*)managedReferenceIndexRemovalCount->Ptr, count );

            mgr->EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, mgr->EntityQueryManager);
            mgr->EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();

            managedReferenceIndexRemovalCount->Clear();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void AssertValidEntityQuery(EntityQueryCommand* cmd, EntityComponentStore* store)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            EntityComponentStore.AssertValidEntityQuery(cmd->Store, store);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void AssertSinglePlayback(ECBCommand commandType, bool isFirstPlayback)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (isFirstPlayback)
                return;

            switch (commandType)
            {
                case ECBCommand.AddComponentWithEntityFixUp:
                case ECBCommand.SetComponentWithEntityFixUp:
                case ECBCommand.SetBufferWithEntityFixUp:
                    throw new InvalidOperationException("EntityCommandBuffer commands which set components with entity references cannot be played more than once.");
                default:
                    return;
            }
#endif
        }

        static BufferHeader CloneBuffer(BufferHeader* srcBuffer, int componentTypeIndex)
        {
            BufferHeader clone = new BufferHeader();
            BufferHeader.Initialize(&clone, 0);

            var alignment = 8; // TODO: Need a way to compute proper alignment for arbitrary non-generic types in TypeManager
            ref readonly var elementSize = ref TypeManager.GetTypeInfo(componentTypeIndex).ElementSize;
            BufferHeader.Assign(&clone, BufferHeader.GetElementPointer(srcBuffer), srcBuffer->Length, elementSize, alignment, false, 0);
            return clone;
        }

        public ParallelWriter AsParallelWriter()
        {
            EntityCommandBuffer.ParallelWriter parallelWriter;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety0);
            parallelWriter.m_Safety0 = m_Safety0;
            AtomicSafetyHandle.UseSecondaryVersion(ref parallelWriter.m_Safety0);
            parallelWriter.m_BufferSafety = m_BufferSafety;
            parallelWriter.m_ArrayInvalidationSafety = m_ArrayInvalidationSafety;
            parallelWriter.m_SafetyReadOnlyCount = 0;
            parallelWriter.m_SafetyReadWriteCount = 3;

            if (m_Data->m_Allocator == Allocator.Temp)
            {
                throw new InvalidOperationException("EntityCommandBuffer.Concurrent can not use Allocator.Temp; use Allocator.TempJob instead");
            }
#endif
            parallelWriter.m_Data = m_Data;
            parallelWriter.m_ThreadIndex = -1;

            if (parallelWriter.m_Data != null)
            {
                parallelWriter.m_Data->InitConcurrentAccess();
            }

            return parallelWriter;
        }

        /// <summary>
        /// Allows concurrent (deterministic) command buffer recording.
        /// </summary>
        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        [StructLayout(LayoutKind.Sequential)]
        unsafe public struct ParallelWriter
        {
            [NativeDisableUnsafePtrRestriction] internal EntityCommandBufferData* m_Data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety0;
            internal AtomicSafetyHandle m_BufferSafety;
            internal AtomicSafetyHandle m_ArrayInvalidationSafety;
            internal int m_SafetyReadOnlyCount;
            internal int m_SafetyReadWriteCount;
#endif

            // NOTE: Until we have a way to safely batch, let's keep it off
            private const bool kBatchableCommand = false;

            //internal ref int m_EntityIndex;
            [NativeSetThreadIndex]
            internal int m_ThreadIndex;

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private void CheckWriteAccess()
            {
                if (m_Data == null)
                    throw new NullReferenceException("The EntityCommandBuffer has not been initialized!");
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety0);
#endif
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private void CheckUsedInJob()
            {
                if (m_ThreadIndex == -1)
                    throw new InvalidOperationException("EntityCommandBuffer.Concurrent must only be used in a Job");
            }

            private EntityCommandBufferChain* ThreadChain
            {
                get
                {
                    CheckUsedInJob();
                    return &m_Data->m_ThreadedChains[m_ThreadIndex];
                }
            }

            /// <summary>
            /// Create an entity with specified archetype.</summary>
            /// <remarks>Returns the new Entity.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to the concurrent command buffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. You can calculate a
            /// similar index in an <see cref="IJobChunk"/> by adding the current entity index within a chunk to the
            /// <see cref="IJobChunk.Execute(ArchetypeChunk, int, int)"/> method's `firstEntityIndex` argument.</param>
            /// <param name="archetype">The archetype of the new entity.</param>
            public Entity CreateEntity(int sortKey, EntityArchetype archetype)
            {
                archetype.CheckValidEntityArchetype();
                return _CreateEntity(sortKey, archetype);
            }

            /// <summary>
            /// Create an entity with no components.</summary>
            /// <remarks>Returns the new Entity.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to the concurrent command buffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. You can calculate a
            /// similar index in an <see cref="IJobChunk"/> by adding the current entity index within a chunk to the
            /// <see cref="IJobChunk.Execute(ArchetypeChunk, int, int)"/> method's `firstEntityIndex` argument.</param>
            public Entity CreateEntity(int sortKey)
            {
                EntityArchetype archetype = new EntityArchetype();
                return _CreateEntity(sortKey, archetype);
            }

            private Entity _CreateEntity(int sortKey, EntityArchetype archetype)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                // NOTE: Contention could be a performance problem especially on ARM
                // architecture. Maybe reserve a few indices for each job would be a better
                // approach or hijack the Version field of an Entity and store sortKey
                int index = Interlocked.Decrement(ref m_Data->m_Entity.Index);
                m_Data->AddCreateCommand(chain, sortKey, ECBCommand.CreateEntity,  index, archetype, kBatchableCommand);
                return new Entity {Index = index};
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void CheckNotNull(Entity e)
            {
                if (e == Entity.Null)
                    throw new ArgumentNullException(nameof(e));
            }

            public Entity Instantiate(int sortKey, Entity e)
            {
                CheckNotNull(e);

                CheckWriteAccess();
                var chain = ThreadChain;
                int index = Interlocked.Decrement(ref m_Data->m_Entity.Index);
                m_Data->AddEntityCommand(chain, sortKey, ECBCommand.InstantiateEntity, index, e, kBatchableCommand);
                return new Entity {Index = index};
            }

            public void DestroyEntity(int sortKey, Entity e)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityCommand(chain, sortKey, ECBCommand.DestroyEntity, 0, e, false);
            }

            public void AddComponent<T>(int sortKey, Entity e, T component) where T : struct, IComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentCommand(chain, sortKey, ECBCommand.AddComponent, e, component);
            }

            public void AddComponent<T>(int sortKey, Entity e) where T : struct, IComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentTypeCommand(chain, sortKey, ECBCommand.AddComponent, e, ComponentType.ReadWrite<T>());
            }

            public void AddComponent(int sortKey, Entity e, ComponentType componentType)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentTypeCommand(chain, sortKey, ECBCommand.AddComponent, e, componentType);
            }

            /// <summary> Records a command to add one or more components to an entity. </summary>
            /// <remarks>
            /// </remarks>
            /// <param name="e"> The entity to get additional components. </param>
            /// <param name="types"> The types of components to add. </param>
            public void AddComponent(int sortKey, Entity e, ComponentTypes types)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentTypesCommand(chain, sortKey,ECBCommand.AddMultipleComponents, e, types);
            }

            public DynamicBuffer<T> AddBuffer<T>(int sortKey, Entity e) where T : struct, IBufferElementData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                return m_Data->CreateBufferCommand<T>(ECBCommand.AddBuffer, chain, sortKey, e, m_BufferSafety, m_ArrayInvalidationSafety);
#else
                return m_Data->CreateBufferCommand<T>(ECBCommand.AddBuffer, chain, sortKey, e);
#endif
            }

            public DynamicBuffer<T> SetBuffer<T>(int sortKey, Entity e) where T : struct, IBufferElementData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                return m_Data->CreateBufferCommand<T>(ECBCommand.SetBuffer, chain, sortKey, e, m_BufferSafety, m_ArrayInvalidationSafety);
#else
                return m_Data->CreateBufferCommand<T>(ECBCommand.SetBuffer, chain, sortKey, e);
#endif
            }

            /// <summary>
            /// Appends a single element to the end of a dynamic buffer component.</summary>
            /// <remarks>
            /// At <see cref="Playback(EntityManager)"/>, this command throws an InvalidOperationException if the entity doesn't
            /// have a <see cref="DynamicBuffer{T}"/> component storing elements of type T.
            /// </remarks>
            /// <param name="sortKey">A unique index for each set of commands added to the concurrent command buffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. You can calculate a
            /// similar index in an <see cref="IJobChunk"/> by adding the current entity index within a chunk to the
            /// <see cref="IJobChunk.Execute(ArchetypeChunk, int, int)"/> method's `firstEntityIndex` argument.</param>
            /// <param name="e">The entity to which the dynamic buffer belongs.</param>
            /// <param name="element">The new element to add to the <see cref="DynamicBuffer{T}"/> component.</param>
            /// <typeparam name="T">The <see cref="IBufferElementData"/> type stored by the <see cref="DynamicBuffer{T}"/>.</typeparam>
            /// <exception cref="InvalidOperationException">Thrown if the entity does not have a <see cref="DynamicBuffer{T}"/>
            /// component storing elements of type T at the time the entity command buffer executes this append-to-buffer command.</exception>
            public void AppendToBuffer<T>(int sortKey, Entity e, T element) where T : struct, IBufferElementData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AppendToBufferCommand<T>(chain, sortKey, e, element);
            }

            public void SetComponent<T>(int sortKey, Entity e, T component) where T : struct, IComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentCommand(chain, sortKey, ECBCommand.SetComponent, e, component);
            }

            public void RemoveComponent<T>(int sortKey, Entity e)
            {
                RemoveComponent(sortKey, e, ComponentType.ReadWrite<T>());
            }

            public void RemoveComponent(int sortKey, Entity e, ComponentType componentType)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentTypeCommand(chain, sortKey, ECBCommand.RemoveComponent, e, componentType);
            }

            /// <summary> Records a command to remove one or more components from an entity. </summary>
            /// <remarks>
            /// </remarks>
            /// <param name="e"> The entity to have the components removed. </param>
            /// <param name="types"> The types of components to remove. </param>
            public void RemoveComponent(int sortKey, Entity e, ComponentTypes types)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentTypesCommand(chain, sortKey,ECBCommand.RemoveMultipleComponents, e, types);
            }

            public void AddSharedComponent<T>(int sortKey, Entity e, T component) where T : struct, ISharedComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                chain->m_CanBurstPlayback = false;
                int hashCode;
                if (IsDefaultObject(ref component, out hashCode))
                    m_Data->AddEntitySharedComponentCommand<T>(chain, sortKey, ECBCommand.AddSharedComponentData, e, hashCode, null);
                else
                    m_Data->AddEntitySharedComponentCommand<T>(chain, sortKey, ECBCommand.AddSharedComponentData, e, hashCode, component);
            }

            public void SetSharedComponent<T>(int sortKey, Entity e, T component) where T : struct, ISharedComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                chain->m_CanBurstPlayback = false;
                int hashCode;
                if (IsDefaultObject(ref component, out hashCode))
                    m_Data->AddEntitySharedComponentCommand<T>(chain, sortKey, ECBCommand.SetSharedComponentData, e, hashCode, null);
                else
                    m_Data->AddEntitySharedComponentCommand<T>(chain, sortKey, ECBCommand.SetSharedComponentData, e, hashCode, component);
            }
        }
    }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
    public static unsafe class EntityCommandBufferManagedComponentExtensions
    {
        public static void AddComponent<T>(this EntityCommandBuffer ecb, Entity e, T component) where T : class, IComponentData
        {
            ecb.EnforceSingleThreadOwnership();
            ecb.AssertDidNotPlayback();
            ecb.m_Data->m_MainThreadChain.m_CanBurstPlayback = false;
            AddEntityComponentCommandFromMainThread(ecb.m_Data, ecb.MainThreadSortKey, ECBCommand.AddManagedComponentData, e, component);
        }

        public static void AddComponent<T>(this EntityCommandBuffer ecb, Entity e) where T : class, IComponentData
        {
            ecb.EnforceSingleThreadOwnership();
            ecb.AssertDidNotPlayback();
            ecb.m_Data->m_MainThreadChain.m_CanBurstPlayback = false;
            ecb.m_Data->AddEntityComponentTypeCommand(&ecb.m_Data->m_MainThreadChain, ecb.MainThreadSortKey, ECBCommand.AddManagedComponentData, e, ComponentType.ReadWrite<T>());
        }

        public static void SetComponent<T>(this EntityCommandBuffer ecb, Entity e, T component) where T : class, IComponentData
        {
            ecb.EnforceSingleThreadOwnership();
            ecb.AssertDidNotPlayback();
            ecb.m_Data->m_MainThreadChain.m_CanBurstPlayback = false;
            AddEntityComponentCommandFromMainThread(ecb.m_Data, ecb.MainThreadSortKey, ECBCommand.SetManagedComponentData, e, component);
        }


        internal static void AddEntityComponentCommandFromMainThread<T>(EntityCommandBufferData* ecbd, int sortKey, ECBCommand op, Entity e, T component) where T : class, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var sizeNeeded = EntityCommandBufferData.Align(sizeof(EntityManagedComponentCommand), 8);

            var chain = &ecbd->m_MainThreadChain;
            ecbd->ResetCommandBatching(chain);
            var data = (EntityManagedComponentCommand*)ecbd->Reserve(chain, sortKey, sizeNeeded);

            data->Header.Header.CommandType = (int)op;
            data->Header.Header.TotalSize = sizeNeeded;
            data->Header.Header.SortKey = chain->m_LastSortKey;
            data->Header.Entity = e;
            data->ComponentTypeIndex = typeIndex;

            if (component != null)
            {
                data->GCNode.BoxedObject = GCHandle.Alloc(component);
                // We need to store all GCHandles on a cleanup list so we can dispose them later, regardless of if playback occurs or not.
                data->GCNode.Prev = chain->m_Cleanup->CleanupList;
                chain->m_Cleanup->CleanupList = &(data->GCNode);
            }
            else
            {
                data->GCNode.BoxedObject = new GCHandle();
            }
        }
    }
#endif
}
