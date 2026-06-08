namespace MoneyManagement.Domain.FxRates;

/// <summary>
/// Where an <see cref="FxRate"/> came from. Manual rates are user-entered and
/// always win priority over auto-fetched ones — see <see cref="MoneyManagement.Application.Abstractions.FxRates.IFxConverter"/>.
/// </summary>
public enum FxRateSource
{
    /// <summary>Hand-entered via <c>POST /fx-rates</c>. The default.</summary>
    Manual = 0,

    /// <summary>Pulled from Banca Națională a Moldovei's daily XML feed.</summary>
    BnmAuto = 1,
}
