module Endpoints

open FsToolkit.ErrorHandling
open MeerkatDHCP.Database
open MeerkatDHCP.GrpcContracts
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open System.Net
open System.Net.NetworkInformation
open System.Net.Sockets
open System.Threading.Tasks


type DhcpServerGrpc(ssf: IServiceScopeFactory, appBehavior: IOptions<Configurations.AppBehavior>, dbContext: DhcpDbContext) =

    interface IDhcpServerGrpc with

        member _.StartDhcpServer (request: StartRequest) : Task<CommandResult> = task {
            try
                // This better be valid! :D
                let listenIp = IPAddress.Parse(request.IpAddress)
                
                let workersCount = appBehavior.Value.StartupWorkerCount
                
                DhcpManager.commandsMailBox.Post <| DhcpManager.Start(listenIp, workersCount, ssf)
                
                return { ErrorMessage = None }
            with ex ->
                return { ErrorMessage = Some ex.Message }
        }
        
        member _.StopDhcpServer () : Task<CommandResult> = task {
            DhcpManager.commandsMailBox.Post DhcpManager.Stop
            
            return { ErrorMessage = None }
        }

        member _.GetServerConfigs () : Task<ServerConfigsResult> = task {

            let currentState = DhcpManager.commandsMailBox.PostAndReply(fun reply -> DhcpManager.GetState reply)

            let result = {
                Name = Dns.GetHostName() |> Option.ofObj
                Interfaces =
                    NetworkInterface.GetAllNetworkInterfaces()
                    |> Array.filter (fun nic -> nic.OperationalStatus = OperationalStatus.Up)
                    |> Array.choose (fun nic ->
                        nic.GetIPProperties().UnicastAddresses
                        |> Seq.tryFind (fun ip -> ip.Address.AddressFamily = AddressFamily.InterNetwork)
                        |> Option.map (fun ip -> ip.Address.ToString())
                    )
                CurrentState =
                    match currentState with
                    | Running ip -> $"Running on [{ip}]"
                    | Stopped -> "Stopped"
                    | RanToError err -> $"Ran to error: {err}"
            }

            return result
        }
            

        member _.AddScope(request: AddScopeRequest) : Task<CommandResult> = task {
            try
                let scope = Scope(
                    Name = request.Name,
                    Network = IPNetwork.Parse request.Network,
                    From = IPAddress.Parse request.FromIp,
                    To = IPAddress.Parse request.ToIp,
                    Gateway = IPAddress.Parse request.GatewayIpAddress,
                    Dns1 = IPAddress.Parse request.Dns1,
                    Dns2 = (request.Dns2 |> Option.map IPAddress.Parse |> Option.toObj),
                    LeaseTimeInSeconds = request.LeaseTimeInSeconds
                )

                dbContext.Add scope |> ignore
                dbContext.SaveChanges() |> ignore

                return { ErrorMessage = None }
            with ex ->
                return { ErrorMessage = Some ex.Message }
        }