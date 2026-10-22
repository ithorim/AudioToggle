using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace AudioToggle;

internal static class PolicyConfig
{
    internal static void SetDefaultForAllRoles(string deviceId, MMDeviceEnumerator enumerator)
    {
        var client = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            SetDefaultForAllRoles(deviceId, role =>
            {
                try
                {
                    using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
                    return endpoint.ID;
                }
                catch (COMException ex) when (ex.HResult == unchecked((int)0x80070490))
                {
                    return null; // Switching also works when a role has no default yet.
                }
            }, (id, role) => Marshal.ThrowExceptionForHR(client.SetDefaultEndpoint(id, role)));
        }
        finally
        {
            Marshal.ReleaseComObject(client);
        }
    }

    internal static void SetDefaultForAllRoles(string deviceId, Func<Role, string?> getDefault,
        Action<string, Role> setDefault)
    {
        var roles = new[] { Role.Console, Role.Multimedia, Role.Communications };
        var previous = roles.Select(getDefault).ToArray();
        int changed = 0;
        try
        {
            for (; changed < roles.Length; changed++)
                setDefault(deviceId, roles[changed]);
        }
        catch (Exception switchError)
        {
            // Windows provides no atomic operation for all three roles. Undo completed calls.
            var rollbackErrors = new List<Exception>();
            for (int index = changed - 1; index >= 0; index--)
            {
                try
                {
                    string original = previous[index] ?? throw new InvalidOperationException(
                        $"{roles[index]} previously had no default device to restore.");
                    setDefault(original, roles[index]);
                }
                catch (Exception ex) { rollbackErrors.Add(ex); }
            }
            if (rollbackErrors.Count > 0)
                throw new AggregateException("Switching failed and some audio roles could not be restored.",
                    new[] { switchError }.Concat(rollbackErrors));
            throw;
        }
    }
}

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClient { }

// Undocumented interface. Never remove or reorder the preceding vtable slots.
// Unused pointer parameters are intentionally opaque; only SetDefaultEndpoint is called.
[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig]
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);

    [PreserveSig]
    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isDefault, out IntPtr format);

    [PreserveSig]
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);

    [PreserveSig]
    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isDefault, out long period, out long minimumPeriod);

    [PreserveSig]
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref long period);

    [PreserveSig]
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

    [PreserveSig]
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

    [PreserveSig]
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);

    [PreserveSig]
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);

    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, Role role);

    [PreserveSig]
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
}
