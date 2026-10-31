using System.Net;
using System.Text.Json;
using Channel.Infrastructure;

namespace Channel.Tests;

public class TelegramSerializationTests
{
    [Fact]
    public async Task SendWithoutButtonsOmitsReplyMarkupInsteadOfSendingNull()
    {
        var handler = new Capture(); using var http = new HttpClient(handler);
        await new TelegramBot(http, "test").Send(42, "Test", default);
        using var json = JsonDocument.Parse(handler.Body);
        Assert.False(json.RootElement.TryGetProperty("reply_markup", out _));
        Assert.Equal("Test", json.RootElement.GetProperty("text").GetString());
    }

    private class Capture : HttpMessageHandler
    {
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true,"result":{"message_id":1}}""") };
        }
    }
}
