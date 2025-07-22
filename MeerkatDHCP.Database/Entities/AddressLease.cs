using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net;

namespace MeerkatDHCP.Database;

[PrimaryKey(nameof(IpAddress))]
public class AddressLease
{
	public required IPAddress IpAddress { get; set; }
	public required string HardwareAddress { get; set; }
	public string? ClientIdentifier { get; set; }
	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
	public DateTimeOffset ExpiresAt { get; set; }
	public DateTimeOffset? AcknowledgedAt { get; set; }
	public DateTimeOffset? DeclinedAt { get; set; }
	public DateTimeOffset? ReleasedAt { get; set; }

	[ConcurrencyCheck]
	public DateTimeOffset LastUpdate { get; set; }

	public required string ScopeName { get; set; }
	[ForeignKey(nameof(ScopeName))]
	public Scope Scope { get; set; } = null!;
}