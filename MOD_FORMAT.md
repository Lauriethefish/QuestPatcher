# Mod Format

QuestPatcher uses a simple format for loading mod files.
QuestPatcher mods have the extension `.QMOD` - they\'re ZIP files containing a `mod.json` file.

An example `mod.json` is below:

```json
{
    "_QPVersion": "0.1.0",
    "name": "ExampleMod",
    "id": "example-mod",
    "author": "The Author (TM)",
    "version": "1.0.0",
    "gameId": "com.AnotherAxiom.GorillaTag",
    "gameVersion": "1.0.1",
    "type": "Gameplay",
    "modFiles": [
        "libexample-mod.so"
    ],
    "libraryFiles": [
        "libbeatsaber-hook_1_0_12.so"
    ]
}
```

The `gameVersion` is shown to the user, but can\'t be easily checked when the mod is installed.
The `type` can be anything you want.
The mod will not install on an app other than `gameId` (the installer will throw an error)

Mod files (optional) should be in the ZIP file, and are copied to `sdcard/Android/data/<game-id>/files/mods`
Library files (optional) should also be in the ZIP file, and are copied to `sdcard/Android/data/<game-id>/files/libs`

Mod files are uninstalled whenever the mod is uninstalled, but library files are only uninstalled when no mod requires that library file any more.