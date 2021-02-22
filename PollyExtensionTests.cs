using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Polly.Timeout;
using Xunit;

namespace YourProjectNamespace
{
    public class PollyExtensionTests
    {
        [Fact]
        public async Task PollyExtension_RetryRequestWithPolicyAsync_Valid()
        {
            // Act
            var elements = new List<string>();

            var exception = await Assert.ThrowsAsync<Exception>(async () =>
                await PollyExtension.RetryRequestWithPolicyAsync<int, Exception>(async () =>
                {
                    elements.Add("test element");
                    return await Retry();
                },
                3,
                TimeSpan.FromSeconds(1))
                );

            // Assert
            Assert.Equal(4, elements.Count);
            Assert.NotNull(exception);
        }

        [Fact]
        public async Task PollyExtension_RetryRequestWithPolicyAsync_WithTwoExceptionsAsParameters_Valid()
        {
            // Act
            var elements = new List<string>();

            var exception = await Assert.ThrowsAsync<CustomException>(async () =>
                await PollyExtension.RetryRequestWithPolicyAsync<int, Exception, CustomException>(async () =>
                    {
                        elements.Add("test element");
                        return await RetryCustomException();
                    },
                    3,
                    TimeSpan.FromSeconds(1))
            );

            // Assert
            Assert.Equal(4, elements.Count);
            Assert.NotNull(exception);
        }

        [Fact]
        public async Task PollyExtension_ConfigureRetryPolicy_RetryForTimeoutRejectedException_WhenTimeOutPerRetryIsConfigured_HttpClientTimeoutNotChanged()
        {
            // Arrange
            var elements = new List<string>();
            var services = new ServiceCollection();
            var testClient = "TestClient";
            var defaultHttpClientTimeout = TimeSpan.FromSeconds(100);
            var codeHandledByPolicy = HttpStatusCode.NotFound;

            services.AddHttpClient(testClient).ConfigureRetryPolicy(
               async (httpResponseMessage, timeSpan, retryCount, context) =>
               {
                   elements.Add("test element");
                   await Task.Delay(1);
               },
               3,
               null,
               null,
               TimeSpan.FromSeconds(1),
               TimeSpan.FromSeconds(5))
                .AddHttpMessageHandler(() => new HttpDelegatingHandlerMock(codeHandledByPolicy, null, TimeSpan.FromSeconds(10), true));

            var configuredClient = services
                    .BuildServiceProvider()
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(testClient);

            // Act
            var exception = await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>  await configuredClient.GetAsync("https://doesnotmatterwhatthisis.com"));
            
            // Assert
            Assert.Equal(defaultHttpClientTimeout, configuredClient.Timeout);
            Assert.Equal(3, elements.Count);
            Assert.NotNull(exception);
        }

        [Fact]
        public void PollyExtension_ConfigureRetryPolicy_WithClientTimeoutSetBeforeCPollyConfigurationIsDone()
        {
            // Arrange
            var elements = new List<string>();
            var services = new ServiceCollection();
            var testClient = "TestClient";
            var codeHandledByPolicy = HttpStatusCode.NotFound;
            var retryAttempts = 3;
            var newRetryAttemps = retryAttempts + 1;
            var durationBetweenRequests = TimeSpan.Zero;
            var clientTimeout = TimeSpan.FromMinutes(2);

            int i = 1;
            while (i <= retryAttempts)
            {
                durationBetweenRequests += TimeSpan.FromSeconds(Math.Pow(2, i));
                i++;
            }
            var calculatedHttpClientTimeout = durationBetweenRequests + clientTimeout * newRetryAttemps;

            services.AddHttpClient(testClient, httpClient => 
            {
                httpClient.Timeout = TimeSpan.FromMinutes(2);
            }).ConfigureRetryPolicy(
               onRetryAsyncFunc: async (httpResponseMessage, timeSpan, retryCount, context) =>
               {
                   elements.Add("test element");
                   await Task.Delay(1);
               },
               timeoutDurationPerPolicyRequest: TimeSpan.FromMinutes(2))
               .AddHttpMessageHandler(() => new HttpDelegatingHandlerMock(codeHandledByPolicy, null, TimeSpan.FromMinutes(3), false));

            var configuredClient = services
                    .BuildServiceProvider()
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(testClient);

            // Assert
            Assert.Equal(calculatedHttpClientTimeout, configuredClient.Timeout);
        }

