using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PjSip.Net.Accounts;
using PjSip.Net.Calls;
using PjSip.Net.Events;
using PjSip.Net.Internal;
using PjSip.Net.Logging;

namespace PjSip.Net.Tests.Unit;

public class SipCallTests
{
    private static ISipCall CreateCall()
    {
        var options = new SipPhoneOptions();
        var optionsWrapper = Options.Create(options);
        var logger = NullLogger<SipPhone>.Instance;
        var epLogger = NullLogger<PjSipEndpointManager>.Instance;
        var loggerAdapter = new PjSipLoggerAdapter(NullLogger<PjSipLoggerAdapter>.Instance);
        var endpointManager = new PjSipEndpointManager(optionsWrapper, epLogger, loggerAdapter);
        var phone = new SipPhone(endpointManager, optionsWrapper, logger);

        var account = phone.AddAccount(new SipAccountOptions
        {
            Username = "alice",
            Password = "secret",
            Domain = "sip.example.com"
        });

        return phone.MakeCall(account, "sip:bob@example.com");
    }

    [Fact]
    public void Call_InitialState_ShouldBeNull()
    {
        var call = CreateCall();
        call.State.Should().Be(SipCallState.Null);
    }

    [Fact]
    public void Hangup_ShouldTransitionToDisconnected()
    {
        var call = CreateCall();
        call.Hangup();
        call.State.Should().Be(SipCallState.Disconnected);
    }

    [Fact]
    public void Answer_ShouldTransitionToConnecting()
    {
        var call = CreateCall();
        call.Answer();
        call.State.Should().Be(SipCallState.Connecting);
    }

    [Fact]
    public void StateChanged_ShouldFireOnHangup()
    {
        var call = CreateCall();
        CallStateChangedEventArgs? receivedArgs = null;
        call.StateChanged += (s, e) => receivedArgs = e;

        call.Hangup();

        receivedArgs.Should().NotBeNull();
        receivedArgs!.NewState.Should().Be(SipCallState.Disconnected);
        receivedArgs.OldState.Should().Be(SipCallState.Null);
    }

    [Fact]
    public void Call_Direction_ShouldBeOutgoing()
    {
        var call = CreateCall();
        call.Direction.Should().Be(CallDirection.Outgoing);
    }

    [Fact]
    public void IsMuted_ShouldDefaultToFalse()
    {
        var call = CreateCall();
        call.IsMuted.Should().BeFalse();
    }

    [Fact]
    public void SetMute_ShouldUpdateIsMuted()
    {
        var call = CreateCall();
        call.SetMute(true);
        call.IsMuted.Should().BeTrue();
        call.SetMute(false);
        call.IsMuted.Should().BeFalse();
    }

    [Fact]
    public void IsOnHold_ShouldDefaultToFalse()
    {
        var call = CreateCall();
        call.IsOnHold.Should().BeFalse();
    }

    [Fact]
    public void Hold_ShouldUpdateIsOnHold()
    {
        var call = CreateCall();
        call.Hold();
        call.IsOnHold.Should().BeTrue();
        call.Unhold();
        call.IsOnHold.Should().BeFalse();
    }

    [Fact]
    public void CustomHeaders_ShouldBeEmptyByDefault()
    {
        var call = CreateCall();
        call.CustomHeaders.Should().BeEmpty();
    }
}
