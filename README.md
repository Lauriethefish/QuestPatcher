# QuestPatcher

QuestPatcher is a WIP GUI based mod installer for any il2cpp unity app on the Oculus Quest.
It was originally created for modding GorillaTag.

It should support Windows, Mac and Linux.

It uses Sc2ad's [QuestLoader](https://github.com/sc2ad/QuestLoader/) for loading mods and is based mostly on RedBrumbler's [CLI Tool](https://github.com/RedBrumbler/QuestAppPatcher).

See `resources/qmod.schema.json` for the `mod.json` that QMODs must contain, alongside their mod files.

**NOTE:** To mod other games, edit `appId.txt` in the installation directory (`C:\Program Files\QuestPatcher\` on Windows) with the package ID of the app on your Quest that you want to mod.

## TODO
- Add cyclic dependency detection.
- Add support for automatically updating a dependency when it is installed, but the installed version is wrong. This would only happen if all mods that rely on that dependency have version ranges that intersect the new version.
