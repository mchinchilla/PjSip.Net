using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PjSip.Net.Interop;

/// <summary>
/// Cross-platform native library loader for PJSIP pjsua2.
/// Registers a custom <see cref="NativeLibrary"/> resolver at module initialization
/// that probes platform-specific runtime directories for the correct native binary.
/// </summary>
internal static class NativeLoader
{
    private const string LibraryName = "pjsua2";

    private static readonly Lock _lock = new();
    private static bool _resolved;

    [ModuleInitializer]
    internal static void Initialize()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(NativeLoader).Assembly,
            ResolveDllImport);
    }

    private static nint ResolveDllImport(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.OrdinalIgnoreCase))
            return nint.Zero;

        // Fast path: let the runtime try its default resolution first.
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out nint handle))
            return handle;

        // Fall back to probing the runtimes folder next to the assembly.
        string probePath = GetPlatformLibraryPath();

        string assemblyDirectory = Path.GetDirectoryName(assembly.Location)
            ?? AppContext.BaseDirectory;

        string fullPath = Path.Combine(assemblyDirectory, probePath);

        lock (_lock)
        {
            if (!_resolved && NativeLibrary.TryLoad(fullPath, out handle))
            {
                _resolved = true;
                return handle;
            }
        }

        return nint.Zero;
    }

    private static string GetPlatformLibraryPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine("runtimes", "win-x64", "native", "pjsua2.dll");
        }

        // Mac Catalyst: runs on macOS but uses the iOS TFM — check before OSX.
        if (OperatingSystem.IsMacCatalyst() || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string rid = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "osx-arm64",
                _                  => "osx-x64",
            };

            return Path.Combine("runtimes", rid, "native", "libpjsua2.dylib");
        }

        if (OperatingSystem.IsAndroid())
        {
            return Path.Combine("runtimes", "android-arm64", "native", "libpjsua2.so");
        }

        if (OperatingSystem.IsIOS())
        {
            return Path.Combine("runtimes", "ios-arm64", "native", "libpjsua2.dylib");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Path.Combine("runtimes", "linux-x64", "native", "libpjsua2.so");
        }

        throw new PlatformNotSupportedException(
            $"PJSIP native library loading is not supported on this platform: " +
            $"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}).");
    }
}
