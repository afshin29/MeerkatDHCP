namespace MeerkatDHCP.GrpcContracts

open System.Net
open System.ServiceModel
open System.Threading.Tasks
open System.Runtime.Serialization


type DhcpState =
    | Running of IPAddress
    | Stopped
    | RanToError of string


[<DataContract; CLIMutable>]
type CommandResult = {

    [<DataMember(Order = 1)>]
    ErrorMessage: string option
}


[<DataContract; CLIMutable>]
type StartRequest = {

    [<DataMember(Order = 1)>]
    IpAddress: string
}


[<DataContract; CLIMutable>]
type AddScopeRequest = {
    
    [<DataMember(Order = 1)>]
    Name: string
    
    [<DataMember(Order = 2)>]
    Network: string
    
    [<DataMember(Order = 3)>]
    FromIp: string
    
    [<DataMember(Order = 4)>]
    ToIp: string
    
    [<DataMember(Order = 5)>]
    GatewayIpAddress: string
    
    [<DataMember(Order = 6)>]
    Dns1: string
    
    [<DataMember(Order = 7)>]
    Dns2: string option
    
    [<DataMember(Order = 8)>]
    LeaseTimeInSeconds: uint32
}


[<DataContract; CLIMutable>]
type ServerConfigsResult = {

    [<DataMember(Order = 1)>]
    Name: string option

    [<DataMember(Order = 2)>]
    Interfaces: string array

    [<DataMember(Order = 3)>]
    CurrentState: string
}


[<ServiceContract>]
type IDhcpServerGrpc =

    abstract StartDhcpServer : request: StartRequest -> Task<CommandResult>

    abstract StopDhcpServer : unit -> Task<CommandResult>

    abstract GetServerConfigs : unit -> Task<ServerConfigsResult>

    abstract AddScope : request: AddScopeRequest -> Task<CommandResult>