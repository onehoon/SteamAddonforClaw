using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMMainUiMutexOwnershipTests
{
    [Fact]
    public void First_acquisition_succeeds()
    {
        using var ownership = new CenterMMainUiMutexOwnership(new FakeFactory());

        Assert.Equal(CenterMMainUiMutexAcquireResult.Acquired, ownership.Acquire());
        Assert.True(ownership.IsOwned);
    }

    [Fact]
    public void Repeated_acquisition_through_the_same_owner_is_idempotent()
    {
        var factory = new FakeFactory();
        using var ownership = new CenterMMainUiMutexOwnership(factory);

        Assert.Equal(CenterMMainUiMutexAcquireResult.Acquired, ownership.Acquire());
        Assert.Equal(CenterMMainUiMutexAcquireResult.AlreadyOwnedByThis, ownership.Acquire());

        Assert.Equal(1, factory.CreateCallCount);
    }

    [Fact]
    public void Collision_with_an_existing_holder_is_classified_as_unavailable_and_not_stolen()
    {
        var factory = new FakeFactory { NextCreatedNew = false };
        using var ownership = new CenterMMainUiMutexOwnership(factory);

        var result = ownership.Acquire();

        Assert.Equal(CenterMMainUiMutexAcquireResult.Unavailable, result);
        Assert.False(ownership.IsOwned);
        // The handle from the failed (not-actually-ours) create attempt must be disposed, never
        // retained as if this instance owned it.
        Assert.True(factory.LastHandle!.Disposed);
    }

    [Fact]
    public void Creation_failure_is_classified_as_unavailable()
    {
        var factory = new FakeFactory { ThrowOnCreate = true };
        using var ownership = new CenterMMainUiMutexOwnership(factory);

        Assert.Equal(CenterMMainUiMutexAcquireResult.Unavailable, ownership.Acquire());
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void Release_is_idempotent()
    {
        var factory = new FakeFactory();
        using var ownership = new CenterMMainUiMutexOwnership(factory);
        ownership.Acquire();

        Assert.True(ownership.Release());
        Assert.True(ownership.Release());
        Assert.False(ownership.IsOwned);
        Assert.True(factory.LastHandle!.Disposed);
    }

    [Fact]
    public void Release_without_ever_acquiring_is_a_no_op()
    {
        using var ownership = new CenterMMainUiMutexOwnership(new FakeFactory());

        Assert.True(ownership.Release());
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void Dispose_releases_ownership()
    {
        var factory = new FakeFactory();
        var ownership = new CenterMMainUiMutexOwnership(factory);
        ownership.Acquire();

        ownership.Dispose();

        Assert.False(ownership.IsOwned);
        Assert.True(factory.LastHandle!.Disposed);
    }

    [Fact]
    public void Failure_never_produces_owned_state()
    {
        var factory = new FakeFactory { NextCreatedNew = false };
        using var ownership = new CenterMMainUiMutexOwnership(factory);

        ownership.Acquire();

        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void Production_mutex_name_matches_the_real_MainUI_singleton_resource()
    {
        Assert.Equal(@"Local\MSI Center M.exe", CenterMMainUiMutexOwnership.MutexName);
    }

    private sealed class FakeHandle : ICenterMMainUiMutexHandle
    {
        internal bool Released { get; private set; }
        internal bool Disposed { get; private set; }
        public void ReleaseMutex() => Released = true;
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeFactory : ICenterMMainUiMutexFactory
    {
        internal bool NextCreatedNew { get; set; } = true;
        internal bool ThrowOnCreate { get; set; }
        internal int CreateCallCount { get; private set; }
        internal FakeHandle? LastHandle { get; private set; }
        internal string? LastRequestedName { get; private set; }

        public (ICenterMMainUiMutexHandle Handle, bool CreatedNew) Create(string name)
        {
            CreateCallCount++;
            LastRequestedName = name;
            if (ThrowOnCreate) throw new UnauthorizedAccessException("simulated");
            var handle = new FakeHandle();
            LastHandle = handle;
            return (handle, NextCreatedNew);
        }
    }
}
