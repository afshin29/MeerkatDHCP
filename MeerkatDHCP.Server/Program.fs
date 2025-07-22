open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Hosting

open Configurations


[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)

    builder
    |> addProjectConfigurations
    |> addProjectDatabase
    |> addProjectServices
    |> addProjectGrpc
    |> ignore

    let app = builder.Build()

    app.UseApplicationSecret() |> ignore
    app.MapGrpcService<Endpoints.DhcpServerGrpc>() |> ignore

    app.Run()

    0 // Exit code

