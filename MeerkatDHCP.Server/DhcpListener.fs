module DhcpListener

open MeerkatDHCP.GrpcContracts
open System
open System.Collections.Concurrent
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks


let private listenAsync (listenIp: IPAddress) (udpClient: UdpClient) (ct: CancellationToken) (queue: BlockingCollection<_>) = task {

    udpClient.Client.Bind(IPEndPoint(listenIp, DhcpProtocol.dhcpServerPort))

    while not ct.IsCancellationRequested do
        try
            let! requestBytes = udpClient.ReceiveAsync(ct)
            queue.Add requestBytes
            ()
        with
        | :? OperationCanceledException -> ()

    return Task.CompletedTask
}



let private queue = new BlockingCollection<UdpReceiveResult>(250)

let private udpClient = new UdpClient()

let mutable private cts : CancellationTokenSource option = None

let mutable private listenerTask: Task option = None

let mutable private currentListenIp: IPAddress = IPAddress.Any


let start listenIp =
    match listenerTask with
    | Some t when not t.IsCompleted -> None
    | _ ->
        if cts.IsSome then
            cts.Value.Dispose()

        currentListenIp <- listenIp
        cts <- Some <| new CancellationTokenSource()
        listenerTask <- Some <| listenAsync listenIp udpClient cts.Value.Token queue

        Some queue


let stop () =
    match listenerTask with
    | Some t when not t.IsCompleted ->
        if cts.IsSome then
            cts.Value.Cancel()
            t.Wait()

    | _ -> ()


let getState () =
    match listenerTask with
    | Some t when not t.IsCompleted -> Running currentListenIp
    | Some t when t.IsFaulted -> RanToError t.Exception.Message
    | _ -> Stopped