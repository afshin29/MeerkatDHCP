using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace MeerkatDHCP.Database;

[PrimaryKey(nameof(Name))]
public class Scope
{
	public required string Name { get; set; }
	public required IPNetwork Network { get; set; }
	public required IPAddress From { get; set; }
	public required IPAddress To { get; set; }
	public required IPAddress Gateway { get; set; }
	public required IPAddress Dns1 { get; set; }
	public IPAddress? Dns2 { get; set; }
	public required uint LeaseTimeInSeconds { get; set; }

	[ConcurrencyCheck]
	public DateTimeOffset LastUpdate { get; set; }

	public ICollection<AddressReservation> Reservations { get; set; } = null!;
	public ICollection<AddressLease> Leases { get; set; } = null!;
}