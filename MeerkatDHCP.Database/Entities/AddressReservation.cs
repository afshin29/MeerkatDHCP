using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net;

namespace MeerkatDHCP.Database;

[PrimaryKey(nameof(HardwareAddress), nameof(IpAddress))]
public class AddressReservation
{
	public required string HardwareAddress { get; set; }
	public required IPAddress IpAddress { get; set; }

	public required string ScopeName { get; set; }
	[ForeignKey(nameof(ScopeName))]
	public Scope Scope { get; set; } = null!;
}