﻿using System;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

internal struct BlobHashMapBucket<TKey, TValue>
    where TKey : struct, IEquatable<TKey>
    where TValue : struct

{
    #region Public Fields

    public BlobArray<TKey> KeysArray; // Contains all unique keys present in that bucket
    public BlobArray<KeyIndex> KeyIndexes; // Contains informations about the values for that key.
    public BlobArray<TValue> ValuesArray;

    #endregion Public Fields

    // Contains all values for all the keys of that bucket.

    #region Public Methods

    public bool ContainsKey(TKey key)
    {
        if (KeysArray.Length == 0) return false; // Empty bucket
        if (KeysArray.Length == 1 && key.Equals(KeysArray[0])) return true;
        return FindKeyIndex(key) >= 0;
    }

    /// <summary>
    /// Retreive the values for a given Key.
    /// If there is no values for that key, retrun a default NativeArray.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="allocator"></param>
    /// <returns></returns>
    public NativeArray<TValue> GetValuesForKey(TKey key)
    {
        int keyIndex = FindKeyIndex(key);
        if (keyIndex < 0)
        {
            return default;
        }
        int elementCount = KeyIndexes[keyIndex].ElementCount;
        int firstIndex = KeyIndexes[keyIndex].FirstIndex;

        return GetReadOnlySubArray(firstIndex, elementCount);
    }

    #endregion Public Methods

    #region Internal Methods

    /// <summary>
    /// Find the index of the TKey in the KeysArray and KeyIndexes.
    /// If the key is not found return -1.
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    internal int FindKeyIndex(TKey key)
    {
        // if there is only one key in the bucket and it's the key we look for return 0 (only exisitng index)
        if (KeysArray.Length == 1 && key.Equals(KeysArray[0])) return 0;
        return BinarySearch(key);
    }

    #endregion Internal Methods

    #region Private Methods

    /// <summary>
    /// Performs a binary search on the Keys to find the index of the requested key.
    /// Return -1 if it's not found.
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    private int BinarySearch(TKey key)
    {
        int hash = BlobHashMapUtils.KeyHash(key);
        int startIndex = 0;
        int endIndex = KeyIndexes.Length - 1;
        while (startIndex <= endIndex)
        {
            int lookupIndex = (int)math.floor((startIndex + endIndex) / 2);

            if (hash > KeyIndexes[lookupIndex].KeyHash)
            {
                startIndex = lookupIndex + 1;
                continue;
            }
            if (hash < KeyIndexes[lookupIndex].KeyHash)
            {
                endIndex = lookupIndex - 1;
                continue;
            }

            // we found the correct hash

            // If it's trueliy the same key, return the index
            if (key.Equals(KeysArray[lookupIndex])) return lookupIndex;

            // If not start linear search backward to find the correct key
            int originalLookUpIndex = lookupIndex;
            while (hash == KeyIndexes[lookupIndex].KeyHash && !key.Equals(KeysArray[lookupIndex]) && lookupIndex > 0)
            {
                lookupIndex--;
                if (key.Equals(KeysArray[lookupIndex]))
                {
                    return lookupIndex; // even if same hash there is still a small chance to have a different key
                }
            }

            // If we still did not find it start linear search forward to find the correct key
            lookupIndex = originalLookUpIndex;
            while (hash == KeyIndexes[lookupIndex].KeyHash && !key.Equals(KeysArray[lookupIndex]) && lookupIndex < KeyIndexes.Length - 1)
            {
                lookupIndex++;
                if (key.Equals(KeysArray[lookupIndex]))
                {
                    return lookupIndex; // even if same hash there is still a small chance to have a different key
                }
            }

            return -1;
        }

        return -1;
    }

    private NativeArray<TValue> GetReadOnlySubArray(int start, int length)
    {
        unsafe
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle safety = AtomicSafetyHandle.GetTempMemoryHandle();
            AtomicSafetyHandle.CheckGetSecondaryDataPointerAndThrow(safety);
#endif
            NativeArray<TValue> result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<TValue>(((byte*)ValuesArray.GetUnsafePtr()) + ((long)UnsafeUtility.SizeOf<TValue>()) * start, length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(safety);
            AtomicSafetyHandle.UseSecondaryVersion(ref safety);
            AtomicSafetyHandle.SetAllowSecondaryVersionWriting(safety, false);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, safety);
#endif
            return result;
        }
    }

    #endregion Private Methods
}