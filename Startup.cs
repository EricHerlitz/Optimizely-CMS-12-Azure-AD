namespace CMS12AzureAd;

public class Startup
{
    private readonly IWebHostEnvironment _webHostingEnvironment;
    private readonly IConfiguration Configuration;
    public Startup(IWebHostEnvironment webHostingEnvironment, IConfiguration configuration)
    {
        _webHostingEnvironment = webHostingEnvironment;
        Configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        if (_webHostingEnvironment.IsDevelopment())
        {
            AppDomain.CurrentDomain.SetData("DataDirectory", Path.Combine(_webHostingEnvironment.ContentRootPath, "App_Data"));

            services.Configure<SchedulerOptions>(options => options.Enabled = false);
        }

        services
            .AddCmsAspNetIdentity<ApplicationUser>()
            .AddCms()
            .AddAdminUserRegistration()
            .AddEmbeddedLocalization<Startup>();

        var azureAdConfig = Configuration.GetSection("Azure:AD");

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = "azure";
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Events.OnSignedIn = async ctx =>
                {
                    if (ctx.Principal?.Identity is ClaimsIdentity claimsIdentity)
                    {
                        // Syncs user and roles so they are available to the CMS
                        var synchronizingUserService = ctx
                            .HttpContext
                            .RequestServices
                            .GetRequiredService<ISynchronizingUserService>();

                        await synchronizingUserService.SynchronizeAsync(claimsIdentity);
                    }
                };
            })
            .AddOpenIdConnect("azure", options =>
            {
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.SignOutScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.CallbackPath = "/signin-oidc";
                options.UsePkce = true;

                options.Authority = "https://login.microsoftonline.com/" + azureAdConfig["TenantID"] + "/v2.0";
                options.ClientId = azureAdConfig["ClientID"];
                options.ClientSecret = azureAdConfig["Secret"];

                options.Scope.Clear();
                options.Scope.Add(OpenIdConnectScope.OfflineAccess); // if you need refresh tokens
                options.Scope.Add(OpenIdConnectScope.Email);
                options.Scope.Add(OpenIdConnectScope.OpenIdProfile);
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    RoleClaimType = "roles",
                    NameClaimType = "name",
                    ValidateIssuer = false
                };

                options.Events = new OpenIdConnectEvents
                {
                    OnRedirectToIdentityProvider = ctx =>
                    {
                        // Prevent redirect loop
                        if (ctx.Response.StatusCode == 401)
                        {
                            ctx.HandleResponse();
                        }

                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = context =>
                    {
                        context.HandleResponse();
                        context.Response.BodyWriter.WriteAsync(Encoding.ASCII.GetBytes(context.Exception.Message));
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        // enable to print claims debug information in the console
                        //var user = context.Principal.Identity;
                        //if (user != null)
                        //{
                        //    var claims = ((System.Security.Claims.ClaimsIdentity)context.Principal.Identity).Claims;
                        //    foreach (var claim in claims)
                        //    {
                        //        Console.WriteLine($"{claim.Type}: {claim.Value}");
                        //    }
                        //}
                        return Task.CompletedTask;
                    }
                };
            });

    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapContent();
        });
    }
}