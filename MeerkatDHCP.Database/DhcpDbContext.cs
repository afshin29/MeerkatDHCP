using Microsoft.EntityFrameworkCore;
using System.Net;

namespace MeerkatDHCP.Database;

public class DhcpDbContext(DbContextOptions<DhcpDbContext> options) : DbContext(options)
{
	public DbSet<AddressLease> Leases { get; set; }
	public DbSet<AddressReservation> Reservations { get; set; }
	public DbSet<RequestLog> RequestLogs { get; set; }
	public DbSet<Scope> Scopes { get; set; }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);

		modelBuilder
			.Entity<Scope>()
			.Property(e => e.Network)
			.HasConversion(
				value => value.ToString(),
				dbValue => IPNetwork.Parse(dbValue)
			);
	}

	public static Action<DbContext, bool> SeederFunc = (dbContext, _) =>
	{
		var scopeTable = dbContext.Set<Scope>();

		if (!scopeTable.Any())
		{
			scopeTable.Add(new Scope
			{
				Name = "TestScope",
				Network = IPNetwork.Parse("10.0.0.0/8"),
				From = IPAddress.Parse("10.0.0.50"),
				To = IPAddress.Parse("10.0.0.150"),
				Gateway = IPAddress.Parse("10.0.0.1"),
				Dns1 = IPAddress.Parse("4.2.2.4"),
				Dns2 = IPAddress.Parse("8.8.8.8"),
				LeaseTimeInSeconds = 2629800 // 1 month
			});

			dbContext.SaveChanges();
		}
	};
}