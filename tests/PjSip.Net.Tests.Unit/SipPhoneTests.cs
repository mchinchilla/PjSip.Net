using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PjSip.Net.Accounts;
using PjSip.Net.Internal;
using PjSip.Net.Logging;

namespace PjSip.Net.Tests.Unit;

public class SipPhoneTests
{
    private static SipPhone CreateSipPhone(SipPhoneOptions? options = null)
    {
        options ??= new SipPhoneOptions();
        var optionsWrapper = Options.Create(options);
        var logger = NullLogger<SipPhone>.Instance;
        var epLogger = NullLogger<PjSipEndpointManager>.Instance;
        var loggerAdapter = new PjSipLoggerAdapter(NullLogger<PjSipLoggerAdapter>.Instance);
        var endpointManager = new PjSipEndpointManager(optionsWrapper, epLogger, loggerAdapter);
        return new SipPhone(endpointManager, optionsWrapper, logger);
    }

    [Fact]
    public void InitialState_ShouldBeIdle()
    {
        using var phone = CreateSipPhone();
        phone.State.Should().Be(SipPhoneState.Idle);
    }

    [Fact]
    public void Accounts_ShouldBeEmptyInitially()
    {
        using var phone = CreateSipPhone();
        phone.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_ShouldTransitionToRunning()
    {
        await using var phone = CreateSipPhone();
        await phone.StartAsync();
        phone.State.Should().Be(SipPhoneState.Running);
    }

    [Fact]
    public async Task StartAsync_WithAccounts_ShouldRegisterThem()
    {
        var options = new SipPhoneOptions
        {
            Accounts =
            [
                new SipAccountOptions
                {
                    Username = "alice",
                    Password = "secret",
                    Domain = "sip.example.com"
                }
            ]
        };

        await using var phone = CreateSipPhone(options);
        await phone.StartAsync();

        phone.Accounts.Should().HaveCount(1);
        phone.Accounts[0].Uri.Should().Be("sip:alice@sip.example.com");
    }

    [Fact]
    public void AddAccount_ShouldAddToAccountsList()
    {
        using var phone = CreateSipPhone();

        var account = phone.AddAccount(new SipAccountOptions
        {
            Username = "bob",
            Password = "secret",
            Domain = "sip.example.com"
        });

        phone.Accounts.Should().HaveCount(1);
        account.Uri.Should().Be("sip:bob@sip.example.com");
    }

    [Fact]
    public void RemoveAccount_ShouldRemoveFromList()
    {
        using var phone = CreateSipPhone();
        var account = phone.AddAccount(new SipAccountOptions
        {
            Username = "bob",
            Password = "secret",
            Domain = "sip.example.com"
        });

        phone.RemoveAccount(account);
        phone.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task StopAsync_ShouldTransitionToStopped()
    {
        await using var phone = CreateSipPhone();
        await phone.StartAsync();
        await phone.StopAsync();
        phone.State.Should().Be(SipPhoneState.Stopped);
    }

    [Fact]
    public void Audio_ShouldNotBeNull()
    {
        using var phone = CreateSipPhone();
        phone.Audio.Should().NotBeNull();
    }

    [Fact]
    public void Codecs_ShouldNotBeNull()
    {
        using var phone = CreateSipPhone();
        phone.Codecs.Should().NotBeNull();
    }

    [Fact]
    public void Presence_ShouldNotBeNull()
    {
        using var phone = CreateSipPhone();
        phone.Presence.Should().NotBeNull();
    }

    [Fact]
    public void Messaging_ShouldNotBeNull()
    {
        using var phone = CreateSipPhone();
        phone.Messaging.Should().NotBeNull();
    }

    [Fact]
    public void Conference_ShouldNotBeNull()
    {
        using var phone = CreateSipPhone();
        phone.Conference.Should().NotBeNull();
    }

    [Fact]
    public void Recorder_ShouldNotBeNull()
    {
        using var phone = CreateSipPhone();
        phone.Recorder.Should().NotBeNull();
    }

    [Fact]
    public void Tones_ShouldNotBeNull()
    {
        using var phone = CreateSipPhone();
        phone.Tones.Should().NotBeNull();
    }

    [Fact]
    public void Quality_ShouldNotBeNull()
    {
        using var phone = CreateSipPhone();
        phone.Quality.Should().NotBeNull();
    }

    [Fact]
    public void History_ShouldNotBeNull()
    {
        using var phone = CreateSipPhone();
        phone.History.Should().NotBeNull();
        phone.History.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Network_ShouldNotBeNull()
    {
        using var phone = CreateSipPhone();
        phone.Network.Should().NotBeNull();
    }

    [Fact]
    public void MakeCall_WithHeaders_ShouldReturnCallWithHeaders()
    {
        using var phone = CreateSipPhone();
        var account = phone.AddAccount(new SipAccountOptions
        {
            Username = "alice",
            Password = "secret",
            Domain = "sip.example.com"
        });

        var headers = new[] { new Calls.SipHeader { Name = "X-Custom", Value = "test" } };
        var call = phone.MakeCall(account, "sip:bob@example.com", headers);
        call.CustomHeaders.Should().HaveCount(1);
        call.CustomHeaders[0].Name.Should().Be("X-Custom");
    }

    [Fact]
    public void CallHistory_ShouldRecordDisconnectedCalls()
    {
        using var phone = CreateSipPhone();
        var account = phone.AddAccount(new SipAccountOptions
        {
            Username = "alice",
            Password = "secret",
            Domain = "sip.example.com"
        });

        var call = phone.MakeCall(account, "sip:bob@example.com");
        call.Hangup();

        phone.History.Entries.Should().HaveCount(1);
        phone.History.Entries[0].RemoteUri.Should().Be("sip:bob@example.com");
    }
}
