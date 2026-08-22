using SteamInputAddonforClaw.QamHost;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public class QamHostCdpEvaluateResultTests
{
    [Fact]
    public void ReportsSuccessWhenTheEvaluatedExpressionReturnsTrue()
    {
        const string json = """{"id":1,"result":{"result":{"type":"boolean","value":true}}}""";

        var result = CdpEvaluateResult.Parse(json);

        Assert.True(result.Succeeded);
        Assert.True(result.BooleanValue);
    }

    [Fact]
    public void ReportsInstallFailureWhenTheEvaluatedExpressionReturnsFalse()
    {
        const string json = """{"id":1,"result":{"result":{"type":"boolean","value":false}}}""";

        var result = CdpEvaluateResult.Parse(json);

        Assert.True(result.Succeeded);
        Assert.False(result.BooleanValue);
    }

    [Fact]
    public void TreatsCdpExceptionDetailsAsFailureRegardlessOfAnyValue()
    {
        const string json = """
            {"id":1,"result":{"result":{"type":"boolean","value":true},"exceptionDetails":{"text":"Uncaught TypeError"}}}
            """;

        var result = CdpEvaluateResult.Parse(json);

        Assert.False(result.Succeeded);
        Assert.Equal("Uncaught TypeError", result.ErrorText);
    }

    [Fact]
    public void IncludesExceptionDescriptionWhenCdpProvidesOne()
    {
        const string json = """
            {"id":1,"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"TypeError: example"}}}}
            """;

        var result = CdpEvaluateResult.Parse(json);

        Assert.False(result.Succeeded);
        Assert.Contains("TypeError: example", result.ErrorText);
        Assert.Contains("Uncaught", result.ErrorText);
    }

    [Fact]
    public void TreatsAMissingResultFieldAsFailure()
    {
        const string json = """{"id":1}""";

        var result = CdpEvaluateResult.Parse(json);

        Assert.False(result.Succeeded);
    }
}
