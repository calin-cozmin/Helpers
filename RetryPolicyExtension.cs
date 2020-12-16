using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace YourProject
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
    [ExcludeFromCodeCoverage]
    public static class PollyExtension
    {
        /// <summary>
        /// This policy will be added and configured in Startup.cs or where you add your http clients to services. It will retry the http call as many times as it is configured.
        /// </summary>
        /// <param name="builder">The http client builder parameter. This is actually the AddHttpClient<>().</param>
        /// <param name="retryAttempts">Represents the number of retries of your http call.</param>
        /// <param name="customAction">This represents the delegate with the parameters obtained after each call. It can be used when it is configured in order to add you custom logic after the http call is done (log, do something else).</param>
        /// <param name="httpCodeFunc">Represents a func of http status codes. You can add you own logic here (for which http codes the retry should happen). Be aware that default behaviour is to not retry an http call if the result code belongs to 2xx http status codes.</param>
        /// <param name="sleepDuration">Represent the number of seconds used to wait between http calls retries.</param>
        /// <param name="timeoutDurationPerPolicyRequest">Represent the number of seconds used to wait for each policy retry request. The exception thrown in case the request will overlap the value is  <see cref="T:Polly.Timeout.TimeoutRejectedException" />. In case this property is not set (null) then no timeout policy will be added to this http client.</param>
        /// <param name="httpClientTimeout">Represent the number of seconds for the HttpClient Timeout.</param>
        /// <returns>The policy handlers with all the configurable rules.</returns>
        public static void AddPolicyExtension(
            this IHttpClientBuilder builder, 
            int retryAttempts, 
            Action<DelegateResult<HttpResponseMessage>, TimeSpan, int, Context> customAction, 
            Func<HttpResponseMessage, bool> httpCodeFunc = null, 
            Func<int, TimeSpan> sleepDuration = null,
            TimeSpan? timeoutDurationPerPolicyRequest = null,
            TimeSpan? httpClientTimeout = null)
        {
            if (httpClientTimeout.HasValue)
            {
                builder.ConfigureHttpClient(client =>
                {
                    client.Timeout = httpClientTimeout.Value;
                });
            }

            builder.AddPolicyHandler(BuildRetryPolicy(retryAttempts, customAction, httpCodeFunc, sleepDuration));

            if (timeoutDurationPerPolicyRequest.HasValue)
            {
                builder.AddPolicyHandler(BuildTimeoutPolicy(timeoutDurationPerPolicyRequest.Value));
            }
        }

        private static IAsyncPolicy<HttpResponseMessage> BuildRetryPolicy(int retryAttempts, Action<DelegateResult<HttpResponseMessage>, TimeSpan, int, Context> customAction, Func<HttpResponseMessage, bool> httpCodeFunc = null, Func<int, TimeSpan> sleepDuration = null)
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .Or<TimeoutRejectedException>()
                .OrResult(httpCodeFunc ?? (msg => !AllWhiteListHttpStatusCodes().Contains(msg.StatusCode)))
                .WaitAndRetryAsync(
                    retryAttempts,
                    sleepDuration ?? (retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))),
                    (ex, timeSpan, retryCount, ctx) =>
                    {
                        customAction(ex, timeSpan, retryCount, ctx);
                    }
                );
        }

        public static IAsyncPolicy<HttpResponseMessage> BuildTimeoutPolicy(TimeSpan timeSpan)
        {
            return Policy.TimeoutAsync<HttpResponseMessage>(timeout: timeSpan, timeoutStrategy: TimeoutStrategy.Pessimistic);
        }

        private static List<HttpStatusCode> AllWhiteListHttpStatusCodes()
        {
            var whiteListEnumList = Enum.GetValues(typeof(HttpStatusCode)).Cast<HttpStatusCode>().Where(htc => (int)htc >= 200 && (int)htc < 300).ToList();
       
            return whiteListEnumList;
        }
    }
}


// ---------------------Startup.cs

// read configs from appSettings.json
var retryAttempts = Configuration.GetSection("HttpClientPolicy:RetryAttempts").Get<int>();

// Add http clients
services.AddHttpClient<IYourService, YourService>().AddPolicyExtension(retryAttempts,
	(httpResponseMessage, timeSpan, retryCount, context) =>
	{
		Console.WriteLine($"{httpResponseMessage?.Exception?.Message} - {timeSpan.Seconds} - {retryCount} - {context.CorrelationId}");
	}, 
	 x => x.StatusCode == System.Net.HttpStatusCode.BadRequest,
	 y => TimeSpan.FromSeconds(10),
	TimeSpan.FromMilliseconds(10));
	

// -------------------appSettings.json
 "HttpClientPolicy": {
      "RetryAttempts": "6" 
  }
