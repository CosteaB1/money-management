using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Imports;

public interface IBankStatementParser
{
    BankSource Source { get; }

    Result<ParsedStatement> Parse(Stream pdfStream);
}
