[<AutoOpen>]
module Helpers

open System
open System.Net
open System.Net.Sockets

let readNetworkUInt32 (bytes: byte array) (offset: int) : uint32 =
    ((uint32 bytes.[offset]) <<< 24)
    ||| ((uint32 bytes.[offset + 1]) <<< 16)
    ||| ((uint32 bytes.[offset + 2]) <<< 8)
    ||| (uint32 bytes.[offset + 3])


let readNetworkUInt16 (bytes: byte array) (offset: int) : uint16 =
    ((uint16 bytes.[offset]) <<< 8)
    ||| (uint16 bytes.[offset + 1])


let writeNetworkUInt32 (value: uint32) = [|
    byte ((value >>> 24) &&& 0xFFu)
    byte ((value >>> 16) &&& 0xFFu)
    byte ((value >>> 8) &&& 0xFFu)
    byte (value &&& 0xFFu)
|]


let writeNetworkUInt16 (value: uint16) = [|
    byte ((value >>> 8) &&& 0xFFus)
    byte (value &&& 0xFFus)
|]


let incrementIp (ipAddress: IPAddress) =
    if ipAddress.AddressFamily <> AddressFamily.InterNetwork then
        invalidArg "ipAddress" "Only IPv4 addresses are supported."

    let bytes = ipAddress.GetAddressBytes()
    if BitConverter.IsLittleEndian then Array.Reverse(bytes)
    let value = BitConverter.ToUInt32(bytes, 0)

    if value = UInt32.MaxValue then
        invalidOp "Cannot increment beyond 255.255.255.255"

    let incremented = value + 1u
    let newBytes = BitConverter.GetBytes(incremented)
    if BitConverter.IsLittleEndian then Array.Reverse(newBytes)
    
    IPAddress(newBytes)


let getSubnetMask (network: IPNetwork) =
    let mask = uint32 (0xFFFFFFFF <<< (32 - network.PrefixLength))
    let bytes = BitConverter.GetBytes(mask)

    // Convert to network byte order (big-endian)
    if BitConverter.IsLittleEndian then
        Array.Reverse(bytes)

    IPAddress(bytes)