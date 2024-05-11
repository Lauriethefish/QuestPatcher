// // See https://aka.ms/new-console-template for more information
// Console.WriteLine("Hello, World!");
using System;
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
    static void Main(string[] args)
    {
        var patchingManager = new PatchingManager();
        Console.WriteLine(string.Join(" ", args));
        var t = Parser.Default.ParseArguments<Options>(args)
        .WithParsed<Options>(options =>
        {

            // Process the arguments
            Console.WriteLine($"Class Path: {options.ClassPath}");
            Console.WriteLine($"Permission: {options.Permission}");
            Console.WriteLine($"Instrumentation: {options.Instrumentation}");
            Console.WriteLine("Receivers:");
            // foreach (string receiver in options.Receivers)
            // {
            //     Console.WriteLine(receiver);
            // }

            // Your code logic goes here...

            Console.WriteLine("Hello, World!");
            Task.Run(async () => await patchingManager.Run(options));
        })
        .WithNotParsed<Options>(errors =>
        {
            // Handle parsing errors
            Console.WriteLine("Failed to parse command line arguments.");
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
        using var ms = new MemoryStream();
        using (var stream = await apk.OpenReaderAsync("AndroidManifest.xml"))
        {
            await stream.CopyToAsync(ms);
        }

        ms.Position = 0;
        var manifest = AxmlLoader.LoadDocument(ms);
        if (options.Permission != null)
        {
            var existingPermissions = AxmlManager.GetExistingChildren(manifest, "uses-permission");
            foreach (var permission in options.Permission)
            {
                if (existingPermissions.Contains(permission)) { continue; } // Do not add existing permissions
                AddPermissionToManifest(manifest, permission);
            }
        }
        if (options.Instrumentation != null)
        {
            AddInstrumentationToManifest(manifest, options.Instrumentation);
        }
        if (options.Receivers != null)
        {
            var existingReceivers = AxmlManager.GetExistingChildren(manifest, "receiver");
            foreach (var receiver in options.Receivers)
            {
                if (existingReceivers.Contains(receiver)) { continue; } // Do not add existing receivers
                AddReceiverToManifest(manifest, receiver);
            }
        }
    }
    private static void AddPermissionToManifest(AxmlElement manifest, string permission)
    {
        AxmlElement permElement = new("uses-permission");
        AxmlManager.AddNameAttribute(permElement, permission);
        manifest.Children.Add(permElement);
    }

    private async Task AddClassToApk(ApkZip apk, string classPath)
    {
        using var dexStream = File.OpenRead(classPath);
        await apk.AddDexAsync(dexStream);
    }

    private void AddPermissionToManifest(AxmlElement manifest, string permission)
    {
        throw new NotImplementedException();
    }
}

// }
// using System;

// class Program
// {
//     static void Main(string[] args)
//     {
//         // Parse the command line arguments
//         if (args.Length == 0)
//         {
//             Console.WriteLine("No arguments provided.");
//             return;
//         }

//         string classPath = null;
//         string permission = null;
//         string instrumentation = null;
//         string[] receivers = null;

//         for (int i = 0; i < args.Length; i++)
//         {
//             if (args[i] == "--class" && i + 1 < args.Length)
//             {
//                 classPath = args[i + 1];
//                 i++;
//             }
//             else if (args[i] == "--permission" && i + 1 < args.Length)
//             {
//                 permission = args[i + 1];
//                 i++;
//             }
//             else if (args[i] == "--instrumentation" && i + 1 < args.Length)
//             {
//                 instrumentation = args[i + 1];
//                 i++;
//             }
//             else if (args[i] == "--receiver" && i + 1 < args.Length)
//             {
//                 if (receivers == null)
//                 {
//                     receivers = new string[] { args[i + 1] };
//                 }
//                 else
//                 {
//                     Array.Resize(ref receivers, receivers.Length + 1);
//                     receivers[receivers.Length - 1] = args[i + 1];
//                 }
//                 i++;
//             }
//         }

//         // Process the arguments
//         Console.WriteLine($"Class Path: {classPath}");
//         Console.WriteLine($"Permission: {permission}");
//         Console.WriteLine($"Instrumentation: {instrumentation}");
//         Console.WriteLine("Receivers:");
//         if (receivers != null)
//         {
//             foreach (string receiver in receivers)
//             {
//                 Console.WriteLine(receiver);
//             }
//         }

//         // Your code logic goes here...

//         Console.WriteLine("Hello, World!");
//     }
// }