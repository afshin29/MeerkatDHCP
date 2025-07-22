module DhcpWorker

open FsToolkit.ErrorHandling
open MeerkatDHCP.Database
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open System
open System.Collections.Concurrent
open System.Linq
open System.Net
open System.Net.Sockets
open System.Threading

open DhcpProtocol


let private getRawBootpMessage (requestBytes: byte array) =
    match requestBytes.Length with
    | n when n < minDhcpRequestLength -> None
    | n when n > maxDhcpRequestLength -> None
    | _ -> Some {
        op = requestBytes[0]
        htype = requestBytes[1]
        hlen = requestBytes[2]
        hops = requestBytes[3]
        xid = readNetworkUInt32 requestBytes 4
        secs = readNetworkUInt16 requestBytes 8
        flags = readNetworkUInt16 requestBytes 10
        ciaddr = requestBytes[12..15]
        yiaddr = requestBytes[16..19]
        siaddr = requestBytes[20..23]
        giaddr = requestBytes[24..27]
        chaddr = requestBytes[28..43]
        sname = requestBytes[44..107]
        file = requestBytes[108..235]
        options = requestBytes[236..]
    }


let private getOptionsWithoutMagicCookie (bytes: byte array) =
    if bytes[..3] <> magicCookie then None
    else Some bytes[4..]


let private getOptionsWithoutEnd (bytes: byte array) =
    match bytes |> Array.tryFindIndex (fun b -> b = 255uy) with
    | Some idx  -> bytes[.. idx - 1]
    | None -> bytes


let private extractOptions (bytes: byte array) =
    let rec extract offset list =
        if offset = bytes.Length then
            Some <| List.rev list
        else
            match bytes[offset] with

            // Pad option
            | 0uy -> extract (offset + 1) list
            
            // If we have a code then there must be at least
            // two more bytes to be processed
            | _ when offset + 2 >= bytes.Length -> None

            | code ->
                let len = bytes[offset + 1] |> int
                let dataStart = offset + 2
                let dataEnd = dataStart + len - 1

                // There must enough data to read
                if dataEnd >= bytes.Length then None
                else
                    let data = bytes[dataStart .. dataEnd]
                    let option = { Code = code; Data = data }
                    extract (dataEnd + 1) (option :: list)

    extract 0 []


let private extractOverloadedOptions bootpMsg (dhcpRawOptions: DhcpRawOption list) =
    match dhcpRawOptions |> List.tryFind (fun opt -> opt.Code = 52uy) with
    | None -> Some dhcpRawOptions
    | Some opt when opt.Data.Length <> 1 -> None
    | Some opt ->
        match opt.Data[0] with
        | 1uy ->
            bootpMsg.file
            |> getOptionsWithoutEnd
            |> extractOptions
            |> Option.map (fun extra -> dhcpRawOptions @ extra)
        | 2uy ->
            bootpMsg.sname
            |> getOptionsWithoutEnd
            |> extractOptions
            |> Option.map (fun extra -> dhcpRawOptions @ extra)
        | 3uy ->
            bootpMsg.file
            |> getOptionsWithoutEnd
            |> extractOptions
            |> Option.bind (fun fileExtra ->
                bootpMsg.sname
                |> getOptionsWithoutEnd
                |> extractOptions
                |> Option.map (fun snameExtra -> fileExtra @ snameExtra)
            )
            |> Option.map (fun extra -> dhcpRawOptions @ extra)
        | _ -> None


