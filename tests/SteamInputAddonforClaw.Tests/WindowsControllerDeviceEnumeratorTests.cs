using SteamInputAddonforClaw.Controllers.Detection;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WindowsControllerDeviceEnumeratorTests
{
    [Fact]
    public void ScopedSelection_HydratesOnlyIndependentlyDiscoveredKnownMsiCandidates()
    {
        const string root1902 = @"USB\VID_0DB0&PID_1902\CLAW_A";
        const string child1902 = @"HID\VID_0DB0&PID_1902&MI_00&COL01\CLAW_A";
        const string control1902 = @"HID\VID_0DB0&PID_1902&MI_00&COL02\CLAW_A";
        const string pid1901 = @"HID\VID_0DB0&PID_1901&MI_00&COL01\CLAW_A";
        const string pid1903 = @"HID\VID_0DB0&PID_1903&MI_00&COL01\CLAW_A";
        const string unrelated = @"HID\VID_045E&PID_0B13\GAMEPAD";
        const string misleadingAncestor = @"ROOT\ANCESTOR_ONLY";
        var presentInstanceIds = new[]
        {
            root1902, child1902, control1902, pid1901, pid1903, unrelated, misleadingAncestor,
        };

        var selected = WindowsControllerDeviceEnumerator.SelectScopedCandidateInstanceIds(
            presentInstanceIds,
            0x0DB0,
            new HashSet<ushort> { 0x1901, 0x1902, 0x1903 });

        Assert.Contains(root1902, selected);
        Assert.Contains(child1902, selected);
        Assert.Contains(control1902, selected);
        Assert.Contains(pid1901, selected);
        Assert.Contains(pid1903, selected);
        Assert.DoesNotContain(misleadingAncestor, selected);
        Assert.DoesNotContain(unrelated, selected);

        var hydratedIds = selected.Select(ReadHardwareIdsForHydration).ToArray();
        Assert.Equal(selected.Count, hydratedIds.Length);
        Assert.DoesNotContain(misleadingAncestor, hydratedIds);

        string ReadHardwareIdsForHydration(string instanceId) =>
            instanceId == misleadingAncestor
                ? throw new PnpDeviceReadException("SetupDiGetDeviceRegistryPropertyW", 5, 1,
                    "SPDRP_HARDWAREID", 128, instanceId, 91)
                : instanceId;
    }

    [Fact]
    public void ScopedIdentity_DistinguishesIndependentCandidateFromAncestryOnlyDescriptor()
    {
        var root = WindowsControllerDeviceEnumerator.GetHydratedIdentity(
            @"USB\VID_0DB0&PID_1902\CLAW_A",
            [@"USB\VID_0DB0&PID_1902\CLAW_A"], [], true);
        var ancestryOnly = WindowsControllerDeviceEnumerator.GetHydratedIdentity(
            @"ROOT\ANCESTOR_ONLY",
            [@"USB\VID_0DB0&PID_1902\MISLEADING"], [], false);
        var globalDiscovery = WindowsControllerDeviceEnumerator.GetHydratedIdentity(
            @"ROOT\GLOBAL",
            [@"USB\VID_0DB0&PID_1902\CLAW_A"], [], null);

        Assert.Equal(((ushort?)0x0DB0, (ushort?)0x1902), root);
        Assert.Equal(((ushort?)null, (ushort?)null), ancestryOnly);
        Assert.Equal(((ushort?)0x0DB0, (ushort?)0x1902), globalDiscovery);
    }

    [Fact]
    public void InterfaceDefaultMultiPidQuery_RemainsCompatibleWithExistingFakes()
    {
        var root = Device(@"USB\VID_0DB0&PID_1902\CLAW_A", 0x1902, null);
        var gamepad = Device(@"HID\VID_0DB0&PID_1902&MI_00&COL01\CLAW_A", 0x1902, root.InstanceId);
        var unrelated = Device(@"HID\VID_045E&PID_0B13\GAMEPAD", 0x0B13, null, 0x045E);
        var enumerator = new ExistingFakeEnumerator([root, gamepad, unrelated]);

        var result = ((IControllerDeviceEnumerator)enumerator).EnumeratePresentDevices(0x0DB0, new ushort[] { 0x1901, 0x1902, 0x1903 });

        Assert.Equal(1, enumerator.GlobalCalls);
        Assert.Contains(root, result);
        Assert.Contains(gamepad, result);
        Assert.DoesNotContain(unrelated, result);
    }

    [Theory]
    [InlineData("SetupDiGetClassDevsW")]
    [InlineData("SetupDiEnumDeviceInfo")]
    [InlineData("SetupDiGetDeviceInstanceIdW")]
    [InlineData("SetupDiGetDeviceRegistryPropertyW")]
    public void NativeFailureDetails_RetainOperationWin32CodeAndRetryableType(string operation)
    {
        var exception = new PnpDeviceReadException(operation, 5,
            propertyId: operation == "SetupDiGetDeviceRegistryPropertyW" ? 1u : null,
            propertyName: operation == "SetupDiGetDeviceRegistryPropertyW" ? "SPDRP_HARDWAREID" : null,
            requiredSize: 16385,
            instanceId: @"HID\VID_0DB0&PID_1902&MI_00&COL01\CLAW_A",
            devInst: 42,
            operationContext: operation == "SetupDiEnumDeviceInfo" ? "Index=7" : null);

        Assert.Equal(operation, exception.Operation);
        Assert.Equal(5, exception.Win32Error);
        Assert.Equal(16385u, exception.RequiredSize);
        Assert.Equal(@"HID\VID_0DB0&PID_1902&MI_00&COL01\CLAW_A", exception.InstanceId);
        Assert.Contains("Win32Error=5", exception.Message);
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        if (operation == "SetupDiGetDeviceRegistryPropertyW")
        {
            Assert.Equal(1u, exception.PropertyId);
            Assert.Contains("SPDRP_HARDWAREID=1", exception.Message);
        }
        if (operation == "SetupDiEnumDeviceInfo") Assert.Equal("Index=7", exception.OperationContext);
    }

    [Fact]
    public void NativeFailureWithoutInstanceId_RetainsDevInstAndExplicitUnavailableMarker()
    {
        var exception = new PnpDeviceReadException("SetupDiGetDeviceInstanceIdW", 122, devInst: 42);

        Assert.True(exception.InstanceIdUnavailable);
        Assert.Contains("DevInst=42", exception.Message);
        Assert.Contains("InstanceIdUnavailable=true", exception.Message);
        Assert.Contains(exception.ToLogFields(), field => field.Key == "InstanceIdUnavailable" && Equals(field.Value, true));
    }

    private static ControllerDeviceInfo Device(string instanceId, ushort productId, string? parent, ushort vendorId = 0x0DB0) =>
        new(instanceId, Guid.NewGuid(), parent, parent is null ? [] : [parent], "HID",
            [$"HID\\VID_{vendorId:X4}&PID_{productId:X4}"], [], "HIDClass", null, null,
            vendorId, productId, true);

    private sealed class ExistingFakeEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        internal int GlobalCalls { get; private set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            GlobalCalls++;
            return devices;
        }
    }
}
