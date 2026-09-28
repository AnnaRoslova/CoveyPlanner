using Planner.Core.Services;
using Planner.Shared;

namespace Planner.Server.Endpoints;

/// <summary>Категории ленты. Список приходит в /api/board.</summary>
public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this RouteGroupBuilder api)
    {
        var categories = api.MapGroup("/categories");

        categories.MapPost("/", async (CreateCategoryRequest request, CategoryService service, CancellationToken ct) =>
        {
            var category = await service.CreateAsync(request.Name, ct);
            return Results.Created($"/api/categories/{category.Id}", category.ToDto());
        });

        categories.MapPut("/{id:int}", async (int id, UpdateCategoryRequest request, CategoryService service, CancellationToken ct) =>
            (await service.UpdateAsync(id, request.Name, request.IsExpanded, ct)).ToDto());

        categories.MapPost("/reorder", async (ReorderRequest request, CategoryService service, CancellationToken ct) =>
        {
            await service.ReorderAsync(request.Ids, ct);
            return Results.NoContent();
        });

        // Дела категории уходят в корзину
        categories.MapDelete("/{id:int}", async (int id, CategoryService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        });
    }
}
