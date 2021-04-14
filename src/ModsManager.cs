using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO.Compression;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Interactivity;
using System.Net;
using Serilog.Core;

namespace QuestPatcher {
    public class ModInstallException : Exception
    {
        public ModInstallException(string message) : base(message) { }
    }

    public class ModsManager
    {
        // Temporarily extract the ZIP file here so that we can use ADB push
        private string INSTALLED_MODS_PATH = "sdcard/QuestPatcher/{app-id}/installedMods/";
        private string EXTRACTED_MODS_PATH;
        private string DEPENDENCY_PATH;

        private MainWindow window;
        private Logger logger;
        private DebugBridge debugBridge;

        // Map of Mod ID -> Mod Manifest
        public Dictionary<string, ModManifest> InstalledMods { get; } = new Dictionary<string, ModManifest>();
        private Random random = new Random();

        public ModsManager(MainWindow window) {
            this.window = window;
            this.debugBridge = window.DebugBridge;
            this.logger = window.Logger;
            this.EXTRACTED_MODS_PATH = window.TEMP_PATH + "extractedMods/";
            this.DEPENDENCY_PATH = window.TEMP_PATH + "downloadedDepdendencies/";

            Directory.CreateDirectory(DEPENDENCY_PATH);
            Directory.CreateDirectory(EXTRACTED_MODS_PATH);
        }

        // Loads the copied manifests from the folder /sdcard/QuestPatcher/{app-id}/installedMods as installed mods.
        public async Task LoadModsFromQuest() {
            string modsNonSplit;
            try
            {
                await debugBridge.RunCommandAsync("shell mkdir -p \"" + INSTALLED_MODS_PATH + "\"");
                await debugBridge.RunCommandAsync("shell mkdir -p \"sdcard/Android/data/{app-id}/files/mods\"");
                await debugBridge.RunCommandAsync("shell mkdir -p \"sdcard/Android/data/{app-id}/files/libs\"");

                // List the manifests in the installed mods directory for this app
                modsNonSplit = await debugBridge.RunCommandAsync("shell ls -R \"" + INSTALLED_MODS_PATH + "\"");
            }
            catch (Exception ex)
            {
                logger.Fatal("An error occurred while loading mods from the Quest: " + ex.Message);
                logger.Verbose(ex.ToString());
                return;
            }

            // Remove unnecessary things that ADB adds
            string[] rawPaths = modsNonSplit.Split("\n");
            List<string> parsedPaths = new List<string>();
            for(int i = 0; i < rawPaths.Length; i++) {
                if(i == 0 || i == rawPaths.Length - 1) {continue;}

                parsedPaths.Add(INSTALLED_MODS_PATH + rawPaths[i].Replace("\r", ""));
            }

            foreach(string path in parsedPaths) {
                string contents = await debugBridge.RunCommandAsync("shell cat \"" + path + "\"");

                try
                {
                    ModManifest manifest = await ModManifest.Load(contents);
                    AddManifest(manifest);
                }
                catch (Exception ex)
                {
                    logger.Error("An error occured while loading " + Path.GetFileNameWithoutExtension(path) + " from the Quest: " + ex.Message);
                    logger.Verbose(ex.ToString());
                }
            }
        }