        [Fact]
        public async Task PollyExtension_ConfigureRetryPolicy_RetryForTimeoutRejectedException_WhenTimeOutPerRetryIsNotConfigured_HttpClientTimeoutChanged()
        {
            // Arrange
            var elements = new List<string>();
            var services = new ServiceCollection();
            var testClient = "TestClient";
            var calculatedTimeout = TimeSpan.FromSeconds(403);
            var codeHandledByPolicy = HttpStatusCode.NotFound;

            services.AddHttpClient(testClient).ConfigureRetryPolicy(
               async (httpResponseMessage, timeSpan, retryCount, context) =>
               {
                   elements.Add("test element");
                   await Task.Delay(1);
               },
               3,
               new List<HttpStatusCode> { codeHandledByPolicy },
               null,
               TimeSpan.FromSeconds(1))
                .AddHttpMessageHandler(() => new HttpDelegatingHandlerMock(codeHandledByPolicy, null, TimeSpan.FromSeconds(200)));

            var configuredClient = services
                    .BuildServiceProvider()
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(testClient);

            // Act
            var exception = await Assert.ThrowsAsync<TaskCanceledException>(async () => await configuredClient.GetAsync("https://doesnotmatterwhatthisis.com"));

            // Assert
            Assert.Equal(calculatedTimeout, configuredClient.Timeout);
            Assert.Equal(3, elements.Count);
            Assert.NotNull(exception);
        }

        [Fact]
        public async Task PollyExtension_ConfigureRetryPolicy_RetryForHttpException()
        {
            // Arrange
            var elements = new List<string>();
            var services = new ServiceCollection();
            var testClient = "TestClient";

            services.AddHttpClient(testClient).ConfigureRetryPolicy(
               async (httpResponseMessage, timeSpan, retryCount, context) =>
               {
                   elements.Add("test element");
                   await Task.Delay(1);
               },
               3,
               null,
               sleepDuration: TimeSpan.FromSeconds(1));

            var configuredClient = services
                    .BuildServiceProvider()
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(testClient);

            // Act
            var exception = await Assert.ThrowsAsync<HttpRequestException>(async () => await configuredClient.GetAsync("http://doesnotmatterwhatthisis.com"));

            // Assert
            Assert.Equal(3, elements.Count);
            Assert.NotNull(exception);
        }

        [Fact]
        public async Task PollyExtension_ConfigureRetryPolicy_OnResultFunc_Valid()
        {
            // Arrange
            var elements = new List<string>();
            var services = new ServiceCollection();
            var testClient = "TestClient";
            var codeHandledByPolicy = HttpStatusCode.OK;
            var responseContent = "Conflicted records";

            services.AddHttpClient(testClient).ConfigureRetryPolicy(
                async (httpResponseMessage, timeSpan, retryCount, context) =>
                {
                    elements.Add("test element");
                    await Task.Delay(1);
                },
                3,
                null,
                onResultFuncAsync: async x => await ShouldRetryCall(x),
                timeoutDurationPerPolicyRequest: TimeSpan.FromSeconds(1),
                sleepDuration: TimeSpan.FromSeconds(0.2))
                .AddHttpMessageHandler(() => new HttpDelegatingHandlerMock(codeHandledByPolicy, responseContent, null));

            var configuredClient = services
                    .BuildServiceProvider()
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(testClient);

            // Act
            var result = await configuredClient.GetAsync("https://doesnotmatterwhatthisis.com");

            // Assert
            Assert.Equal(3, elements.Count);
        }

        [Fact]
        public async Task PollyExtension_ConfigureRetryPolicy_ForConflictedRecords_RetryNoConfigurationSet()
        {
            // Arrange
            var elements = new List<string>();
            var services = new ServiceCollection();
            var testClient = "TestClient";
            var codeHandledByPolicy = HttpStatusCode.OK;
            var responseContent = "Conflicted records";

            services.AddHttpClient(testClient)
               .ConfigureRetryPolicy(
               async (httpResponseMessage, timeSpan, retryCount, context) =>
               {
                   elements.Add("test element");
                   await Task.Delay(1);
               },
               onResultFuncAsync: async x => await ShouldRetryCall(x))
               .AddHttpMessageHandler(() => new HttpDelegatingHandlerMock(codeHandledByPolicy, responseContent, null));

            var configuredClient = services
                    .BuildServiceProvider()
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(testClient);

            // Act
            var result = await configuredClient.GetAsync("https://doesnotmatterwhatthisis.com");

            // Assert
            Assert.Equal(3, elements.Count);
        }

