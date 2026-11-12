namespace Q.Sock.Tests.UI;

internal sealed class CourseFixture : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"Проектирование ИС\",\"roomId\":\"22222222-2222-2222-2222-222222222222\"}]",
                    System.Text.Encoding.UTF8,
                    "application/json"
                ),
            }
        );
}