        // Attempts to download the specified DeependencyInfo
        // Will throw an exception if it's not installed and there's no downloadIfMissing, or if the installed version is not within the stated version range.
        // Also does sanity checking of the downloaded version to make sure that the downloaded version/ID is correct.
        private async Task InstallDependency(DependencyInfo dependency, List<string> installedInChain)
        {
            int existingIndex = installedInChain.FindIndex((string downloadedDep) => downloadedDep == dependency.Id);
            if(existingIndex != -1)
            {
                string dependMessage = "";
                for(int i = existingIndex; i < installedInChain.Count; i++)
                {
                    dependMessage += $"{installedInChain[i]} depends on ";
                }
                dependMessage += dependency.Id;

                throw new ModInstallException($"Recursive dependency detected: {dependMessage}");
            }

            ModManifest? existing = null;
            bool isAlreadyInstalled = InstalledMods.TryGetValue(dependency.Id, out existing);
            bool hasDownloadLink = dependency.DownloadIfMissing != null;

            // If the dependency is already installed, and the installed version is within the version range, return
            if(isAlreadyInstalled)
            {
                if (dependency.ParsedVersion.IsSatisfied(existing.ParsedVersion))
                {
                    logger.Debug($"Dependency {dependency.Version} is already installed and within the version range");
                    return;
                }   else if(hasDownloadLink)
                {
                    logger.Warning($"Dependency with ID {dependency.Id} is already installed but with an incorrect version ({existing.Version} does not intersect {dependency.Version}). QuestPatcher will attempt to upgrade the dependency");
                }   else    {
                    throw new ModInstallException($"Dependency with ID { dependency.Id } is already installed but with an incorrect version({existing.Version} does not intersect {dependency.Version}). Upgrading was not possible as there was no download link provided");
                }
            }   else if(!hasDownloadLink)
            {
                throw new ModInstallException($"Dependency {dependency.Id} is not installed, and the mod depending on it does not specify a download path if missing");
            }

            WebClient webClient = new WebClient();

            logger.Information("Downloading dependency " + dependency.Id);

            string downloadedPath = DEPENDENCY_PATH + dependency.Id + ".qmod";
            await webClient.DownloadFileTaskAsync(dependency.DownloadIfMissing, downloadedPath);

            // We clone the list to avoid dependencies down another "branch" of the tree adding to the list, and causing a recursive dependency when the isn't one
            List<string> newInstalledChain = new List<string>(installedInChain);
            // Add us to the installed chain so that any dependencies further down in the same tree that attempt to install us will trigger the recursive dependency error.
            newInstalledChain.Add(dependency.Id);

            await InstallMod(downloadedPath, newInstalledChain);

            File.Delete(downloadedPath); // Remove the temporarily downloaded mod

            ModManifest dependencyManifest = InstalledMods[dependency.Id];
                
            // Sanity checks that the download link actually pointed to the right mod
            if(!dependency.ParsedVersion.IsSatisfied(dependencyManifest.ParsedVersion))
            {
                await UninstallMod(dependencyManifest);
                throw new ModInstallException("Downloaded dependency " + dependency.Id + " was not within the version stated in the mod's manifest");
            }

            if(dependency.Id != dependencyManifest.Id)
            {
                await UninstallMod(dependencyManifest);
                throw new ModInstallException("Downloaded dependency had ID " + dependencyManifest.Id + ", whereas the dependency stated ID " + dependency.Id);
            }
        }

        // Checks to see if the currently installed manifest can be safely uninstalled, and the mod upgraded to the new version
        // Dependencies are checked that verify that newVersion is within all of the version ranges
        // An exception is thrown if this is not possible
        private async Task AttemptVersionUpgrade(ModManifest currentlyInstalled, ModManifest newVersion)
        {
            logger.Information($"Attempting to upgrade {currentlyInstalled.Id} v{currentlyInstalled.Version} to {newVersion.Id} v{newVersion.Version}");
            string id = currentlyInstalled.Id;

            bool didError = false;
            foreach(ModManifest mod in InstalledMods.Values)
            {

                foreach(DependencyInfo dependency in mod.Dependencies) {
                    if(dependency.Id == id && !dependency.ParsedVersion.IsSatisfied(newVersion.Version))
                    {
                        logger.Error($"Dependency of mod {mod.Id} requires version range {dependency.Version} of {id}, however the version of {id} being upgraded to ({newVersion.Version}) does not match this range");
                        didError = true;
                    }
                }
            }

            if(didError)
            {
                throw new ModInstallException($"Could not upgrade existing installation of mod {id}, see log for details");
            }
            else
            {
                logger.Information("Uninstalling old version");
                await UninstallMod(currentlyInstalled);
            }
        }