let private detectRequestType listenIp bootp (dhcpRawOptions: DhcpRawOption list) =
            
    let hardwareAddress = Convert.ToHexString(bootp.chaddr[.. (int bootp.hlen) - 1])
    let messageType = dhcpRawOptions |> List.tryFind (fun opt -> opt.Code = 53uy)

    match bootp, messageType with
    | (msg, _) when
        msg.op <> 1uy
        || msg.htype <> 1uy
        || msg.hlen <> 6uy -> None

    | (_, None) -> None
    | (_, Some opt) when opt.Data.Length <> 1 -> None
    | (_, Some opt) ->
        match opt.Data[0] with
        | 1uy ->
            let gatewayIp =
                if bootp.giaddr = zeroIp then None
                else Some <| IPAddress(bootp.giaddr)

            let clientId =
                match dhcpRawOptions |> List.tryFind (fun opt -> opt.Code = 61uy) with
                | Some ci -> Some <| Convert.ToHexString(ci.Data)
                | None -> None

            Some <| Discover(gatewayIp, hardwareAddress, clientId)

        | 3uy when
            bootp.ciaddr = zeroIp
            && dhcpRawOptions |> hasOption 54uy
            && dhcpRawOptions |> hasOption 50uy ->
                let serverIp = getIpAddressOption 54uy dhcpRawOptions
                if serverIp <> listenIp then
                    None
                else
                    Some <| RequestForSelecting(hardwareAddress, getIpAddressOption 50uy dhcpRawOptions)

        | 3uy when
            bootp.ciaddr = zeroIp
            && dhcpRawOptions |> hasOption 50uy ->
                Some <| RequestForInitReboot(hardwareAddress, getIpAddressOption 50uy dhcpRawOptions)

        | 3uy when bootp.ciaddr <> zeroIp ->
            Some <| RequestForRenewing(hardwareAddress, IPAddress(bootp.ciaddr))

        | 4uy when dhcpRawOptions |> hasOption 50uy ->
            Some <| Decline(hardwareAddress, getIpAddressOption 50uy dhcpRawOptions)
        
        | 7uy when bootp.ciaddr <> zeroIp ->
            Some <| Release(hardwareAddress, IPAddress(bootp.ciaddr))
        
        | 8uy ->
            let gatewayIp =
                if bootp.giaddr = zeroIp then None
                else Some <| IPAddress(bootp.giaddr)
            
            Some <| Inform gatewayIp
        
        | _ -> None


let private saveBootpMessage (dbContext: DhcpDbContext) bootp =
    dbContext.Add(RequestLog(
        Operation = bootp.op,
        HardwareType = bootp.htype,
        HardwareLength = bootp.hlen,
        Hops = bootp.hops,
        TransactionId = bootp.xid,
        Seconds = bootp.secs,
        Flags = bootp.flags,
        ClientIpAddress = bootp.ciaddr,
        YourIpAddress = bootp.yiaddr,
        ServerIpAddress = bootp.siaddr,
        GatewayIpAddress = bootp.giaddr,
        ServerName = bootp.sname,
        File = bootp.file,
        Options = bootp.options
    ))
    |> ignore

    dbContext.SaveChanges() |> ignore


let private processDiscoverRequest listenIp gatewayIp hardwareAddress clientId (dbContext: DhcpDbContext) =

    let scopes =
        dbContext.Scopes
            .Include(fun s -> s.Leases.OrderByDescending(fun l -> l.CreatedAt))
            .Include(fun s -> s.Reservations)
            .ToArray()

    let scopeIp = gatewayIp |> Option.defaultValue listenIp

    scopes
    |> Array.tryFind (fun s -> s.Network.Contains(scopeIp))
    |> Option.bind (fun s ->
        // 1. Let's see if this client already has a binding
        s.Leases
        |> Seq.tryFind (fun l -> l.HardwareAddress = hardwareAddress && not l.DeclinedAt.HasValue)
        |> Option.map _.IpAddress
        
        // 2. Let's see if this client has a reserved IP address
        |> Option.bind (fun _ ->
            s.Reservations
            |> Seq.tryFind (fun r -> r.HardwareAddress = hardwareAddress)
            |> Option.map _.IpAddress
        )

        // 3. Asign from pool if available
        |> Option.bind (fun _ ->
            let rec checkAvailability ipAddress endIp =
                match
                    (
                        s.Leases |> Seq.tryFind (fun l -> l.IpAddress = ipAddress),
                        s.Reservations |> Seq.tryFind (fun r -> r.IpAddress = ipAddress)
                    )
                with
                | (None, None) -> Some ipAddress
                | _ when ipAddress.Equals(endIp) -> None
                | _ -> checkAvailability (incrementIp ipAddress) endIp

            checkAvailability s.From s.To
        )

        |> Option.map (fun ip ->
            AddressLease(
                IpAddress = ip,
                HardwareAddress = hardwareAddress,
                ClientIdentifier = (clientId |> Option.toObj),
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(float s.LeaseTimeInSeconds),
                LastUpdate = DateTimeOffset.UtcNow,
                ScopeName = s.Name,
                Scope = s
            )
        )
    )
    |> Option.map (fun lease ->
        try
            dbContext.Add(lease) |> ignore
            dbContext.SaveChanges() |> ignore
            Ok lease
        with
        | :? DbUpdateException as ex -> Error ex
    )
    |> Option.map (fun result ->
        result
        |> Result.map (fun lease ->
            Offer(
                lease.IpAddress,
                lease.Scope.LeaseTimeInSeconds,
                {
                    SubnetMask = getSubnetMask lease.Scope.Network
                    GatewayIpAddress = lease.Scope.Gateway
                    Dns1 = lease.Scope.Dns1
                    Dns2 = lease.Scope.Dns2 |> Option.ofObj
                }
            )
        )
    )


