using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Categories.ArchiveCategory;

public sealed record ArchiveCategoryCommand(Guid Id) : ICommand;