        // Installs the .qmod at path.
        // This works by first extracting the mod - we're forced to do this since ADB can't push files from the ZIP directly
        // Also does sanity checks - making sure that the mod is for the correct game, that it isn't already installed, etc.
        // Will attempt to download dependencies of the mod.
        // If any part of this fails, then it'll throw an exception.
        public async Task InstallMod(string path, List<string> installedInChain = null) {
            if(installedInChain == null)
            {
                installedInChain = new List<string>();
            }

            string extractPath = EXTRACTED_MODS_PATH + Path.GetFileNameWithoutExtension(path) + "/";
            Directory.CreateDirectory(extractPath);

            try
            {
                logger.Information("Extracting mod . . .");
                Directory.CreateDirectory(extractPath);
                ZipFile.ExtractToDirectory(path, extractPath);

                // Read the manifest
                logger.Information("Loading manifest . . .");
                string manifestText = await File.ReadAllTextAsync(extractPath + "mod.json");
                ModManifest manifest = await ModManifest.Load(manifestText);

                if (manifest.PackageId != window.Config.AppId)
                {
                    throw new ModInstallException("This mod is not intended for the selected game!");
                }

                // If the mod is already installed, attempt a version upgrade, making sure that any mods that depend on this one's dependency ranges intersect the version of the new mod
                ModManifest? installedVersion;
                InstalledMods.TryGetValue(manifest.Id, out installedVersion);
                if (installedVersion != null)
                {
                    if(installedVersion.Version == manifest.Version)
                    {
                        throw new ModInstallException($"Mod {manifest.Id} is already installed, and the version is the same");
                    }
                    await AttemptVersionUpgrade(installedVersion, manifest);
                }

                foreach(DependencyInfo dependency in manifest.Dependencies)
                {
                    await InstallDependency(dependency, installedInChain);
                }

                // Copy all of the SO files
                foreach (string libraryPath in manifest.LibraryFiles)
                {
                    logger.Information("Copying library file " + libraryPath);
                    await debugBridge.RunCommandAsync("push \"" + extractPath + libraryPath + "\" \"sdcard/Android/data/{app-id}/files/libs/" + Path.GetFileName(libraryPath) + "\"");
                }

                foreach (string modFilePath in manifest.ModFiles)
                {
                    logger.Information("Copying mod file " + modFilePath);
                    await debugBridge.RunCommandAsync("push \"" + extractPath + modFilePath + "\" \"sdcard/Android/data/{app-id}/files/mods/" + Path.GetFileName(modFilePath) + "\"");
                }

                // Copy the stated file copies
                foreach (FileCopyInfo fileCopy in manifest.FileCopies)
                {
                    logger.Information($"Copying file {fileCopy.Name} to {fileCopy.Destination}");
                    await debugBridge.RunCommandAsync("push \"" + extractPath + fileCopy.Name + "\" \"" + fileCopy.Destination + "\"");
                }

                // Store that the mod was successfully installed
                logger.Information("Copying manifest . . .");
                await debugBridge.RunCommandAsync("push \"" + extractPath + "mod.json\" \"" + INSTALLED_MODS_PATH + manifest.Id + ".json\"");

                AddManifest(manifest);
                logger.Information("Done!");
            }
            finally
            {
                Directory.Delete(extractPath, true);
            }
        }

        // Attempts to uninstall the mod/library with this manifest
        // modFiles will always be removed when a mod is uninstalled, however libraryFiles will only be removed if no other mods have a library with the same name
        // fileCopies are also removed when a mod is uninstalled.
        // Finally, this method will automatically remove any library mods that are not depended on by any other mod.
        public async Task UninstallMod(ModManifest manifest)
        {
            logger.Information("Uninstalling mod with ID " + manifest.Id + " . . .");
            window.InstalledModsPanel.Children.Remove(manifest.GuiElement);
            InstalledMods.Remove(manifest.Id);

            foreach (string modFilePath in manifest.ModFiles) {
                logger.Information("Removing mod file " + modFilePath);
                await debugBridge.RunCommandAsync("shell rm -f \"sdcard/Android/data/{app-id}/files/mods/" + Path.GetFileName(modFilePath) + "\""); // Remove each mod file
            }
            
            foreach (string libraryPath in manifest.LibraryFiles)
            {
                // Only remove libraries if they aren't used by another mod
                bool isUsedElsewhere = false;
                foreach(ModManifest otherManifest in InstalledMods.Values)
                {
                    if(otherManifest.LibraryFiles.Contains(libraryPath))
                    {
                        logger.Information("Other mod " + otherManifest.Id + " still needs library " + libraryPath + ", not removing");
                        isUsedElsewhere = true;
                        break;
                    }
                }

                if(!isUsedElsewhere)
                {
                    logger.Information("Removing library file " + libraryPath);
                    await debugBridge.RunCommandAsync("shell rm -f \"sdcard/Android/data/{app-id}/files/libs/" + Path.GetFileName(libraryPath) + "\"");
                }
            }

            foreach (FileCopyInfo fileCopy in manifest.FileCopies)
            {
                logger.Information("Removing copied file " + fileCopy.Destination);
                await debugBridge.RunCommandAsync("shell rm -f \"" + fileCopy.Destination + "\"");
            }

            logger.Information("Removing mod manifest . . .");
            await debugBridge.RunCommandAsync("shell rm -f \"" + INSTALLED_MODS_PATH + manifest.Id + ".json\""); // Remove the mod manifest

            if (!manifest.IsLibrary)
            {
                await RemoveUnusedLibraries();
            }
            logger.Information("Done!");
        }

