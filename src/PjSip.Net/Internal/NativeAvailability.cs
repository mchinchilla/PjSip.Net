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
            // Attempt to call a cheap SWIG P/Invoke that exercises the native loader.
            _ = new Interop.Generated.Endpoint();
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (TypeInitializationException ex) when (ex.InnerException is DllNotFoundException)
        {
            return false;
        }
        catch
        {
            // Any other unexpected error during probe – treat as unavailable.
            return false;
        }
    }
}
