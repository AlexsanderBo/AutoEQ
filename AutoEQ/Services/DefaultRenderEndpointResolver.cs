using NAudio.CoreAudioApi;

namespace AutoEQ.Services;

public readonly record struct RenderEndpointInfo(string Id, string FriendlyName);

public interface IDefaultRenderEndpointResolver
{
    /// <summary>Returns the current default Windows render endpoint, or empty values on failure.</summary>
    RenderEndpointInfo Resolve();
}

public sealed class WindowsDefaultRenderEndpointResolver : IDefaultRenderEndpointResolver
{
    public RenderEndpointInfo Resolve()
    {
        using var enumerator = new MMDeviceEnumerator();
        using MMDevice device = SystemVolumeService.GetWindowsDefaultRenderEndpoint(enumerator);
        return new RenderEndpointInfo(device.ID, device.FriendlyName);
    }
}

public sealed class NullDefaultRenderEndpointResolver : IDefaultRenderEndpointResolver
{
    public RenderEndpointInfo Resolve() => new(string.Empty, string.Empty);
}
