namespace Infrastructure.Maine;

public sealed class CTraderOpenApiOptions
{
    public string Mode { get; set; } = "Demo";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public long   AccountId { get; set; }
}