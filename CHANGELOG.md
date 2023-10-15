# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).
## [1.0.0] - 2023-10-15

### Change 
* Update to entities 1.0.16
  * This is now a requirement for the package to work without having to manually and locally edit the entities package

  
## [0.5.0] - 2022-07-01

### Change 
* Update to entities 0.51

## [0.4.0] - 2022-03-16

### Change 
* Update to entities 0.50

### Fix
* Infinite loop in binary search is some cases

## [0.3.0] - 2021-06-28

### Update 
* Revmoved allocator parameter from GetValuesForKey(TKey key) : was defaulted to Temp and used only to allocate the zero size array, now allocator is always None.

### Fix
* Memory leak on GetValuesForKey(TKey key)

## [0.2.1] - 2021-06-11

### Add
* Added tests lost during package conversion

### Update
* Harmonisation of naming from WAYN Group to WAYN Games
* Respect package naming convention for package and assembly definitions

### Fix
* Missing com.unity.entities dependency

## [0.2.0] - 2021-06-06

### Update
* Convert to a package

## [0.1.0] - 2021-02-05

### This is the first release of *\<wayn.blob\>*.

*Short description of this release*
