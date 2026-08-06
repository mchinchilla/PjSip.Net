using FluentAssertions;
using PjSip.Net.Internal;

namespace PjSip.Net.Tests.Unit;

/// <summary>
/// Tests for the raw-INVITE log scrubber. The contract has two halves that pull against
/// each other: the SRTP master key must never survive into a log file, and everything that
/// tells you WHY an SDP answer failed — which m-lines exist, which carry a=crypto, which
/// suite each names — must survive untouched.
/// </summary>
public class SipMessageRedactorTests
{
    private const string CryptoLine =
        "a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:NzB4d1BINUAvLEw6UzF3WSJ+PSdFcGdUJShoX4cJ|2^31|1:1";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_NullOrEmpty_ReturnsEmpty(string? input)
    {
        SipMessageRedactor.Redact(input).Should().BeEmpty();
    }

    [Fact]
    public void Redact_CryptoLine_DropsKeyKeepsTagAndSuite()
    {
        var result = SipMessageRedactor.Redact(CryptoLine);

        result.Should().Be("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:<redacted>");
        result.Should().NotContain("NzB4d1BINUAvLEw6UzF3WSJ+PSdFcGdUJShoX4cJ");
    }

    [Fact]
    public void Redact_CryptoLineWithSessionParams_PreservesThemAfterTheKey()
    {
        const string line = "a=crypto:2 AES_CM_128_HMAC_SHA1_32 inline:PS1uQCVeeCFCanVrcjDOBw== UNENCRYPTED_SRTCP";

        SipMessageRedactor.Redact(line)
            .Should().Be("a=crypto:2 AES_CM_128_HMAC_SHA1_32 inline:<redacted> UNENCRYPTED_SRTCP");
    }

    [Fact]
    public void Redact_MikeyKeyManagement_DropsWholeValue()
    {
        SipMessageRedactor.Redact("a=key-mgmt:mikey AQAFgM0XflABAAAAAAAAAAAAAAsA")
            .Should().Be("a=key-mgmt:<redacted>");
    }

    [Fact]
    public void Redact_AuthorizationHeader_DropsCredentials()
    {
        SipMessageRedactor.Redact("Authorization: Digest username=\"1095m\", response=\"deadbeef\"")
            .Should().Be("Authorization: <redacted>");
    }

    [Fact]
    public void Redact_PreservesCrlfLineEndings()
    {
        SipMessageRedactor.Redact("m=audio 4000 RTP/SAVP 0\r\n" + CryptoLine + "\r\n")
            .Should().Be("m=audio 4000 RTP/SAVP 0\r\na=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:<redacted>\r\n");
    }

    /// <summary>
    /// The whole point of the dump: a crypto-less RTP/SAVP m-line has to stay legible,
    /// because that is the shape that makes pjmedia answer 406 (PJMEDIA_SRTP_ESDPREQCRYPTO).
    /// </summary>
    [Fact]
    public void Redact_RealInvite_KeepsMLinesAndShowsWhichOneLacksCrypto()
    {
        const string invite =
            "INVITE sip:1095m@192.168.1.20:5061;transport=tls SIP/2.0\r\n" +
            "From: \"Jonathan Davis\" <sip:1084@ai.alpha-voice.us>;tag=fHRzaqQf\r\n" +
            "To: <sip:1095m@ai.alpha-voice.us>\r\n" +
            "Content-Type: application/sdp\r\n" +
            "\r\n" +
            "v=0\r\n" +
            "m=audio 22000 RTP/SAVP 0 8 101\r\n" +
            CryptoLine + "\r\n" +
            "m=text 22002 RTP/SAVP 98\r\n" +
            "a=rtpmap:98 t140/1000\r\n";

        var result = SipMessageRedactor.Redact(invite);

        // Structure survives: both m-lines, the suite name, and the t140 rtpmap.
        result.Should().Contain("m=audio 22000 RTP/SAVP 0 8 101");
        result.Should().Contain("a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:<redacted>");
        result.Should().Contain("m=text 22002 RTP/SAVP 98");
        result.Should().Contain("a=rtpmap:98 t140/1000");
        result.Should().Contain("From: \"Jonathan Davis\" <sip:1084@ai.alpha-voice.us>;tag=fHRzaqQf");

        // The key does not.
        result.Should().NotContain("NzB4d1BINUAvLEw6UzF3WSJ+PSdFcGdUJShoX4cJ");
    }

    [Fact]
    public void Redact_MessageWithoutSecrets_IsUnchanged()
    {
        const string invite =
            "INVITE sip:1095m@192.168.1.20 SIP/2.0\r\nCall-ID: abc123\r\n\r\nm=audio 4000 RTP/AVP 0\r\n";

        SipMessageRedactor.Redact(invite).Should().Be(invite);
    }
}
