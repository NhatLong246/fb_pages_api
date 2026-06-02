using BackendApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Data
{
    public class BackendDbContext : DbContext
    {
        public BackendDbContext(DbContextOptions<BackendDbContext> options) : base(options) { }

        public DbSet<CommandExecution> CommandExecutions => Set<CommandExecution>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CommandExecution>().HasKey(command => command.CommandId);
        }

        public Task EnsureCommandTableAsync(CancellationToken ct = default)
        {
            return Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "CommandExecutions" (
                    "CommandId" text PRIMARY KEY,
                    "EventId" text NOT NULL,
                    "Status" text NOT NULL,
                    "RetryCount" integer NOT NULL,
                    "ErrorMessage" text NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL
                );
                """,
                ct);
        }

        public Task UpdateEventStateAsync(
            string eventId,
            string status,
            string? errorMessage,
            CancellationToken ct = default)
        {
            var statusValue = status == "replied" ? 3 : status == "failed" ? 4 : 2;
            return Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE "EventStates"
                 SET "Status" = {statusValue},
                     "ErrorMessage" = {errorMessage},
                     "ProcessedAt" = {DateTimeOffset.UtcNow},
                     "UpdatedAt" = {DateTimeOffset.UtcNow}
                 WHERE "EventId" = {eventId};
                 """,
                ct);
        }
    }
}
