namespace LumiCine.Application.Accounts;

public sealed class EmailDeliveryException(string message) : Exception(message);
