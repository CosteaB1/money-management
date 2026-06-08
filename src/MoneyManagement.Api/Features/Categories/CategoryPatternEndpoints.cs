using MoneyManagement.Api.Endpoints;
using MoneyManagement.Api.Extensions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.Categories.CreateCategoryPattern;
using MoneyManagement.Application.Features.Categories.DeleteCategoryPattern;
using MoneyManagement.Application.Features.Categories.GetCategoryPatterns;
using MoneyManagement.Application.Features.Categories.UpdateCategoryPattern;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Api.Features.Categories;

public sealed class CategoryPatternEndpoints : IEndpoint
{
    public sealed record CreateCategoryPatternRequest(string Keyword, Guid CategoryId);

    public sealed record UpdateCategoryPatternRequest(string Keyword, Guid CategoryId);

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/category-patterns").WithTags("Category Patterns");

        group.MapGet("/", GetCategoryPatterns);
        group.MapPost("/", CreateCategoryPattern);
        group.MapPut("/{id:guid}", UpdateCategoryPattern);
        group.MapDelete("/{id:guid}", DeleteCategoryPattern);
    }

    private static async Task<IResult> GetCategoryPatterns(
        IQueryHandler<GetCategoryPatternsQuery, IReadOnlyList<CategoryPatternDto>> handler,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<CategoryPatternDto>> result =
            await handler.Handle(new GetCategoryPatternsQuery(), cancellationToken);

        return result.Match(Results.Ok);
    }

    private static async Task<IResult> CreateCategoryPattern(
        CreateCategoryPatternRequest request,
        ICommandHandler<CreateCategoryPatternCommand, Guid> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateCategoryPatternCommand(request.Keyword, request.CategoryId);

        Result<Guid> result = await handler.Handle(command, cancellationToken);
        return result.Match(id => Results.Created($"/category-patterns/{id}", new { id }));
    }

    private static async Task<IResult> UpdateCategoryPattern(
        Guid id,
        UpdateCategoryPatternRequest request,
        ICommandHandler<UpdateCategoryPatternCommand> handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateCategoryPatternCommand(id, request.Keyword, request.CategoryId);

        Result result = await handler.Handle(command, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }

    private static async Task<IResult> DeleteCategoryPattern(
        Guid id,
        ICommandHandler<DeleteCategoryPatternCommand> handler,
        CancellationToken cancellationToken)
    {
        Result result = await handler.Handle(new DeleteCategoryPatternCommand(id), cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.ToProblemDetails();
    }
}
