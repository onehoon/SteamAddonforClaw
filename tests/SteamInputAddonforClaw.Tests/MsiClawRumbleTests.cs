using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Feedback;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawRumbleTests
{
    [Fact]
    public void Endpoint_diagnostics_compare_physical_roots_case_insensitively()
    {
        Assert.True(WindowsMsiClawRumbleEndpointCatalog.MatchesPhysicalRoot("USB\\VID_0DB0&PID_1902\\A", "usb\\vid_0db0&pid_1902\\a"));
        Assert.False(WindowsMsiClawRumbleEndpointCatalog.MatchesPhysicalRoot("USB\\VID_0DB0&PID_1902\\A", "USB\\VID_0DB0&PID_1902\\B"));
    }
    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 0)]
    [InlineData(256, 1)]
    [InlineData(32767, 127)]
    [InlineData(32768, 128)]
    [InlineData(65280, 255)]
    [InlineData(65535, 255)]
    public void Conversion_uses_the_exact_high_byte_boundary(ushort value, byte expected)
        => Assert.Equal(expected, MsiClawRumblePacketBuilder.ToPhysicalByte(value));

    [Fact]
    public void PacketBuilder_preserves_MSI_small_then_large_order_and_stop()
    {
        Assert.Equal([0x05, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0], MsiClawRumblePacketBuilder.Build(TwoMotorRumble.Stopped));
        Assert.Equal([0x05, 0x01, 0, 0, 0xFF, 0xFF, 0, 0, 0, 0, 0], MsiClawRumblePacketBuilder.Build(new(ushort.MaxValue, ushort.MaxValue)));
        Assert.Equal([0x05, 0x01, 0, 0, 0, 0xFF, 0, 0, 0, 0, 0], MsiClawRumblePacketBuilder.Build(new(ushort.MaxValue, 0)));
        Assert.Equal([0x05, 0x01, 0, 0, 0xFF, 0, 0, 0, 0, 0, 0], MsiClawRumblePacketBuilder.Build(new(0, ushort.MaxValue)));
        Assert.All(new[] { MsiClawRumblePacketBuilder.Build(TwoMotorRumble.Stopped) }, packet => Assert.Equal(11, packet.Length));
    }

    [Fact]
    public void Sink_returns_unavailable_without_authoritative_identity_and_repeats_stop()
    {
        var identity = new FakeIdentity(null);
        var transport = new FakeTransport();
        using var sink = new MsiClawRumbleSink(identity, transport, new VerifiedEndpointResolver());
        Assert.Equal(PhysicalRumbleWriteStatus.Unavailable, sink.SetRumble(new(1, 2)).Status);
        identity.Current = new(Guid.NewGuid(), "path-a", "PNP", "USB\\VID_0DB0&PID_1902");
        Assert.Equal(PhysicalRumbleWriteStatus.Succeeded, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Equal(PhysicalRumbleWriteStatus.Succeeded, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Equal(2, transport.Packets.Count);
        Assert.Equal(transport.Packets[0], transport.Packets[1]);
    }

    [Fact]
    public void Transport_reuses_handle_changes_path_and_invalidates_after_write_failure()
    {
        var native = new FakeNativeHid();
        using var transport = new WindowsMsiClawRumbleTransport(native);
        var packet = new byte[11];
        Assert.True(transport.Write("path-a", packet).Succeeded);
        Assert.True(transport.Write("path-a", packet).Succeeded);
        Assert.Equal(1, native.OpenCount);
        Assert.True(transport.Write("path-b", packet).Succeeded);
        Assert.Equal(2, native.OpenCount);
        native.WriteResult = false;
        Assert.False(transport.Write("path-b", packet).Succeeded);
        native.WriteResult = true;
        Assert.True(transport.Write("path-b", packet).Succeeded);
        Assert.Equal(3, native.OpenCount);
    }

    [Fact]
    public void Transport_reopens_same_path_after_physical_session_retirement()
    {
        var native = new FakeNativeHid();
        using var transport = new WindowsMsiClawRumbleTransport(native);
        var packet = new byte[11];
        Assert.True(transport.Write("path-a", packet).Succeeded);
        transport.InvalidatePhysicalSession();
        Assert.True(transport.Write("path-a", packet).Succeeded);
        Assert.Equal(2, native.OpenCount);
    }

    [Fact]
    public void Transport_rejects_non_11_byte_packets_before_native_io_and_reopens_after_partial_write()
    {
        var native = new FakeNativeHid();
        using var transport = new WindowsMsiClawRumbleTransport(native);
        Assert.False(transport.Write("path-a", new byte[10]).Succeeded);
        Assert.Equal(0, native.OpenCount);
        Assert.True(transport.Write("path-a", new byte[11]).Succeeded);
        native.PartialWrite = true;
        Assert.Equal("PartialWrite", transport.Write("path-a", new byte[11]).Reason);
        native.PartialWrite = false;
        Assert.True(transport.Write("path-a", new byte[11]).Succeeded);
        Assert.Equal(2, native.OpenCount);
    }

    [Fact]
    public void Sink_returns_disposed_without_transport_io()
    {
        var transport = new FakeTransport();
        using var sink = new MsiClawRumbleSink(new FakeIdentity(new(Guid.NewGuid(), "path-a", "PNP", "USB\\VID_0DB0&PID_1902")), transport, new VerifiedEndpointResolver());
        sink.Dispose();
        Assert.Equal(PhysicalRumbleWriteStatus.Disposed, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Empty(transport.Packets);
    }

    [Fact]
    public async Task Transport_serializes_native_writes_and_preserves_request_order()
    {
        var native = new FakeNativeHid { BlockFirstWrite = true };
        using var transport = new WindowsMsiClawRumbleTransport(native);
        var first = Task.Run(() => transport.Write("path-a", [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        await native.FirstWriteEntered.Task;
        var secondRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.WriteRequested = () => secondRequested.TrySetResult();
        // The request signal is installed before a second request so the test positively observes
        // that B reached the transport boundary while A is still inside native Write.
        var second = Task.Run(() => transport.Write("path-a", [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        await secondRequested.Task;
        Assert.Equal(1, native.WriteCalls);
        native.ReleaseFirstWrite.Set();
        Assert.True((await first).Succeeded);
        Assert.True((await second).Succeeded);
        Assert.Equal(2, native.Writes.Count);
        Assert.Equal(1, native.Writes[0][0]);
        Assert.Equal(2, native.Writes[1][0]);
        Assert.All(native.Writes, write => { Assert.Equal(64, write.Length); Assert.All(write[11..], value => Assert.Equal(0, value)); });
    }

    [Fact]
    public async Task Invalidation_waits_for_in_progress_write_and_forces_fresh_open()
    {
        var native = new FakeNativeHid { BlockFirstWrite = true };
        using var transport = new WindowsMsiClawRumbleTransport(native);
        var first = Task.Run(() => transport.Write("path-a", new byte[11]));
        await native.FirstWriteEntered.Task;
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.InvalidationRequested = () => requested.TrySetResult();
        var invalidation = Task.Run(transport.InvalidatePhysicalSession);
        await requested.Task;
        Assert.False(invalidation.IsCompleted);
        native.ReleaseFirstWrite.Set();
        Assert.True((await first).Succeeded);
        await invalidation;
        Assert.True(transport.Write("path-a", new byte[11]).Succeeded);
        Assert.Equal(2, native.OpenCount);
    }

    [Fact]
    public void Sink_maps_transport_failures_and_exceptions_to_generic_failed_result()
    {
        var identity = new FakeIdentity(new(Guid.NewGuid(), "path-a", "PNP", "USB\\VID_0DB0&PID_1902"));
        var transport = new FakeTransport { Result = new(false, "OpenFailed", 5) };
        using var sink = new MsiClawRumbleSink(identity, transport, new VerifiedEndpointResolver());
        Assert.Equal(PhysicalRumbleWriteStatus.Failed, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        transport.Result = new(false, "PartialWrite");
        Assert.Equal(PhysicalRumbleWriteStatus.Failed, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        transport.Exception = new IOException("test");
        Assert.Equal(PhysicalRumbleWriteStatus.Failed, sink.SetRumble(TwoMotorRumble.Stopped).Status);
    }

    [Fact]
    public void Sink_rejects_empty_identity_path_without_transport_call()
    {
        var transport = new FakeTransport();
        using var sink = new MsiClawRumbleSink(new FakeIdentity(new(Guid.NewGuid(), "", "PNP", "USB\\VID_0DB0&PID_1902")), transport, new VerifiedEndpointResolver());
        Assert.Equal(PhysicalRumbleWriteStatus.Unavailable, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Empty(transport.Packets);
    }

    [Fact]
    public void Catalog_reads_report_lengths_from_real_hid_capabilities_not_device_information_properties()
    {
        var hidApi = new FakeCapabilityHidApi { InputReportLength = 64, OutputReportLength = 64 };
        var catalog = new WindowsMsiClawRumbleEndpointCatalog(hidApi: hidApi);

        var succeeded = catalog.TryReadHidCapabilities("path-a", out var input, out var output, out var writable, out var win32Error, out var hidStatus);

        Assert.True(succeeded);
        Assert.Equal(64, input);
        Assert.Equal(64, output);
        Assert.True(writable);
        Assert.Equal(0, win32Error);
        Assert.Equal(0x00110000, hidStatus);
        Assert.Equal(1, hidApi.OpenCalls);
        Assert.Equal(1, hidApi.CapabilityCalls);
    }

    [Fact]
    public void Catalog_capability_lookup_fails_when_the_hid_interface_cannot_be_opened()
    {
        var hidApi = new FakeCapabilityHidApi { OpenSucceeds = false };
        var catalog = new WindowsMsiClawRumbleEndpointCatalog(hidApi: hidApi);

        var succeeded = catalog.TryReadHidCapabilities("path-a", out _, out _, out var writable, out var win32Error, out _);

        Assert.False(succeeded);
        Assert.False(writable);
        Assert.NotEqual(0, win32Error);
        Assert.Equal(0, hidApi.CapabilityCalls);
    }

    [Fact]
    public void Catalog_capability_lookup_fails_when_HidP_GetCaps_fails_after_a_successful_open()
    {
        var hidApi = new FakeCapabilityHidApi { CapabilitiesSucceed = false };
        var catalog = new WindowsMsiClawRumbleEndpointCatalog(hidApi: hidApi);

        var succeeded = catalog.TryReadHidCapabilities("path-a", out var input, out var output, out _, out var win32Error, out var hidStatus);

        Assert.False(succeeded);
        Assert.Equal(0, input);
        Assert.Equal(0, output);
        Assert.Equal(0, win32Error);
        Assert.NotEqual(0, hidStatus);
    }

    [Fact]
    public void Endpoint_resolver_requires_one_exact_writable_64_byte_pid1902_candidate()
    {
        var identity = new MsiClawPhysicalInputIdentity(Guid.NewGuid(), "dinput", "PNP-A", "ROOT-A");
        MsiClawRumbleEndpointCandidate Candidate(string path, ushort pid = 0x1902, int length = 64, bool writable = true, string pnp = "PNP-A", string physical = "ROOT-A") =>
            new(path, pnp, physical, 0x0DB0, pid, 64, length, writable);

        Assert.Equal("NoVerifiedEndpoint", new MsiClawRumbleEndpointResolver(_ => []).Resolve(identity).Reason);
        Assert.Equal("AmbiguousEndpoints", new MsiClawRumbleEndpointResolver(_ => [Candidate("a"), Candidate("b")]).Resolve(identity).Reason);
        Assert.Equal("NoVerifiedEndpoint", new MsiClawRumbleEndpointResolver(_ => [Candidate("a", pid: 0x1901)]).Resolve(identity).Reason);
        Assert.Equal("NoVerifiedEndpoint", new MsiClawRumbleEndpointResolver(_ => [Candidate("a", length: 11)]).Resolve(identity).Reason);
        Assert.Equal("NoVerifiedEndpoint", new MsiClawRumbleEndpointResolver(_ => [Candidate("a", writable: false)]).Resolve(identity).Reason);
        Assert.Equal("NoVerifiedEndpoint", new MsiClawRumbleEndpointResolver(_ => [Candidate("a", physical: "ROOT-WRONG")]).Resolve(identity).Reason);
        Assert.Equal("a", new MsiClawRumbleEndpointResolver(_ => [Candidate("a")]).Resolve(identity).DevicePath);
    }

    [Fact]
    public void Endpoint_resolver_rejects_a_candidate_whose_input_report_length_is_not_64_bytes()
    {
        var identity = new MsiClawPhysicalInputIdentity(Guid.NewGuid(), "dinput", "PNP-A", "ROOT-A");
        var wrongInputLength = new MsiClawRumbleEndpointCandidate("a", "PNP-A", "ROOT-A", 0x0DB0, 0x1902, InputReportLength: 32, OutputReportLength: 64, Writable: true);

        var result = new MsiClawRumbleEndpointResolver(_ => [wrongInputLength]).Resolve(identity);

        Assert.Equal("NoVerifiedEndpoint", result.Reason);
        Assert.Null(result.DevicePath);
    }

    [Fact]
    public void Endpoint_resolver_treats_a_capability_lookup_failure_as_no_candidates_without_throwing()
    {
        // WindowsMsiClawRumbleEndpointCatalog.Find skips any candidate whose HID capability
        // query fails, so the resolver simply sees an empty candidate list -- this must not fail
        // main Steam Deck routing, and must not be conflated with a retryable transient error.
        var identity = new MsiClawPhysicalInputIdentity(Guid.NewGuid(), "dinput", "PNP-A", "ROOT-A");

        var result = new MsiClawRumbleEndpointResolver(_ => []).Resolve(identity);

        Assert.False(result.IsAvailable);
        Assert.Equal("NoVerifiedEndpoint", result.Reason);
    }

    [Fact]
    public void Endpoint_resolver_does_not_retry_a_com_exception_without_authoritative_transient_hresult()
    {
        var identity = new MsiClawPhysicalInputIdentity(Guid.NewGuid(), "dinput", "PNP-A", "ROOT-A");
        var calls = 0;
        var resolver = new MsiClawRumbleEndpointResolver(_ =>
        {
            calls++;
            throw new COMException("deterministic", unchecked((int)0x80070057));
        });

        Assert.Throws<COMException>(() => resolver.Resolve(identity));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Endpoint_resolver_stops_retrying_after_max_attempts_and_rethrows()
    {
        var identity = new MsiClawPhysicalInputIdentity(Guid.NewGuid(), "dinput", "PNP-A", "ROOT-A");
        var calls = 0;
        var resolver = new MsiClawRumbleEndpointResolver(_ =>
        {
            calls++;
            throw new COMException($"attempt-{calls}");
        }, _ => { });

        var exception = Assert.Throws<COMException>(() => resolver.Resolve(identity));

        Assert.Equal("attempt-1", exception.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Endpoint_resolver_does_not_retry_non_com_exceptions()
    {
        var identity = new MsiClawPhysicalInputIdentity(Guid.NewGuid(), "dinput", "PNP-A", "ROOT-A");
        var calls = 0;
        var delayed = false;
        var resolver = new MsiClawRumbleEndpointResolver(_ =>
        {
            calls++;
            throw new InvalidOperationException("deterministic");
        }, _ => delayed = true);

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(identity));

        Assert.Equal(1, calls);
        Assert.False(delayed);
    }

    [Fact]
    public void Sink_rejects_identity_that_becomes_stale_before_write_admission()
    {
        var identity = new FakeIdentity(new(Guid.NewGuid(), "path-a", "PNP", "ROOT")) { Generation = 1 };
        var transport = new FakeTransport();
        var resolver = new MutatingResolver(identity);
        using var sink = new MsiClawRumbleSink(identity, transport, resolver);
        Assert.Equal(PhysicalRumbleWriteStatus.Unavailable, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Empty(transport.Packets);
    }

    [Fact]
    public void Sink_resolves_endpoint_once_per_session_generation()
    {
        var identity = new FakeIdentity(new(Guid.NewGuid(), "path-a", "PNP", "ROOT")) { Generation = 1 };
        var resolver = new CountingResolver();
        using var sink = new MsiClawRumbleSink(identity, new FakeTransport(), resolver);
        Assert.Equal(PhysicalRumbleWriteStatus.Succeeded, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Equal(PhysicalRumbleWriteStatus.Succeeded, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Equal(1, resolver.Calls);
        sink.BeginPhysicalSessionRetirement();
        identity.Generation = 2;
        sink.BeginPhysicalSession();
        Assert.Equal(PhysicalRumbleWriteStatus.Succeeded, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Equal(2, resolver.Calls);
    }

    [Fact]
    public void Sink_does_not_cache_failed_endpoint_resolution()
    {
        var identity = new FakeIdentity(new(Guid.NewGuid(), "path-a", "PNP", "ROOT")) { Generation = 1 };
        var resolver = new SequenceResolver(new(null, "NoVerifiedEndpoint"), new("path-a", "VerifiedTestEndpoint"));
        var transport = new FakeTransport();
        using var sink = new MsiClawRumbleSink(identity, transport, resolver);
        Assert.Equal(PhysicalRumbleWriteStatus.Unavailable, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Equal(PhysicalRumbleWriteStatus.Succeeded, sink.SetRumble(TwoMotorRumble.Stopped).Status);
        Assert.Equal(2, resolver.Calls);
    }

    [Fact]
    public void Sink_contains_endpoint_resolver_exceptions()
    {
        var identity = new FakeIdentity(new(Guid.NewGuid(), "path-a", "PNP", "ROOT"));
        using var sink = new MsiClawRumbleSink(identity, new FakeTransport(), new ThrowingResolver());
        var result = sink.SetRumble(TwoMotorRumble.Stopped);
        Assert.Equal(PhysicalRumbleWriteStatus.Failed, result.Status);
        Assert.Equal("EndpointResolutionException", result.Reason);
    }

    [Fact]
    public async Task Sink_retirement_barrier_waits_for_admitted_write_and_closes_admission()
    {
        var identity = new FakeIdentity(new(Guid.NewGuid(), "path-a", "PNP", "ROOT")) { Generation = 1 };
        var transport = new FakeTransport { BlockWrites = true };
        using var sink = new MsiClawRumbleSink(identity, transport, new VerifiedEndpointResolver());
        var write = Task.Run(() => sink.SetRumble(new(0xFF00, 0xFF00)));
        await transport.WriteEntered.Task;
        var retire = Task.Run(sink.BeginPhysicalSessionRetirement);
        Assert.False(retire.IsCompleted);
        transport.ReleaseWrite.Set();
        Assert.Equal(PhysicalRumbleWriteStatus.Succeeded, (await write).Status);
        await retire;
        Assert.Equal(PhysicalRumbleWriteStatus.Unavailable, sink.SetRumble(TwoMotorRumble.Stopped).Status);
    }

    private sealed class FakeIdentity(MsiClawPhysicalInputIdentity? identity) : IMsiClawPhysicalInputIdentityProvider
    {
        public MsiClawPhysicalInputIdentity? Current { get; set; } = identity;
        public MsiClawPhysicalInputIdentity? CurrentIdentity => Current;
        public long Generation { get; set; }
        public long CurrentSessionGeneration => Generation;
    }

    private sealed class VerifiedEndpointResolver : IMsiClawRumbleEndpointResolver
    {
        public MsiClawRumbleEndpointResolution Resolve(MsiClawPhysicalInputIdentity identity) =>
            string.IsNullOrWhiteSpace(identity.DevicePath) ? new(null, "NoVerifiedEndpoint") : new(identity.DevicePath, "VerifiedTestEndpoint");
    }

    private sealed class MutatingResolver(FakeIdentity identity) : IMsiClawRumbleEndpointResolver
    {
        public MsiClawRumbleEndpointResolution Resolve(MsiClawPhysicalInputIdentity value)
        {
            identity.Current = null;
            identity.Generation++;
            return new(value.DevicePath, "VerifiedTestEndpoint");
        }
    }

    private sealed class CountingResolver : IMsiClawRumbleEndpointResolver
    {
        public int Calls { get; private set; }
        public MsiClawRumbleEndpointResolution Resolve(MsiClawPhysicalInputIdentity identity)
        {
            Calls++;
            return new(identity.DevicePath, "VerifiedTestEndpoint");
        }
    }

    private sealed class SequenceResolver(params MsiClawRumbleEndpointResolution[] results) : IMsiClawRumbleEndpointResolver
    {
        private int _index;
        public int Calls { get; private set; }
        public MsiClawRumbleEndpointResolution Resolve(MsiClawPhysicalInputIdentity identity)
        {
            Calls++;
            return results[Math.Min(_index++, results.Length - 1)];
        }
    }

    private sealed class ThrowingResolver : IMsiClawRumbleEndpointResolver
    {
        public MsiClawRumbleEndpointResolution Resolve(MsiClawPhysicalInputIdentity identity) => throw new InvalidOperationException("resolver");
    }

    private sealed class FakeTransport : IMsiClawRumbleTransport
    {
        public List<byte[]> Packets { get; } = [];
        public MsiClawRumbleTransportResult Result { get; set; } = new(true, "OK");
        public Exception? Exception { get; set; }
        public bool BlockWrites { get; set; }
        public TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseWrite { get; } = new(false);
        public MsiClawRumbleTransportResult Write(string _, ReadOnlySpan<byte> packet) { if (Exception is not null) throw Exception; if (BlockWrites) { WriteEntered.TrySetResult(); ReleaseWrite.Wait(); } Packets.Add(packet.ToArray()); return Result; }
        public void Dispose() { }
        public void InvalidatePhysicalSession() { }
    }

    private sealed class FakeNativeHid : IMsiClawNativeHidApi
    {
        public int LastError { get; private set; }
        public int OpenCount { get; private set; }
        public bool WriteResult { get; set; } = true;
        public bool PartialWrite { get; set; }
        public bool BlockFirstWrite { get; init; }
        public int WriteCalls { get; private set; }
        public TaskCompletionSource FirstWriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseFirstWrite { get; } = new(false);
        public List<byte[]> Writes { get; } = [];
        public SafeFileHandle Open(string path, uint desiredAccess, uint shareMode, uint creationDisposition)
        { OpenCount++; return new SafeFileHandle(new IntPtr(OpenCount), ownsHandle: false); }
        public bool Write(SafeFileHandle handle, byte[] buffer, out uint bytesWritten)
        {
            WriteCalls++;
            Writes.Add(buffer.ToArray());
            if (BlockFirstWrite && WriteCalls == 1)
            {
                FirstWriteEntered.TrySetResult();
                ReleaseFirstWrite.Wait();
            }
            bytesWritten = WriteResult ? (uint)(PartialWrite ? buffer.Length - 1 : buffer.Length) : 0;
            LastError = WriteResult ? 0 : 5;
            return WriteResult;
        }
        public bool TryGetReportLengths(SafeFileHandle handle, out int inputReportLength, out int outputReportLength, out int hidStatus)
        {
            inputReportLength = 0;
            outputReportLength = 0;
            hidStatus = 0;
            return false;
        }
    }

    private sealed class FakeCapabilityHidApi : IMsiClawNativeHidApi
    {
        public bool OpenSucceeds { get; set; } = true;
        public bool CapabilitiesSucceed { get; set; } = true;
        public int InputReportLength { get; set; } = 64;
        public int OutputReportLength { get; set; } = 64;
        public int LastError { get; private set; }
        public int OpenCalls { get; private set; }
        public int CapabilityCalls { get; private set; }

        public SafeFileHandle Open(string devicePath, uint desiredAccess, uint shareMode, uint creationDisposition)
        {
            OpenCalls++;
            if (!OpenSucceeds)
            {
                LastError = 2;
                return new SafeFileHandle(new IntPtr(-1), ownsHandle: false);
            }
            return new SafeFileHandle(new IntPtr(1), ownsHandle: false);
        }

        public bool Write(SafeFileHandle handle, byte[] buffer, out uint bytesWritten)
        {
            bytesWritten = (uint)buffer.Length;
            return true;
        }

        public bool TryGetReportLengths(SafeFileHandle handle, out int inputReportLength, out int outputReportLength, out int hidStatus)
        {
            CapabilityCalls++;
            hidStatus = CapabilitiesSucceed ? 0x00110000 : unchecked((int)0xC0110001);
            if (!CapabilitiesSucceed)
            {
                inputReportLength = 0;
                outputReportLength = 0;
                return false;
            }
            inputReportLength = InputReportLength;
            outputReportLength = OutputReportLength;
            return true;
        }
    }
}
