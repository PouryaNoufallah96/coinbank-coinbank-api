using Autofac;
using Autofac.Extensions.DependencyInjection;
using CoinBank.Api.Utilities.Configurations;
using CoinBank.Api.Utilities.Middlewares;
using CoinBank.Services._PreSale._Hub;
using CoinBank.Services._Price._Hub;
using CoinBank.Services._Swap._Hub;
using CoinBank.Services._Transaction._Hub;
using System.Text.Json.Serialization;
using Utilities.Configuration;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddCustomControllers();

builder.Services.AddCustomApiVersioning();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwagger();

builder.Services.AddHttpClient();

builder.Services.AddMemoryCache();

builder.Services.AddCodeAssistantSettings(builder.Configuration);

builder.Services.AddSettings(builder.Configuration);



builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(autofacConfigure =>
{
    autofacConfigure.AddServices();
    autofacConfigure.AddControllerServices();
});

builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.Converters
       .Add(new JsonStringEnumConverter());
});

builder.WebHost.UseSentry(o =>
{
    o.Dsn = "https://30c849fb66da66c45623128c8bc84f76@o4510492345368576.ingest.de.sentry.io/4511528763326544";
    o.TracesSampleRate = 1.0;
    o.AttachStacktrace = true;
    o.SendDefaultPii = true;
    o.Debug = true;
    o.IncludeActivityData = true;
});


var app = builder.Build();

app.UseHsts(app.Environment);

app.UseDeveloperExceptionPage(app.Environment);

app.UseSwaggerAndUI();

app.UseRequestLogger();

app.UseCustomExceptionHandler();

app.UseJWTBlackList();

app.UseProductionCors();

app.UseFirewall();

app.UseSignature();

app.UseJwt();

app.UseRouting();

app.UseCustomRateLimiting();

app.UseAuthorization();

app.UseEndpoints();


app.MapHub<PriceHub>("/hubs/prices");
app.MapHub<PreSaleHub>("/hubs/presales");
app.MapHub<SwapHub>("/hubs/swap");
app.MapHub<WalletNotifyHub>("/hubs/notifywallet");
app.Run();
