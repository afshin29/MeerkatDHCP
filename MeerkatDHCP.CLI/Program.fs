open MeerkatDHCP.GrpcContracts
open Grpc.Net.Client
open ProtoBuf.Grpc.Client
open System
open System.Net.Http

let serverAddr = "http://192.168.70.217:8182"

let secret = "123456"

[<EntryPoint>]
let main _ =
    GrpcClientFactory.AllowUnencryptedHttp2 <- true
    task {
        let options = GrpcChannelOptions()
        options.HttpClient <- new HttpClient()
        options.HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-secret", secret) |> ignore
        
        use http = GrpcChannel.ForAddress(serverAddr, options)
        
        let client = http.CreateGrpcService<IDhcpServerGrpc>()
        
        let! result = client.StartDhcpServer { IpAddress = "192.168.70.217" }
        if result.ErrorMessage.IsSome then
            printfn $"Error: {result.ErrorMessage.Value}"

        let! state = client.GetServerConfigs ()
        printfn $"""
        
        Name: {state.Name}
        State: {state.CurrentState}
        Interfacaes: {String.Join(',', state.Interfaces)}

        """

        return 0
    } |> fun t -> t.Result