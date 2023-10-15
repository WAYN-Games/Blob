# Blob [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
Repository of additional unity BlobAsset

# Quality ![](https://github.com/WAYN-Games/Blob/blob/main/Documentation~/badge_linecoverage.png)  

![](https://github.com/WAYN-Games/Blob/blob/main/Documentation~/tests_coverage_report.png)  

# Documentation :


[![openupm](https://img.shields.io/npm/v/com.wayn-games.blob?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.wayn-games.blob/)

# Overview

This package provides an implmeentaion of a Blob Map that allows fast access to read only data from its key.

# Package contents
* N/A
# Installation instructions
* Import the package via the package manager (see :  [Package Manager installation instructions](https://docs.unity3d.com/Manual/upm-ui-actions.html)
  * Add package from git URL : Use the following URL https://github.com/WAYN-Games/Blob.git?path=/games.wayn.blob 
# Requirements
* com.unity.entities 1.0.16
* Unity 2022 LTS
# Limitations
* N/A
# Workflows
* see [Samples](#Samples)
# Advanced topics
* N/A
# Reference
* N/A
# Samples

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

# Tutorials
