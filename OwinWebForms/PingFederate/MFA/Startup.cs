using Microsoft.Owin;
using Owin;

[assembly: OwinStartup(typeof(Startup))]
namespace PingFederate
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
        }
    }
}