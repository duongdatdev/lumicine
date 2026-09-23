namespace LumiCine.Application.Common;

public sealed record PageQuery(int Page = 1, int PageSize = 20)
{
    public long Offset => (long)(Page - 1) * PageSize;

    public PageQuery Validate()
    {
        if (Page < 1 || PageSize < 1 || PageSize > 100)
            throw new ApiException(400, "Page must be positive and pageSize must be between 1 and 100.");
        return this;
    }
}
