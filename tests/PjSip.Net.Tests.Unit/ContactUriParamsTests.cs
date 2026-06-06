using FluentAssertions;
using PjSip.Net.Internal;

namespace PjSip.Net.Tests.Unit;

/// <summary>
/// Tests for RFC 8599 Contact-URI param normalization (push: pn-provider/pn-prid/pn-param).
/// pjsua2 appends contactUriParams raw to the Contact URI and expects exactly one leading ';'.
/// </summary>
public class ContactUriParamsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_NullOrBlank_ReturnsNull(string? input)
    {
        ManagedAccount.NormalizeContactUriParams(input).Should().BeNull();
    }

    [Fact]
    public void Normalize_NoLeadingSemicolon_AddsOne()
    {
        ManagedAccount.NormalizeContactUriParams("pn-provider=apns;pn-prid=abc")
            .Should().Be(";pn-provider=apns;pn-prid=abc");
    }

    [Fact]
    public void Normalize_AlreadyHasLeadingSemicolon_Unchanged()
    {
        ManagedAccount.NormalizeContactUriParams(";pn-provider=fcm;pn-prid=tok")
            .Should().Be(";pn-provider=fcm;pn-prid=tok");
    }

    [Fact]
    public void Normalize_TrimsWhitespace()
    {
        ManagedAccount.NormalizeContactUriParams("  pn-provider=apns  ")
            .Should().Be(";pn-provider=apns");
    }

    [Fact]
    public void Normalize_RealApnsExample()
    {
        const string p = "pn-provider=apns;pn-prid=DEADBEEF;pn-param=ABCD1234.com.companyname.tekiumphone.voip";
        ManagedAccount.NormalizeContactUriParams(p).Should().Be(";" + p);
    }
}
