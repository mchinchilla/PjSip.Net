using System.Runtime.InteropServices;
using PjSip.Net.Interop.Generated;

namespace PjSip.Net.Internal;

/// <summary>
/// Checks whether the native PJSIP library is available at runtime.
/// When native binaries are not present (e.g. in unit tests), all
/// native calls are skipped gracefully.
/// </summary>
internal static class NativeAvailability
{
    private static readonly Lazy<bool> _isAvailable = new(Probe);

    public static bool IsAvailable => _isAvailable.Value;

    private static bool Probe()
    {
        try
        {
            var interopAssembly = typeof(Endpoint).Assembly;

            // Try standard library resolution first (works with NuGet packages + deps.json).
            if (NativeLibrary.TryLoad("pjsua2", interopAssembly, null, out _))
                return true;

            // Fallback: probe the runtimes folder next to the Interop assembly.
            // NativeLibrary.TryLoad with assembly does NOT invoke DllImportResolvers,
            // so for ProjectReference builds (no deps.json native entry) we need to
            // replicate the same probe path that NativeLoader.ResolveDllImport uses.
            string baseDir = Path.GetDirectoryName(interopAssembly.Location)
                ?? AppContext.BaseDirectory;
            string fullPath = Path.Combine(baseDir, GetPlatformLibraryPath());

            return NativeLibrary.TryLoad(fullPath, out _);
        }
        catch
        {
            return false;
        }
    }

    private static string GetPlatformLibraryPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine("runtimes", "win-x64", "native", "pjsua2.dll");

        // Mac Catalyst: runs on macOS but uses the iOS TFM — check before OSX.
        if (OperatingSystem.IsMacCatalyst() || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "osx-arm64" : "osx-x64";
            return Path.Combine("runtimes", rid, "native", "libpjsua2.dylib");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Path.Combine("runtimes", "linux-x64", "native", "libpjsua2.so");

        // Best-effort for unknown platforms
        return "pjsua2";
    }
}
