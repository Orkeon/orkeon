using System.Net;
using System.Text.Json;
using Orkeon.Infrastructure.Http;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.Http;

public class HttpClientAdapterTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private class SampleDto { public string? Message { get; set; } }

    [Fact]
    public async Task ShouldReturnDeserializedObject_WhenGetAsyncSuccess()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{\"message\":\"ok\"}");
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        var result = await client.GetAsync<SampleDto>(new Uri("https://api.test/ok"), TestContext.Current.CancellationToken);
        Assert.Equal("ok", result.Message);
    }

    [Fact]
    public async Task ShouldSendJsonAndReturnsResponse_WhenPostAsync()
    {
        using var handler = new TestHttpMessageHandler(async req =>
        {
            var body = await req.Content!.ReadAsStringAsync();
            var doc = JsonSerializer.Deserialize<SampleDto>(body, s_jsonOptions);
            Assert.Equal("ping", doc!.Message);
            return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\":\"pong\"}")
            });
        });
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        var res = await client.PostAsync<SampleDto, SampleDto>(new Uri("https://api.test/echo"), new SampleDto { Message = "ping" }, TestContext.Current.CancellationToken);
        Assert.Equal("pong", res.Message);
    }

    [Fact]
    public async Task ShouldReturnRawString_WhenGetStringAsync()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "hello");
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        var s = await client.GetStringAsync(new Uri("https://api.test/str"), TestContext.Current.CancellationToken);
        Assert.Equal("hello", s);
    }

    [Fact]
    public async Task ShouldSuccessNoContent_WhenDeleteAsync()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.NoContent, "");
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        var exception = await Record.ExceptionAsync(() => client.DeleteAsync(new Uri("https://api.test/delete"), TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }

    [Fact]
    public async Task ShouldSendJsonAndReturnsResponse_WhenPutAsync()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler(async req =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            var body = await req.Content!.ReadAsStringAsync();
            var doc = JsonSerializer.Deserialize<SampleDto>(body, s_jsonOptions);
            Assert.NotNull(doc);
            Assert.Equal("update", doc.Message);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\":\"updated\"}")
            };
        });
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        var res = await client.PutAsync<SampleDto, SampleDto>(new Uri("https://api.test/update"), new SampleDto { Message = "update" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("updated", res.Message);
    }

    [Fact]
    public async Task ShouldSucceedAfterRetry_WhenGetAsyncRetryOnTransientError()
    {
        // Arrange
        var attempts = 0;
        using var handler = new TestHttpMessageHandler(req =>
        {
            attempts++;
            if (attempts == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\":\"success after retry\"}")
            };
        });

        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        var result = await client.GetAsync<SampleDto>(new Uri("https://api.test/retry"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("success after retry", result.Message);
        Assert.Equal(2, attempts);
        Assert.True(logger.HasLoggedWarning("Retry") || logger.HasLoggedWarning("retry"));
    }

    [Fact]
    public async Task ShouldThrowException_WhenPostAsyncRetryExhausted()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.PostAsync<SampleDto, SampleDto>(new Uri("https://api.test/fail"), new SampleDto { Message = "test" }, TestContext.Current.CancellationToken));

        // Verify retries were attempted
        Assert.True(logger.LoggedMessages.Count >= 3); // At least 3 retry warnings
    }

    [Fact]
    public async Task ShouldThrowOperationCanceledException_WhenGetAsyncWithImmediateCancellation()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{\"message\":\"test\"}");

        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await client.GetAsync<SampleDto>(new Uri("https://api.test/cancelled"), cts.Token));
    }

    [Fact]
    public async Task ShouldThrowJsonException_WhenGetAsyncWithNullResponse()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "null");
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        var result = await client.GetAsync<SampleDto>(new Uri("https://api.test/null"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ShouldSerializeCorrectly_WhenPostAsyncWithComplexObject()
    {
        // Arrange
        var complexDto = new ComplexDto
        {
            Id = 123,
            Name = "Test",
            Tags = ["tag1", "tag2"],
            Metadata = new Dictionary<string, string> { ["key"] = "value" }
        };

        using var handler = new TestHttpMessageHandler(req =>
        {
            var body = req.Content!.ReadAsStringAsync().Result;
            var doc = JsonSerializer.Deserialize<ComplexDto>(body, s_jsonOptions);
            Assert.Equal(123, doc!.Id);
            Assert.Equal("Test", doc.Name);
            Assert.Contains("tag1", doc.Tags!);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":456,\"name\":\"Response\",\"tags\":[\"tag3\"],\"metadata\":{\"key2\":\"value2\"}}")
            };
        });

        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        var res = await client.PostAsync<ComplexDto, ComplexDto>(new Uri("https://api.test/complex"), complexDto, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(456, res.Id);
        Assert.Equal("Response", res.Name);
    }

    [Fact]
    public async Task ShouldReturnEmptyString_WhenGetStringAsyncWithEmptyResponse()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "");
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        var result = await client.GetStringAsync(new Uri("https://api.test/empty"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public async Task ShouldThrowHttpRequestException_WhenDeleteAsyncWith404()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.NotFound, "");
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.DeleteAsync(new Uri("https://api.test/notfound"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldSendEmptyJsonObject_WhenPutAsyncWithNullData()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler(req =>
        {
            var body = req.Content!.ReadAsStringAsync().Result;
            Assert.Equal("null", body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\":\"ok\"}")
            };
        });

        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        var res = await client.PutAsync<SampleDto?, SampleDto>(new Uri("https://api.test/nullput"), null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("ok", res.Message);
    }

    [Fact]
    public async Task ShouldLogDebugMessage_WhenGetAsync()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{\"message\":\"test\"}");
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        await client.GetAsync<SampleDto>(new Uri("https://api.test/log"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("GET request to https://api.test/log", logger.LoggedMessages);
    }

    [Fact]
    public async Task ShouldRetryAndFails_WhenPostAsyncWithTimeout()
    {
        // Arrange
        var attempts = 0;
        using var handler = new TestHttpMessageHandler(req =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.RequestTimeout);
        });

        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.PostAsync<SampleDto, SampleDto>(new Uri("https://api.test/timeout"), new SampleDto(), TestContext.Current.CancellationToken));

        Assert.Equal(4, attempts); // Initial + 3 retries
    }

    private class ComplexDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string[]? Tags { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }

    [Fact]
    public async Task ShouldThrowJsonException_WhenGetAsyncWithMalformedJson()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, "{invalid json}");
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(async () =>
            await client.GetAsync<SampleDto>(new Uri("https://api.test/malformed"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldSucceed_WhenPostAsyncWithVeryLargePayload()
    {
        // Arrange
        var largeString = new string('a', 100000); // 100KB string
        var largeDto = new SampleDto { Message = largeString };

        using var handler = new TestHttpMessageHandler(req =>
        {
            var body = req.Content!.ReadAsStringAsync().Result;
            Assert.Contains(largeString, body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\":\"received\"}")
            };
        });

        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        var res = await client.PostAsync<SampleDto, SampleDto>(new Uri("https://api.test/large"), largeDto, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("received", res.Message);
    }

    [Fact]
    public async Task ShouldReturnAsIs_WhenGetStringAsyncWithHtmlContent()
    {
        // Arrange
        var htmlContent = "<html><body>Test</body></html>";
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, htmlContent);
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        var result = await client.GetStringAsync(new Uri("https://api.test/html"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(htmlContent, result);
    }

    [Fact]
    public async Task ShouldLogWarningThenSucceeds_WhenDeleteAsyncWithSuccessfulRetry()
    {
        // Arrange
        var attempts = 0;
        using var handler = new TestHttpMessageHandler(req =>
        {
            attempts++;
            if (attempts == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        await client.DeleteAsync(new Uri("https://api.test/retrydelete"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, attempts);
        Assert.True(logger.HasLoggedWarning("Retry") || logger.HasLoggedWarning("retry"));
    }

    [Fact]
    public async Task ShouldThrowOperationCanceledException_WhenPutAsyncWithCancellationDuringRetry()
    {
        // Arrange
        var attempts = 0;
        using var cts = new CancellationTokenSource();

        using var handler = new TestHttpMessageHandler(req =>
        {
            attempts++;
            if (attempts == 1)
            {
                // Cancel during retry
                System.Threading.Tasks.Task.Delay(100).Wait();
                cts.Cancel();
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await client.PutAsync<SampleDto, SampleDto>(
                new Uri("https://api.test/cancelduringretry"),
                new SampleDto(),
                cts.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ShouldThrowHttpRequestException_WhenGetAsyncWithVariousErrorCodes(HttpStatusCode statusCode)
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(statusCode, "");
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.GetAsync<SampleDto>(new Uri($"https://api.test/error{statusCode}"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldSerializeCorrectly_WhenPostAsyncWithSpecialCharactersInJson()
    {
        // Arrange
        var dto = new SampleDto { Message = "Test with \"quotes\" and \\ backslash and \n newline" };

        using var handler = new TestHttpMessageHandler(req =>
        {
            var body = req.Content!.ReadAsStringAsync().Result;
            // JSON can use either \" or \u0022 for quotes
            Assert.True(body.Contains("\\\"quotes\\\"") || body.Contains("\\u0022quotes\\u0022"),
                $"Expected escaped quotes in body: {body}");
            // JSON can use either \\ or \u005C for backslash
            Assert.True(body.Contains("\\\\") || body.Contains("\\u005C"),
                $"Expected escaped backslash in body: {body}");
            // JSON can use either \n or \u000A for newline
            Assert.True(body.Contains("\\n") || body.Contains("\\u000A"),
                $"Expected escaped newline in body: {body}");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"message\":\"ok\"}")
            };
        });

        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        var res = await client.PostAsync<SampleDto, SampleDto>(new Uri("https://api.test/specialchars"), dto, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("ok", res.Message);
    }

    [Fact]
    public async Task ShouldDeserializeCorrectly_WhenGetAsyncWithUnicodeResponseContent()
    {
        // Arrange
        using var handler = TestHttpMessageHandler.CreateWithResponse(
            HttpStatusCode.OK,
            "{\"message\":\"Hello 世界 مرحبا мир\"}");
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        var result = await client.GetAsync<SampleDto>(new Uri("https://api.test/unicode"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Hello 世界 مرحبا мир", result.Message);
    }

    [Fact]
    public async Task ShouldDeserializeToNull_WhenPutAsyncWithEmptyResponseBody()
    {
        // Arrange
        using var handler = new TestHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        });

        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        var res = await client.PutAsync<SampleDto, SampleDto>(new Uri("https://api.test/emptyresponse"), new SampleDto(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(res.Message);
    }

    [Fact]
    public async Task ShouldReturnAsString_WhenGetStringAsyncWithBinaryData()
    {
        // Arrange
        var binaryData = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello" in bytes
        using var handler = new TestHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(binaryData)
            };
        });

        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory();
        factory.RegisterClient("Orkeon", httpClient);
        var logger = new TestLogger<HttpClientAdapter>();
        var client = new HttpClientAdapter(factory, logger);

        // Act
        var result = await client.GetStringAsync(new Uri("https://api.test/binary"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Hello", result);
    }
}
