namespace Nyssa.Rbac.IntegrationTests.Setup;

/// <summary>
/// XUnit collection definition for database fixture sharing
/// </summary>
[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    // This class has no code, and is never instantiated.
    // Its purpose is simply to be the place to apply [CollectionDefinition] and all the ICollectionFixture<> interfaces.
}