using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PMCommonEntities.Models;
using PMUnifiedAPI.Models;

namespace PMSODOrderQueueService
{
    public class PseudoMarketsDbContext : DbContext
    {
        public DbSet<QueuedOrders> QueuedOrders { get; set; }
        public DbSet<Tokens> Tokens { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Grab the connection string from appsettings.json
            var connectionString = Program.Configuration.GetConnectionString("PMDB");

            // Use the SQL Server Entity Framework Core connector
            optionsBuilder.UseSqlServer(connectionString);
        }
    }
}
