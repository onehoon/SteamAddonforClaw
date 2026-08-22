using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Diagnostics;
using System.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawPhysicalInputStage : IRoutingPipelineStage, IMsiClawPhysicalInputIdentityProvider
{
    internal event Action? PhysicalSessionRetired;
    internal event Action? PhysicalSessionRetiring;
    internal event Action? PhysicalSessionStarted;
    private readonly Func<IDirectInputDeviceEnumerator> _enumeratorFactory;
    private readonly Func<TimeSpan, CancellationToken, Task> _resumeDelay;
    private readonly IMsiClawPreparedInputSource _inputSource;
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private DirectInputDeviceDescriptor? _preparedDescriptor;
    private bool _ownsInputSession;
    private MsiClawPhysicalInputIdentity? _currentIdentity;
    private long _sessionGeneration;
    private bool _suspendPaused;

    private static readonly TimeSpan ResumeRetryWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ResumeRetryInterval = TimeSpan.FromMilliseconds(100);

    internal MsiClawPhysicalInputStage(Func<IDirectInputDeviceEnumerator> enumeratorFactory, IMsiClawPreparedInputSource inputSource, Func<TimeSpan, CancellationToken, Task>? resumeDelay = null)
    {
        _enumeratorFactory = enumeratorFactory ?? throw new ArgumentNullException(nameof(enumeratorFactory));
        _inputSource = inputSource ?? throw new ArgumentNullException(nameof(inputSource));
        _resumeDelay = resumeDelay ?? Task.Delay;
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
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExecuteMutationCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
    }

    private async ValueTask<RoutingStageOperationResult> ExecuteMutationCoreAsync(CancellationToken cancellationToken)
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
            _suspendPaused = false;
            _sessionGeneration++;
            _currentIdentity = new(descriptor.InstanceGuid, descriptor.DevicePath!, descriptor.PnpInstanceId!, descriptor.PhysicalIdentity!);
        }
        PhysicalSessionStarted?.Invoke();
        AppLog.Debug("PhysicalInput", "PhysicalInput selected", ("InstanceGuid", descriptor.InstanceGuid), ("DevicePath", descriptor.DevicePath), ("PnpInstanceId", descriptor.PnpInstanceId), ("PhysicalIdentity", descriptor.PhysicalIdentity));
        return RoutingStageOperationResult.Success("Started");
    }

    public async ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RollbackMutationCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
    }

    private async ValueTask<RoutingStageOperationResult> RollbackMutationCoreAsync(CancellationToken cancellationToken)
    {
        bool ownsSession;
        lock (_sync) ownsSession = _ownsInputSession;
        if (ownsSession)
        {
            PhysicalSessionRetiring?.Invoke();
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
            _suspendPaused = false;
            _currentIdentity = null;
            _sessionGeneration++;
        }
        PhysicalSessionRetired?.Invoke();
        return RoutingStageOperationResult.Success("Stopped");
    }

    internal async ValueTask<RoutingStageOperationResult> PauseForSuspendAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (!_ownsInputSession)
                    return RoutingStageOperationResult.Failure("PhysicalInputNotOwned");
                if (_suspendPaused)
                    return RoutingStageOperationResult.Success("AlreadyPaused");
            }

            AppLog.Info("PhysicalInput", "Physical input suspend pause started.",
                ("Event", "PhysicalInputSuspendPauseStarted"), ("SessionGeneration", CurrentSessionGeneration));
            await _inputSource.StopAsync().ConfigureAwait(false);
            if (_inputSource.IsRunning)
                return RoutingStageOperationResult.Failure("InputSourceStillRunning");

            lock (_sync) _suspendPaused = true;
            AppLog.Info("PhysicalInput", "Physical input suspend pause completed.",
                ("Event", "PhysicalInputSuspendPaused"), ("SessionGeneration", CurrentSessionGeneration));
            return RoutingStageOperationResult.Success("Paused");
        }
        finally { _operationGate.Release(); }
    }

    internal async ValueTask<RoutingStageOperationResult> ResumeAfterSuspendAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            MsiClawPhysicalInputIdentity? identity;
            lock (_sync)
            {
                if (!_ownsInputSession) return RoutingStageOperationResult.Failure("PhysicalInputNotOwned");
                if (!_suspendPaused) return RoutingStageOperationResult.Success("AlreadyRunning");
                identity = _currentIdentity;
            }

            AppLog.Info("PhysicalInput", "Physical input resume reacquire started.",
                ("Event", "PhysicalInputResumeReacquireStarted"), ("SessionGeneration", CurrentSessionGeneration),
                ("PhysicalIdentity", identity!.PhysicalIdentity));
            var deadline = Stopwatch.GetTimestamp() + (long)(ResumeRetryWindow.TotalSeconds * Stopwatch.Frequency);
            var attempt = 0;
            while (attempt < 30 && Stopwatch.GetTimestamp() <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;
                IDirectInputDeviceEnumerator? enumerator = null;
                try
                {
                    enumerator = _enumeratorFactory();
                    var candidates = enumerator.EnumerateGameControllers();
                    var selection = MsiClawDirectInputDeviceSelector.Select(candidates);
                    if (selection.IsSelected && string.Equals(selection.Descriptor!.PhysicalIdentity, identity!.PhysicalIdentity, StringComparison.OrdinalIgnoreCase))
                    {
                        var result = _inputSource.StartPrepared(selection.Descriptor);
                        if (result.Started)
                        {
                            var ready = await _inputSource.WaitForFirstValidStateAsync(cancellationToken).ConfigureAwait(false);
                            if (ready && _inputSource.IsRunning)
                            {
                                lock (_sync)
                                {
                                    _currentIdentity = new(selection.Descriptor.InstanceGuid, selection.Descriptor.DevicePath!, selection.Descriptor.PnpInstanceId!, selection.Descriptor.PhysicalIdentity!);
                                    _suspendPaused = false;
                                }
                                AppLog.Info("PhysicalInput", "Physical input resume reacquired.",
                                    ("Event", "PhysicalInputResumeReacquired"), ("SessionGeneration", CurrentSessionGeneration),
                                    ("InstanceGuid", selection.Descriptor.InstanceGuid), ("PnpInstanceId", selection.Descriptor.PnpInstanceId), ("Attempt", attempt));
                                return RoutingStageOperationResult.Success("Resumed");
                            }
                            await _inputSource.StopAsync().ConfigureAwait(false);
                        }
                        else
                        {
                            AppLog.Debug("PhysicalInput", "Physical input resume is waiting for a usable topology.",
                                ("Event", "PhysicalInputResumeWaitingForTopology"), ("Attempt", attempt), ("Reason", result.Status));
                        }
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    AppLog.Debug("PhysicalInput", "Physical input resume probe failed.", ("Attempt", attempt), ("ExceptionType", exception.GetType().Name));
                }
                finally { enumerator?.Dispose(); }

                await _resumeDelay(ResumeRetryInterval, cancellationToken).ConfigureAwait(false);
            }

            AppLog.Warn("PhysicalInput", "Physical input resume reacquire failed.", null,
                ("Event", "PhysicalInputResumeReacquireFailed"), ("SessionGeneration", CurrentSessionGeneration),
                ("FailureReason", "BoundedTopologyWindowExpired"), ("Attempts", attempt));
            return RoutingStageOperationResult.Failure("ResumeReacquireFailed");
        }
        finally { _operationGate.Release(); }
    }
}
