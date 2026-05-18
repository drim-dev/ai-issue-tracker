namespace Api.Common.Http;

/// <summary>
/// Marks a vertical-slice endpoint. Implementations map their HTTP route(s)
/// in <see cref="MapEndpoint"/> and are auto-discovered at startup.
/// </summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
