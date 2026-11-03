using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace ImageScanner.Organizer.Plugins;

/// <summary>
/// Isolated load context for one plugin folder. Contract assemblies (and anything the app already
/// loaded) always resolve to the app's copy so plugin types are compatible with the host's interfaces.
/// </summary>
internal sealed class PluginLoadContext(string mainAssemblyPath) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(mainAssemblyPath), isCollectible: false) {
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase) {
        "ImageScanner.Core",
        "ImageScanner.Plugins.Abstractions",
        "LiteDB",
        "SixLabors.ImageSharp"
    };

    private readonly AssemblyDependencyResolver resolver = new(mainAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName) {
        if (assemblyName.Name != null && (SharedAssemblies.Contains(assemblyName.Name)
                                          || Default.Assemblies.Any(loaded => string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase)))) {
            return null;
        }

        string? path = resolver.ResolveAssemblyToPath(assemblyName);
        return path != null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) {
        string? path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}
