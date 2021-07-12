open Fable.SignalR
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Shared.Hub
open Serilog
open Serilog.Events
open Server.Config
open System.Threading
open System.Threading.Tasks

type Pinger (hub:FableHubCaller<Action,Response>) =
    let cts = new CancellationTokenSource()
    let rec loop () =
        async {
            Log.Information("Sending ping")
            do! hub.Clients.All.Send(Response.Ping "hello") |> Async.AwaitTask
            do! Async.Sleep 5000
            return! loop()
        }
        
    interface IHostedService with
        member _.StartAsync(ct) =
            let work = async { do Async.Start(loop(), cts.Token) }
            Async.StartAsTask(work, cancellationToken=ct) :> Task
        member _.StopAsync(ct) =
            let work = async { do cts.Cancel() }
            Async.StartAsTask(work, cancellationToken=ct) :> Task
    

let configureServices (config:Config) (services:IServiceCollection) =
    services
        .AddSignalR(Server.Hub.settings)
        .AddSingleton<Server.Store.LiveCosmosStore>(fun s ->
            Server.Store.LiveCosmosStore(config.CosmosDBConfig))
        .AddSingleton<Server.Vehicle.Service>(fun s ->
            let cosmosStore = s.GetRequiredService<Server.Store.LiveCosmosStore>()
            Server.Vehicle.Service(cosmosStore))
        .AddSingleton<Server.Inventory.Service>(fun s ->
            let cosmosStore = s.GetRequiredService<Server.Store.LiveCosmosStore>()
            Server.Inventory.Service(cosmosStore))
//        .AddHostedService<Pinger>(fun s ->
//            let hub = s.GetRequiredService<FableHubCaller<Action,Response>>()
//            Pinger(hub))
//        .AddHostedService<Server.Reactor.Service>(fun s ->
//            let cosmosStore = s.GetRequiredService<Server.Store.LiveCosmosStore>()
//            let inventoryService = s.GetRequiredService<Server.Inventory.Service>()
//            Server.Reactor.Service(cosmosStore, inventoryService))
        |> ignore
        
let configureApp (appBuilder:IApplicationBuilder) =
    appBuilder
        // NB: rewrite route / -> /index.html 
        .UseDefaultFiles()
        // NB: service static files from wwwroot dir
        .UseStaticFiles()
        .UseSignalR(Server.Hub.settings)
        |> ignore

[<EntryPoint>]
let main _argv =
    let config = Config.Load()
    let logger =
        LoggerConfiguration()
            .Enrich.WithProperty("Application", config.AppName)
            .Enrich.WithProperty("Environment", config.AppEnv)
            .MinimumLevel.Is(if config.AppEnv = AppEnv.Dev then LogEventLevel.Debug else LogEventLevel.Information)
            .WriteTo.Console()
            .WriteTo.Seq(config.SeqConfig.Url)
            .CreateLogger()
    Log.Logger <- logger
    Log.Debug("Debug mode")
    Log.Debug("{@Config}", config)
    try
       try
           WebHostBuilder()
               .UseKestrel()
               .UseSerilog()
               .ConfigureServices(configureServices config)
               .Configure(configureApp)
               .UseUrls(config.ServerConfig.Url)
               .Build()
               .Run()
       with ex ->
           Log.Error(ex, "Error running server")
    finally
        Log.CloseAndFlush()
    0 // return an integer exit code