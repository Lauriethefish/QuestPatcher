# ACVPatcher

ACVPatcher is a CLI tool to patch AndroidManifest and DEX classes inside an APK without reassembling resources. ACVPatcher is a replacement of Apktool for the ACVTool purpuses to improve its repackaging success rate.

## Usage

```shell
$ acvpatcher --class ./classes.dex --class ./classes2.dex --permission android.permission.WRITE_EXTERNAL_STORAGE --instrumentation tool.acv.AcvInstrumentation --receiver tool.acv.AcvReceiver:tool.acv.calculate --receiver tool.acv.AcvReceiver:tool.acv.calculate --receiver tool.acv.AcvReceiver:tool.acv.snap --receiver tool.acv.AcvReceiver:tool.acv.flush
```

ACVPatcher updates DEX classes and AndroidManifest inside the APK file. ACVPatcher may insert new permissions, a broadcast receiver, and instrumentation tag through corresponding options.


# Acknowledgement

ACVPatcher employes modules from QuestPatcher project developed by @Lauriethefish
