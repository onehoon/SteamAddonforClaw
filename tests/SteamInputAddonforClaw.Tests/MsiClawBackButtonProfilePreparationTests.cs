using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using FrontendMsiBackButtonProfileClassification = SteamInputAddonforClaw.Devices.MSI.Claw.MsiClawBackButtonProfileClassification;
using FrontendMsiBackButtonProfileReadOutcome = SteamInputAddonforClaw.Devices.MSI.Claw.MsiClawBackButtonProfileReadOutcome;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawBackButtonProfilePreparationTests
{
    [Fact]
    public void Read_profile_builder_emits_only_the_two_bounded_slot_queries()
    {
        var requests = MsiClawBackButtonProfileProtocol.BuildSlotReadRequests();

        Assert.Equal(2, requests.Count);
        Assert.Collection(requests,
            request => AssertRequest(request, 0x00BA),
            request => AssertRequest(request, 0x0163));
    }

    [Theory]
    [InlineData(0x00BA, 0x59, (int)FrontendMsiBackButtonProfileClassification.ObservedLinuxRightBraceCandidate)]
    [InlineData(0x0163, 0x58, (int)FrontendMsiBackButtonProfileClassification.ObservedLinuxLeftBraceCandidate)]
    public void Read_profile_parser_uses_exact_observed_response_offset_and_retains_payload(
        ushort address,
        byte observedCode,
        int classification)
    {
        var payload = new byte[] { 0x01, 0x04, 0x00, observedCode, 0xFF, 0xFF, 0xFF, 0xFF };
        var response = ReadResponse(address, payload);

        var outcome = MsiClawBackButtonProfileProtocol.ParseReadResponse(response, address, out var parsed);

        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.ReadbackParsed, outcome);
        Assert.Equal(payload, parsed.Payload);
        Assert.Equal((FrontendMsiBackButtonProfileClassification)classification, parsed.Classification);

        response[0] = 0x11;
        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.UnexpectedReport,
            MsiClawBackButtonProfileProtocol.ParseReadResponse(response, address, out _));
        response[0] = 0x10;
        response[3] = 0x00;
        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.UnexpectedReport,
            MsiClawBackButtonProfileProtocol.ParseReadResponse(response, address, out _));
        response[3] = 0x3C;
        response[4] = 0x04;
        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.UnexpectedReport,
            MsiClawBackButtonProfileProtocol.ParseReadResponse(response, address, out _));
        response[4] = 0x05;
        response[5] = 0x00;
        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.WrongAddressOrIndex,
            MsiClawBackButtonProfileProtocol.ParseReadResponse(response, address, out _));
        response[5] = MsiClawBackButtonProfileProtocol.ReadIndex;
        response[8] = 0x07;
        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.WrongAddressOrIndex,
            MsiClawBackButtonProfileProtocol.ParseReadResponse(response, address, out _));
        response[8] = MsiClawBackButtonProfileProtocol.PayloadLength;
        response[6] = 0x02;
        response[7] = 0x4A;
        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.WrongAddressOrIndex,
            MsiClawBackButtonProfileProtocol.ParseReadResponse(response, address, out _));
        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.UnexpectedReport,
            MsiClawBackButtonProfileProtocol.ParseReadResponse(response.AsSpan(0, 17), address, out _));
    }

    [Fact]
    public void Read_profile_parser_preserves_historical_and_unknown_forms_as_evidence_only()
    {
        var historical = ReadResponse(0x00BA, [0x01, 0x04, 0x0C, 0x9D, 0xAA, 0xBB, 0xCC, 0xDD]);
        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.ReadbackParsed,
            MsiClawBackButtonProfileProtocol.ParseReadResponse(historical, 0x00BA, out var oldForm));
        Assert.Equal(FrontendMsiBackButtonProfileClassification.HistoricalMarkerPresent, oldForm.Classification);

        var unknown = ReadResponse(0x0163, [0xF2, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);
        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.ReadbackParsed,
            MsiClawBackButtonProfileProtocol.ParseReadResponse(unknown, 0x0163, out var unknownForm));
        Assert.Equal(FrontendMsiBackButtonProfileClassification.OtherFormat, unknownForm.Classification);
    }

    [Fact]
    public void Production_prepare_builders_emit_only_the_exact_DirectInput_packets()
    {
        AssertWritePacket(MsiClawBackButtonProfileProtocol.BuildDirectInputRestoreWriteRequest(0x00BA), 0x00BA);
        AssertWritePacket(MsiClawBackButtonProfileProtocol.BuildDirectInputRestoreWriteRequest(0x0163), 0x0163);
        var sync = MsiClawBackButtonProfileProtocol.BuildSyncToRomRequest();
        Assert.Equal(64, sync.Length);
        Assert.Equal(new byte[] { 0x0F, 0x00, 0x00, 0x3C, 0x22 }, sync[..5]);
        Assert.All(sync.Skip(5), value => Assert.Equal(0, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => MsiClawBackButtonProfileProtocol.BuildDirectInputRestoreWriteRequest(0x00BB));
    }

    [Theory]
    [InlineData("msi.claw.a2vm.8", 0x0230)]
    [InlineData("msi.claw.cg3em", 0x0414)]
    [InlineData("msi.claw.a2vm.7", 0x7777)]
    public async Task Supported_models_read_two_slots_without_firmware_mutation_or_version_guessing(string modelId, ushort firmware)
    {
        var fixture = new ReaderFixture(firmware);
        var reader = fixture.CreateReader();

        var result = await reader.ReadAsync(modelId, () => fixture.Identity, () => true, CancellationToken.None);

        Assert.Equal(firmware, result.FirmwareVersion);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed, result.M1.Outcome);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed, result.M2.Outcome);
        Assert.Equal(2, fixture.Transport.Requests.Count);
        Assert.Equal(0x00BA, (ushort)((fixture.Transport.Requests[0][6] << 8) | fixture.Transport.Requests[0][7]));
        Assert.Equal(0x0163, (ushort)((fixture.Transport.Requests[1][6] << 8) | fixture.Transport.Requests[1][7]));
        Assert.All(fixture.Transport.Requests, request => Assert.Equal(0x04, request[4]));
        Assert.Empty(fixture.Transport.Mutations);
        Assert.Equal(0x01, result.M1.ReadIndex);
        Assert.Equal(0x08, result.M1.RequestedLength);
        Assert.Equal(FrontendMsiBackButtonProfileClassification.ObservedLinuxRightBraceCandidate, result.M1.Classification);
        Assert.Equal(FrontendMsiBackButtonProfileClassification.ObservedLinuxLeftBraceCandidate, result.M2.Classification);
    }

    [Fact]
    public async Task Unsupported_model_center_m_enabled_weak_owner_and_ambiguous_hid_fail_before_any_query()
    {
        var unsupported = new ReaderFixture(0x0230);
        var unsupportedResult = await unsupported.CreateReader().ReadAsync(
            "unknown-model", () => unsupported.Identity, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.UnsupportedModel, unsupportedResult.M1.Outcome);
        Assert.Empty(unsupported.Transport.Requests);

        var centerMEnabled = new ReaderFixture(0x0230);
        var centerMResult = await centerMEnabled.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => centerMEnabled.Identity, () => false, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, centerMResult.M1.Outcome);
        Assert.Empty(centerMEnabled.Transport.Requests);

        var weakOwner = new ReaderFixture(0x0230);
        var weak = weakOwner.Identity with { Confidence = MsiClawIdentityConfidence.Indeterminate };
        var weakResult = await weakOwner.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => weak, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, weakResult.M1.Outcome);
        Assert.Empty(weakOwner.Transport.Requests);

        var ambiguous = new ReaderFixture(0x0230, duplicateControlHidPath: true);
        var ambiguousResult = await ambiguous.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => ambiguous.Identity, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, ambiguousResult.M1.Outcome);
        Assert.Empty(ambiguous.Transport.Requests);

        var noOwner = new ReaderFixture(0x0230);
        var noOwnerResult = await noOwner.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => null, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, noOwnerResult.M1.Outcome);
        Assert.Empty(noOwner.Transport.Requests);

        var wrongPid = new ReaderFixture(0x0230, wrongPid: true);
        var wrongPidResult = await wrongPid.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => wrongPid.Identity, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, wrongPidResult.M1.Outcome);
        Assert.Empty(wrongPid.Transport.Requests);

        var unavailableTransport = new ReaderFixture(0x0230);
        unavailableTransport.Transport.AttributesAvailable = false;
        var unavailableResult = await unavailableTransport.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => unavailableTransport.Identity, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.TransportUnavailable, unavailableResult.M1.Outcome);
        Assert.Empty(unavailableTransport.Transport.Requests);
    }

    [Fact]
    public async Task Profile_reader_classifies_no_reply_unexpected_ack_and_cancellation_without_extra_commands()
    {
        var timeout = new ReaderFixture(0x0230);
        timeout.Transport.Exchange = _ => new(true, []);
        var timeoutResult = await timeout.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => timeout.Identity, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.NoReplyOrTimeout, timeoutResult.M1.Outcome);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.NoReplyOrTimeout, timeoutResult.M2.Outcome);
        Assert.Equal(2, timeout.Transport.Requests.Count);

        var wrongAck = new ReaderFixture(0x0414);
        wrongAck.Transport.Exchange = request =>
        {
            var address = (ushort)((request[6] << 8) | request[7]);
            var report = ReadResponse(address, new byte[] { 0x01, 0x04, 0x0C, 0x9D, 1, 2, 3, 4 });
            report[4] = 0x21;
            return new(true, [report]);
        };
        var wrongAckResult = await wrongAck.CreateReader().ReadAsync(
            "msi.claw.cg3em", () => wrongAck.Identity, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.UnexpectedReport, wrongAckResult.M1.Outcome);
        Assert.Equal(2, wrongAck.Transport.Requests.Count);

        var cancelled = new ReaderFixture(0x0230);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledResult = await cancelled.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => cancelled.Identity, () => true, cancellation.Token);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.Cancelled, cancelledResult.M1.Outcome);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.Cancelled, cancelledResult.M2.Outcome);
        Assert.Empty(cancelled.Transport.Requests);
    }

    [Theory]
    [InlineData("msi.claw.a2vm.8", 0x0230)]
    [InlineData("msi.claw.cg3em", 0x0414)]
    [InlineData("msi.claw.cg3em", 0x0419)]
    public async Task Production_prepare_writes_only_exact_candidate_pair_then_syncs_and_verifies_profile_readback(
        string modelId,
        int firmwareValue)
    {
        var firmware = (ushort)firmwareValue;
        // EX/CG3EM coverage is contract-only, not hardware evidence.
        var fixture = new ReaderFixture(firmware);
        var result = await fixture.CreateReader().PrepareDirectInputProfileAsync(
            modelId, "Startup", () => fixture.AllowOwner ? fixture.Identity : null,
            () => true, () => true, CancellationToken.None);

        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.RestoredProfileReadback, result.Outcome);
        Assert.True(result.ProfileReadbackVerified);
        Assert.Contains("not a physical button test", result.Status, StringComparison.Ordinal);
        Assert.Equal("01040059FFFFFFFF", result.M1.BeforePayloadHex);
        Assert.Equal("01040058FFFFFFFF", result.M2.BeforePayloadHex);
        Assert.Equal("01000059FFFFFFFF", result.M1.AfterPayloadHex);
        Assert.Equal("01000058FFFFFFFF", result.M2.AfterPayloadHex);
        Assert.True(result.M1.WriteIssued && result.M1.WriteSucceeded);
        Assert.True(result.M2.WriteIssued && result.M2.WriteSucceeded);
        Assert.True(result.RomSyncIssued && result.RomSyncTransportSucceeded);
        Assert.True(result.ReadbackObserved);
        Assert.Equal(3, fixture.Transport.Mutations.Count);
        AssertWritePacket(fixture.Transport.Mutations[0], 0x00BA);
        AssertWritePacket(fixture.Transport.Mutations[1], 0x0163);
        Assert.Equal((byte)0x22, fixture.Transport.Mutations[2][4]);
        Assert.Equal(1, fixture.Transport.SyncCount);
        Assert.Equal(4, fixture.Transport.Requests.Count);
    }

    [Theory]
    [InlineData("msi.claw.a2vm.7", 0x0230)]
    [InlineData("msi.claw.a2vm.8", 0x0231)]
    [InlineData("msi.claw.cg3em", 0x0413)]
    [InlineData("unknown-model", 0x0230)]
    public async Task Production_prepare_rejects_models_and_firmware_outside_the_exact_allowlist(string modelId, ushort firmware)
    {
        var fixture = new ReaderFixture(firmware);
        var result = await fixture.CreateReader().PrepareDirectInputProfileAsync(
            modelId, "Startup", () => fixture.Identity, () => true, () => true, CancellationToken.None);

        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.UnsupportedModelOrFirmware, result.Outcome);
        Assert.Empty(fixture.Transport.Mutations);
    }

    [Fact]
    public async Task Production_prepare_rejects_unavailable_ambiguous_wrong_pid_and_CenterM_sessions_without_writes()
    {
        var noOwner = new ReaderFixture(0x0230);
        var noOwnerResult = await noOwner.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => null, () => true, () => true, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.OwnerUnavailable, noOwnerResult.Outcome);
        Assert.Empty(noOwner.Transport.Mutations);

        var centerM = new ReaderFixture(0x0230);
        var centerMResult = await centerM.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => centerM.Identity, () => false, () => true, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.OwnerUnavailable, centerMResult.Outcome);
        Assert.Empty(centerM.Transport.Mutations);

        var unsafeRuntime = new ReaderFixture(0x0230);
        var unsafeResult = await unsafeRuntime.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => unsafeRuntime.Identity, () => true, () => false, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.OwnerUnavailable, unsafeResult.Outcome);
        Assert.Empty(unsafeRuntime.Transport.Mutations);

        var ambiguous = new ReaderFixture(0x0230, duplicateControlHidPath: true);
        var ambiguousResult = await ambiguous.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => ambiguous.Identity, () => true, () => true, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.Unverified, ambiguousResult.Outcome);
        Assert.Empty(ambiguous.Transport.Mutations);

        var wrongPid = new ReaderFixture(0x0230, wrongPid: true);
        var wrongPidResult = await wrongPid.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => wrongPid.Identity, () => true, () => true, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.OwnerUnavailable, wrongPidResult.Outcome);
        Assert.Empty(wrongPid.Transport.Mutations);
    }

    [Fact]
    public async Task Production_prepare_honors_cancellation_and_preserves_unknown_or_prepared_profiles()
    {
        var cancelled = new ReaderFixture(0x0230);
        using var cancellation = new CancellationTokenSource();
        cancelled.Transport.AfterExchange = count => { if (count == 2) cancellation.Cancel(); };
        var cancelledResult = await cancelled.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => cancelled.Identity, () => true, () => true, cancellation.Token);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.OwnerUnavailable, cancelledResult.Outcome);
        Assert.Empty(cancelled.Transport.Mutations);

        var unknown = new ReaderFixture(0x0230);
        unknown.Transport.SetPayloads([0x01, 0x04, 0x0C, 0x11, 0x22, 0x33, 0x44, 0x55], [0x01, 0x04, 0x00, 0x58, 0xFF, 0xFF, 0xFF, 0xFF]);
        var unknownResult = await unknown.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => unknown.Identity, () => true, () => true, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.MixedOrUnknownProfilePreserved, unknownResult.Outcome);
        Assert.Equal(FrontendMsiBackButtonProfileClassification.HistoricalMarkerPresent, unknownResult.M1.BeforeClassification);
        Assert.Equal(FrontendMsiBackButtonProfileClassification.ObservedLinuxLeftBraceCandidate, unknownResult.M2.BeforeClassification);
        Assert.Empty(unknown.Transport.Mutations);

        var alreadyPrepared = new ReaderFixture(0x0230);
        alreadyPrepared.Transport.SetPayloads([0x01, 0x00, 0xA0, 0x99, 1, 2, 3, 4], [0x01, 0x00, 0x12, 0x34, 5, 6, 7, 8]);
        var preparedResult = await alreadyPrepared.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => alreadyPrepared.Identity, () => true, () => true, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.AlreadyPrepared, preparedResult.Outcome);
        Assert.Empty(alreadyPrepared.Transport.Mutations);

        var mixedPreparedAndCandidate = new ReaderFixture(0x0230);
        mixedPreparedAndCandidate.Transport.SetPayloads(
            [0x01, 0x00, 0xA0, 0x99, 1, 2, 3, 4],
            [0x01, 0x04, 0x00, 0x58, 0xFF, 0xFF, 0xFF, 0xFF]);
        var mixedResult = await mixedPreparedAndCandidate.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => mixedPreparedAndCandidate.Identity, () => true, () => true, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.MixedOrUnknownProfilePreserved, mixedResult.Outcome);
        Assert.Empty(mixedPreparedAndCandidate.Transport.Mutations);
    }

    [Fact]
    public async Task Production_prepare_read_timeout_and_unrecognized_EX_profile_never_mutate_firmware()
    {
        var timedOut = new ReaderFixture(0x0230);
        timedOut.Transport.Exchange = _ => new(true, []);
        var timeoutResult = await timedOut.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => timedOut.Identity, () => true, () => true, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.Unverified, timeoutResult.Outcome);
        Assert.Equal(2, timedOut.Transport.Requests.Count);
        Assert.Empty(timedOut.Transport.Mutations);

        var unknownEx = new ReaderFixture(0x0414);
        unknownEx.Transport.SetPayloads(
            [0x01, 0x04, 0x55, 0x11, 0x22, 0x33, 0x44, 0x55],
            [0x01, 0x04, 0x66, 0x88, 0x22, 0x33, 0x44, 0x55]);
        var unknownExResult = await unknownEx.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.cg3em", "Startup", () => unknownEx.Identity, () => true, () => true, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.MixedOrUnknownProfilePreserved, unknownExResult.Outcome);
        Assert.Empty(unknownEx.Transport.Mutations);
    }

    [Fact]
    public async Task Production_prepare_stops_on_write_failure_or_owner_loss_and_never_syncs_an_incomplete_pair()
    {
        var firstWriteFails = new ReaderFixture(0x0230);
        firstWriteFails.Transport.FailWriteAt = 1;
        var firstFailure = await firstWriteFails.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => firstWriteFails.Identity,
            () => true, () => true, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.PartialVolatileWritePossible, firstFailure.Outcome);
        Assert.True(firstFailure.M1.WriteIssued);
        Assert.False(firstFailure.M1.WriteSucceeded);
        Assert.False(firstFailure.M2.WriteIssued);
        Assert.False(firstFailure.RomSyncIssued);
        Assert.Single(firstWriteFails.Transport.Mutations);

        var secondWriteFails = new ReaderFixture(0x0230);
        secondWriteFails.Transport.FailWriteAt = 2;
        var failedResult = await secondWriteFails.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => secondWriteFails.AllowOwner ? secondWriteFails.Identity : null,
            () => true, () => true, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.PartialVolatileWritePossible, failedResult.Outcome);
        Assert.True(failedResult.M1.WriteSucceeded);
        Assert.True(failedResult.M2.WriteIssued);
        Assert.False(failedResult.M2.WriteSucceeded);
        Assert.False(failedResult.RomSyncIssued);
        Assert.Equal(2, secondWriteFails.Transport.Mutations.Count);

        var ownerLost = new ReaderFixture(0x0230);
        ownerLost.Transport.AfterWrite = count => { if (count == 1) ownerLost.AllowOwner = false; };
        var ownerLostResult = await ownerLost.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => ownerLost.AllowOwner ? ownerLost.Identity : null,
            () => true, () => ownerLost.AllowOwner, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.PartialVolatileWritePossible, ownerLostResult.Outcome);
        Assert.Single(ownerLost.Transport.Mutations);
        Assert.False(ownerLostResult.M2.WriteIssued);
        Assert.False(ownerLostResult.RomSyncIssued);

        var pathChanged = new ReaderFixture(0x0230);
        pathChanged.Transport.AfterWrite = count =>
        {
            if (count == 1) pathChanged.HidLookup.DevicePathOverride = "changed-control-path";
        };
        var changedPathResult = await pathChanged.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => pathChanged.Identity, () => true, () => true, CancellationToken.None);
        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.PartialVolatileWritePossible, changedPathResult.Outcome);
        Assert.Single(pathChanged.Transport.Mutations);
        Assert.False(changedPathResult.M2.WriteIssued);
        Assert.False(changedPathResult.RomSyncIssued);
    }

    [Fact]
    public async Task Production_prepare_does_not_verify_profile_readback_when_rom_sync_fails()
    {
        var fixture = new ReaderFixture(0x0230);
        fixture.Transport.FailWriteAt = 3;

        var result = await fixture.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.a2vm.8", "Startup", () => fixture.Identity, () => true, () => true, CancellationToken.None);

        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.Unverified, result.Outcome);
        Assert.True(result.WriteTransportSucceeded);
        Assert.True(result.RomSyncIssued);
        Assert.False(result.RomSyncTransportSucceeded);
        Assert.True(result.ReadbackObserved);
        Assert.False(result.ProfileReadbackVerified);
        Assert.Equal("01000059FFFFFFFF", result.M1.AfterPayloadHex);
        Assert.Equal("01000058FFFFFFFF", result.M2.AfterPayloadHex);
        Assert.Contains("ROM sync transport failed", result.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_prepare_mismatched_post_read_is_unverified_even_when_transport_and_sync_succeed()
    {
        var fixture = new ReaderFixture(0x0419);
        fixture.Transport.ApplyWritesToProfiles = false;
        var result = await fixture.CreateReader().PrepareDirectInputProfileAsync(
            "msi.claw.cg3em", "Startup", () => fixture.Identity, () => true, () => true, CancellationToken.None);

        Assert.Equal(MsiClawBackButtonProductionPrepareOutcome.Unverified, result.Outcome);
        Assert.True(result.WriteTransportSucceeded);
        Assert.True(result.RomSyncIssued && result.RomSyncTransportSucceeded);
        Assert.True(result.ReadbackObserved);
        Assert.False(result.ProfileReadbackVerified);
        Assert.Equal("01040059FFFFFFFF", result.M1.AfterPayloadHex);
        Assert.Equal("01040058FFFFFFFF", result.M2.AfterPayloadHex);
    }

    private static void AssertRequest(byte[] request, ushort address)
    {
        Assert.Equal(64, request.Length);
        Assert.Equal(0x0F, request[0]);
        Assert.Equal(0x3C, request[3]);
        Assert.Equal(0x04, request[4]);
        Assert.Equal(0x01, request[5]);
        Assert.Equal((byte)(address >> 8), request[6]);
        Assert.Equal((byte)address, request[7]);
        Assert.Equal(0x08, request[8]);
        Assert.All(request.Skip(9), value => Assert.Equal(0, value));
        Assert.DoesNotContain(request[4], new byte[] { 0x21, 0x22, 0x24 });
    }

    private static void AssertWritePacket(byte[] request, ushort address)
    {
        Assert.Equal(64, request.Length);
        Assert.Equal(0x0F, request[0]);
        Assert.Equal(0x3C, request[3]);
        Assert.Equal(0x21, request[4]);
        Assert.Equal(0x01, request[5]);
        Assert.Equal((byte)(address >> 8), request[6]);
        Assert.Equal((byte)address, request[7]);
        Assert.Equal(0x02, request[8]);
        Assert.Equal(0x01, request[9]);
        Assert.Equal(0x00, request[10]);
        Assert.All(request.Skip(11), value => Assert.Equal(0, value));
        Assert.DoesNotContain(request[4], new byte[] { 0x07, 0x24 });
    }

    private static byte[] ReadResponse(ushort address, byte[] payload)
    {
        var response = new byte[64];
        response[0] = 0x10;
        response[3] = 0x3C;
        response[4] = 0x05;
        response[5] = MsiClawBackButtonProfileProtocol.ReadIndex;
        response[6] = (byte)(address >> 8);
        response[7] = (byte)address;
        response[8] = MsiClawBackButtonProfileProtocol.PayloadLength;
        payload.CopyTo(response, 9);
        return response;
    }

    private sealed class ReaderFixture
    {
        private const string DevicePath = "exact-control-path";
        private readonly ControllerDeviceInfo[] _devices;
        private readonly MsiClawHidDeviceInformation[] _hidInformation;

        internal ReaderFixture(ushort firmware, bool duplicateControlHidPath = false, bool wrongPid = false)
        {
            var containerId = Guid.NewGuid();
            const string instanceId = "HID\\VID_0DB0&PID_1902&MI_00&COL01\\CLAW_A";
            var control = new ControllerDeviceInfo(
                instanceId,
                containerId,
                "USB\\VID_0DB0&PID_1902\\CLAW_A",
                ["USB\\VID_0DB0&PID_1902\\CLAW_A"],
                "HID", ["HID\\VID_0DB0&PID_1902"], [], "HIDClass", null, "HidUsb",
                0x0DB0, wrongPid ? (ushort)0x1901 : (ushort)0x1902, true, "MSI Claw Control", 0xFFF0, 0x0040);
            _devices = [control];
            var info = new MsiClawHidDeviceInformation(DevicePath, instanceId, containerId);
            _hidInformation = duplicateControlHidPath ? [info, info] : [info];
            HidLookup = new FixedHidLookup(_hidInformation);
            Identity = MsiClawPhysicalIdentity.From(control);
            Transport = new RecordingTransport(firmware);
        }

        internal MsiClawPhysicalIdentity Identity { get; }
        internal RecordingTransport Transport { get; }
        internal FixedHidLookup HidLookup { get; }
        internal bool AllowOwner { get; set; } = true;

        internal MsiClawBackButtonProfileReader CreateReader() => new(
            new FixedDeviceEnumerator(_devices),
            new MsiClawControlHidResolver(),
            HidLookup,
            Transport);
    }

    private sealed class FixedDeviceEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices;
    }

    internal sealed class FixedHidLookup(IReadOnlyList<MsiClawHidDeviceInformation> information) : IMsiClawHidDeviceInformationLookup
    {
        internal string? DevicePathOverride { get; set; }

        public Task<IReadOnlyList<MsiClawHidDeviceInformation>> FindAsync(string selector, CancellationToken cancellationToken)
        {
            IReadOnlyList<MsiClawHidDeviceInformation> resolved = DevicePathOverride is { } path
                ? information.Select(info => info with { Id = path }).ToArray()
                : information;
            return Task.FromResult(resolved);
        }
    }

    private sealed class RecordingTransport(ushort firmware) : IMsiClawRawHidTransport
    {
        private readonly Dictionary<ushort, byte[]> _profiles = new()
        {
            [0x00BA] = [0x01, 0x04, 0x00, 0x59, 0xFF, 0xFF, 0xFF, 0xFF],
            [0x0163] = [0x01, 0x04, 0x00, 0x58, 0xFF, 0xFF, 0xFF, 0xFF],
        };

        public List<byte[]> Requests { get; } = [];
        public List<byte[]> Mutations { get; } = [];
        public bool AttributesAvailable { get; set; } = true;
        public Func<byte[], MsiClawHidWriteAndReadResult>? Exchange { get; set; }
        public Action<int>? AfterExchange { get; set; }
        public Action<int>? AfterWrite { get; set; }
        public int FailWriteAt { get; set; }
        public int SyncCount { get; private set; }
        public bool ApplyWritesToProfiles { get; set; } = true;

        public void SetPayloads(byte[] m1, byte[] m2)
        {
            _profiles[0x00BA] = (byte[])m1.Clone();
            _profiles[0x0163] = (byte[])m2.Clone();
        }

        public bool TryGetDeviceAttributes(string devicePath, out MsiClawHidDeviceAttributes attributes)
        {
            attributes = new(MsiClawHardware.VendorId, MsiClawHardware.DirectInputProductId, firmware);
            return AttributesAvailable && devicePath == "exact-control-path";
        }

        public Task<bool> WriteAsync(string devicePath, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packet = bytes.ToArray();
            Mutations.Add(packet);
            var succeeded = FailWriteAt != Mutations.Count;
            if (succeeded && packet[4] == 0x21 && ApplyWritesToProfiles)
            {
                var address = (ushort)((packet[6] << 8) | packet[7]);
                if (_profiles.TryGetValue(address, out var profile))
                {
                    profile[0] = packet[9];
                    profile[1] = packet[10];
                }
            }
            if (succeeded && packet[4] == 0x22)
                SyncCount++;
            AfterWrite?.Invoke(Mutations.Count);
            return Task.FromResult(succeeded);
        }

        public Task<MsiClawHidWriteAndReadResult> WriteAndReadAsync(
            string devicePath, ReadOnlyMemory<byte> bytes, int reportLength, int maxReports,
            TimeSpan timeout, CancellationToken cancellationToken, bool preserveShortReports = false)
        {
            var request = bytes.ToArray();
            Requests.Add(request);
            if (Exchange is not null) return Task.FromResult(Exchange(request));
            var address = (ushort)((request[6] << 8) | request[7]);
            var result = new MsiClawHidWriteAndReadResult(true, [ReadResponse(address, _profiles[address])]);
            AfterExchange?.Invoke(Requests.Count);
            return Task.FromResult(result);
        }
    }
}
