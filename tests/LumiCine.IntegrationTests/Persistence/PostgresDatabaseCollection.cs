namespace LumiCine.IntegrationTests.Persistence;

[CollectionDefinition("PostgreSQL", DisableParallelization = true)]
public sealed class PostgresDatabaseCollection : ICollectionFixture<PostgresDatabaseFixture>
{
    public const string Name = "PostgreSQL";
}