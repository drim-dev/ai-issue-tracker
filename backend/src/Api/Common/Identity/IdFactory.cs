using IdGen;
using IdGen.DependencyInjection;

namespace Api.Common.Identity;

/// <summary>
/// Generates distributed, time-ordered <see cref="long"/> IDs via IdGen.
/// Inject into handlers; call <see cref="Create"/> before persisting an entity.
/// </summary>
public class IdFactory(IdGenerator idGenerator)
{
    public long Create() => idGenerator.CreateId();
}

public static class IdFactoryExtensions
{
    public static IServiceCollection AddIdFactory(this IServiceCollection services, int generatorId)
    {
        services.AddIdGen(generatorId, () => new IdGeneratorOptions
        {
            IdStructure = new IdStructure(52, 8, 3),
        });

        services.AddSingleton<IdFactory>();

        return services;
    }
}
