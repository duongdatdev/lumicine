using LumiCine.Application.Common;

namespace LumiCine.IntegrationTests.Catalog;

public sealed class CatalogContractTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Page_query_rejects_out_of_range_values(int page, int pageSize) =>
        Assert.Throws<ApiException>(() => new PageQuery(page, pageSize).Validate());

    [Fact]
    public void Page_query_accepts_default_values() =>
        Assert.Equal(new PageQuery(1, 20), new PageQuery().Validate());

    [Fact]
    public void Page_offset_does_not_overflow_int_arithmetic() =>
        Assert.Equal(((long)int.MaxValue - 1) * 100, new PageQuery(int.MaxValue, 100).Validate().Offset);
}
