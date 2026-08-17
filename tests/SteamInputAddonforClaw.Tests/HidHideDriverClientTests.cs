using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.HidHide;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HidHideDriverClientTests
{
    private const string AddonDos = @"C:\Apps\SteamInputAddonforClaw.exe";
    private const string AddonNt = @"\Device\HarddiskVolume3\Apps\SteamInputAddonforClaw.exe";

    // The concurrent-gate tests below coordinate background Task.Run operations through
    // TaskCompletionSources with a bounded wait. 5 seconds was tight enough that a contended
    // CI thread pool could burn through it before the background task was even scheduled --
    // and because these tests don't cancel the background Task.Run on timeout, a false
    // timeout here left it running forever, holding the process-wide ControlDeviceGate lock
    // and hanging every later test that touches it. Widen the bound for CI headroom; it's an
    // upper bound, not an expected duration, so passing runs are unaffected.
    private static readonly TimeSpan GateWait = TimeSpan.FromSeconds(20);

    [Fact]
    public void NativeDeviceIoControl_BindsToKernel32DeviceIoControlExport()
    {
        var method = typeof(HidHideControlDevice).GetMethod("NativeDeviceIoControl", BindingFlags.NonPublic | BindingFlags.Static)!;
        var import = method.GetCustomAttribute<DllImportAttribute>();

        Assert.NotNull(import);
        Assert.Equal("kernel32.dll", import.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("DeviceIoControl", import.EntryPoint);
    }

    [Fact]
    public void Inspect_UsesGenericReadAndSuccessfulTwoPassQueries()
    {
        var device = new FakeDevice([AddonNt], []);
        var native = new FakeNative(device);
        var client = new HidHideDriverClient(native, Converter());

        var inspection = client.Inspect();

        Assert.Equal(HidHideDriverClient.GenericRead, native.DesiredAccess);
        Assert.Contains(AddonDos, inspection.ApplicationWhitelist, StringComparer.OrdinalIgnoreCase);
        Assert.True(device.ZeroBufferQueries >= 2);
    }

    [Fact]
    public void Inspect_InverseWhitelistWinsWhenInactive()
    {
        var device = new FakeDevice([], []) { Active = false, Inverse = true };
        var inspection = new HidHideDriverClient(new FakeNative(device), Converter()).Inspect();

        Assert.Equal(HidHideInspectionStatus.InverseWhitelist, inspection.Status);
        Assert.True(inspection.IsInverseWhitelist);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ReadMultiString_InvalidSizeFailsClosed(uint size)
    {
        var device = new FakeDevice([], []) { QuerySizeOverride = size };
        Assert.Throws<InvalidDataException>(() => HidHideDriverClient.ReadMultiString(device, 1));
    }

    [Fact]
    public void ReadMultiString_OversizedReturnedLengthFailsClosed()
    {
        var device = new FakeDevice([], []) { ReturnedLengthOverride = 100 };
        Assert.Throws<InvalidDataException>(() => HidHideDriverClient.ReadMultiString(device, 1));
    }

    [Theory]
    [InlineData("A\0B\0\0", "A", "B")]
    [InlineData("A\0B\0\0\0", "A", "B")]
    [InlineData("A\0\0B\0\0", "A", "B")]
    [InlineData("\0")]
    [InlineData("\0\0")]
    public void DeserializeMultiString_AcceptsHidHideCompatibleTerminations(string multiString, params string[] expected)
    {
        Assert.Equal(expected, HidHideDriverClient.DeserializeMultiString(Encoding.Unicode.GetBytes(multiString)));
    }

    [Fact]
    public void DeserializeMultiString_WithoutTerminalNulFailsClosed()
    {
        Assert.Throws<InvalidDataException>(() => HidHideDriverClient.DeserializeMultiString(Encoding.Unicode.GetBytes("A\0B")));
    }

    [Fact]
    public void DeserializeMultiString_OddByteLengthFailsClosed()
    {
        Assert.Throws<InvalidDataException>(() => HidHideDriverClient.DeserializeMultiString([0, 0, 0]));
    }

    [Fact]
    public void DeserializeMultiString_InvalidUnicodeFailsClosed()
    {
        Assert.Throws<DecoderFallbackException>(() => HidHideDriverClient.DeserializeMultiString([0x00, 0xD8, 0, 0, 0, 0]));
    }

    [Fact]
    public void SerializeMultiString_UsesHidHideCompatibleTerminations()
    {
        Assert.Equal("A\0B\0\0", Encoding.Unicode.GetString(HidHideDriverClient.SerializeMultiString(["A", "B"])));
        Assert.Equal("\0", Encoding.Unicode.GetString(HidHideDriverClient.SerializeMultiString([])));
    }

    [Fact]
    public void PathConverter_ConvertsBothDirectionsCaseInsensitively()
    {
        var converter = Converter();
        Assert.Equal(AddonNt, converter.ToFullImageName(AddonDos));
        Assert.Equal(AddonDos, converter.ToDosPath(AddonNt.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateWhitelist_PreservesExistingRawEntriesAndRemovesOnlyExactAddon()
    {
        var clawTweaks = @"\Device\HarddiskVolume3\Apps\ClawTweaks.exe";
        var helper = @"\Device\HarddiskVolume3\Apps\SteamInputAddonforClaw.Helper.exe";
        var device = new FakeDevice([clawTweaks, helper], []);
        var client = new HidHideDriverClient(new FakeNative(device), Converter());

        Assert.True(client.AddApplication(AddonDos));
        Assert.Equal([clawTweaks, helper, AddonNt], device.Whitelist);
        Assert.True(client.RemoveApplication(AddonDos));
        Assert.Equal([clawTweaks, helper], device.Whitelist);
    }

    [Fact]
    public void AddHiddenDevice_PreservesExistingEntriesAndUsesVerifiedIoctl()
    {
        var device = new FakeDevice([], ["A", "B"]) { Active = true };
        var native = new FakeNative(device);
        var client = new HidHideDriverClient(native, Converter());

        Assert.True(client.AddHiddenDevice("C"));
        Assert.Equal(["A", "B", "C"], device.Blacklist);
        Assert.Contains(0x8001600Cu, device.WrittenControlCodes);
        Assert.Equal(HidHideDriverClient.GenericRead | 0x40000000u, native.WriteDesiredAccess);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetActive_UsesOneByteBooleanAndVerifiesState(bool active)
    {
        var device = new FakeDevice([], []) { Active = false };
        var client = new HidHideDriverClient(new FakeNative(device), Converter());

        Assert.True(client.SetActive(active));
        Assert.Equal(active, device.Active);
        Assert.Equal((byte)(active ? 1 : 0), Assert.Single(device.ActivePayloads));
    }

    [Fact]
    public void AddHiddenDevice_ExistingExactEntryDoesNotWrite()
    {
        var device = new FakeDevice([], ["ABC"]) { Active = true };
        var client = new HidHideDriverClient(new FakeNative(device), Converter());

        Assert.True(client.AddHiddenDevice("abc"));
        Assert.Empty(device.WrittenFunctions);
    }

    [Fact]
    public void RemoveHiddenDevice_RemovesOnlyExactEntry()
    {
        var device = new FakeDevice([], ["ABC", "ABC2", "Other"]) { Active = true };
        var client = new HidHideDriverClient(new FakeNative(device), Converter());

        Assert.True(client.RemoveHiddenDevice("abc"));
        Assert.Equal(["ABC2", "Other"], device.Blacklist);
    }

    [Fact]
    public void RemoveHiddenDevice_AlreadyAbsentDoesNotWrite()
    {
        var device = new FakeDevice([], ["ABC"]) { Active = true };
        var client = new HidHideDriverClient(new FakeNative(device), Converter());

        Assert.True(client.RemoveHiddenDevice("missing"));
        Assert.Empty(device.WrittenFunctions);
    }

    [Fact]
    public void RemoveHiddenDevice_WhenDisabled_RemovesExactEntry()
    {
        var device = new FakeDevice([], ["ABC"]) { Active = false };
        var client = new HidHideDriverClient(new FakeNative(device), Converter());

        Assert.True(client.RemoveHiddenDevice("abc"));
        Assert.Empty(device.Blacklist);
    }

    [Fact]
    public void AddHiddenDevice_UsesOneHandleThroughVerification()
    {
        var device = new FakeDevice([], ["Foreign"]) { Active = true };
        var native = new ExclusiveFakeNative(device);

        Assert.True(new HidHideDriverClient(native, Converter()).AddHiddenDevice("Addon"));

        Assert.Equal(["Foreign", "Addon"], device.Blacklist);
        Assert.Equal(1, native.OpenCount);
        Assert.Equal(1, native.MaximumLiveHandleCount);
        Assert.Equal(0, native.LiveHandleCount);
    }

    [Fact]
    public void RemoveHiddenDevice_UsesOneHandleThroughVerification()
    {
        var device = new FakeDevice([], ["Foreign", "Addon"]) { Active = true };
        var native = new ExclusiveFakeNative(device);

        Assert.True(new HidHideDriverClient(native, Converter()).RemoveHiddenDevice("Addon"));

        Assert.Equal(["Foreign"], device.Blacklist);
        Assert.Equal(1, native.OpenCount);
        Assert.Equal(1, native.MaximumLiveHandleCount);
        Assert.Equal(0, native.LiveHandleCount);
    }

    [Fact]
    public void SetActive_ClosesMutationHandleBeforeVerification()
    {
        var device = new FakeDevice([], []) { Active = false };
        var native = new ExclusiveFakeNative(device);

        Assert.True(new HidHideDriverClient(native, Converter()).SetActive(true));

        Assert.True(device.Active);
        Assert.Equal(2, native.OpenCount);
        Assert.Equal(1, native.MaximumLiveHandleCount);
        Assert.Equal(0, native.LiveHandleCount);
    }

    [Fact]
    public void AddHiddenDevice_PreservesForeignEntryAddedAfterReadWriteVerification()
    {
        var device = new FakeDevice([], ["Foreign"]);
        var native = new ExclusiveFakeNative(device)
        {
            OnFirstHandleDisposed = () => device.Blacklist.Add("ForeignLate")
        };

        Assert.True(new HidHideDriverClient(native, Converter()).AddHiddenDevice("Addon"));

        Assert.Equal(["Foreign", "Addon", "ForeignLate"], device.Blacklist);
        Assert.Equal(1, native.OpenCount);
    }

    [Fact]
    public void UpdateWhitelist_WithExclusiveHandlePreservesForeignEntries()
    {
        var foreign = @"\Device\HarddiskVolume3\Apps\Foreign.exe";
        var device = new FakeDevice([foreign], []);
        var native = new ExclusiveFakeNative(device);
        var client = new HidHideDriverClient(native, Converter());

        Assert.True(client.AddApplication(AddonDos));

        Assert.Equal([foreign, AddonNt], device.Whitelist);
        Assert.Equal(1, native.MaximumLiveHandleCount);
        Assert.Equal(0, native.LiveHandleCount);
    }

    [Fact]
    public async Task SameClient_ConcurrentInspectsShareTheProcessWideGate()
    {
        var device = new FakeDevice([], []) { Active = true };
        var native = new ExclusiveFakeNative(device) { BlockFirstOpen = true };
        var client = new HidHideDriverClient(native, Converter());
        var secondOperationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(client.Inspect);
        await native.FirstHandleOpened.Task.WaitAsync(GateWait);
        var second = Task.Run(() =>
        {
            secondOperationStarted.SetResult();
            return client.Inspect();
        });
        await secondOperationStarted.Task.WaitAsync(GateWait);

        Assert.False(second.IsCompleted);
        Assert.Equal(1, native.OpenCount);
        native.AllowFirstHandleDispose.SetResult();

        var inspections = await Task.WhenAll(first, second).WaitAsync(GateWait);
        Assert.All(inspections, inspection => Assert.Equal(HidHideInspectionStatus.Available, inspection.Status));
        Assert.Equal(2, native.OpenCount);
        Assert.Equal(1, native.MaximumLiveHandleCount);
        Assert.Equal(0, native.LiveHandleCount);
    }

    [Fact]
    public async Task DifferentClients_ConcurrentInspectsShareTheProcessWideGate()
    {
        var device = new FakeDevice([], []) { Active = true };
        var native = new ExclusiveFakeNative(device) { BlockFirstOpen = true };
        var clientA = new HidHideDriverClient(native, Converter());
        var clientB = new HidHideDriverClient(native, Converter());
        var secondOperationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(clientA.Inspect);
        await native.FirstHandleOpened.Task.WaitAsync(GateWait);

        var second = Task.Run(() =>
        {
            secondOperationStarted.SetResult();
            return clientB.Inspect();
        });
        await secondOperationStarted.Task.WaitAsync(GateWait);

        Assert.False(second.IsCompleted);
        Assert.Equal(1, native.OpenCount);
        native.AllowFirstHandleDispose.SetResult();

        var inspections = await Task.WhenAll(first, second).WaitAsync(GateWait);
        Assert.All(inspections, inspection => Assert.Equal(HidHideInspectionStatus.Available, inspection.Status));
        Assert.Equal(2, native.OpenCount);
        Assert.Equal(1, native.MaximumLiveHandleCount);
        Assert.Equal(0, native.LiveHandleCount);
    }

    [Fact]
    public async Task DifferentClients_ConcurrentInspectAndMutationAreSerialized()
    {
        var device = new FakeDevice([], ["Foreign"]) { Active = true };
        var native = new ExclusiveFakeNative(device) { BlockFirstOpen = true };
        var inspector = new HidHideDriverClient(native, Converter());
        var mutator = new HidHideDriverClient(native, Converter());
        var mutationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var inspect = Task.Run(inspector.Inspect);
        await native.FirstHandleOpened.Task.WaitAsync(GateWait);
        var mutation = Task.Run(() =>
        {
            mutationStarted.SetResult();
            return mutator.AddHiddenDevice("Addon");
        });
        await mutationStarted.Task.WaitAsync(GateWait);

        Assert.False(mutation.IsCompleted);
        Assert.Equal(1, native.OpenCount);
        native.AllowFirstHandleDispose.SetResult();

        Assert.Equal(HidHideInspectionStatus.Available, (await inspect.WaitAsync(GateWait)).Status);
        Assert.True(await mutation.WaitAsync(GateWait));
        Assert.Equal(["Foreign", "Addon"], device.Blacklist);
        Assert.Equal(1, native.MaximumLiveHandleCount);
        Assert.Equal(0, native.LiveHandleCount);
    }

    private static HidHidePathConverter Converter() => new(new FakeDosDevices());

    private sealed class FakeDosDevices : IDosDeviceNameResolver
    {
        public string Resolve(string driveName) => string.Equals(driveName, "C:", StringComparison.OrdinalIgnoreCase) ? @"\Device\HarddiskVolume3" : throw new IOException();
    }

    private sealed class FakeNative(FakeDevice device) : IHidHideNativeApi
    {
        public uint DesiredAccess { get; private set; }
        public uint WriteDesiredAccess { get; private set; }
        public IHidHideControlDevice Open(uint desiredAccess) { DesiredAccess = desiredAccess; if ((desiredAccess & 0x40000000u) != 0) WriteDesiredAccess = desiredAccess; return device; }
    }

    private sealed class ExclusiveFakeNative(FakeDevice device) : IHidHideNativeApi
    {
        private readonly object _sync = new();
        public bool BlockFirstOpen { get; init; }
        public int LiveHandleCount { get; private set; }
        public int MaximumLiveHandleCount { get; private set; }
        public int OpenCount { get; private set; }
        public TaskCompletionSource FirstHandleOpened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowFirstHandleDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? OnFirstHandleDisposed { get; init; }

        public IHidHideControlDevice Open(uint desiredAccess)
        {
            bool block;
            lock (_sync)
            {
                if (LiveHandleCount != 0) throw new System.ComponentModel.Win32Exception(5);
                LiveHandleCount++;
                MaximumLiveHandleCount = Math.Max(MaximumLiveHandleCount, LiveHandleCount);
                OpenCount++;
                block = BlockFirstOpen && OpenCount == 1;
            }

            if (block)
            {
                FirstHandleOpened.TrySetResult();
                AllowFirstHandleDispose.Task.GetAwaiter().GetResult();
            }

            return new ExclusiveFakeControlDevice(this, device);
        }

        private void Release()
        {
            lock (_sync)
            {
                LiveHandleCount--;
            }
        }

        private sealed class ExclusiveFakeControlDevice(ExclusiveFakeNative native, FakeDevice device) : IHidHideControlDevice
        {
            private int _disposed;

            public bool DeviceIoControl(uint code, byte[]? input, byte[]? output, out uint bytesReturned, out int errorCode)
                => device.DeviceIoControl(code, input, output, out bytesReturned, out errorCode);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    native.Release();
                    if (native.OpenCount == 1) native.OnFirstHandleDisposed?.Invoke();
                }
            }
        }
    }

    private sealed class FakeDevice(List<string> whitelist, List<string> blacklist) : IHidHideControlDevice
    {
        public List<string> Whitelist { get; } = whitelist;
        public List<string> Blacklist { get; } = blacklist;
        public bool Active { get; set; }
        public bool Inverse { get; init; }
        public List<byte> ActivePayloads { get; } = [];
        public List<uint> WrittenFunctions { get; } = [];
        public List<uint> WrittenControlCodes { get; } = [];
        public uint? QuerySizeOverride { get; init; }
        public uint? ReturnedLengthOverride { get; init; }
        public int ZeroBufferQueries { get; private set; }
        public bool DeviceIoControl(uint code, byte[]? input, byte[]? output, out uint bytesReturned, out int errorCode)
        {
            errorCode = 0;
            if (code == 1)
            {
                if (output is null) { bytesReturned = QuerySizeOverride ?? 4; return true; }
                bytesReturned = ReturnedLengthOverride ?? 4; Array.Clear(output); return true;
            }
            var function = (code >> 2) & 0xFFF;
            if (function is 2052 or 2054)
            {
                bytesReturned = 1;
                if (output is not null) output[0] = function == 2052 ? (Active ? (byte)1 : (byte)0) : (Inverse ? (byte)1 : (byte)0);
                return true;
            }
            if (function is 2048 or 2050)
            {
                var values = function == 2048 ? Whitelist : Blacklist;
                var bytes = HidHideDriverClient.SerializeMultiString(values);
                if (output is null) { ZeroBufferQueries++; bytesReturned = (uint)bytes.Length; return true; }
                Array.Copy(bytes, output, bytes.Length); bytesReturned = (uint)bytes.Length; return true;
            }
            if (function == 2053)
            {
                ActivePayloads.Add(input is { Length: 1 } ? input[0] : byte.MaxValue);
                if (input is not { Length: 1 }) { bytesReturned = 0; return false; }
                Active = input[0] != 0; bytesReturned = 0; return true;
            }
            if (function == 2049)
            {
                Whitelist.Clear(); Whitelist.AddRange(HidHideDriverClient.DeserializeMultiString(input!)); bytesReturned = 0; return true;
            }
            if (function == 2051)
            {
                WrittenFunctions.Add(function);
                WrittenControlCodes.Add(code);
                Blacklist.Clear(); Blacklist.AddRange(HidHideDriverClient.DeserializeMultiString(input!)); bytesReturned = 0; return true;
            }
            bytesReturned = 0; errorCode = 1; return false;
        }
        public void Dispose() { }
    }
}
