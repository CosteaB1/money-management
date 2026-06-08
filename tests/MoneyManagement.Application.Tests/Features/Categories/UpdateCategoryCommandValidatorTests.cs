using FluentAssertions;
using MoneyManagement.Application.Features.Categories.UpdateCategory;
using MoneyManagement.Domain.Categories;

namespace MoneyManagement.Application.Tests.Features.Categories;

public class UpdateCategoryCommandValidatorTests
{
    private readonly UpdateCategoryCommandValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithBlankName_Fails(string name)
    {
        var command = new UpdateCategoryCommand(Guid.CreateVersion7(), name, CategoryFlow.Expense, null);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithEmptyId_Fails()
    {
        var command = new UpdateCategoryCommand(Guid.Empty, "Food", CategoryFlow.Expense, null);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WithValidCommand_Passes()
    {
        var command = new UpdateCategoryCommand(Guid.CreateVersion7(), "Food", CategoryFlow.Expense, "#16a34a");

        _validator.Validate(command).IsValid.Should().BeTrue();
    }
}
