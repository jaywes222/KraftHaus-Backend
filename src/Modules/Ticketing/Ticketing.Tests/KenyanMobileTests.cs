using Ticketing.Domain.Common;
using Xunit;
using Common.Domain.Phones;

namespace Ticketing.Tests;

public sealed class KenyanMobileTests
{
    [Theory]
    [InlineData("0712345678", "+254712345678")]
    [InlineData("712345678", "+254712345678")]
    [InlineData("254712345678", "+254712345678")]
    [InlineData("+254712345678", "+254712345678")]
    [InlineData("+254 712 345 678", "+254712345678")]
    [InlineData("0712-345-678", "+254712345678")]
    [InlineData("(0712) 345 678", "+254712345678")]
    [InlineData("0112345678", "+254112345678")]
    [InlineData("+254112345678", "+254112345678")]
    public void CommonShapesNormaliseToE164(string input, string expected)
    {
        Assert.True(KenyanMobile.TryCreate(input, out var mobile));
        Assert.Equal(expected, mobile!.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0212345678")]
    [InlineData("+255712345678")]
    [InlineData("07123456789")]
    [InlineData("071234567")]
    [InlineData("+2540712345678")]
    [InlineData("07123a5678")]
    [InlineData("+254712345678 ext 2")]
    public void AnythingElseIsRejected(string? input)
    {
        Assert.False(KenyanMobile.TryCreate(input, out var mobile));
        Assert.Null(mobile);
    }
}
