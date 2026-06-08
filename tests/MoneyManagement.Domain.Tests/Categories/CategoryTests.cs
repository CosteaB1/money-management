using FluentAssertions;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.Categories;

public class CategoryTests
{
    [Fact]
    public void Create_WithValidInput_ReturnsSuccess()
    {
        Result<Category> result = Category.Create("Groceries", CategoryFlow.Expense, parentId: null, color: "#16a34a", icon: "cart");

        result.IsSuccess.Should().BeTrue();
        Category category = result.Value;
        category.Name.Should().Be("Groceries");
        category.Flow.Should().Be(CategoryFlow.Expense);
        category.Color.Should().Be("#16a34a");
        category.Icon.Should().Be("cart");
        category.IsArchived.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyName_ReturnsNameRequired(string name)
    {
        Result<Category> result = Category.Create(name, CategoryFlow.Expense);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.NameRequired);
    }

    [Fact]
    public void Create_WithNameTooLong_ReturnsNameTooLong()
    {
        string name = new string('a', Category.NameMaxLength + 1);

        Result<Category> result = Category.Create(name, CategoryFlow.Expense);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.NameTooLong);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#1234")]
    [InlineData("16a34a")]
    [InlineData("#1234567")]
    public void Create_WithInvalidColor_ReturnsInvalidColor(string color)
    {
        Result<Category> result = Category.Create("Food", CategoryFlow.Expense, color: color);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.InvalidColor);
    }

    [Fact]
    public void Create_WithUndefinedFlow_ReturnsInvalidFlow()
    {
        // Cast an out-of-range value so Enum.IsDefined fails.
        Result<Category> result = Category.Create("Food", (CategoryFlow)999);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.InvalidFlow);
    }

    [Fact]
    public void Create_WithIconTooLong_ReturnsIconTooLong()
    {
        string icon = new string('x', Category.IconMaxLength + 1);

        Result<Category> result = Category.Create("Food", CategoryFlow.Expense, icon: icon);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.IconTooLong);
    }

    [Fact]
    public void Update_WithUndefinedFlow_ReturnsInvalidFlow()
    {
        Category category = Category.Create("Food", CategoryFlow.Expense).Value;

        Result result = category.Update("Food", (CategoryFlow)999, color: null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.InvalidFlow);
    }

    [Fact]
    public void Archive_SetsIsArchivedToTrue()
    {
        Category category = Category.Create("Food", CategoryFlow.Expense).Value;

        category.Archive();

        category.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Update_WithValidInput_ReassignsNameFlowAndColor()
    {
        Category category = Category.Create("Food", CategoryFlow.Expense, color: "#16a34a").Value;

        Result result = category.Update("  Dining  ", CategoryFlow.Both, "#dc2626");

        result.IsSuccess.Should().BeTrue();
        category.Name.Should().Be("Dining");
        category.Flow.Should().Be(CategoryFlow.Both);
        category.Color.Should().Be("#dc2626");
    }

    [Fact]
    public void Update_WithNullColor_ClearsColor()
    {
        Category category = Category.Create("Food", CategoryFlow.Expense, color: "#16a34a").Value;

        Result result = category.Update("Food", CategoryFlow.Expense, color: null);

        result.IsSuccess.Should().BeTrue();
        category.Color.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithEmptyName_ReturnsNameRequired(string name)
    {
        Category category = Category.Create("Food", CategoryFlow.Expense).Value;

        Result result = category.Update(name, CategoryFlow.Expense, color: null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.NameRequired);
        category.Name.Should().Be("Food");
    }

    [Fact]
    public void Update_WithNameTooLong_ReturnsNameTooLong()
    {
        Category category = Category.Create("Food", CategoryFlow.Expense).Value;
        string name = new string('a', Category.NameMaxLength + 1);

        Result result = category.Update(name, CategoryFlow.Expense, color: null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.NameTooLong);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#1234")]
    [InlineData("16a34a")]
    [InlineData("#1234567")]
    public void Update_WithInvalidColor_ReturnsInvalidColor(string color)
    {
        Category category = Category.Create("Food", CategoryFlow.Expense).Value;

        Result result = category.Update("Food", CategoryFlow.Expense, color);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(CategoryErrors.InvalidColor);
    }
}
