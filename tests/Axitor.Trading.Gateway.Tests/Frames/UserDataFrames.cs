namespace Axitor.Trading.Gateway.Tests.Frames;

/// <summary>User data events as the exchange sends them. Strings, so tests can break one field at a time.</summary>
internal static class UserDataFrames
{
    /// <summary>A partial fill of a limit order.</summary>
    public const string OrderTradeUpdate =
        """{"e":"ORDER_TRADE_UPDATE","E":1700000000123,"T":1700000000120,"o":{"s":"BTCUSDT","c":"my-order-1","S":"BUY","o":"LIMIT","f":"GTC","q":"0.010","p":"60000","ap":"60000","sp":"0","x":"TRADE","X":"PARTIALLY_FILLED","i":4000000001,"l":"0.004","z":"0.004","L":"60000","N":"USDT","n":"0.096","T":1700000000120,"t":555,"b":"360","a":"0","m":true,"R":false,"wt":"CONTRACT_PRICE","ot":"LIMIT","ps":"BOTH","cp":false,"rp":"1.5","pP":false,"si":0,"ss":0,"V":"EXPIRE_MAKER","pm":"NONE","gtd":0,"er":"0"}}""";

    public const string AccountUpdate =
        """{"e":"ACCOUNT_UPDATE","E":1564745798939,"T":1564745798938,"a":{"m":"ORDER","B":[{"a":"USDT","wb":"122624.12345678","cw":"100.12345678","bc":"50.12345678"},{"a":"BUSD","wb":"1.00000000","cw":"0.00000000","bc":"-49.12345678"}],"P":[{"s":"BTCUSDT","pa":"0","ep":"0.00000","bep":"0","cr":"200","up":"0","mt":"isolated","iw":"0.00000000","ps":"BOTH"},{"s":"BTCUSDT","pa":"20","ep":"6563.66500","bep":"6563.6","cr":"0","up":"2850.21200","mt":"isolated","iw":"13200.70726908","ps":"LONG"}]}}""";

    public const string TradeLite =
        """{"e":"TRADE_LITE","E":1721895408092,"T":1721895408214,"s":"BTCUSDT","q":"0.001","p":"0","m":false,"c":"z8hcUoOsqEdKMeKPSABslD","S":"BUY","L":"64089.20","l":"0.040","t":109100866,"i":8886774}""";

    /// <summary>Canceled before it triggered: ai is empty and act is "0".</summary>
    public const string AlgoUpdate =
        """{"e":"ALGO_UPDATE","T":1750515742297,"E":1750515742303,"o":{"caid":"Q5xaq5EGKgXXa0fD7fs0Ip","aid":2148719,"at":"CONDITIONAL","o":"TAKE_PROFIT","s":"BNBUSDT","S":"SELL","ps":"BOTH","f":"GTC","q":"0.01","X":"CANCELED","ai":"","ap":"0.00000","aq":"0.00000","act":"0","tp":"750","p":"750","V":"EXPIRE_MAKER","wt":"CONTRACT_PRICE","pm":"NONE","cp":false,"pP":false,"R":false,"tt":0,"gtd":0,"rm":"Reduce Only reject"}}""";

    public const string LeverageUpdate =
        """{"e":"ACCOUNT_CONFIG_UPDATE","E":1611646737479,"T":1611646737476,"ac":{"s":"BTCUSDT","l":25}}""";

    public const string MultiAssetsUpdate =
        """{"e":"ACCOUNT_CONFIG_UPDATE","E":1611646737479,"T":1611646737476,"ai":{"j":true}}""";

    public const string MarginCall =
        """{"e":"MARGIN_CALL","E":1587727187525,"cw":"3.16812045","p":[{"s":"ETHUSDT","ps":"LONG","pa":"1.327","mt":"CROSSED","iw":"0","mp":"187.17127","up":"-1.166074","mm":"1.614445"}]}""";

    /// <summary>A new spot order: no expiry reason, no commission asset, no original client order id.</summary>
    public const string SpotExecutionReport =
        """{"e":"executionReport","E":1499405658658,"s":"ETHBTC","c":"mUvoqJxFIILMdfAW5iGSOW","S":"BUY","o":"LIMIT","f":"GTC","q":"1.00000000","p":"0.10264410","P":"0.00000000","F":"0.00000000","g":-1,"C":"","x":"NEW","X":"NEW","r":"NONE","i":4293153,"l":"0.00000000","z":"0.00000000","L":"0.00000000","n":"0","N":null,"T":1499405658657,"t":-1,"v":3,"I":8641984,"w":true,"m":false,"M":false,"O":1499405658657,"Z":"0.00000000","Y":"0.00000000","Q":"0.00000000","W":1499405658657,"V":"NONE"}""";

    /// <summary>A partial fill of a spot limit order.</summary>
    public const string SpotExecutionFill =
        """{"e":"executionReport","E":1700000000500,"s":"ETHBTC","c":"my-order-2","S":"SELL","o":"LIMIT","f":"GTC","q":"2.00000000","p":"0.05000000","P":"0.00000000","F":"0.00000000","g":-1,"C":"","x":"TRADE","X":"PARTIALLY_FILLED","r":"NONE","i":4293154,"l":"0.50000000","z":"0.50000000","L":"0.05000000","n":"0.00012000","N":"BNB","T":1700000000490,"t":77,"I":8641990,"w":false,"m":true,"M":true,"O":1700000000100,"Z":"0.02500000","Y":"0.02500000","Q":"0.00000000","W":1700000000100,"V":"EXPIRE_MAKER"}""";

    public const string SpotAccountPosition =
        """{"e":"outboundAccountPosition","E":1564034571105,"u":1564034571073,"B":[{"a":"ETH","f":"10000.000000","l":"0.000000"},{"a":"BTC","f":"1.500000","l":"0.250000"}]}""";

    public const string SpotBalanceUpdate =
        """{"e":"balanceUpdate","E":1573200697110,"a":"BTC","d":"100.00000000","T":1573200697068}""";

    public const string SpotListStatus =
        """{"e":"listStatus","E":1564035303637,"s":"ETHBTC","g":2,"c":"OCO","l":"EXEC_STARTED","L":"EXECUTING","r":"NONE","C":"F4QN4G8DlFATFlIUQ0cjdD","T":1564035303625,"O":[{"s":"ETHBTC","i":17,"c":"AJYsMjErWJesZvqlJCTUgL"},{"s":"ETHBTC","i":18,"c":"bfYPSQdLoqAJeNrOr9adzq"}]}""";

    public const string SpotExternalLockUpdate =
        """{"e":"externalLockUpdate","E":1581557507324,"a":"NEO","d":"10.00000000","T":1581557507268}""";

    /// <summary>The end of a spot subscription: after our unsubscribe, or when the exchange drops it.</summary>
    public const string SpotStreamTerminated =
        """{"e":"eventStreamTerminated","E":1759089357377}""";

    /// <summary>Futures key expiry of the key, written as the exchange does: spaces after colons and the event time as a string.</summary>
    public static string ListenKeyExpired(string listenKey) =>
        "{\"e\": \"listenKeyExpired\",\"E\": \"1736996475556\",\"listenKey\": \"" + listenKey + "\"}";
}