let private processRequestForSelecting hardwareAddress requestedIp (dbContext: DhcpDbContext) =

    dbContext.Leases
        .Include(_.Scope)
        .FirstOrDefault(fun l ->
            l.IpAddress = requestedIp
            && l.HardwareAddress = hardwareAddress
            && not l.AcknowledgedAt.HasValue)
    |> Option.ofObj
    |> Option.teeSome (fun lease ->
        lease.AcknowledgedAt <- DateTimeOffset.UtcNow
        lease.LastUpdate <- DateTimeOffset.UtcNow
    )
    |> Option.bind (fun lease ->
        try
            dbContext.SaveChanges() |> ignore
            AckForRequest(
                lease.IpAddress,
                lease.Scope.LeaseTimeInSeconds,
                {
                    SubnetMask = getSubnetMask lease.Scope.Network
                    GatewayIpAddress = lease.Scope.Gateway
                    Dns1 = lease.Scope.Dns1
                    Dns2 = lease.Scope.Dns2 |> Option.ofObj
                }
            ) |> Ok |> Some
        with
        // If another request already changed this row then we can ignore this one
        // Concurrent requests are either client's error or malicious behavior and
        // as far as server is concerned, the first one is the winner
        | :? DbUpdateConcurrencyException -> None
    )
    // Because selecting request is only for our server, we should send Nack
    |> Option.orElse (Some <| Ok Nack)


let private processRequestForInitReboot hardwareAddress requestedIp (dbContext: DhcpDbContext) =

    dbContext.Leases
        .Include(_.Scope)
        .FirstOrDefault(fun l ->
            l.IpAddress = requestedIp
            && l.HardwareAddress = hardwareAddress
            && l.AcknowledgedAt.HasValue)
    |> Option.ofObj
    |> Option.bind (fun lease ->
        if lease.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1) then
            AckForRequest(
                lease.IpAddress,
                lease.Scope.LeaseTimeInSeconds,
                {
                    SubnetMask = getSubnetMask lease.Scope.Network
                    GatewayIpAddress = lease.Scope.Gateway
                    Dns1 = lease.Scope.Dns1
                    Dns2 = lease.Scope.Dns2 |> Option.ofObj
                }
            ) |> Ok |> Some
        else
            // Because Init-Reboot is for all servers, we should remain silent
            None
    )


