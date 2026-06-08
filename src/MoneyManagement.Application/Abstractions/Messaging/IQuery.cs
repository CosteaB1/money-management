using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Abstractions.Messaging;

public interface IQuery<TResponse>;

public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}
