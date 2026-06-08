using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace MoneyManagement.Api.Tests;

/// <summary>
/// Guards that the FluentValidation 400 path stays distinct from the malformed-body
/// 400 path (F-3.1). A well-formed body that fails a domain rule must surface the
/// FluentValidation ProblemDetails (error code == the failing property name), NOT
/// the generic "request body is malformed" message.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TypedValidationTests(CustomWebApplicationFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_transaction_with_negative_amount_returns_validation_problem_for_amount()
    {
        // Valid-shaped body: random (non-empty) AccountId, valid date/direction, so the
        // FIRST and only failing rule is Amount.GreaterThan(0). The Application's
        // ValidationDecorator maps the failure to Error.Validation(PropertyName=...),
        // and ResultExtensions surfaces that code as ProblemDetails `type`/`errorCode`.
        var body = new
        {
            accountId = Guid.NewGuid(),
            transactionDate = "2026-01-01",
            direction = "Expense",
            amount = -50m,
            description = "Negative amount",
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/transactions", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = problem.RootElement;

        // Distinct from the malformed path: this carries the property-named error code,
        // not the generic "Bad Request" malformed-body title.
        root.GetProperty("type").GetString().Should().Be("Amount");
        root.GetProperty("errorCode").GetString().Should().Be("Amount");
        root.GetProperty("errorType").GetString().Should().Be("Validation");
        root.GetProperty("detail").GetString().Should()
            .NotContain("malformed", "the typed-validation path must not reuse the malformed-body message");
    }
}
