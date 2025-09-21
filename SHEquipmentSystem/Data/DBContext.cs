using DiceEquipmentSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

public class IdMappingDbContext : DbContext
{
    public IdMappingDbContext(DbContextOptions<IdMappingDbContext> options) : base(options) { }

    // DbSets
    public DbSet<SvidMapping> SvidMappings { get; set; }
    public DbSet<CeidMapping> CeidMappings { get; set; }
    public DbSet<AlidMapping> AlidMappings { get; set; }
    public DbSet<EcidMapping> EcidMappings { get; set; }
    public DbSet<RptidMapping> RptidMappings { get; set; }
    public DbSet<RptidSvidMapping> RptidSvidMappings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 实体配置
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdMappingDbContext).Assembly);

        // 全局配置
        //ConfigureAuditableEntities(modelBuilder);
        //ConfigureStringProperties(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        //UpdateAuditableEntities();
        return await base.SaveChangesAsync(cancellationToken);
    }
}