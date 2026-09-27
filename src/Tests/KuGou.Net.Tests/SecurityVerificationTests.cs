using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KuGou.Net.Abstractions.Models;
using KuGou.Net.Adapters.Common;
using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KuGou.Net.Protocol.Transport;
using KugouAvaloniaPlayer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KuGou.Net.Tests;

public sealed class SecurityVerificationTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task ChallengeHeaderSurvivesHttpErrorsWithoutRetry(HttpStatusCode status)
    {
        var handler = new ResponseHandler(status, """{"status":0,"error_code":20028,"data":{}}""", "event-1");
        using var http = new HttpClient(handler);
        var body = await new KgHttpTransport(http).SendAsync(new KgRequest { Path = "/test" });
        var model = KgApiResponseParser.Parse(body, TestJsonContext.Default.LoginResponse);
        Assert.Equal("event-1", model!.SsaCode);
        Assert.Equal(20028, model.ErrorCode);
        Assert.True(model.RequiresVerification);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task OrdinaryHttpFailureStillThrows()
    {
        using var http = new HttpClient(new ResponseHandler(HttpStatusCode.BadRequest, "{}", null));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new KgHttpTransport(http).SendAsync(new KgRequest { Path = "/test" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>blocked</html>")]
    [InlineData("[]")]
    public async Task NonJsonChallengeRetainsEventWithoutRetry(string body)
    {
        var handler = new ResponseHandler(HttpStatusCode.BadGateway, body, "event-2");
        using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<KgVerificationRequiredException>(() =>
            new KgHttpTransport(http).SendAsync(new KgRequest { Path = "/test" }));
        Assert.Equal("event-2", ex.SsaCode);
        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void SuccessUnwrapKeepsOuterChallenge()
    {
        var model = KgApiResponseParser.Parse(JsonElement.Parse(
            """{"status":1,"ssaCode":"event-3","data":{"userid":42,"token":"not-authorized"}}"""),
            TestJsonContext.Default.LoginResponse)!;
        Assert.Equal(42, model.UserId);
        Assert.Equal(1, model.Status);
        Assert.True(model.RequiresVerification);
    }

    [Fact]
    public async Task ChallengeDoesNotPersistLoginToken()
    {
        var fake = new FakeTransport("""{"status":1,"ssaCode":"event-3","data":{"userid":42,"token":"not-authorized"}}""");
        var session = new KgSessionManager(new CookieContainer());
        var login = new LoginClient(new RawLoginApi(fake, session, NullLogger<RawLoginApi>.Instance),
            session, NullLogger<LoginClient>.Instance);
        var result = await login.LoginByMobileAsync("13800000000", "123456");
        Assert.True(result!.RequiresVerification);
        Assert.Equal("", session.Session.Token);
    }

    [Theory]
    [InlineData("\"12345\"")]
    [InlineData("12345")]
    public async Task VerificationInfoSupportsStringAndNumberAppId(string appId)
    {
        var transport = new FakeTransport("{\"status\":1,\"data\":{\"v_type\":23,\"txappid\":" + appId + "}}");
        var api = new RawVerificationApi(transport, new KgSessionManager(new CookieContainer()));
        var result = await new VerificationClient(api).GetInfoAsync("event");
        Assert.Equal("12345", result!.TencentAppId);
        Assert.Equal(23, result.VerificationType);
        Assert.Equal("/verifyservice/v3/get_verify_info", transport.Request!.Path);
        Assert.Equal("event", ((JsonObject)transport.Request.Body!)["eventid"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(23, "verifycode")]
    [InlineData(32, "code")]
    public async Task SubmissionPreservesUnescapedProofAndUsesExpectedEndpoint(int type, string field)
    {
        var transport = new FakeTransport("""{"status":1,"error_code":0,"data":{}}""");
        var api = new RawVerificationApi(transport, new KgSessionManager(new CookieContainer()));
        var result = await new VerificationClient(api).SubmitAsync("event", type, "proof%2B+", "sid+/=", "edt+/=");
        Assert.True(result!.IsSuccess);
        var request = transport.Request!;
        var body = (JsonObject)request.Body!;
        Assert.Equal("https://verifyservice.kugou.com", request.BaseUrl);
        Assert.Equal("/v4/verify_user_info", request.Path);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("11510", request.Params["clientver"]);
        Assert.Equal(type, body["v_type"]!.GetValue<int>());
        Assert.Equal("proof%2B+", body[field]!.GetValue<string>());
        Assert.Equal("sid+/=", body["sid"]!.GetValue<string>());
        Assert.Equal("edt+/=", body["edt"]!.GetValue<string>());
        Assert.NotEmpty(Convert.FromHexString(body["params"]!.GetValue<string>()));
        Assert.NotEmpty(Convert.FromHexString(body["pk"]!.GetValue<string>()));
    }

    [Fact]
    public async Task FailedVerificationIsNotSuccess()
    {
        var transport = new FakeTransport("""{"status":0,"error_code":12345}""");
        var client = new VerificationClient(new RawVerificationApi(transport, new KgSessionManager(new CookieContainer())));
        Assert.False((await client.SubmitAsync("event", 23, "proof", "sid", "edt"))!.IsSuccess);
    }

    [Fact]
    public async Task SuccessfulVerificationRetriesExactlyOnce()
    {
        var calls = 0;
        var verifications = 0;
        var result = await SecurityVerifiedRequest.ExecuteAsync(
            () => Task.FromResult<LoginResponse?>(++calls == 1 ? Challenge() : new LoginResponse { Status = 1 }),
            (id, _) => { Assert.Equal("event", id); verifications++; return Task.FromResult(true); }, default);
        Assert.Equal(1, result!.Status);
        Assert.Equal(2, calls);
        Assert.Equal(1, verifications);
    }

    [Fact]
    public async Task RepeatedChallengeDoesNotLoop()
    {
        var calls = 0;
        var verifications = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => SecurityVerifiedRequest.ExecuteAsync(
            () => { calls++; return Task.FromResult<LoginResponse?>(Challenge()); },
            (_, _) => { verifications++; return Task.FromResult(true); }, default));
        Assert.Equal(2, calls);
        Assert.Equal(1, verifications);
    }

    [Fact]
    public async Task CancelledVerificationNeverReplaysOperation()
    {
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SecurityVerifiedRequest.ExecuteAsync(
            () => { calls++; return Task.FromResult<LoginResponse?>(Challenge()); },
            (_, _) => Task.FromResult(false), default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellationAfterVerificationStopsRetry()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SecurityVerifiedRequest.ExecuteAsync(
            () => { calls++; return Task.FromResult<LoginResponse?>(Challenge()); },
            (_, _) => { cts.Cancel(); return Task.FromResult(true); }, cts.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task MissingEventDoesNotOpenVerification()
    {
        var opened = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => SecurityVerifiedRequest.ExecuteAsync(
            () => Task.FromResult<LoginResponse?>(new LoginResponse { Status = 0, ErrorCode = 20028 }),
            (_, _) => { opened = true; return Task.FromResult(true); }, default));
        Assert.False(opened);
    }

    private static LoginResponse Challenge() => new() { Status = 0, ErrorCode = 20028, SsaCode = "event" };

    private sealed class ResponseHandler(HttpStatusCode status, string body, string? eventId) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            if (eventId is not null) response.Headers.Add("SSA-CODE", eventId);
            return Task.FromResult(response);
        }
    }

    private sealed class FakeTransport(string response) : IKgTransport
    {
        public KgRequest? Request { get; private set; }
        public Task<JsonElement> SendAsync(KgRequest request)
        {
            Request = request;
            return Task.FromResult(JsonElement.Parse(response));
        }
    }
}

[JsonSerializable(typeof(LoginResponse))]
internal partial class TestJsonContext : JsonSerializerContext;
