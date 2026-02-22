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

        string assemblyDirectory = Path.GetDirectoryName(assembly.Location)
            ?? AppContext.BaseDirectory;

        lock (_lock)
        {
            if (_resolved)
                return nint.Zero;

            // On mobile/Mac Catalyst the native binary sits next to the managed
            // assemblies (flat layout, no runtimes subfolder). Try the bare file
            // name first, then the flat path, then the runtimes subfolder.
            string libFileName = GetPlatformLibraryFileName();

            if (NativeLibrary.TryLoad(libFileName, out handle))
            {
                _resolved = true;
                return handle;
            }

            string flatPath = Path.Combine(assemblyDirectory, libFileName);
            if (NativeLibrary.TryLoad(flatPath, out handle))
            {
                _resolved = true;
                return handle;
            }

            // Desktop NuGet layout: runtimes/<rid>/native/<lib>
            string runtimesPath = Path.Combine(assemblyDirectory, GetPlatformRuntimesPath());
            if (NativeLibrary.TryLoad(runtimesPath, out handle))
            {
                _resolved = true;
                return handle;
            }
        }

        return nint.Zero;
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
            string rid = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "osx-arm64",
                _                  => "osx-x64",
            };
            return Path.Combine("runtimes", rid, "native", "libpjsua2.dylib");
        }

        if (OperatingSystem.IsAndroid())
            return Path.Combine("runtimes", "android-arm64", "native", "libpjsua2.so");

        if (OperatingSystem.IsIOS())
            return Path.Combine("runtimes", "ios-arm64", "native", "libpjsua2.dylib");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Path.Combine("runtimes", "linux-x64", "native", "libpjsua2.so");

        throw new PlatformNotSupportedException(
            $"PJSIP native library loading is not supported on this platform: " +
            $"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}).");
    }
}
