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
    "isLibrary": false,
    "type": "Gameplay",
    "modFiles": [
        "libexample-mod.so"
    ],
    "libraryFiles": [
        "libbeatsaber-hook_1_0_12.so"
    ],
    "dependencies": [
        {
            "id": "my-dependency",
            "version": "^0.1.0",
            "downloadIfMissing": "https://somesite.com/my_dependency_0_1_0.qmod",
        }
    ]
}
```

The `version` must be semver, i.e. three version numbers like `0.6.8`. Versions like `0.84` or `0.1.0.0` are *not* allowed. Semver documentation can be found [here](https://semver.org/).

The `gameVersion` is shown to the user, but can\'t be easily checked when the mod is installed.

The `type` can be anything you want.

The mod will not install on an app other than `gameId` (the installer will throw an error)

Mod files (optional) should be in the ZIP file, and are copied to `sdcard/Android/data/<game-id>/files/mods`
Library files (optional) should also be in the ZIP file, and are copied to `sdcard/Android/data/<game-id>/files/libs`

Mod files are uninstalled whenever the mod is uninstalled, but library files are only uninstalled when no mod requires that library file any more.

## Dependency Format

- Dependencies are any mods that your mod requires in order to work/activate.
- Dependencies can be included as libraries inside your mod, or as QMODs downloaded from an external source.

It's easier to distribute small dependencies that have versioned SO files inside your mod, since multiple different versions can work fine.

Dependency format:
```json
"dependencies": [
    {
        "id": "my-dependency",
        "version": "^0.1.0",
        "downloadIfMissing": "https://somesite.com/my_dependency_0_1_0.qmod",
    }
]
```

`id`: The ID in the dependency's manifest. Must match with the given download, if there is one.
`version`: A version range in the below format:
- It can be two version numbers separated by a hypen. e.g. `0.8.4-0.9.0`. The smaller version number must come first.
- It can be `^` followed by the minimum version of the dependency. e.g. `^0.9.2` matches any version that it at least `0.9.2`.
- It can be a single supported version, e.g. `0.9.0`.
- It can be a wildcard, e.g. `0.9.*`.
- Or anything else supported by a semver range.

`downloadIfMissing` is optional, and is where QuestPatcher will attempt to download the mod if it isn't installed. If this isn't present, attempting to install the mod with the dependency will fail.


