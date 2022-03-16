# Blob
Repository of additional unity BlobAsset

# Quality

![](https://github.com/WAYN-Games/Blob/blob/main/Tests/Report/badge_branchcoverage.png)  ![](https://github.com/WAYN-Games/Blob/blob/main/Tests/Report/badge_linecoverage.png)  

# Known issues
To work with the unity Entities 0.17 (or 0.50) package, some changes must be done to you local entity package. more details here : https://forum.unity.com/threads/blobarray-with-valuetype-generic-parameters-fails.1045639/

[![openupm](https://img.shields.io/npm/v/com.wayn-games.blob?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.wayn-games.blob/)

# Exemple :
```cs
	[Test]
    public void SimpleExempleTest()
    {
        using (BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp))
        {
            // Populating the map
            BlobHashMapBuilder<int, int> blobHashMapBuilder = new BlobHashMapBuilder<int, int>(blobBuilder);
            blobHashMapBuilder.Add(1, 2);
            blobHashMapBuilder.Add(1, 3);
            blobHashMapBuilder.Add(3, 4);
            blobHashMapBuilder.Add(5, 6);

            // Creating the blob reference
            BlobAssetReference<BlobMultiHashMap<int, int>> blobAssetReference = blobHashMapBuilder.CreateBlobAssetReference(Allocator.Temp);

            // reading the data
            ref var map = ref blobAssetReference.Value;
            NativeArray<int> valuesForKey1 = map.GetValuesForKey(1);    // The blobmap can contain multiple values for the same key
            Assert.AreEqual(2, valuesForKey1[0]);                       // Check that the first value for the key is the expected one
            Assert.AreEqual(3, valuesForKey1[1]);                       // Check that the second value for the key is the expected one
            Assert.AreEqual(4, map.ValueCount.Value);                   // Check that the blob asset contains the expected number of values
            Assert.IsTrue(map.ContainsKey(5));                          // Check that the blob asset contains at least one value for key 5
        }
    }
```
