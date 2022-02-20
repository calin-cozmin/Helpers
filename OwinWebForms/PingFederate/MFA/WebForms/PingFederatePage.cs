using System;
using System.Web.UI;
using System.Linq;
using System.Web;
using Microsoft.Owin.Security;
using System.Globalization;
using System.Security.Claims;
using System.Collections.Generic;

namespace PingFederate
{
    public class PingFederatePage : MasterPage //Page
    {
        public void AuthenticateThroughPingFederate()
        {
            // TODO: Get Data from Dynamo SessionState
            // if data is null then check if the user is authenticaten and save the claims to session
            // if data is not null get the refresn token is not null but the user is not authenticated anymore, get a new access token from ping federate using the session sotred refresh token
            // then authenticate user
            // then save the user claims to session and repeate the process when needed

            var sessionStoredOwinClaims = Session["OwinClaims"];
            var authenticatonContext = Context.GetOwinContext().Authentication;

            if (sessionStoredOwinClaims == null && authenticatonContext.User.Identity.IsAuthenticated) 
            {
                //var identityClaims = GetClaimsIdentity();
                //if (identityClaims != null)
                //{
                //    Session["OwinClaims"] = identityClaims;
                //    sessionStoredOwinClaims = Session["OwinClaims"];
                //}
            }

            if (sessionStoredOwinClaims == null && !authenticatonContext.User.Identity.IsAuthenticated)
            {
                AuthenticateUser();
            }

            // refresh access token if access token is about to expire
            if (sessionStoredOwinClaims != null && authenticatonContext.User.Identity.IsAuthenticated)
            { 
            
            }

            // try to use the refresh token to authenticate the user again and if refresh token has failed to retrieve data then it means that the user needs to
            // go through the basic authentication process again
            if (sessionStoredOwinClaims != null && !authenticatonContext.User.Identity.IsAuthenticated)
            {

            }
        }

        public void SingOutFromPingFederate()
        {
            IAuthenticationManager authenticationManager = HttpContext.Current.GetOwinContext().Authentication;
            //authenticationManager.SignOut(DefaultAuthenticationTypes.ExternalCookie);

            //HttpContext.Current.Request.Cookies[".AspNet.PingFederateCookie"]
        }

        private void AuthenticateUser()
        {
            try
            {
                Context.GetOwinContext().Authentication.SignOut();
                var oauthProvider = Context.GetOwinContext().Authentication.GetAuthenticationTypes().Where(x => x.AuthenticationType.Equals("PingFederate")).FirstOrDefault();
                string redirectUrl = ResolveUrl(String.Format(CultureInfo.InvariantCulture, "~/Default.aspx"));
                var properties = new AuthenticationProperties() { RedirectUri = string.Empty };

                Context.GetOwinContext().Authentication.Challenge(properties, oauthProvider.AuthenticationType);
            }
            catch (Exception exception)
            {
                Context.GetOwinContext().Authentication.SignOut();
                Response.RedirectPermanent("InValidLogIn.aspx");
            }
        }

        //private IEnumerable<Claim> GetClaimsIdentity()
        //{ 
        //    return ((ClaimsIdentity)User.Identity).Claims;
        //}
    }
}
