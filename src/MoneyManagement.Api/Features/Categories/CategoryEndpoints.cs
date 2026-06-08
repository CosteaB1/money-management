using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Categories;
using MoneyManagement.Application.Features.Categories.ArchiveCategory;
using MoneyManagement.Application.Features.Categories.CreateCategory;
using MoneyManagement.Application.Features.Categories.GetCategories;
using MoneyManagement.Application.Features.Categories.UpdateCategory;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.Categories;

public sealed class CategoryEndpoints : IEndpoint
{
    public sealed record CreateCategoryRequest(
        string Name,
        CategoryFlow Flow,
        Guid? ParentId,
        string? Color,
        string? Icon);

    public sealed record UpdateCategoryRequest(
        string Name,
        CategoryFlow Flow,
        string? Color);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/categories").WithTags("Categories");

        group.MapPost("/", CreateCategory);
        group.MapGet("/", GetCategories);
        group.MapPut("/{id:guid}", UpdateCategory);
        group.MapDelete("/{id:guid}", ArchiveCategory);
    }

    private static async Task<IResult> CreateCategory(
        CreateCategoryRequest request,
        ICommandHandler<CreateCategoryCommand, Guid> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateCategoryCommand(
            request.Name,
            request.Flow,
            request.ParentId,
            request.Color,
            request.Icon);

        Result<Guid> result = await handler.Handle(command, cancellationToken);
        return result.Match(id => Results.Created($"/categories/{id}", new { id }));
    }

    private static async Task<IResult> GetCategories(
        IQueryHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>> handler,
        CancellationToken cancellationToken,
        bool includeArchived = false)
    {
        Result<IReadOnlyList<CategoryDto>> result = await handler.Handle(new GetCategoriesQuery(includeArchived), cancellationToken);
        return result.Match(Results.Ok);
    }

    private static async Task<IResult> UpdateCategory(
        Guid id,
        UpdateCategoryRequest request,
        ICommandHandler<UpdateCategoryCommand> handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateCategoryCommand(
            id,
            request.Name,
            request.Flow,
            request.Color);

        Result result = await handler.Handle(command, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> ArchiveCategory(
        Guid id,
        ICommandHandler<ArchiveCategoryCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new ArchiveCategoryCommand(id), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }
}
