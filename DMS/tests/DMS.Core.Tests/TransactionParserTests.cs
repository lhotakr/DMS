using DMS.Core.Transactions;
using Xunit;
using Assert = Xunit.Assert;

namespace DMS.Core.Tests;

public class TransactionParserTests
{
    [Fact]
    public void Parse_ShouldReadReplaceModeTransaction()
    {
        var command = TransactionParser.Parse("/nART03 1000018165");

        Assert.Equal("Replace", command.Mode);
        Assert.Equal("ART03", command.Code);
        Assert.Equal("1000018165", command.Parameter);
    }

    [Fact]
    public void Parse_ShouldReadCurrentTransactionWithoutPrefix()
    {
        var command = TransactionParser.Parse("ART03 1000018165");

        Assert.Equal("Current", command.Mode);
        Assert.Equal("ART03", command.Code);
        Assert.Equal("1000018165", command.Parameter);
    }
}