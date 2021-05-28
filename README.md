# QuestPatcher 2

QuestPatcher is a GUI based mod installer for any il2cpp unity app on the Oculus Quest that runs on Windows or Linux.
It was originally created for modding Gorilla Tag.

It uses Sc2ad's [QuestLoader](https://github.com/sc2ad/QuestLoader/) for loading mods and is based mostly on RedBrumbler's [CLI Tool](https://github.com/RedBrumbler/QuestAppPatcher).

This branch is the rewrite/remake of QuestPatcher.
## Rewrite Goals
- Fix many bugs in the old QuestPatcher.
- Improve and modernize UI.
- Improve ease of use and error messages
- Improve code quality.
- Separate core functionality from UI to allow another type of interface to be created in the future. A CLI, for example.
- Allow customisation of patching permissions. For instance, allow or disallow debugging and hand tracking permissions.
- Add support for 32 bit quest apps. (`armeabi-v7a`) 

## TODO
- Finish UI for viewing installed cosmetics.
- Add general import window with information about what kind of files QuestPatcher supports.
- Automatically download Java instead of prompting the user toc download it if they don't have it.
- Finish this TODO list because I've totally forgotten things.

Note that the rewrite is __incomplete__. It should only be used by developers for testing purposes, and it's in its early stages!

See `QuestPatcher.Core/Resources/qmod.schema.json` for the `mod.json` that QMODs must contain, alongside their mod files.
