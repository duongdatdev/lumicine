using Xunit;

namespace LumiCine.IntegrationTests.Persistence;

[CollectionDefinition(Name)]
public sealed class WorkerEmailDatabaseCollection : ICollectionFixture<PostgresDatabaseFixture>
{
    // The email worker tests run against their own database so that stray hosted workers
    // from other test hosts cannot claim or retry the queue rows they assert on.
    public const string Name = "WorkerEmailDatabase";
}
