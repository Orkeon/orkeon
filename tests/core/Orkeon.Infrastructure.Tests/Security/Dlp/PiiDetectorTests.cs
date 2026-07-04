using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Security.Dlp;

namespace Orkeon.Infrastructure.Tests.Security.Dlp;

public class PiiDetectorTests
{
    private readonly PiiDetector _detector = new();

    [Fact]
    public void Scan_EmailDetected_ReturnsMatch()
    {
        var result = _detector.Scan("Contact us at user@example.com for details.");

        Assert.True(result.HasPii);
        Assert.Contains(result.Matches, m => m.Type == PiiType.Email && m.MatchedText == "user@example.com");
    }

    [Fact]
    public void Scan_PhoneDetected_ReturnsMatch()
    {
        var result = _detector.Scan("Call me at +1-555-123-4567 tomorrow.");

        Assert.True(result.HasPii);
        Assert.Contains(result.Matches, m => m.Type == PiiType.Phone);
    }

    [Fact]
    public void Scan_SsnDetected_ReturnsMatch()
    {
        var result = _detector.Scan("My SSN is 123-45-6789.");

        Assert.True(result.HasPii);
        Assert.Contains(result.Matches, m => m.Type == PiiType.Ssn && m.MatchedText == "123-45-6789");
    }

    [Fact]
    public void Scan_CreditCardDetected_ReturnsMatch()
    {
        var result = _detector.Scan("Card number: 4111-1111-1111-1111");

        Assert.True(result.HasPii);
        Assert.Contains(result.Matches, m => m.Type == PiiType.CreditCard);
    }

    [Fact]
    public void Scan_NoPii_ReturnsEmpty()
    {
        var result = _detector.Scan("This is a normal message with no personal data.");

        Assert.False(result.HasPii);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Scan_MultiplePii_ReturnsAll()
    {
        var result = _detector.Scan("Email: test@example.com, SSN: 123-45-6789, Card: 4111-1111-1111-1111");

        Assert.True(result.HasPii);
        Assert.True(result.Matches.Count >= 3);
        Assert.Contains(result.Matches, m => m.Type == PiiType.Email);
        Assert.Contains(result.Matches, m => m.Type == PiiType.Ssn);
        Assert.Contains(result.Matches, m => m.Type == PiiType.CreditCard);
    }

    [Fact]
    public void Mask_ReplacesMatchesWithStars()
    {
        var content = "Email: user@example.com";
        var scanResult = _detector.Scan(content);

        var masked = _detector.Mask(content, scanResult.Matches);

        Assert.DoesNotContain("user@example.com", masked);
        Assert.Contains("****************", masked);
    }
}
