namespace SmartBasket.Core.Configuration;

public class DatabaseSettings
{
    public string Provider { get; set; } = "PostgreSQL";
    public string ConnectionString { get; set; } = string.Empty;
}
