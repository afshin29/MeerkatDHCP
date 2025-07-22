namespace Jobs

open MeerkatDHCP.Database
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open System
open System.Linq
open System.Threading
open System.Threading.Tasks


type DbCleanerJob(ssf: IServiceScopeFactory) =
    inherit BackgroundService()

    override this.ExecuteAsync (stoppingToken: CancellationToken): Task = task {
        
        while not stoppingToken.IsCancellationRequested do
            use scope = ssf.CreateScope()

            let dbContext = scope.ServiceProvider.GetRequiredService<DhcpDbContext>()

            let danglingLeases = dbContext.Leases.ToList()
                    
            danglingLeases
            |> Seq.filter (fun lease -> not lease.AcknowledgedAt.HasValue || lease.ReleasedAt.HasValue)
            |> Seq.filter (fun lease -> lease.CreatedAt.AddMinutes 1 < DateTimeOffset.UtcNow)
            |> Seq.iter (fun lease -> dbContext.Remove lease |> ignore)

            dbContext.SaveChanges() |> ignore

            do! Task.Delay(30_000, stoppingToken) |> Async.AwaitTask
    }
