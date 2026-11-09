using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axitor.Trading.Gateway.Binance.Models.Account;
using Axitor.Trading.Gateway.Binance.Models.Common.Enums;
using Axitor.Trading.Gateway.Binance.Parsing.Readers;
using Axitor.Trading.Gateway.Core.Abstractions;
using Axitor.Trading.Gateway.Core.Models.Primitives;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Helpers;
using Axitor.Trading.Gateway.Core.Transport.RestApi.Models;

namespace Axitor.Trading.Gateway.Binance.Parsing.Account;

/// <summary>Account information V2 parser of REST and the WebSocket API.</summary>
/// <remarks>
/// <para>
/// DESIGN NOTE: One pass over UTF-8, no serializer. REST sends feeBurn and no breakEvenPrice, the WebSocket API the other way round:
/// a field that is not sent, or sent as null, keeps its default. Leverage comes as a string on REST and is read either way.
/// </para>
/// <para>
/// DESIGN NOTE: A field that is sent but cannot be read throws, as does an unknown position side: a wrong margin figure would mislead risk checks.
/// A symbol whose name does not fit Symbol is kept as Empty.
/// </para>
/// <para>
/// DESIGN NOTE: Positions list every symbol of the market. They are collected in a pooled buffer and copied out once at their exact size,
/// so no growing list allocates twice the data or lands on the large object heap.
/// </para>
/// </remarks>
internal readonly struct AccountInfoV2Parser : IResponseParser<AccountInfoV2Model>, IWsApiResultParser<AccountInfoV2Model>
{
    // Above the number of listed symbols and assets, so the buffers rarely grow.
    private const int InitialPositions = 1024;
    private const int InitialAssets = 32;

    // Every symbol of the market is listed, a few hundred kilobytes: the body buffer starts at its size instead of doubling up to it.
    private const int ExpectedBodyBytes = 512 * 1024;

    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<AccountInfoV2Model> ParseAsync(Stream body, CancellationToken cancellationToken)
    {
        RestApiBody read = await RestApiBodyReader.ReadAsync(body, cancellationToken, ExpectedBodyBytes).ConfigureAwait(false);

        try
        {
            return Read(read.Buffer.AsSpan(0, read.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(read.Buffer);
        }
    }

    /// <inheritdoc/>
    public static AccountInfoV2Model Parse(ReadOnlySpan<byte> result) => Read(result);

    private static AccountInfoV2Model Read(ReadOnlySpan<byte> json)
    {
        AccountInfoV2Model model = default;
        model.Assets = [];
        model.Positions = [];

        Utf8JsonReader reader = new(json);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            ThrowMalformed();
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            ReadOnlySpan<byte> name = reader.ValueSpan;
            reader.Read();

            // A null value counts as not sent.
            if (reader.TokenType != JsonTokenType.Null)
            {
                ReadAccountField(ref reader, name, ref model);
            }

            // Steps over a field that was not read.
            reader.Skip();
        }

        return model;
    }

    /// <summary>Top-level fields come once per answer, so a plain chain of compares is enough.</summary>
    private static void ReadAccountField(ref Utf8JsonReader reader, ReadOnlySpan<byte> name, ref AccountInfoV2Model model)
    {
        if (name.SequenceEqual("positions"u8)) model.Positions = ReadPositions(ref reader);
        else if (name.SequenceEqual("assets"u8)) model.Assets = ReadAssets(ref reader);
        else if (name.SequenceEqual("feeTier"u8)) model.FeeTier = ReadInt32(ref reader);
        else if (name.SequenceEqual("feeBurn"u8)) model.FeeBurn = ReadBoolean(ref reader);
        else if (name.SequenceEqual("canTrade"u8)) model.CanTrade = ReadBoolean(ref reader);
        else if (name.SequenceEqual("canDeposit"u8)) model.CanDeposit = ReadBoolean(ref reader);
        else if (name.SequenceEqual("canWithdraw"u8)) model.CanWithdraw = ReadBoolean(ref reader);
        else if (name.SequenceEqual("multiAssetsMargin"u8)) model.IsMultiAssetsMargin = ReadBoolean(ref reader);
        else if (name.SequenceEqual("tradeGroupId"u8)) model.TradeGroupId = ReadInt64(ref reader);
        else if (name.SequenceEqual("totalInitialMargin"u8)) model.TotalInitialMargin = ReadDouble(ref reader);
        else if (name.SequenceEqual("totalMaintMargin"u8)) model.TotalMaintenanceMargin = ReadDouble(ref reader);
        else if (name.SequenceEqual("totalWalletBalance"u8)) model.TotalWalletBalance = ReadDouble(ref reader);
        else if (name.SequenceEqual("totalUnrealizedProfit"u8)) model.TotalUnrealizedProfit = ReadDouble(ref reader);
        else if (name.SequenceEqual("totalMarginBalance"u8)) model.TotalMarginBalance = ReadDouble(ref reader);
        else if (name.SequenceEqual("totalPositionInitialMargin"u8)) model.TotalPositionInitialMargin = ReadDouble(ref reader);
        else if (name.SequenceEqual("totalOpenOrderInitialMargin"u8)) model.TotalOpenOrderInitialMargin = ReadDouble(ref reader);
        else if (name.SequenceEqual("totalCrossWalletBalance"u8)) model.TotalCrossWalletBalance = ReadDouble(ref reader);
        else if (name.SequenceEqual("totalCrossUnPnl"u8)) model.TotalCrossUnrealizedProfit = ReadDouble(ref reader);
        else if (name.SequenceEqual("availableBalance"u8)) model.AvailableBalance = ReadDouble(ref reader);
        else if (name.SequenceEqual("maxWithdrawAmount"u8)) model.MaxWithdrawAmount = ReadDouble(ref reader);
    }

    /// <summary>Reads the asset array the reader is on, up to its closing bracket.</summary>
    private static AccountInfoV2Asset[] ReadAssets(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            ThrowMalformed();
        }

        AccountInfoV2Asset[] scratch = ArrayPool<AccountInfoV2Asset>.Shared.Rent(InitialAssets);
        int count = 0;

        try
        {
            while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                if (count == scratch.Length)
                {
                    scratch = Grow(scratch);
                }

                ReadAsset(ref reader, out scratch[count++]);
            }

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                ThrowMalformed();
            }

            return scratch.AsSpan(0, count).ToArray();
        }
        finally
        {
            ArrayPool<AccountInfoV2Asset>.Shared.Return(scratch);
        }
    }

    /// <summary>Reads the position array the reader is on, up to its closing bracket.</summary>
    private static AccountInfoV2Position[] ReadPositions(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            ThrowMalformed();
        }

        AccountInfoV2Position[] scratch = ArrayPool<AccountInfoV2Position>.Shared.Rent(InitialPositions);
        int count = 0;

        try
        {
            while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                if (count == scratch.Length)
                {
                    scratch = Grow(scratch);
                }

                ReadPosition(ref reader, out scratch[count++]);
            }

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                ThrowMalformed();
            }

            return scratch.AsSpan(0, count).ToArray();
        }
        finally
        {
            ArrayPool<AccountInfoV2Position>.Shared.Return(scratch);
        }
    }

    /// <summary>Reads the asset object the reader is on, up to its closing brace.</summary>
    private static void ReadAsset(ref Utf8JsonReader reader, out AccountInfoV2Asset asset)
    {
        asset = default;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            ReadOnlySpan<byte> name = reader.ValueSpan;
            reader.Read();

            if (reader.TokenType != JsonTokenType.Null)
            {
                ReadAssetField(ref reader, name, ref asset);
            }

            reader.Skip();
        }
    }

    /// <summary>Reads the position object the reader is on, up to its closing brace.</summary>
    private static void ReadPosition(ref Utf8JsonReader reader, out AccountInfoV2Position position)
    {
        position = default;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            ReadOnlySpan<byte> name = reader.ValueSpan;
            reader.Read();

            if (reader.TokenType != JsonTokenType.Null)
            {
                ReadPositionField(ref reader, name, ref position);
            }

            reader.Skip();
        }
    }

    private static void ReadAssetField(ref Utf8JsonReader reader, ReadOnlySpan<byte> name, ref AccountInfoV2Asset asset)
    {
        switch (name.Length)
        {
            case 5:
                if (name.SequenceEqual("asset"u8)) asset.Asset = ReadName(ref reader);
                return;

            case 10:
                if (name.SequenceEqual("crossUnPnl"u8)) asset.CrossUnrealizedProfit = ReadDouble(ref reader);
                else if (name.SequenceEqual("updateTime"u8)) asset.UpdateTime = ReadInt64(ref reader);
                return;

            case 11:
                if (name.SequenceEqual("maintMargin"u8)) asset.MaintenanceMargin = ReadDouble(ref reader);
                return;

            case 13:
                if (name.SequenceEqual("walletBalance"u8)) asset.WalletBalance = ReadDouble(ref reader);
                else if (name.SequenceEqual("marginBalance"u8)) asset.MarginBalance = ReadDouble(ref reader);
                else if (name.SequenceEqual("initialMargin"u8)) asset.InitialMargin = ReadDouble(ref reader);
                return;

            case 15:
                if (name.SequenceEqual("marginAvailable"u8)) asset.MarginAvailable = ReadBoolean(ref reader);
                return;

            case 16:
                if (name.SequenceEqual("unrealizedProfit"u8)) asset.UnrealizedProfit = ReadDouble(ref reader);
                else if (name.SequenceEqual("availableBalance"u8)) asset.AvailableBalance = ReadDouble(ref reader);
                return;

            case 17:
                if (name.SequenceEqual("maxWithdrawAmount"u8)) asset.MaxWithdrawAmount = ReadDouble(ref reader);
                return;

            case 18:
                if (name.SequenceEqual("crossWalletBalance"u8)) asset.CrossWalletBalance = ReadDouble(ref reader);
                return;

            case 21:
                if (name.SequenceEqual("positionInitialMargin"u8)) asset.PositionInitialMargin = ReadDouble(ref reader);
                return;

            case 22:
                if (name.SequenceEqual("openOrderInitialMargin"u8)) asset.OpenOrderInitialMargin = ReadDouble(ref reader);
                return;
        }
    }

    /// <summary>HOT: runs for every field of every symbol, so names are picked by length first.</summary>
    private static void ReadPositionField(ref Utf8JsonReader reader, ReadOnlySpan<byte> name, ref AccountInfoV2Position position)
    {
        switch (name.Length)
        {
            case 6:
                if (name.SequenceEqual("symbol"u8)) position.Symbol = ReadName(ref reader);
                return;

            case 8:
                if (name.SequenceEqual("leverage"u8)) position.Leverage = ReadInt32(ref reader);
                else if (name.SequenceEqual("isolated"u8)) position.IsIsolated = ReadBoolean(ref reader);
                return;

            case 10:
                if (name.SequenceEqual("entryPrice"u8)) position.EntryPrice = ReadDouble(ref reader);
                else if (name.SequenceEqual("updateTime"u8)) position.UpdateTime = ReadInt64(ref reader);
                return;

            case 11:
                if (name.SequenceEqual("positionAmt"u8)) position.Quantity = ReadDouble(ref reader);
                else if (name.SequenceEqual("maintMargin"u8)) position.MaintenanceMargin = ReadDouble(ref reader);
                else if (name.SequenceEqual("maxNotional"u8)) position.MaxNotional = ReadDouble(ref reader);
                return;

            case 12:
                if (name.SequenceEqual("positionSide"u8)) position.Side = ReadSide(ref reader);
                return;

            case 13:
                if (name.SequenceEqual("initialMargin"u8)) position.InitialMargin = ReadDouble(ref reader);
                return;

            case 14:
                if (name.SequenceEqual("breakEvenPrice"u8)) position.BreakEvenPrice = ReadDouble(ref reader);
                return;

            case 16:
                if (name.SequenceEqual("unrealizedProfit"u8)) position.UnrealizedProfit = ReadDouble(ref reader);
                return;

            case 21:
                if (name.SequenceEqual("positionInitialMargin"u8)) position.PositionInitialMargin = ReadDouble(ref reader);
                return;

            case 22:
                if (name.SequenceEqual("openOrderInitialMargin"u8)) position.OpenOrderInitialMargin = ReadDouble(ref reader);
                return;
        }
    }

    /// <summary>A symbol or asset name. One that does not fit Symbol reads as Empty; a value that is not text throws.</summary>
    private static Symbol ReadName(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            ThrowMalformed();
        }

        _ = BinanceJsonFieldReader.TryReadSymbol(ref reader, out Symbol value);
        return value;
    }

    private static PositionSide ReadSide(ref Utf8JsonReader reader)
    {
        if (!BinanceJsonFieldReader.TryReadPositionSide(ref reader, out PositionSide value))
        {
            ThrowMalformed();
        }

        return value;
    }

    private static double ReadDouble(ref Utf8JsonReader reader)
    {
        if (!BinanceJsonFieldReader.TryReadDouble(ref reader, out double value))
        {
            ThrowMalformed();
        }

        return value;
    }

    private static long ReadInt64(ref Utf8JsonReader reader)
    {
        if (!BinanceJsonFieldReader.TryReadInt64(ref reader, out long value))
        {
            ThrowMalformed();
        }

        return value;
    }

    private static int ReadInt32(ref Utf8JsonReader reader)
    {
        if (!BinanceJsonFieldReader.TryReadInt32(ref reader, out int value))
        {
            ThrowMalformed();
        }

        return value;
    }

    private static bool ReadBoolean(ref Utf8JsonReader reader)
    {
        if (!BinanceJsonFieldReader.TryReadBoolean(ref reader, out bool value))
        {
            ThrowMalformed();
        }

        return value;
    }

    /// <summary>Cold path. A larger pooled buffer for more items than any answer before.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static T[] Grow<T>(T[] scratch)
    {
        T[] larger = ArrayPool<T>.Shared.Rent(scratch.Length * 2);
        scratch.CopyTo(larger, 0);
        ArrayPool<T>.Shared.Return(scratch);
        return larger;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowMalformed() => throw new JsonException("Account information response did not have the expected shape.");
}
