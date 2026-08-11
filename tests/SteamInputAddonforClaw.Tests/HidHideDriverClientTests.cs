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
        var client = new HidHideDriverClient(new FakeNative(device), Converter());

        Assert.True(client.AddHiddenDevice("C"));
        Assert.Equal(["A", "B", "C"], device.Blacklist);
        Assert.Contains(2051u, device.WrittenFunctions);
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

    private static HidHidePathConverter Converter() => new(new FakeDosDevices());

    private sealed class FakeDosDevices : IDosDeviceNameResolver
    {
        public string Resolve(string driveName) => string.Equals(driveName, "C:", StringComparison.OrdinalIgnoreCase) ? @"\Device\HarddiskVolume3" : throw new IOException();
    }

    private sealed class FakeNative(FakeDevice device) : IHidHideNativeApi
    {
        public uint DesiredAccess { get; private set; }
        public IHidHideControlDevice Open(uint desiredAccess) { DesiredAccess = desiredAccess; return device; }
    }

    private sealed class FakeDevice(List<string> whitelist, List<string> blacklist) : IHidHideControlDevice
    {
        public List<string> Whitelist { get; } = whitelist;
        public List<string> Blacklist { get; } = blacklist;
        public bool Active { get; init; }
        public List<uint> WrittenFunctions { get; } = [];
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
                if (output is not null) output[0] = function == 2052 && Active ? (byte)1 : (byte)0;
                return true;
            }
            if (function is 2048 or 2050)
            {
                var values = function == 2048 ? Whitelist : Blacklist;
                var bytes = HidHideDriverClient.SerializeMultiString(values);
                if (output is null) { ZeroBufferQueries++; bytesReturned = (uint)bytes.Length; return true; }
                Array.Copy(bytes, output, bytes.Length); bytesReturned = (uint)bytes.Length; return true;
            }
            if (function == 2049)
            {
                Whitelist.Clear(); Whitelist.AddRange(HidHideDriverClient.DeserializeMultiString(input!)); bytesReturned = 0; return true;
            }
            if (function == 2051)
            {
                WrittenFunctions.Add(function);
                Blacklist.Clear(); Blacklist.AddRange(HidHideDriverClient.DeserializeMultiString(input!)); bytesReturned = 0; return true;
            }
            bytesReturned = 0; errorCode = 1; return false;
        }
        public void Dispose() { }
    }
}
