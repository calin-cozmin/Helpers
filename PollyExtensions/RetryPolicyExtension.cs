using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Timeout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace YourProjectNamespace
{
    /// <summary>
    /// The Policy Extension class.
    /// </summary>
    /// <see cref="https://github.com/App-vNext/Polly"/>
    /// <see cref="https://docs.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/use-httpclientfactory-to-implement-resilient-http-requests"/>
    /// <see cref="https://docs.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/implement-http-call-retries-exponential-backoff-polly"/>
    /// <remarks>
    /// HttpClient.Timeout represents the overall timeout which is applied for all retry requests made by polly. The default timeout for HttpClient is 100 seconds.
    /// The Timeout Policy sets the timeout for each retry request from retry policy defined for builder.
    /// Do not confuse overall timeout with policy retry timeout.
    /// If the sum of overall retry timeouts and sleep duration between retries is greater than the default value of the HttpClient Timeout, then a TaskCanceledException will be thrown because the entire operation will be closed by the HttpClient itself <see cref="System.Threading.Tasks.TaskCanceledException"/>
    /// </remarks>
    public static class PollyExtension
    {
        private const string PolyRetryOnResultHeader = "x-poly-retry-on-result";

        /// <summary>
        /// This retry policy is used to retry async a piece of code.
        /// </summary>
        /// <param name="retryCount">Represents the number of retries of your custom code.</param>
        /// <param name="customAction">This represents the delegate with the custom code that should be retried.</param>
        /// <param name="pauseSecondsBetweenFailures">Represent the number of seconds used to wait between retries.</param>
        public static Task<T> RetryRequestWithPolicyAsync<T, T1>(
            Func<Task<T>> customAction,
            int retryCount,
            TimeSpan pauseSecondsBetweenFailures) where T1 : Exception
        {
            return
                Policy
                    .Handle<T1>()
                    .WaitAndRetryAsync(retryCount, i => pauseSecondsBetweenFailures).ExecuteAsync(() => customAction?.Invoke());
        }

        /// <summary>
        /// This retry policy is used to retry async a piece of code.
        /// </summary>
        /// <param name="retryCount">Represents the number of retries of your custom code.</param>
        /// <param name="customAction">This represents the delegate with the custom code that should be retried.</param>
        /// <param name="pauseSecondsBetweenFailures">Represent the number of seconds used to wait between retries.</param>
        public static Task<T> RetryRequestWithPolicyAsync<T, T1, T2>(
            Func<Task<T>> customAction,
            int retryCount,
            TimeSpan pauseSecondsBetweenFailures) where T1 : Exception where T2 : Exception
        {
            return
                Policy
                    .Handle<T1>()
                    .Or<T2>()
                    .WaitAndRetryAsync(retryCount, i => pauseSecondsBetweenFailures).ExecuteAsync(() => customAction?.Invoke());
        }

        /// <summary>
        /// This policy will be added and configured in Startup.cs or where you add your http clients to services. It will retry the http call as many times as it is configured.
        /// </summary>
        /// <param name="builder">The http client builder parameter. This is actually the AddHttpClient<>().</param>
        /// <param name="onRetryAsyncFunc">This represents the delegate with the parameters obtained after each call. It can be used when it is configured in order to add you custom logic after the http call is done (log, do something else).</param>
        /// <param name="retryAttempts">Represents the number of retries of your http call.</param>
        /// <param name="httpStatusCodeToRetry">Represents a list of http status codes. You can add you own logic here (for which http codes the retry should happen). Be aware that default behaviour is to not retry an http call if the result code belongs to 2xx http status codes.</param>
        /// <param name="onResultFunc">Represent a custom action which can be condigured at startup in order to execeute that specific action on the received http response message.</param>
        /// <param name="sleepDuration">Represent the number of seconds used to wait between http calls retries.</param>
        /// <param name="timeoutDurationPerPolicyRequest">Represent the number of seconds used to wait for each policy retry request. The exception thrown in case the request will overlap the value is  <see cref="T:Polly.Timeout.TimeoutRejectedException" />. In case this property is not set (null) then no timeout policy will be added to this http client.</param>
        /// <returns>The policy handlers with all the configurable rules.</returns>
        public static IHttpClientBuilder ConfigureRetryPolicy(
              this IHttpClientBuilder builder,
              Func<DelegateResult<HttpResponseMessage>, TimeSpan, int, Context, Task> onRetryAsyncFunc = null,
              int retryAttempts = 3,
              IEnumerable<HttpStatusCode> httpStatusCodeToRetry = null,
              Func<HttpResponseMessage, Task<bool>> onResultFuncAsync = null,
              TimeSpan? sleepDuration = null,
              TimeSpan? timeoutDurationPerPolicyRequest = null)
        {
            var concreteTimeoutDurationPerPolicyRequest = GetConcreteTimeoutPerPolicyRequest(timeoutDurationPerPolicyRequest);

            var timeOutPolicy = BuildTimeoutPolicy(concreteTimeoutDurationPerPolicyRequest);
            var retryPolicy = BuildRetryPolicy(retryAttempts, onRetryAsyncFunc, httpStatusCodeToRetry, onResultFuncAsync, sleepDuration);

            builder.AddPolicyHandler(retryPolicy);
            builder.AddPolicyHandler(timeOutPolicy);

            builder.ChangeHttpClientTimoutValue(retryAttempts, concreteTimeoutDurationPerPolicyRequest, sleepDuration);

            return builder;
        }

        private static IAsyncPolicy<HttpResponseMessage> BuildRetryPolicy(
            int retryAttempts,
            Func<DelegateResult<HttpResponseMessage>, TimeSpan, int, Context, Task> onRetryAsyncFunc = null,
            IEnumerable<HttpStatusCode> httpStatusCodeToRetry = null,
            Func<HttpResponseMessage, Task<bool>> onResultFuncAsync = null,
            TimeSpan? sleepDuration = null)
        {
            Polly.Retry.AsyncRetryPolicy<HttpResponseMessage> retryPolicy;
            if (onRetryAsyncFunc == null)
            {
                retryPolicy = Policy<HttpResponseMessage>
                    .Handle<TimeoutRejectedException>()
                    .Or<HttpRequestException>()
                    .OrResult(httpResponseMessage => RetryFor(httpStatusCodeToRetry, httpResponseMessage))
                    .OrResult(httpResponseMessage => onResultFuncAsync != null && DoesHeadersContainsCustomPollyHeader(httpResponseMessage))
                    .WaitAndRetryAsync(
                        retryAttempts,
                        retryAttempt => sleepDuration ?? TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                    );
            }
            else
            {
                retryPolicy = Policy<HttpResponseMessage>
                    .Handle<TimeoutRejectedException>()
                    .Or<HttpRequestException>()
                    .OrResult(httpResponseMessage => RetryFor(httpStatusCodeToRetry, httpResponseMessage))
                    .OrResult(httpResponseMessage => onResultFuncAsync != null && DoesHeadersContainsCustomPollyHeader(httpResponseMessage))
                    .WaitAndRetryAsync(
                        retryAttempts,
                        retryAttempt => sleepDuration ?? TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        onRetryAsyncFunc
                    );
            }

            return GetFinalPolicy(retryPolicy, onResultFuncAsync);
        }

        private static IAsyncPolicy<HttpResponseMessage> GetFinalPolicy(Polly.Retry.AsyncRetryPolicy<HttpResponseMessage> retryPolicy, Func<HttpResponseMessage, Task<bool>> onResultFuncAsync = null)
        {
            if (onResultFuncAsync == null)
            {
                return retryPolicy;
            }

            var fallbackPolicy = BuildFallbackPolicy(onResultFuncAsync);
            return retryPolicy.WrapAsync(fallbackPolicy);
        }

        private static IAsyncPolicy<HttpResponseMessage> BuildFallbackPolicy(Func<HttpResponseMessage, Task<bool>> onResultAsync)
        {
            var policy = Policy<HttpResponseMessage>
                .HandleResult(r => !r.Headers.Contains(PolyRetryOnResultHeader))
                .FallbackAsync(async (delegateOutcome, context, token) =>
                {
                    var httpResponseMessage = delegateOutcome.Result;
                    var result = await onResultAsync(httpResponseMessage);
                    httpResponseMessage.Headers.Add(PolyRetryOnResultHeader, result.ToString());

                    return httpResponseMessage;
                },
                (delegateOutcome, context) => { return Task.CompletedTask; });

            return policy;
        }

        private static bool DoesHeadersContainsCustomPollyHeader(HttpResponseMessage httpResponseMessage)
        {
            var result = httpResponseMessage.Headers.TryGetValues(PolyRetryOnResultHeader, out var vs) && vs.Any(v => v == true.ToString());
            return result;
        }

        private static bool RetryFor(IEnumerable<HttpStatusCode> httpStatusCodeToRetry, HttpResponseMessage httpResponseMessage)
        {
            if (httpStatusCodeToRetry != null)
            {
                return httpStatusCodeToRetry.Contains(httpResponseMessage.StatusCode);
            }

            return false;
        }

        private static IAsyncPolicy<HttpResponseMessage> BuildTimeoutPolicy(TimeSpan retryTimeout)
        {
            return Policy.TimeoutAsync<HttpResponseMessage>(timeout: retryTimeout, timeoutStrategy: TimeoutStrategy.Pessimistic);
        }

        private static void ChangeHttpClientTimoutValue(
            this IHttpClientBuilder builder,
            int retryAttempts,
            TimeSpan timeoutDurationPerPolicyRequest,
            TimeSpan? sleepDuration)
        {
            var durationBetweenRequests = CalculateDurationBetweenRequests(retryAttempts, sleepDuration);

            var newRetryAttemps = retryAttempts + 1;
            var calculatedHttpClientTimeout = durationBetweenRequests + newRetryAttemps * timeoutDurationPerPolicyRequest;

            builder.ConfigureHttpClient(client =>
            {
                if (client.Timeout < calculatedHttpClientTimeout)
                {
                    client.Timeout = calculatedHttpClientTimeout;
                }
            });
        }

        private static TimeSpan CalculateDurationBetweenRequests(int retryAttempts, TimeSpan? sleepDuration)
        {
            if (sleepDuration.HasValue)
            {
                return retryAttempts * sleepDuration.Value;
            }

            TimeSpan calculatedHttpClientTimeout = TimeSpan.Zero;

            int i = 1;
            while (i <= retryAttempts)
            {
                calculatedHttpClientTimeout += TimeSpan.FromSeconds(Math.Pow(2, i));
                i++;
            }

            return calculatedHttpClientTimeout;
        }

        private static TimeSpan GetConcreteTimeoutPerPolicyRequest(TimeSpan? timeoutDurationPerPolicyRequest = null)
        {
            if (timeoutDurationPerPolicyRequest.HasValue)
            {
                return timeoutDurationPerPolicyRequest.Value;
            }
            else
            {
                return TimeSpan.FromSeconds(100);
            }
        }
    }
}



// ---------------------Startup.cs

// read configs from appSettings.json
var retryAttempts = Configuration.GetSection("HttpClientPolicy:RetryAttempts").Get<int>();

// Add http clients
ILogger<YourService> logger;
services.AddHttpClient<IYourService, YourService>((serviceProvider, httpClient) =>
            {
                logger = serviceProvider.GetService<ILogger<YourService>>();
            })
		.ConfigureRetryPolicy(
		 onRetryAsyncFunc: async (httpResponseMessage, timeSpan, retryCount, context) =>
		 {
		     await Task.FromResult($"{httpResponseMessage}, {timeSpan}, {retryCount}, {context}, {leaseWaveLogger}");
		 },
		retryAttempts: httpClientPolicyOptions.RetryAttempts,
		httpStatusCodeToRetry: new List<HttpStatusCode>() { HttpStatusCode.BadGateway },
		onResultFuncAsync: async x => await // do something to check the httpresonsemessage result);

// -------------------appSettings.json
 "HttpClientPolicy": {
      "RetryAttempts": "6" 
  }