        [Fact]
        public async Task PollyExtension_ConfigureRetryPolicy_ForLockedUsserErrorMessageAndLockedUserErrorOperation_RetryNoConfigurationSet()
        {
            // Arrange
            var elements = new List<string>();
            var services = new ServiceCollection();
            var testClient = "TestClient";
            var codeHandledByPolicy = HttpStatusCode.OK;
            var responseContent = "Locked user error message locked user error operation";

            services.AddHttpClient(testClient).ConfigureRetryPolicy(
                async (httpResponseMessage, timeSpan, retryCount, context) =>
                {
                    elements.Add("test element");
                    await Task.Delay(1);
                },
                onResultFuncAsync: async x => await ShouldRetryCall(x))
                .AddHttpMessageHandler(() => new HttpDelegatingHandlerMock(codeHandledByPolicy, responseContent, null));

            var configuredClient = services
                    .BuildServiceProvider()
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(testClient);

            // Act
            var result = await configuredClient.GetAsync("https://doesnotmatterwhatthisis.com");

            // Assert
            Assert.Equal(3, elements.Count);
        }

        [Fact]
        public async Task PollyExtension_ConfigureRetryPolicy_ForOtherMessageThanErrorOnes_RetryNoConfigurationSet()
        {
            // Arrange
            var elements = new List<string>();
            var services = new ServiceCollection();
            var testClient = "TestClient";
            var codeHandledByPolicy = HttpStatusCode.OK;
            var responseContent = "test message";

            services.AddHttpClient(testClient).ConfigureRetryPolicy(
                async (httpResponseMessage, timeSpan, retryCount, context) =>
                {
                    elements.Add("test element");
                    await Task.Delay(1);
                },
                onResultFuncAsync: async x => await ShouldRetryCall(x))
                .AddHttpMessageHandler(() => new HttpDelegatingHandlerMock(codeHandledByPolicy, responseContent, null));

            var configuredClient = services
                    .BuildServiceProvider()
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(testClient);

            // Act
            var result = await configuredClient.GetAsync("https://doesnotmatterwhatthisis.com");

            // Assert
            Assert.Empty(elements);
        }

        [Fact]
        public async void PollyExtension_ConfigureRetryPolicy_HttpStatusCodesToRetry_Valid()
        {
            // Arrange
            var elements = new List<string>();
            var services = new ServiceCollection();
            var testClient = "TestClient";
            var codeHandledByPolicy = HttpStatusCode.BadGateway;

            services.AddHttpClient(testClient).ConfigureRetryPolicy(
                async (httpResponseMessage, timeSpan, retryCount, context) =>
                {
                    elements.Add("test element");
                    await Task.Delay(1);
                },
                3,
                new List<HttpStatusCode>() { codeHandledByPolicy },
                sleepDuration: TimeSpan.FromSeconds(1))
                .AddHttpMessageHandler(() => new HttpDelegatingHandlerMock(codeHandledByPolicy, null, null));

            var configuredClient = services
                    .BuildServiceProvider()
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(testClient);


           var result = await configuredClient.GetAsync("https://doesnotmatterwhatthisis.com");

            // Assert
            Assert.Equal(3, elements.Count);
        }

        public async Task<bool> ShouldRetryCall(HttpResponseMessage response) 
        {
            var stringResponse = await response.Content.ReadAsStringAsync();
           
            if (stringResponse != null && stringResponse.Contains("specific response contect message"))
            {
                return true;
            }

            return false;
        }

        private async Task<int> Retry()
        {
            await Task.Run(() => throw new Exception("test message"));

            return 1;
        }

        private async Task<int> RetryCustomException()
        {
            await Task.Run(() => throw new CustomException("test message"));

            return 1;
        }
    }
    
    internal class HttpDelegatingHandlerMock : DelegatingHandler
    {
        private readonly HttpStatusCode _returnedStatusCode;
        private readonly string _returnedJsonContent;
        private readonly TimeSpan? _requestDelay;
        private readonly bool _throwTimeoutRejectedException;
        private readonly bool _throwTaskCanceledException;

        public HttpDelegatingHandlerMock(
            HttpStatusCode returnedStatusCode, 
            string returnedJsonContent, 
            TimeSpan? requestDelay, 
            bool throwTimeoutRejectedException = false,
            bool throwTaskCanceledException = false)
        {
            _returnedStatusCode = returnedStatusCode;
            _returnedJsonContent = returnedJsonContent;
            _requestDelay = requestDelay;
            _throwTimeoutRejectedException = throwTimeoutRejectedException;
            _throwTaskCanceledException = throwTaskCanceledException;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_throwTimeoutRejectedException)
            {
                throw new TimeoutRejectedException();
            }

            if (_throwTaskCanceledException)
            {
                throw new TaskCanceledException();
            }

            if (_requestDelay.HasValue)
            {
                Thread.Sleep(_requestDelay.Value);
            }

            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = _returnedStatusCode,
                Content = _returnedJsonContent != null ? new StringContent(_returnedJsonContent) : null,
                RequestMessage = request
            });
        }
    }
}
