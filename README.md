
# QuestPatcher

QuestPatcher is a GUI based mod installer for any il2cpp unity app on the Oculus Quest that runs on Windows, Linux or macOS.
It was originally created for modding Gorilla Tag.

It supports modding with the [QuestLoader](https://github.com/sc2ad/QuestLoader/) and [Scotland2](https://github.com/sc2ad/Scotland2) modloaders.
The QMOD format used by QuestPatcher is specified [here](https://github.com/Lauriethefish/QuestPatcher.QMod/tree/main/SPECIFICATION.md).

[Latest Stable Release](https://github.com/Lauriethefish/QuestPatcher/releases/latest) | [Latest Nightly Build](https://nightly.link/Lauriethefish/QuestPatcher/workflows/standalone/main) | [Latest aarch64 Release](https://github.com/Jaydenha09/QuestPatcher/releases/latest)


# How to build QuestPatcher for aacrh64 by yourself

Install [.NET 6.0.](https://learn.microsoft.com/en-us/dotnet/core/install/linux)
Clone the project: `git clone https://github.com/Lauriethefish/QuestPatcher.git`.
Publish the project: `dotnet publish ./QuestPatcher/QuestPatcher.csproj -r ubuntu-arm64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -c Release --self-contained`.
Find your executable in: ./QuestPatcher/bin/Release/net6.0/ubuntu-x64/publish/.

I haven't finish everything yet, so the path name is still ubuntu-x64
