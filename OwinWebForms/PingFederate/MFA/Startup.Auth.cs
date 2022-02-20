namespace PingFederate
{
    using System;
    using System.Net;
    using System.Security.Claims;
    using System.Threading.Tasks;
    using System.Web.Helpers;
    using Microsoft.Owin;
    using Microsoft.Owin.Security;
    using Microsoft.Owin.Security.Cookies;

    using Owin;
    using Owin.Security.Providers.PingFederate;
    using Owin.Security.Providers.PingFederate.Provider;

    public partial class Startup
    {
        public void ConfigureAuth(IAppBuilder app)
        {
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            AntiForgeryConfig.UniqueClaimTypeIdentifier = "antiforgery";
            const string Cookies = "PingFederateCookie";
            const int SessionTimeout = 15;

            app.SetDefaultSignInAsAuthenticationType(Cookies);
            app.UseCookieAuthentication(
                new CookieAuthenticationOptions
                {
                    //LoginPath = new PathString("https://localhost:<yourprotnumber>/Default.aspx"),
                    LoginPath = new PathString("/"),
                    AuthenticationMode = AuthenticationMode.Active,
                    AuthenticationType = Cookies,
                    ExpireTimeSpan = TimeSpan.FromSeconds(SessionTimeout),
                    CookieSecure = CookieSecureOption.SameAsRequest,
                    CookiePath = "/",
                    SlidingExpiration = true,
                    //CookieManager = new SystemWebChunkingCookieManager(),
                    //ReturnUrlParameter = "Default.aspx"
                    //CookieName = Cookies
                });

            // SET UP VARIABLES
            const string ClientId = "your client id";
            const string ClientSecret = "your client secred";
            const string Scopes = "openid";
            const string PingServer = "your ping server";
            const string IdpAdapterId = "myidp";

            app.UsePingFederateAuthentication(
                new PingFederateAuthenticationOptions
                {
                    ClientId = ClientId,
                    ClientSecret = ClientSecret,
                    RequestUserInfo = false,
                    AuthenticationMode = AuthenticationMode.Active,
                    Scope = Scopes.Split(' '),
                    PingFederateUrl = PingServer,
                    IdpAdapterId = IdpAdapterId,
                    DiscoverMetadata = true,
                    Endpoints = new PingFederateAuthenticationEndpoints
                    {
                        MetadataEndpoint = PingFederateAuthenticationOptions.OpenIdConnectMetadataEndpoint
                    },
                    SignInAsAuthenticationType = Cookies,
                    Provider = new PingFederateAuthenticationProvider
                    {
                        OnAuthenticated = context =>
                             {
                                 context.Identity.AddClaim(new Claim("antiforgery", Guid.NewGuid().ToString()));

                                 return Task.FromResult(0);
                             },
                        OnReturnEndpoint = context =>
                        {
                            return Task.FromResult(0);
                        },
                        OnTokenRequest = context =>
                        {
                            return Task.FromResult(0);
                        },
                        OnAuthenticating = context => 
                        {
                            return Task.FromResult(0);
                        }
                    },
                    //CallbackPath = new PathString("/Default.aspx"),

                });
        }
    }
}
