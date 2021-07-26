# Quatcher.Axml

This project is used for the parsing and writing of the AXML format used for the `AndroidManifest.xml` file inside the APK.

The AXML isn't completely parsed - styles are not currently supported.

This implementation was based mostly off of this [Java Library](https://github.com/Sable/axml).

### Why Quatcher doesn't use apktool
Quatcher used to use apktool, however it proved quite unreliable, and a lot of its cached local data got continually corrupted.

Fully decompiling the APK is also far more than Quatcher needs - we only need to add a couple of permissions to the manifest and change a few library files.
