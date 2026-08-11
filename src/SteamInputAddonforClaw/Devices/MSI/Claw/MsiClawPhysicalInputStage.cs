using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawPhysicalInputStage : IRoutingPipelineStage, IMsiClawPhysicalInputIdentityProvider
{
    private readonly Func<IDirectInputDeviceEnumerator> _enumeratorFactory;
    private readonly IMsiClawPreparedInputSource _inputSource;
    private readonly Lock _sync = new();
    private DirectInputDeviceDescriptor? _preparedDescriptor;
    private bool _ownsInputSession;
    private MsiClawPhysicalInputIdentity? _currentIdentity;

    internal MsiClawPhysicalInputStage(Func<IDirectInputDeviceEnumerator> enumeratorFactory, IMsiClawPreparedInputSource inputSource)
    {
        _enumeratorFactory = enumeratorFactory ?? throw new ArgumentNullException(nameof(enumeratorFactory));
        _inputSource = inputSource ?? throw new ArgumentNullException(nameof(inputSource));
    }

    public RoutingStageKind Kind => RoutingStageKind.PhysicalInput;
    public MsiClawPhysicalInputIdentity? CurrentIdentity { get { lock (_sync) return _currentIdentity; } }

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

    public ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectInputDeviceDescriptor? descriptor;
        lock (_sync) descriptor = _preparedDescriptor;
        if (descriptor is null)
            return ValueTask.FromResult(RoutingStageOperationResult.Failure("PhysicalInputNotPrepared"));

        var result = _inputSource.StartPrepared(descriptor);
        if (!result.Started)
            return ValueTask.FromResult(RoutingStageOperationResult.Failure(result.Status.ToString()));
        if (!_inputSource.IsRunning)
            return ValueTask.FromResult(RoutingStageOperationResult.Failure("InputSourceDidNotStart"));
        lock (_sync)
        {
            _ownsInputSession = true;
            _currentIdentity = new(descriptor.InstanceGuid, descriptor.DevicePath!, descriptor.PnpInstanceId!, descriptor.PhysicalIdentity!);
        }
        return ValueTask.FromResult(RoutingStageOperationResult.Success("Started"));
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
        }
        return RoutingStageOperationResult.Success("Stopped");
    }
}
