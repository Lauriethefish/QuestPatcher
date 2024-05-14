// // See https://aka.ms/new-console-template for more information
// Console.WriteLine("Hello, World!");
using System;
using System.IO.Compression;
using ACVPatcher;
using CommandLine;
using CommandLine.Text;
using QuestPatcher.Axml;
using QuestPatcher.Zip;

class Options
{
    [Option('c', "class", Required = false, HelpText = "Path to the DEX file.")]
    public IEnumerable<string>? ClassPath { get; set; }

    [Option('p', "permission", Required = false, HelpText = "The permission.")]
    public IEnumerable<string>? Permission { get; set; }

    [Option('i', "instrumentation", Required = false, HelpText = "The instrumentation.")]
    public string? Instrumentation { get; set; }

    [Option('r', "receiver", Required = false, HelpText = "The receiver.")]
    public IEnumerable<string>? Receivers { get; set; }

    [Option('a', "apkpath", Required = true, HelpText = "Path to the APK file to patch.")]
    public required string ApkPath { get; set; }
}

class Program
{
    static async Task Main(string[] args)
    {
        var patchingManager = new PatchingManager();
        // args = "-p android.permission.WRITE_EXTERNAL_STORAGE -i tool.acv.AcvInstrumentation -r tool.acv.AcvReceiver:tool.acv.calculate -a /Users/ap/projects/dblt/apks/debloatapp/base.apk".Split(' ');
        Console.WriteLine(string.Join(" ", args));
        var options = await Parser.Default.ParseArguments<Options>(args).WithParsedAsync(async options =>
        {
            Console.WriteLine($"Class Path: {(options.ClassPath != null ? string.Join(", ", options.ClassPath) : string.Empty)}");
            Console.WriteLine($"Permission: {(options.Permission != null ? string.Join(", ", options.Permission) : string.Empty)}");
            Console.WriteLine($"Instrumentation: {options.Instrumentation}");
            Console.WriteLine($"Receivers: {(options.Receivers != null ? string.Join(", ", options.Receivers) : string.Empty)}");
            Console.WriteLine($"APK Path: {options.ApkPath}");
            await patchingManager.Run(options);
        });
    }
}

internal class PatchingManager
{
    public PatchingManager()
    {
    }

    public async Task Run(Options options)
    {
        using var apkStream = File.Open(options.ApkPath, FileMode.Open);
        var apk = await ApkZip.OpenAsync(apkStream);

        if (options.ClassPath != null)
        {
            foreach (var classPath in options.ClassPath)
            {
                await AddClassToApk(apk, classPath);
            }
        }
        if (options.Permission != null || options.Instrumentation != null || options.Receivers != null)
        {
            await PatchManifest(apk, options);
        }

        await apk.DisposeAsync();
    }

    private async Task PatchManifest(ApkZip apk, Options options)
    {
        bool modified = false;
        using var ms = new MemoryStream();
        using (var stream = await apk.OpenReaderAsync("AndroidManifest.xml"))
        {
            await stream.CopyToAsync(ms);
        }

        ms.Position = 0;
        var manifest = AxmlLoader.LoadDocument(ms);
        string package = AxmlManager.GetPackage(manifest);
        if (options.Permission != null)
        {
            var existingPermissions = AxmlManager.GetExistingChildren(manifest, "uses-permission");
            foreach (var permission in options.Permission)
            {
                if (existingPermissions.Contains(permission)) { continue; } // Do not add existing permissions
                AddPermissionToManifest(manifest, permission);
                modified = true;
            }
        }
        if (options.Instrumentation != null)
        {
            AddInstrumentationToManifest(manifest, options.Instrumentation, package);
            modified = true;
        }
        if (options.Receivers != null)
        {
            var appElement = manifest.Children.Single(child => child.Name == "application");
            // var existingReceivers = AxmlManager.GetExistingChildren(appElement, "receiver");
            var existingReceiverElements = GetExistingReceiverElements(appElement);
            var receiverActions = ParseReceiverActions(options.Receivers);
            foreach (var receiverAction in receiverActions)
            {
                var receiverName = receiverAction.Key;
                List<string> actions = receiverAction.Value;
                var receiverElement = existingReceiverElements.ContainsKey(receiverName) ? existingReceiverElements[receiverName] : null;
                if (receiverElement == null)
                {
                    receiverElement = AddReceiverToManifest(appElement, receiverName);
                    existingReceiverElements[receiverName] = receiverElement;
                    modified = true;
                }

                var receiverIntentFilter = receiverElement.Children.Any(ch => ch.Name == "intent-filter") ? receiverElement.Children.Single(ch => ch.Name == "intent-filter") : null;
                if (receiverIntentFilter == null)
                {
                    receiverIntentFilter = new AxmlElement("intent-filter");
                    receiverElement.Children.Add(receiverIntentFilter);
                }
                List<string?> existingActions = receiverIntentFilter.Children
                    .Where(ch => ch.Name == "action").Select(ch => ch.Attributes.Single(attr => attr.Name == "name")?.Value as string)
                    .ToList();
                var newActions = actions.Where(action => action != null).Except(existingActions).ToList();

                foreach (var action in newActions)
                {
                    AddIntentAction(receiverIntentFilter, action!);
                    modified = true;
                }
            }
        }
        if (modified)
        {
            ms.SetLength(0);
            ms.Position = 0;
            AxmlSaver.SaveDocument(ms, manifest);
            ms.Position = 0;
            await apk.AddFileAsync("AndroidManifest.xml", ms, CompressionLevel.Optimal);
        }
    }

