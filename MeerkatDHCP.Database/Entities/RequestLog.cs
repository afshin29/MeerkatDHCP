namespace MeerkatDHCP.Database;

public class RequestLog
{
	public Guid Id { get; init; } = Guid.NewGuid();
	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
	public byte Operation { get; set; }
	public byte HardwareType { get; set; }
	public byte HardwareLength { get; set; }
	public byte Hops { get; set; }
	public uint TransactionId { get; set; }
	public ushort Seconds { get; set; }
	public ushort Flags { get; set; }
	public required byte[] ClientIpAddress { get; set; }
	public required byte[] YourIpAddress { get; set; }
	public required byte[] ServerIpAddress { get; set; }
	public required byte[] GatewayIpAddress { get; set; }
	public required byte[] ServerName { get; set; }
	public required byte[] File { get; set; }
	public required byte[] Options { get; set; }
}