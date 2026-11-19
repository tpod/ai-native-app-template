using AppTemplate.Api.Actions;
using AppTemplate.Api.Actions.Preview;
using Microsoft.Extensions.Options;
using Shouldly;

namespace AppTemplate.Api.Tests.Actions;

public sealed class PreviewTokenSignerTests
{
    private const string Key = "test-signing-key-0123456789abcdef-0123";

    private static PreviewTokenSigner SignerWith(string key)
        => new(Options.Create(new PreviewOptions { SigningKey = key }));

    private static PreviewTokenPayload SamplePayload() => new(
        "rename_project",
        "abc123",
        [new AggregateVersion("Project", "p1", 3)],
        "def456",
        [Guid.Parse("11111111-1111-1111-1111-111111111111")],
        new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Verify_SignedPayload_RoundTrips()
    {
        var signer = SignerWith(Key);
        var payload = SamplePayload();

        var result = signer.Verify(signer.Sign(payload));

        var back = result.AsT0;
        back.Action.ShouldBe(payload.Action);
        back.InputHash.ShouldBe(payload.InputHash);
        back.ChangesHash.ShouldBe(payload.ChangesHash);
        back.AggregateVersions.ShouldBe(payload.AggregateVersions);
        back.AllocatedIds.ShouldBe(payload.AllocatedIds);
        back.ExpiresAt.ShouldBe(payload.ExpiresAt);
    }

    [Fact]
    public void Verify_FlippedPayloadChar_ReturnsInvalid()
    {
        var signer = SignerWith(Key);
        var token = signer.Sign(SamplePayload());
        var chars = token.ToCharArray();
        var i = token.IndexOf('.') / 2;
        chars[i] = chars[i] == 'A' ? 'B' : 'A';

        signer.Verify(new string(chars)).IsT1.ShouldBeTrue();
    }

    [Fact]
    public void Verify_OtherKey_ReturnsInvalid()
    {
        var token = SignerWith(Key).Sign(SamplePayload());

        SignerWith("another-signing-key-0123456789abcdef").Verify(token).IsT1.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("a.b")]
    [InlineData("a.b.c")]
    [InlineData(null)]
    public void Verify_Garbage_ReturnsInvalid(string? token)
    {
        SignerWith(Key).Verify(token).AsT1.ShouldBeOfType<PreviewTokenInvalid>();
    }

    [Fact]
    public void Options_ShortKey_FailsValidation()
    {
        var validator = new PreviewOptionsValidator();

        validator.Validate(null, new PreviewOptions { SigningKey = new string('k', 31) }).Failed.ShouldBeTrue();
        validator.Validate(null, new PreviewOptions { SigningKey = new string('k', 32) }).Succeeded.ShouldBeTrue();
    }
}
