namespace MoneyManagement.Application.Abstractions.Imports;

/// <summary>
/// Auto-suggests whether a parsed statement row looks like an internal transfer.
/// The user reviews and confirms each suggestion in the import preview UI before commit.
/// </summary>
public interface ITransferDetector
{
    bool IsLikelyTransfer(string description);
}