let private processRequestForRenewing hardwareAddress clientIp (dbContext: DhcpDbContext) =

    dbContext.Leases
        .Include(_.Scope)
        .FirstOrDefault(fun l ->
            l.IpAddress = clientIp
            && l.HardwareAddress = hardwareAddress
            && l.AcknowledgedAt.HasValue)
    |> Option.ofObj
    |> Option.teeSome (fun lease ->
        if (lease.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds < float(lease.Scope.LeaseTimeInSeconds / 2u) then
            lease.ExpiresAt <- DateTimeOffset.UtcNow.AddSeconds(float lease.Scope.LeaseTimeInSeconds)
            lease.LastUpdate <- DateTimeOffset.UtcNow
    )
    |> Option.bind (fun lease ->
        try
            dbContext.SaveChanges() |> ignore
            AckForRequest(
                lease.IpAddress,
                lease.Scope.LeaseTimeInSeconds,
                {
                    SubnetMask = getSubnetMask lease.Scope.Network
                    GatewayIpAddress = lease.Scope.Gateway
                    Dns1 = lease.Scope.Dns1
                    Dns2 = lease.Scope.Dns2 |> Option.ofObj
                }
            ) |> Ok |> Some
        with
        | :? DbUpdateConcurrencyException -> None
    )


let private processDeclineRequest hardwareAddress requetedIp (dbContext: DhcpDbContext) =

    dbContext.Leases
        .FirstOrDefault(fun l ->
            l.IpAddress = requetedIp
            && l.HardwareAddress = hardwareAddress
            && l.AcknowledgedAt.HasValue)
    |> Option.ofObj
    |> Option.teeSome (fun lease ->
        lease.DeclinedAt <- DateTimeOffset.UtcNow
        lease.LastUpdate <- DateTimeOffset.UtcNow
    )
    |> Option.bind (fun lease ->
        try
            dbContext.SaveChanges() |> ignore
        with
        | :? DbUpdateConcurrencyException -> ()

        // Decline has no answer
        None
    )


let private processReleaseRequest hardwareAddress clientIp (dbContext: DhcpDbContext) =

    dbContext.Leases
        .FirstOrDefault(fun l ->
            l.IpAddress = clientIp
            && l.HardwareAddress = hardwareAddress
            && l.AcknowledgedAt.HasValue)
    |> Option.ofObj
    |> Option.teeSome (fun lease ->
        lease.ReleasedAt <- DateTimeOffset.UtcNow
        lease.LastUpdate <- DateTimeOffset.UtcNow
    )
    |> Option.bind (fun lease ->
        try
            dbContext.SaveChanges() |> ignore
        with
        | :? DbUpdateConcurrencyException -> ()

        // Release has no answer
        None
    )


let private processInformRequest listenIp gatewayIp (dbContext: DhcpDbContext) =

    let scopes = dbContext.Scopes.ToArray()

    let scopeIp = gatewayIp |> Option.defaultValue listenIp

    scopes
    |> Array.tryFind (fun s -> s.Network.Contains(scopeIp))
    |> Option.map (fun scope ->
        Ok <| AckForInform {
            SubnetMask = getSubnetMask scope.Network
            GatewayIpAddress = scope.Gateway
            Dns1 = scope.Dns1
            Dns2 = scope.Dns2 |> Option.ofObj
        }
    )


let private sendDhcpResponse listenIp bootp (udpClient: UdpClient) (response: DhcpResponseType) =

    let yiaddr =
        match response with
        | Offer (yourIp, _, _)
        | AckForRequest (yourIp, _, _) -> yourIp.GetAddressBytes()
        | _ -> zeroIp

    let options =
        Array.concat [
            magicCookie
        
            match response with
            | Offer (_, leaseTime, _)
            | AckForRequest (_, leaseTime, _) -> writeOption51 leaseTime
            | _ -> ()
            
            writeOption53 response
        
            writeOption54 listenIp

            match response with
            | Offer (_, _, commonOptions)
            | AckForRequest (_, _, commonOptions)
            | AckForInform commonOptions ->
                writeOption1 commonOptions.SubnetMask

                writeOption3 commonOptions.GatewayIpAddress

                writeOption6 commonOptions.Dns1 commonOptions.Dns2
            | _ -> ()

            optionEnd
        ]

    let bootpReply =
        seq {
            // op
            2uy
            // htype
            1uy
            // hlen
            6uy
            // hops
            0uy
            // xid
            yield! writeNetworkUInt32 bootp.xid
            // secs
            0uy
            0uy
            // flags
            yield! writeNetworkUInt16 bootp.flags
            // ciaddr
            yield! zeroIp
            // yiaddr
            yield! yiaddr
            // siaddr
            yield! zeroIp
            // giaddr
            yield! bootp.giaddr
            // chaddr
            yield! bootp.chaddr
            // sname
            yield! bootp.sname
            // file
            yield! bootp.file
            // options
            yield! options    
        }
        |> Seq.toArray

    let ipEndpoint =
        match bootp.ciaddr, bootp.giaddr, bootp.flags <> 0us with
        | (clientIp, _, _) when clientIp <> zeroIp -> IPEndPoint(IPAddress(clientIp), dhcpClientPort)
        | (_, gatewayIp, _) when gatewayIp <> zeroIp -> IPEndPoint(IPAddress(gatewayIp), dhcpServerPort)
        | (_, _, broadcast) when broadcast = false -> IPEndPoint(IPAddress(yiaddr), dhcpClientPort)
        | _ -> IPEndPoint(IPAddress.Broadcast, dhcpClientPort)

    udpClient.Send(bootpReply, bootpReply.Length, ipEndpoint) |> ignore
        

let private processRequest listenIp dbContext (udpRequest: UdpReceiveResult) udpClient =
    udpRequest.Buffer
    |> getRawBootpMessage
    |> Option.teeSome (saveBootpMessage dbContext)
    |> Option.bind (fun bootp ->
        bootp.options
        |> getOptionsWithoutMagicCookie
        |> Option.map getOptionsWithoutEnd
        |> Option.bind extractOptions
        |> Option.bind (extractOverloadedOptions bootp)
        |> Option.bind (detectRequestType listenIp bootp)
        |> Option.bind (function
            | Discover (gatewayIp, hardwareAddress, clientId) -> processDiscoverRequest listenIp gatewayIp hardwareAddress clientId dbContext
            | RequestForSelecting (hardwareAddress, requestedIp) -> processRequestForSelecting hardwareAddress requestedIp dbContext
            | RequestForInitReboot (hardwareAddress, requestedIp) -> processRequestForInitReboot hardwareAddress requestedIp dbContext
            | RequestForRenewing (hardwareAddress, clientIp) -> processRequestForRenewing hardwareAddress clientIp dbContext
            | Decline (hardwareAddress, requestedIp) -> processDeclineRequest hardwareAddress requestedIp dbContext
            | Release (hardwareAddress, clientIp) -> processReleaseRequest hardwareAddress clientIp dbContext
            | Inform gatewayIp -> processInformRequest listenIp gatewayIp dbContext
        )
        // [None] means we remain silent
        // [Some Ok] means we have some response
        // [Some Error] means we had error and it should be handled by the worker loop
        |> Option.teeSome (fun result ->
            result
            |> Result.tee (sendDhcpResponse listenIp bootp udpClient)
            |> ignore
        )
    )


let queueWorker id listenIp (ssf: IServiceScopeFactory) udpClient (ct: CancellationToken) (queue: BlockingCollection<UdpReceiveResult>) =

    for request in queue.GetConsumingEnumerable(ct) do

        use scope = ssf.CreateScope()

        let logger = scope.ServiceProvider.GetRequiredService<ILogger>()
        let dbContext = scope.ServiceProvider.GetRequiredService<DhcpDbContext>()
        
        logger.LogInformation $"""
            **************************************
            Processing new request in worker #{id}
            --------------------------------------
            [{request.RemoteEndPoint.Address.ToString()}]:{request.RemoteEndPoint.Port}
            
            """
        
        // In case of concurrency issues, we will try to add the request
        // back to the queue, if queue is full then request will be lost
        match processRequest listenIp dbContext request udpClient with
        | Some result when result.IsError ->
            if queue.TryAdd(request) then
                logger.LogError "**Concurrency Error** request was queued"
            else
                logger.LogError "**Concurrency Error** request cannot be queued"
        | _ -> ()