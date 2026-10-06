using G54.BLL.Services;

namespace G54.UnitTests;

public sealed class PasswordEncoderTests
{
    private readonly PasswordEncoder _encoder = new();

    [Fact]
    public void Encode_ProducesVerifiablePasswordHash()
    {
        var encoded = _encoder.Encode("Correct Horse Battery Staple!");

        Assert.StartsWith("pbkdf2-sha256$600000$", encoded);
        Assert.True(_encoder.Verify("Correct Horse Battery Staple!", encoded));
    }

    [Fact]
    public void Verify_ReturnsFalseForIncorrectPasswordOrMalformedHash()
    {
        var encoded = _encoder.Encode("correct-password");

        Assert.False(_encoder.Verify("wrong-password", encoded));
        Assert.False(_encoder.Verify("correct-password", "not-a-password-hash"));
    }
}
