# ACVPatcher

ACVPatcher is a CLI tool to patch AndroidManifest and DEX classes inside an APK without reassembling resources. ACVPatcher is a replacement of Apktool for the ACVTool purpuses to improve its repackaging success rate.


## Usage

ACVPatcher updates DEX classes and/or AndroidManifest inside the APK file. ACVPatcher may insert new permissions, a broadcast receiver, and instrumentation tag into AndroidManifest through corresponding options.

### Add permission to AndroidManifest

```shell
$ acvpatcher --permission android.permission.WRITE_EXTERNAL_STORAGE android.permission.READ_EXTERNAL_STORAGE
```

### Add receiver to AndroidManifest

This example will add the AcvReceiver receiver tag with two intent filters (`calculate` and `snap`)

```shell
$ acvpatcher --receiver tool.acv.AcvReceiver:tool.acv.calculate tool.acv.AcvReceiver:tool.acv.snap
```

### Rewrite DEX files

```shell
$ acvpatcher --class ./classes.dex ./classes2.dex
```

### AndroidManifest Patching

Here is an example of XML added to patched AndroidManifest

```xml
<manifest ...>
    ...
<application>
    ...
    <receiver
        android:name="tool.acv.AcvReceiver"
        android:enabled="true"
        android:exported="true">
        <intent-filter>
            <action android:name="tool.acv.snap" />
            <action android:name="tool.acv.calculate" />
        </intent-filter>
    </receiver>
</application>
    <instrumentation
        android:name="tool.acv.AcvInstrumentation"
        android:targetPackage="package.name" />
    <uses-permission
        android:name="android.permission.WRITE_EXTERNAL_STORAGE" />
</manifest>
```


# Acknowledgement

ACVPatcher is build on top of QuestPatcher modules developed by @Lauriethefish
