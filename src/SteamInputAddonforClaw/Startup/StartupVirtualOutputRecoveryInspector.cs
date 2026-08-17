using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Startup;

internal sealed class StartupVirtualOutputRecoveryInspector : IStartupVirtualOutputRecoveryInspector
{
    private readonly IControllerDeviceEnumerator _deviceEnumerator;
    private readonly TimeSpan _pollInterval;
    private readonly int _requiredStableSnapshots;
    private readonly int _maximumSnapshots;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    internal StartupVirtualOutputRecoveryInspector(
        IControllerDeviceEnumerator deviceEnumerator,
        TimeSpan? pollInterval = null,
        int requiredStableSnapshots = 2,
        int maximumSnapshots = 5,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _deviceEnumerator = deviceEnumerator ?? throw new ArgumentNullException(nameof(deviceEnumerator));
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
        _requiredStableSnapshots = requiredStableSnapshots > 0 ? requiredStableSnapshots : throw new ArgumentOutOfRangeException(nameof(requiredStableSnapshots));
        _maximumSnapshots = maximumSnapshots >= _requiredStableSnapshots ? maximumSnapshots : throw new ArgumentOutOfRangeException(nameof(maximumSnapshots));
        _delay = delay ?? Task.Delay;
    }

    public async Task<StartupVirtualOutputRecoveryAssessment> AssessAsync(
        IReadOnlyList<AddonOwnedVirtualDeviceRecoveryEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries is null || entries.Count == 0)
            return new(true, "NoVirtualOutputEvidence");
        if (!Validate(entries, out var validationReason))
            return new(false, validationReason);

        var stable = 0;
        for (var attempt = 0; attempt < _maximumSnapshots; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ControllerDeviceInfo> current;
            try
            {
                current = _deviceEnumerator.EnumeratePresentDevices();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new(false, "EnumerationFailed");
            }

            var assessment = AssessSnapshot(entries, current);
            if (!assessment.SafeToRetire)
                return assessment;
            stable++;
            if (attempt + 1 < _maximumSnapshots)
                await _delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }

        return stable >= _requiredStableSnapshots
            ? new(true, "StableSafeAbsence")
            : new(false, "SafeAbsenceDidNotStabilize");
    }

    private static StartupVirtualOutputRecoveryAssessment AssessSnapshot(
        IReadOnlyList<AddonOwnedVirtualDeviceRecoveryEntry> entries,
        IReadOnlyList<ControllerDeviceInfo> current)
    {
        var currentExactIds = current
            .Where(device => device.Present && device.VendorId == SteamDeckVirtualDeviceIdentityPolicy.VendorId && device.ProductId == SteamDeckVirtualDeviceIdentityPolicy.ProductId)
            .Select(device => device.InstanceId.Trim())
            .Where(id => id.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var resolved = entry.ResolvedInstanceIds.Select(id => id.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var preExisting = entry.PreExistingMatchingInstanceIds.Select(id => id.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (resolved.Any(currentExactIds.Contains))
                return new(false, "RecordedOwnedVirtualDeviceStillPresent");
            if (currentExactIds.Except(preExisting, StringComparer.OrdinalIgnoreCase).Any())
                return new(false, "ResidualOrAmbiguousVirtualDevicePresent");
        }
        return new(true, "SafeSnapshot");
    }

    private static bool Validate(IReadOnlyList<AddonOwnedVirtualDeviceRecoveryEntry> entries, out string reason)
    {
        var mutations = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            if (entry.MutationId == Guid.Empty || !mutations.Add(entry.MutationId))
            {
                reason = "EvidenceInvalid";
                return false;
            }
            if (!string.Equals(entry.DeviceType, "steamdeck", StringComparison.OrdinalIgnoreCase) ||
                entry.VendorId != SteamDeckVirtualDeviceIdentityPolicy.VendorId ||
                entry.ProductId != SteamDeckVirtualDeviceIdentityPolicy.ProductId)
            {
                reason = "EvidenceInvalid";
                return false;
            }
            var preExisting = entry.PreExistingMatchingInstanceIds;
            var resolved = entry.ResolvedInstanceIds;
            if (preExisting is null || resolved is null)
            {
                reason = "EvidenceInvalid";
                return false;
            }
            if (preExisting.Any(string.IsNullOrWhiteSpace) || resolved.Any(string.IsNullOrWhiteSpace) ||
                preExisting.GroupBy(id => id.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1) ||
                resolved.GroupBy(id => id.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1) ||
                preExisting.Any(id => resolved.Contains(id, StringComparer.OrdinalIgnoreCase)))
            {
                reason = "EvidenceInvalid";
                return false;
            }
        }
        reason = "Valid";
        return true;
    }
}
