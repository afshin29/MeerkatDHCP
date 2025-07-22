module DhcpManager

open Microsoft.Extensions.DependencyInjection
open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open MeerkatDHCP.GrpcContracts


[<Literal>]
let private minWorkerCount = 2

[<Literal>]
let private maxWorkerCount = 16


let private udpClient = new UdpClient(DhcpProtocol.dhcpServerPort, AddressFamily.InterNetwork)
udpClient.EnableBroadcast <- true

let mutable private workersCount = 0

let mutable private workerTasks: (CancellationTokenSource * Task) list = []


let private initWorkers listenIp count ssf queue =
    workersCount <-
        count
        |> min maxWorkerCount
        |> max minWorkerCount

    workerTasks <-
        [1..count]
        |> List.map (fun id -> (id, new CancellationTokenSource()))
        |> List.map (fun (id, cts) -> (cts, Task.Run(fun () -> DhcpWorker.queueWorker id listenIp ssf udpClient cts.Token queue)))

    ()


type DhcpCommand =
    | Start of ip: IPAddress * initWorkersCount: int * ssf: IServiceScopeFactory
    | Stop
    | IncreaseWorker
    | DecreaseWorker
    | GetWorkersCount of AsyncReplyChannel<int>
    | GetState of AsyncReplyChannel<DhcpState>


let commandsMailBox =
    MailboxProcessor.Start(fun inbox ->
        let rec loop () = async {
            let! cmd = inbox.Receive()
            match cmd with
            | Start (ip, count, ssf) ->
                printfn "*** Starting DHCP Server ***"
                match DhcpListener.start ip with
                | None -> ()
                | Some queue -> if workersCount = 0 then initWorkers ip count ssf queue

            | Stop -> DhcpListener.stop ()

            | IncreaseWorker -> raise <| new NotImplementedException()

            | DecreaseWorker -> raise <| new NotImplementedException()

            | GetWorkersCount reply -> reply.Reply workersCount

            | GetState reply ->
                reply.Reply <| DhcpListener.getState ()

            return! loop ()
        }

        loop ()
    )