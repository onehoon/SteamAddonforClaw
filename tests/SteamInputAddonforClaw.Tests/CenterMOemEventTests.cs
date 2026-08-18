using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMOemEventTests
{
    [Theory]
    [InlineData(0x220029u, (int)CenterMOemCode.Oem1)]
    [InlineData(0x220058u, (int)CenterMOemCode.Oem2)]
    [InlineData(0x220099u, (int)CenterMOemCode.Other)]
    [InlineData(41u, (int)CenterMOemCode.Oem1)]
    [InlineData(88u, (int)CenterMOemCode.Oem2)]
    public void Classify_maps_raw_codes(uint rawCode, int expected) =>
        Assert.Equal((CenterMOemCode)expected, CenterMOemEventMapper.Classify(rawCode));

    [Fact]
    public void TryParseRawCode_valid_numeric_value_succeeds()
    {
        Assert.True(WmiMsiEventSource.TryParseRawCode(0x220029, out var rawCode));
        Assert.Equal(0x220029u, rawCode);
    }

    [Fact]
    public void TryParseRawCode_missing_property_is_not_parsed_as_any_code() =>
        Assert.False(WmiMsiEventSource.TryParseRawCode(null, out _));

    [Fact]
    public void TryParseRawCode_malformed_property_is_not_parsed_as_any_code() =>
        Assert.False(WmiMsiEventSource.TryParseRawCode("not-a-number", out _));

    [Fact]
    public void Start_failure_is_safe_and_does_not_throw()
    {
        var adapter = new FakeManagementEventWatcherAdapter(startSucceeds: false);
        using var source = new WmiMsiEventSource(adapter);

        var started = source.Start();

        Assert.False(started);
    }

    [Fact]
    public void Event_arriving_after_dispose_does_not_reenter_subscribers()
    {
        var adapter = new FakeManagementEventWatcherAdapter(startSucceeds: true);
        var source = new WmiMsiEventSource(adapter);
        var receivedCount = 0;
        source.EventReceived += _ => receivedCount++;
        Assert.True(source.Start());

        source.Dispose();
        adapter.Raise(0x220029);

        Assert.Equal(0, receivedCount);
    }

    [Fact]
    public void Event_received_before_dispose_classifies_and_forwards()
    {
        var adapter = new FakeManagementEventWatcherAdapter(startSucceeds: true);
        using var source = new WmiMsiEventSource(adapter);
        MsiOemEvent? received = null;
        source.EventReceived += evt => received = evt;
        Assert.True(source.Start());

        adapter.Raise(0x220029);

        Assert.NotNull(received);
        Assert.Equal(CenterMOemCode.Oem1, received.Value.Code);
    }

    private sealed class FakeManagementEventWatcherAdapter(bool startSucceeds) : IManagementEventWatcherAdapter
    {
        public event Action<object?>? MsiEventArrived;

        public bool TryStart(out Exception? error)
        {
            error = startSucceeds ? null : new InvalidOperationException("WMI unavailable in test.");
            return startSucceeds;
        }

        internal void Raise(object? rawPropertyValue) => MsiEventArrived?.Invoke(rawPropertyValue);

        public void Dispose() { }
    }
}
