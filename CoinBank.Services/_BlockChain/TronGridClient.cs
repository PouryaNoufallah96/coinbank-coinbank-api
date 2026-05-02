public class TronGridClient
{
    private readonly HttpClient _httpClient;

    public TronGridClient(HttpClient httpClient)
    {
        _httpClient = httpClient;

        _httpClient.BaseAddress = new Uri("https://api.trongrid.io/");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<decimal> GetTrc20BalanceAsync(string wallet, string token)
    {
        var response = await _httpClient.GetAsync(
            $"v1/accounts/{wallet}/tokens/{token}");

        if (!response.IsSuccessStatusCode)
            return 0;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var data = doc.RootElement.GetProperty("data");

        if (data.GetArrayLength() == 0)
            return 0;

        return data[0].GetProperty("balance").GetDecimal();
    }
}
