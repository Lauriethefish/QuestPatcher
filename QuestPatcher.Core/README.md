# QuestPatcher.Core

This library project contains QuestPatcher functionality that does not depend on any kind of user interface.
The idea is that anybody could make another UI for QuestPatcher, e.g. a CLI.

Mod install, patching, and pretty much all code is separate from the actual user interface itself.

## How to Implement

- Inherit `QuestPatcher.Core.QuestPatcherService` and override the abstract methods.
- A custom `IUserPrompter` must be given to the constructor. This is used for pausing certain operations, like patching, at particular points. You can read the interface to decide what UI prompts you need.
- NOTE: You can easily just make these prompts do nothing by returning `true` to continue, or if there is no return type, overriding them with an empty method.