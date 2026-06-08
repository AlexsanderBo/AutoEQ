using AutoEQ.Services;

namespace AutoEQ.Tests;

public sealed class DefaultRenderEndpointResolverTests
{
    // ── NullDefaultRenderEndpointResolver ──────────────────────────────────

    [Fact]
    public void NullResolver_ReturnsEmptyId()
    {
        var resolver = new NullDefaultRenderEndpointResolver();
        Assert.Equal(string.Empty, resolver.Resolve().Id);
    }

    [Fact]
    public void NullResolver_ReturnsEmptyFriendlyName()
    {
        var resolver = new NullDefaultRenderEndpointResolver();
        Assert.Equal(string.Empty, resolver.Resolve().FriendlyName);
    }

    [Fact]
    public void NullResolver_IsCallableMultipleTimes()
    {
        var resolver = new NullDefaultRenderEndpointResolver();
        RenderEndpointInfo first = resolver.Resolve();
        RenderEndpointInfo second = resolver.Resolve();
        Assert.Equal(first, second);
    }

    [Fact]
    public void NullResolver_ImplementsInterface()
    {
        IDefaultRenderEndpointResolver resolver = new NullDefaultRenderEndpointResolver();
        Assert.NotNull(resolver);
    }

    // ── RenderEndpointInfo record ──────────────────────────────────────────

    [Fact]
    public void RenderEndpointInfo_EqualityByValue()
    {
        var a = new RenderEndpointInfo("{dev-1}", "Speakers");
        var b = new RenderEndpointInfo("{dev-1}", "Speakers");
        Assert.Equal(a, b);
    }

    [Fact]
    public void RenderEndpointInfo_InequalityOnDifferentId()
    {
        var a = new RenderEndpointInfo("{dev-1}", "Speakers");
        var b = new RenderEndpointInfo("{dev-2}", "Speakers");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RenderEndpointInfo_InequalityOnDifferentName()
    {
        var a = new RenderEndpointInfo("{dev-1}", "Speakers");
        var b = new RenderEndpointInfo("{dev-1}", "Headphones");
        Assert.NotEqual(a, b);
    }

    // ── WindowsDefaultRenderEndpointResolver (requires Windows + audio device) ──

    [Fact]
    public void WindowsResolver_ReturnsNonEmptyId()
    {
        if (!OperatingSystem.IsWindows()) return;

        var resolver = new WindowsDefaultRenderEndpointResolver();
        RenderEndpointInfo info = resolver.Resolve();
        Assert.False(string.IsNullOrWhiteSpace(info.Id));
    }

    [Fact]
    public void WindowsResolver_ReturnsNonEmptyFriendlyName()
    {
        if (!OperatingSystem.IsWindows()) return;

        var resolver = new WindowsDefaultRenderEndpointResolver();
        RenderEndpointInfo info = resolver.Resolve();
        Assert.False(string.IsNullOrWhiteSpace(info.FriendlyName));
    }

    [Fact]
    public void WindowsResolver_IsStableAcrossConsecutiveCalls()
    {
        if (!OperatingSystem.IsWindows()) return;

        var resolver = new WindowsDefaultRenderEndpointResolver();
        RenderEndpointInfo first = resolver.Resolve();
        RenderEndpointInfo second = resolver.Resolve();
        Assert.Equal(first.Id, second.Id);
    }
}
