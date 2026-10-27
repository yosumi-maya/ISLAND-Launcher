using Microsoft.Win32;

namespace Island.Core;

public sealed class GpuPreferenceLease : IDisposable
{
    private readonly IGpuPreferenceStore store;
    private readonly string executable, requested;
    private readonly object? original;
    private readonly RegistryValueKind originalKind;
    private bool disposed;

    public static GpuPreferenceLease Acquire(string executable) => Acquire(executable, new WindowsGpuPreferenceStore());
    internal static GpuPreferenceLease Acquire(string executable, IGpuPreferenceStore store)
    {
        try { return new(Path.GetFullPath(executable), store); }
        catch { store.Dispose(); throw; }
    }
    private GpuPreferenceLease(string executable, IGpuPreferenceStore store)
    {
        this.store = store; this.executable = executable;
        original = store.Read(executable); originalKind = original is null ? RegistryValueKind.String : store.Kind(executable);
        var entries = (original as string ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => !entry.TrimStart().StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase));
        requested = "GpuPreference=2;" + string.Concat(entries.Select(entry => entry + ";"));
        if (!Equals(original, requested)) store.Write(executable, requested, RegistryValueKind.String);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            // Do not overwrite a preference changed by the user while the game ran.
            if (!Equals(store.Read(executable), requested)) return;
            if (original is null) store.Delete(executable); else store.Write(executable, original, originalKind);
        }
        finally { store.Dispose(); }
    }
}

internal interface IGpuPreferenceStore : IDisposable
{
    object? Read(string executable);
    RegistryValueKind Kind(string executable);
    void Write(string executable, object value, RegistryValueKind kind);
    void Delete(string executable);
}

internal sealed class WindowsGpuPreferenceStore : IGpuPreferenceStore
{
    private readonly RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", true)
        ?? throw new IOException("无法打开当前用户的 Windows 图形偏好设置。");
    public object? Read(string executable) => key.GetValue(executable, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    public RegistryValueKind Kind(string executable) => key.GetValueKind(executable);
    public void Write(string executable, object value, RegistryValueKind kind) => key.SetValue(executable, value, kind);
    public void Delete(string executable) => key.DeleteValue(executable, false);
    public void Dispose() => key.Dispose();
}
