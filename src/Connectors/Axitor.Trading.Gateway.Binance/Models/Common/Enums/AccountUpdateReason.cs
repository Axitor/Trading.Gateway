using System.Diagnostics.CodeAnalysis;

namespace Axitor.Trading.Gateway.Binance.Models.Common.Enums;

/// <summary>Why balances or positions changed.</summary>
[SuppressMessage("Naming", "CA1707", Justification = "Binance identifiers are spelled as the exchange spells them.")]
public enum AccountUpdateReason : byte
{
    /// <summary>A value this version does not know. Reported to telemetry.</summary>
    UNKNOWN,

    /// <summary>Deposit.</summary>
    DEPOSIT,

    /// <summary>Withdrawal.</summary>
    WITHDRAW,

    /// <summary>An order filled.</summary>
    ORDER,

    /// <summary>Funding fee.</summary>
    FUNDING_FEE,

    /// <summary>Withdrawal rejected.</summary>
    WITHDRAW_REJECT,

    /// <summary>Adjustment.</summary>
    ADJUSTMENT,

    /// <summary>Insurance fund clearance.</summary>
    INSURANCE_CLEAR,

    /// <summary>Deposit by the exchange.</summary>
    ADMIN_DEPOSIT,

    /// <summary>Withdrawal by the exchange.</summary>
    ADMIN_WITHDRAW,

    /// <summary>Isolated margin added or removed.</summary>
    MARGIN_TRANSFER,

    /// <summary>Margin mode changed.</summary>
    MARGIN_TYPE_CHANGE,

    /// <summary>Transfer between wallets.</summary>
    ASSET_TRANSFER,

    /// <summary>Options premium fee.</summary>
    OPTIONS_PREMIUM_FEE,

    /// <summary>Options settlement profit.</summary>
    OPTIONS_SETTLE_PROFIT,

    /// <summary>Automatic asset exchange.</summary>
    AUTO_EXCHANGE,

    /// <summary>Coin swap deposit.</summary>
    COIN_SWAP_DEPOSIT,

    /// <summary>Coin swap withdrawal.</summary>
    COIN_SWAP_WITHDRAW,
}
