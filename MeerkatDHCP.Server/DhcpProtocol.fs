module DhcpProtocol

open System.Net


[<Literal>]
let dhcpServerPort = 67

[<Literal>]
let dhcpClientPort = 68


[<Literal>]
let minDhcpRequestLength = 244

[<Literal>]
let maxDhcpRequestLength = 576


let zeroIp = [| 0uy; 0uy; 0uy; 0uy |]


let magicCookie = [| 99uy; 130uy; 83uy; 99uy |]


let optionEnd = [| 255uy |]


type BOOTP = {
    op: byte
    htype: byte
    hlen: byte
    hops: byte
    xid: uint32
    secs: uint16
    flags: uint16
    ciaddr: byte array //   4 bytes
    yiaddr: byte array //   4 bytes
    siaddr: byte array //   4 bytes
    giaddr: byte array //   4 bytes
    chaddr: byte array //  16 bytes
    sname: byte array  //  64 bytes
    file: byte array   // 128 bytes
    options: byte array
}


// Note: we use the same logic for Renewing and Rebinding
type DhcpRequestType =
    | Discover of gatewayIp: IPAddress option * hardwareAddress: string * clientId: string option
    | RequestForSelecting of hardwareAddress: string * requestedIp: IPAddress
    | RequestForInitReboot of hardwareAddress: string * requestedIp: IPAddress
    | RequestForRenewing of hardwareAddress: string * clientIp: IPAddress
    | Decline of hardwareAddress: string * requestedIp: IPAddress
    | Release of hardwareAddress: string * clientIp: IPAddress
    | Inform of gatewayIp: IPAddress option


type CommonOptions = {
    SubnetMask: IPAddress
    GatewayIpAddress: IPAddress
    Dns1: IPAddress
    Dns2: IPAddress option
}


type DhcpResponseType =
    | Offer of yourIp: IPAddress * leaseTime: uint32 * commonOptions: CommonOptions
    | AckForRequest of yourIp: IPAddress * leaseTime: uint32 * commonOptions: CommonOptions
    | AckForInform of CommonOptions
    | Nack


type DhcpRawOption = {
    Code: byte
    Data: byte array
}


let hasOption code options =
    options
    |> List.tryFind (fun opt -> opt.Code = code)
    |> Option.isSome


let getIpAddressOption code options =
    options
    |> Seq.filter (fun opt -> opt.Code = code)
    |> Seq.map (fun opt -> IPAddress(opt.Data))
    |> Seq.head


let writeOption51 (seconds: uint32) = 
    Array.concat [
        [|
            51uy
            4uy
        |]
        writeNetworkUInt32 seconds
    ]


let writeOption53 response = [|
    53uy
    1uy
    match response with
    | Offer _ -> 2uy
    | AckForRequest _ | AckForInform _ -> 5uy
    | Nack -> 6uy
|]


let private writeIpAddressOption code (ipAddress: IPAddress) =
    Array.concat [
        [|
            code
            4uy
        |]
        ipAddress.GetAddressBytes()
    ]

let writeOption54 ipAddress = writeIpAddressOption 54uy ipAddress

let writeOption1 ipAddress = writeIpAddressOption 1uy ipAddress

let writeOption3 ipAddress = writeIpAddressOption 3uy ipAddress


let writeOption6 (ipAddress1: IPAddress) (ipAddress2: IPAddress option) =
    Array.concat [
        [|
            6uy
            if ipAddress2.IsSome then 8uy
            else 4uy
        |]

        ipAddress1.GetAddressBytes()

        if ipAddress2.IsSome then ipAddress2.Value.GetAddressBytes()
    ]