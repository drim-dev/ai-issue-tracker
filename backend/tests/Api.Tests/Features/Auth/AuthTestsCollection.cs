using Api.Tests.Fixtures;

namespace Api.Tests.Features.Auth;

[CollectionDefinition(Name)]
public class AuthTestsCollection : ICollectionFixture<TestFixture>
{
    public const string Name = nameof(AuthTestsCollection);
}
