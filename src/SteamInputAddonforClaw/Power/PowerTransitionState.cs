namespace SteamInputAddonforClaw.Power;

internal enum PowerTransitionState { Awake, Quiescing, Suspended, Recovering, Unsafe }
internal enum PowerSignal { Suspend, ResumeAutomatic, ResumeSuspend, Unknown }
internal sealed record PowerNotificationObservation(uint RawCode, PowerSignal Signal, DateTimeOffset ObservedUtc, long Sequence, int CallbackThreadId, long EpochBefore, long EpochAfter, bool BarrierApplied);
