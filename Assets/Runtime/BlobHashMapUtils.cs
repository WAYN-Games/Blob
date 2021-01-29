using System;

internal struct BlobHashMapUtils
{

    public static int KeyHash<TKey>(TKey key)
        where TKey : struct, IEquatable<TKey>
    {
        int hashcode = key.GetHashCode();
        // The XOR bit shift operation is inspired by https://stackoverflow.com/questions/45125497/why-return-h-key-hashcode-h-16-other-than-key-hashcode
        // It's suposed to help spreading the hash code and avoid colision on 
        // key hashes taht don't change much on their lower bits but
        // influancing them with the higher bits of the hash.
        // That way it provides a better spread in hte buckets once the bucket mask is applied.
        return hashcode ^ (hashcode >> 16);
    }

    internal static (int bucketIndex, int keyHash) ComputeBucketIndex<TKey>(TKey key, int bucketCount)
        where TKey : struct, IEquatable<TKey>
    {
        // Apply the bucket mask to the key hash so that we are sure the result is no larger than the bucket size.
        // For exemple if we have a KeyHash of 19 and a bucket mask of 15 
        // (bucket mask is equals to number of buckets - 1, so in this case we have 16 buckets)  we will have :
        // 19 in binary is : 10011
        // 15 in binary is : 01111
        // The & operator result is 10011 & 01111 = 00011 = 3 
        // so the values for that key are store in the fourth bucket (bucket at index 3)
        int keyHash = KeyHash(key);
        int bucketIndex = keyHash & (bucketCount - 1);
        return (bucketIndex, keyHash);
    }
}
