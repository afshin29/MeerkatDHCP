module Configurations

open MeerkatDHCP.Database
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open ProtoBuf.Grpc.Server
open System.Linq
open System.Threading.Tasks
open System.Reflection


[<CLIMutable>]
type AppBehavior = {
    ApplicationPort: int
    ApplicationSecret: string
    StartupWorkerCount: int
    KeepLogsAndHistoryPeriodInDays: int
    LeaseOfferReservationTimeInSeconds: int
}


let addProjectConfigurations (builder: WebApplicationBuilder) =
    builder.Services
        .Configure<AppBehavior>(builder.Configuration.GetSection <| nameof AppBehavior)
        |> ignore

    builder


let addProjectDatabase (builder: WebApplicationBuilder) =
    let connectionString =
        builder.Configuration.GetConnectionString "Default"
        |> Option.ofObj
        |> Option.defaultValue "Data Source=./dhcp.db"

    let migrationAssembly = Assembly.GetAssembly(typeof<DhcpDbContext>).FullName
    builder.Services.AddDbContext<DhcpDbContext>(fun options ->
        options
            .UseSqlite(
                connectionString,

                (fun sqliteBuilder ->
                    sqliteBuilder.MigrationsAssembly(migrationAssembly) |> ignore
                )
            )
            .UseSeeding(DhcpDbContext.SeederFunc)
            |> ignore
    ) |> ignore

    builder


let addProjectServices (builder: WebApplicationBuilder) =
    builder.Services.AddHostedService<Jobs.DbCleanerJob>() |> ignore

    builder


let addProjectGrpc (builder: WebApplicationBuilder) =
    let applicationPort = builder.Configuration.GetValue<int>("AppBehavior:ApplicationPort")

    // Setup a HTTP/2 endpoint without TLS for gRPC
    builder.WebHost.ConfigureKestrel(fun options ->
        options.ListenAnyIP(applicationPort, fun config -> config.Protocols <- HttpProtocols.Http2)
    )
    |> ignore

    builder.Services.AddCodeFirstGrpc()

    builder



// Middlewares

type IApplicationBuilder with

    member app.UseApplicationSecret() =
        app.Use (fun (ctx: HttpContext) (next: RequestDelegate) ->
            let appBehavior = ctx.RequestServices.GetRequiredService<IOptions<AppBehavior>>().Value

            if ctx.Request.Headers["x-secret"].FirstOrDefault() <> appBehavior.ApplicationSecret then
                ctx.Response.StatusCode <- 401
                Task.CompletedTask
            else
                next.Invoke ctx
        ) |> ignore

        app