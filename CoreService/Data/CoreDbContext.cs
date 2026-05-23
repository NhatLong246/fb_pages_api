using CoreService.Models;
using Microsoft.EntityFrameworkCore;

namespace CoreService.Data
{
    public class CoreDbContext : DbContext
    {
        public CoreDbContext(DbContextOptions<CoreDbContext> options) : base(options) { }

        public DbSet<EventState> EventStates => Set<EventState>();
        public DbSet<BlacklistedUser> BlacklistedUsers => Set<BlacklistedUser>();
        public DbSet<ReviewQueueItem> ReviewQueueItems => Set<ReviewQueueItem>();

        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.Entity<EventState>().HasKey(e => e.EventId);
            mb.Entity<BlacklistedUser>().HasKey(e => new { e.UserId, e.PageId });
            mb.Entity<ReviewQueueItem>().HasKey(e => e.EventId);
        }
    }
}
