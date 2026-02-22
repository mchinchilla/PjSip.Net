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

            Log($"Assembly.Location='{interopAssembly.Location}'");
            Log($"AppContext.BaseDirectory='{AppContext.BaseDirectory}'");
            Log($"IsMacCatalyst={OperatingSystem.IsMacCatalyst()}, IsIOS={OperatingSystem.IsIOS()}, IsOSX={RuntimeInformation.IsOSPlatform(OSPlatform.OSX)}");

            // Try standard library resolution first (works with NuGet packages + deps.json).
            if (NativeLibrary.TryLoad("pjsua2", interopAssembly, null, out _))
            {
                Log("Loaded via TryLoad('pjsua2', assembly)");
                return true;
            }
            Log("TryLoad('pjsua2', assembly) failed");

            // On mobile/Mac Catalyst, Assembly.Location is often empty and there is
            // no deps.json — try loading the bare library name directly.  The native
            // binary sits next to the managed assemblies in the app bundle.
            string libFileName = GetPlatformLibraryFileName();
            if (NativeLibrary.TryLoad(libFileName, out _))
            {
                Log($"Loaded via TryLoad('{libFileName}')");
                return true;
            }
            Log($"TryLoad('{libFileName}') failed");

            // Fallback: probe the runtimes folder next to the Interop assembly.
            string baseDir = Path.GetDirectoryName(interopAssembly.Location)
                ?? AppContext.BaseDirectory;
            Log($"baseDir='{baseDir}'");

            // Try flat path first (library next to assemblies in bundle).
            string flatPath = Path.Combine(baseDir, libFileName);
            bool flatExists = File.Exists(flatPath);
            Log($"flatPath='{flatPath}' exists={flatExists}");
            if (NativeLibrary.TryLoad(flatPath, out _))
            {
                Log("Loaded via flat path");
                return true;
            }
            Log("TryLoad(flatPath) failed");

            // Try runtimes subfolder (desktop NuGet layout).
            string runtimesPath = Path.Combine(baseDir, GetPlatformRuntimesPath());
            bool rtExists = File.Exists(runtimesPath);
            Log($"runtimesPath='{runtimesPath}' exists={rtExists}");
            if (NativeLibrary.TryLoad(runtimesPath, out _))
            {
                Log("Loaded via runtimes path");
                return true;
            }
            Log("All probe attempts failed");
            return false;
        }
        catch (Exception ex)
        {
            Log($"Probe exception: {ex}");
            return false;
        }
    }

    private static void Log(string message)
    {
        string line = $"[NativeAvailability] {message}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            string logDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(logDir))
                logDir = AppContext.BaseDirectory;
            string logFile = Path.Combine(logDir, "pjsip_native_probe.log");
            File.AppendAllText(logFile, $"{DateTime.Now:HH:mm:ss.fff} {line}\n");
        }
        catch { /* best effort */ }
    }

    /// <summary>Returns just the native library file name for the current platform.</summary>
    private static string GetPlatformLibraryFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "pjsua2.dll";

        if (OperatingSystem.IsMacCatalyst() || OperatingSystem.IsIOS()
            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "libpjsua2.dylib";

        if (OperatingSystem.IsAndroid() || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "libpjsua2.so";

        return "pjsua2";
    }

    /// <summary>Returns the runtimes sub-path (desktop NuGet layout).</summary>
    private static string GetPlatformRuntimesPath()
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

        if (OperatingSystem.IsAndroid())
            return Path.Combine("runtimes", "android-arm64", "native", "libpjsua2.so");

        if (OperatingSystem.IsIOS())
            return Path.Combine("runtimes", "ios-arm64", "native", "libpjsua2.dylib");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Path.Combine("runtimes", "linux-x64", "native", "libpjsua2.so");

        return "pjsua2";
    }
}
