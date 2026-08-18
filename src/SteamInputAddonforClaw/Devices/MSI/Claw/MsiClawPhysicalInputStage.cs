using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawPhysicalInputStage : IRoutingPipelineStage, IMsiClawPhysicalInputIdentityProvider
{
    internal event Action? PhysicalSessionRetired;
    private readonly Func<IDirectInputDeviceEnumerator> _enumeratorFactory;
    private readonly IMsiClawPreparedInputSource _inputSource;
    private readonly Lock _sync = new();
    private DirectInputDeviceDescriptor? _preparedDescriptor;
    private bool _ownsInputSession;
    private MsiClawPhysicalInputIdentity? _currentIdentity;
    private long _sessionGeneration;

    internal MsiClawPhysicalInputStage(Func<IDirectInputDeviceEnumerator> enumeratorFactory, IMsiClawPreparedInputSource inputSource)
    {
        _enumeratorFactory = enumeratorFactory ?? throw new ArgumentNullException(nameof(enumeratorFactory));
        _inputSource = inputSource ?? throw new ArgumentNullException(nameof(inputSource));
    }

    public RoutingStageKind Kind => RoutingStageKind.PhysicalInput;
    public MsiClawPhysicalInputIdentity? CurrentIdentity { get { lock (_sync) return _currentIdentity; } }
    public long CurrentSessionGeneration { get { lock (_sync) return _sessionGeneration; } }

    public ValueTask<RoutingStageOperationResult> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IDirectInputDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = _enumeratorFactory();
            var selection = MsiClawDirectInputDeviceSelector.Select(enumerator.EnumerateGameControllers());
            return ValueTask.FromResult(selection.IsSelected
                ? RoutingStageOperationResult.Success(selection.Reason)
                : RoutingStageOperationResult.Failure(selection.Reason));
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(RoutingStageOperationResult.Failure(exception.GetType().Name));
        }
        finally
        {
            enumerator?.Dispose();
        }
    }

    public ValueTask<RoutingStageOperationResult> PrepareMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_ownsInputSession || _preparedDescriptor is not null)
                return ValueTask.FromResult(RoutingStageOperationResult.Failure("PhysicalInputAlreadyPrepared"));
            if (_inputSource.IsRunning)
                return ValueTask.FromResult(RoutingStageOperationResult.Failure("InputSourceAlreadyRunning"));
        }

        IDirectInputDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = _enumeratorFactory();
            var selection = MsiClawDirectInputDeviceSelector.Select(enumerator.EnumerateGameControllers());
            if (!selection.IsSelected)
                return ValueTask.FromResult(RoutingStageOperationResult.Failure(selection.Reason));
            lock (_sync) _preparedDescriptor = selection.Descriptor;
            return ValueTask.FromResult(RoutingStageOperationResult.Success(selection.Reason));
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(RoutingStageOperationResult.Failure(exception.GetType().Name));
        }
        finally
        {
            enumerator?.Dispose();
        }
    }

    public async ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectInputDeviceDescriptor? descriptor;
        lock (_sync) descriptor = _preparedDescriptor;
        if (descriptor is null)
            return RoutingStageOperationResult.Failure("PhysicalInputNotPrepared");

        var result = _inputSource.StartPrepared(descriptor);
        if (!result.Started)
            return RoutingStageOperationResult.Failure(result.Status.ToString());
        if (!_inputSource.IsRunning)
            return RoutingStageOperationResult.Failure("InputSourceDidNotStart");
        bool ready;
        try
        {
            ready = await _inputSource.WaitForFirstValidStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await _inputSource.StopAsync().ConfigureAwait(false);
            throw;
        }
        if (!ready)
        {
            await _inputSource.StopAsync().ConfigureAwait(false);
            return RoutingStageOperationResult.Failure("FirstValidStateNotObserved");
        }
        if (!_inputSource.IsRunning)
            return RoutingStageOperationResult.Failure("InputSourceStoppedBeforeReady");
        lock (_sync)
        {
            _ownsInputSession = true;
            _sessionGeneration++;
            _currentIdentity = new(descriptor.InstanceGuid, descriptor.DevicePath!, descriptor.PnpInstanceId!, descriptor.PhysicalIdentity!);
        }
        AppLog.Debug("PhysicalInput", "PhysicalInput selected", ("InstanceGuid", descriptor.InstanceGuid), ("DevicePath", descriptor.DevicePath), ("PnpInstanceId", descriptor.PnpInstanceId), ("PhysicalIdentity", descriptor.PhysicalIdentity));
        return RoutingStageOperationResult.Success("Started");
    }

    public async ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken)
    {
        bool ownsSession;
        lock (_sync) ownsSession = _ownsInputSession;
        if (ownsSession)
        {
            try
            {
                await _inputSource.StopAsync().ConfigureAwait(false);
                if (_inputSource.IsRunning)
                    return RoutingStageOperationResult.Failure("InputSourceStillRunning");
            }
            catch (Exception exception)
            {
                return RoutingStageOperationResult.Failure(exception.GetType().Name);
            }
        }

        lock (_sync)
        {
            _preparedDescriptor = null;
            _ownsInputSession = false;
            _currentIdentity = null;
            _sessionGeneration++;
        }
        PhysicalSessionRetired?.Invoke();
        return RoutingStageOperationResult.Success("Stopped");
    }
}
