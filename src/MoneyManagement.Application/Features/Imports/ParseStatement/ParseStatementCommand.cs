using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Imports.ParseStatement;

public sealed record ParseStatementCommand(
    byte[] FileBytes,
    string FileName,
    Guid AccountId) : ICommand<StatementPreviewDto>;
