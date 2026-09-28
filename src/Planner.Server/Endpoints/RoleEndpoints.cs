using Planner.Core.Services;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>Роли. Список приходит в /api/board.</summary>
public static class RoleEndpoints
{
    public static void MapRoleEndpoints(this RouteGroupBuilder api)
    {
        var roles = api.MapGroup("/roles");

        roles.MapPost("/", async (CreateRoleRequest request, RoleService service, CancellationToken ct) =>
        {
            var role = await service.CreateAsync(request.Name, request.Color, ct);
            return Results.Created($"/api/roles/{role.Id}", role.ToDto());
        });

        roles.MapPut("/{id:int}", async (int id, UpdateRoleRequest request, RoleService service, CancellationToken ct) =>
            (await service.UpdateAsync(id, request.Name, request.Color, ct)).ToDto());

        roles.MapPost("/reorder", async (ReorderRequest request, RoleService service, CancellationToken ct) =>
        {
            await service.ReorderAsync(request.Ids, ct);
            return Results.NoContent();
        });

        // ?categoryId= — куда вернуть дела целей без дня, у которых нет своей категории
        roles.MapDelete("/{id:int}", async (int id, int? categoryId, RoleService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, categoryId, ct);
            return Results.NoContent();
        });
    }
}