    private Dictionary<string, AxmlElement> GetExistingReceiverElements(AxmlElement appElement)
    {
        var receiverElements = new Dictionary<string, AxmlElement>();

        foreach (var receiver in appElement.Children)
        {
            if (receiver.Name != "receiver") { continue; }

            var receiverName = receiver.Attributes.Single(attr => attr.Name == "name")?.Value as string;
            if (receiverName != null)
            {
                receiverElements[receiverName] = receiver;
            }
        }
        return receiverElements;
    }

    private Dictionary<string, List<string>> GetExistingReceiverActions(AxmlElement appElement)
    {
        var receiverActions = new Dictionary<string, List<string>>();

        foreach (var receiver in appElement.Children)
        {
            if (receiver.Name != "receiver") { continue; }

            var receiverName = receiver.Attributes.Single(attr => attr.Name == "name")?.Value as string;
            if (receiverName == null) { continue; }

            var intentFilters = receiver.Children.Where(child => child.Name == "intent-filter").ToList();
            if (intentFilters.Count == 0) { continue; }

            var actions = new List<string>();
            foreach (var intentFilter in intentFilters)
            {
                foreach (var action in intentFilter.Children)
                {
                    if (action.Name != "action") { continue; }

                    var actionName = action.Attributes.Single(attr => attr.Name == "name")?.Value as string;
                    if (actionName != null)
                    {
                        actions.Add(actionName);
                    }
                }
            }
            receiverActions[receiverName] = actions;
        }
        return receiverActions;
    }

    private Dictionary<string, List<string>> ParseReceiverActions(IEnumerable<string> receiverArgs)
    {
        var receiverActions = new Dictionary<string, List<string>>();

        foreach (string receiverArg in receiverArgs)
        {
            // Split the receiverArg string into two separate variables
            var receiverParts = receiverArg.Split(':');
            var receiverClassName = receiverParts[0];
            var actionName = receiverParts[1];

            if (receiverActions.ContainsKey(receiverClassName))
            {
                receiverActions[receiverClassName].Add(actionName);
            }
            else
            {
                receiverActions[receiverClassName] = new List<string> { actionName };
            }
        }

        return receiverActions;
    }

    private AxmlElement AddReceiverToManifest(AxmlElement appElement, string receiver)
    {
        AxmlElement receiverElement = new("receiver");
        AxmlManager.AddNameAttribute(receiverElement, receiver);
        AxmlManager.AddExportedAttribute(receiverElement, true);
        AxmlManager.AddEnabledAttribute(receiverElement, true);
        appElement.Children.Add(receiverElement);
        return receiverElement;
    }

    private void AddIntentAction(AxmlElement intentFilterElement, string actionName)
    {
        AxmlElement actionElement = new("action");
        AxmlManager.AddNameAttribute(actionElement, actionName);
        intentFilterElement.Children.Add(actionElement);
    }

    private void AddInstrumentationToManifest(AxmlElement manifest, string instrumentationName, string package)
    {
        AxmlElement instrElement = new("instrumentation");
        AxmlManager.AddNameAttribute(instrElement, instrumentationName);
        AxmlManager.AddTargetPackageAttribute(instrElement, package);
        manifest.Children.Add(instrElement);
    }

    private static void AddPermissionToManifest(AxmlElement manifest, string permission)
    {
        AxmlElement permElement = new("uses-permission");
        AxmlManager.AddNameAttribute(permElement, permission);
        manifest.Children.Add(permElement);
    }

    private async Task AddClassToApk(ApkZip apk, string classPath)
    {
        var fileName = Path.GetFileName(classPath);
        using var dexStream = File.OpenRead(classPath);
        await apk.AddFileAsync(fileName, dexStream, CompressionLevel.Optimal);
    }

}
