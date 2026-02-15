using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PjSip.Net.Accounts;
using PjSip.Net.Calls;
using PjSip.Net.Internal;
using PjSip.Net.Logging;

namespace PjSip.Net.Tests.Unit;

public class SipAccountTests
{
    private static (SipPhone phone, ISipAccount account) CreatePhoneWithAccount()
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
            Domain = "sip.example.com",
            Registrar = "sip:sip.example.com"
        });

        return (phone, account);
    }

    [Fact]
    public void Account_ShouldHaveCorrectUri()
    {
        var (phone, account) = CreatePhoneWithAccount();
        using (phone)
        {
            account.Uri.Should().Be("sip:alice@sip.example.com");
        }
    }

    [Fact]
    public void Account_InitialState_ShouldBeUnregistered()
    {
        var (phone, account) = CreatePhoneWithAccount();
        using (phone)
        {
            account.RegistrationState.Should().Be(SipRegistrationState.Unregistered);
        }
    }

    [Fact]
    public void MakeCall_ShouldReturnCallWithCorrectInfo()
    {
        var (phone, account) = CreatePhoneWithAccount();
        using (phone)
        {
            var call = account.MakeCall("sip:bob@example.com");
            call.Direction.Should().Be(CallDirection.Outgoing);
            call.Info.RemoteUri.Should().Be("sip:bob@example.com");
            call.Info.LocalUri.Should().Be("sip:alice@sip.example.com");
        }
    }

    [Fact]
    public void MakeCall_ShouldAddToActiveCalls()
    {
        var (phone, account) = CreatePhoneWithAccount();
        using (phone)
        {
            var call = account.MakeCall("sip:bob@example.com");
            account.ActiveCalls.Should().HaveCount(1);
            account.ActiveCalls[0].Should().BeSameAs(call);
        }
    }

    [Fact]
    public void DndMode_ShouldDefaultToOff()
    {
        var (phone, account) = CreatePhoneWithAccount();
        using (phone)
        {
            account.DndMode.Should().Be(DndMode.Off);
        }
    }

    [Fact]
    public void DndMode_ShouldBeSettable()
    {
        var (phone, account) = CreatePhoneWithAccount();
        using (phone)
        {
            account.DndMode = DndMode.RejectWithBusy;
            account.DndMode.Should().Be(DndMode.RejectWithBusy);
        }
    }

    [Fact]
    public void CallForwarding_ShouldNotBeNull()
    {
        var (phone, account) = CreatePhoneWithAccount();
        using (phone)
        {
            account.CallForwarding.Should().NotBeNull();
            account.CallForwarding.Enabled.Should().BeFalse();
        }
    }

    [Fact]
    public void MwiInfo_ShouldBeNullInitially()
    {
        var (phone, account) = CreatePhoneWithAccount();
        using (phone)
        {
            account.MwiInfo.Should().BeNull();
        }
    }

    [Fact]
    public void MakeCall_WithHeaders_ShouldReturnCallWithHeaders()
    {
        var (phone, account) = CreatePhoneWithAccount();
        using (phone)
        {
            var headers = new[] { new SipHeader { Name = "X-Test", Value = "123" } };
            var call = account.MakeCall("sip:bob@example.com", headers);
            call.CustomHeaders.Should().HaveCount(1);
            call.CustomHeaders[0].Name.Should().Be("X-Test");
        }
    }
}