        // Repeatedly iterates to remove any unused libraries - that aren't depended on by any mod.
        // Not very efficient but gets the job done
        private async Task RemoveUnusedLibraries()
        {
            logger.Information("Cleaning unused libraries . . .");

            int lastSize = -1;
            while (InstalledMods.Count != lastSize) // Keep attempting to remove libraries until none get removed this time
            {
                lastSize = InstalledMods.Count;

                List<ModManifest> unused = new List<ModManifest>();
                foreach (ModManifest manifest in InstalledMods.Values)
                {
                    if (!manifest.IsLibrary) { continue; } // Mods aren't uninstalled if unused

                    // Check if any other mods/libraries depend on this one
                    bool used = false;
                    foreach (ModManifest otherManifest in InstalledMods.Values)
                    {

                        if (otherManifest.DependsOn(manifest.Id))
                        {
                            used = true;
                            break;
                        }
                    }

                    if (!used)
                    {
                        unused.Add(manifest);
                    }
                }

                // Uninstall any unused libraries this iteration
                foreach (ModManifest manifest in unused)
                {
                    await UninstallMod(manifest);
                }
            }
        }

        // Creates the UI for displaying this ModManifest in the installed mods section, and adds it to the stack panel.
        // Kind of janky, but there isn't another good way to do this unless I set up MVVM which will take ages.
        private void AddManifest(ModManifest manifest) {
            Border border = new Border();
            border.Padding = new Thickness(5);
            border.BorderThickness = new Thickness(1);
            border.BorderBrush = Brushes.Gray;
            border.CornerRadius = new CornerRadius(3);

            StackPanel stackPanel = new StackPanel();
            border.Child = stackPanel;

            stackPanel.Orientation = Avalonia.Layout.Orientation.Vertical;
            
            TextBlock name = new TextBlock();
            name.Text = "Mod Name: " + manifest.Name;

            TextBlock author = new TextBlock();
            author.Text = "Mod Author: " + manifest.Author;
            
            TextBlock version = new TextBlock();
            version.Text = "Mod Version: " + manifest.Version;

            TextBlock gameVersion = new TextBlock();
            gameVersion.Text = "Intended for game version: " + manifest.PackageVersion;

            TextBlock a = new TextBlock();
            a.Text = "william gay";
            a.FontSize = 6;

            Button uninstall = new Button();
            if(manifest.IsLibrary)
            {
                // Libraries should not be uninstalled manually.
                // Instead, they are automatically uninstalled whenever there aren't any mods that use them installed.
                uninstall.Foreground = Brushes.Red;
                uninstall.Content = "Force Uninstall Library";
            }
            else
            {
                uninstall.Content = "Uninstall Mod";
            }

            stackPanel.Children.Add(name);
            stackPanel.Children.Add(version);
            stackPanel.Children.Add(gameVersion);
            stackPanel.Children.Add(author);
            stackPanel.Children.Add(uninstall);
            if(random.Next() % 30 == 0)
            {
                stackPanel.Children.Add(a);
            }

            manifest.GuiElement = border;

            window.InstalledModsPanel.Children.Add(border);
            InstalledMods[manifest.Id] = manifest;

            uninstall.Click += async delegate (object? sender, RoutedEventArgs args)
            {
                try
                {
                    if(manifest.IsLibrary)
                    {
                        logger.Warning("WARNING: Libraries should not be uninstalled manually. They are automatically uninstalled when no mods that use them are installed");
                    }

                    // Uninstall the mod from the Quest
                    await UninstallMod(manifest);

                    window.ModInstallErrorText.IsVisible = false;
                }
                catch (Exception ex)
                {
                    window.ModInstallErrorText.IsVisible = true;
                    window.ModInstallErrorText.Text = "Error while uninstalling mod: " + ex.Message;
                }
            };
        }
    }
}