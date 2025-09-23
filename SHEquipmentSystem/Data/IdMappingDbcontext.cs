// 文件路径: src/DiceEquipmentSystem/Data/IdMappingDbContext.cs
using DiceEquipmentSystem.Core.Interfaces;
using DiceEquipmentSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DiceEquipmentSystem.Data
{
    //数据库迁移
    //dotnet ef migrations add InitialCreate
    //dotnet ef database update
    /// <summary>
    /// ID映射数据库上下文
    /// </summary>
    public class IdMappingDbContext : DbContext
    {
        private readonly ILogger<IdMappingDbContext>? _logger;
        public string DbPath { get; }
        public IdMappingDbContext()
        {
            var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            DbPath = Path.Join(path, "\\Data\\blogging.db");
        }
        public IdMappingDbContext(DbContextOptions<IdMappingDbContext> options) : base(options)
        {
        }

        public IdMappingDbContext(DbContextOptions<IdMappingDbContext> options, ILogger<IdMappingDbContext> logger)
            : base(options)
        {
            _logger = logger;
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // 只有当没有通过DI配置时才使用默认配置
                optionsBuilder.UseSqlite($"Data Source={DbPath}");
            }
        }
        // DbSets
        public DbSet<SvidMapping> SvidMappings { get; set; }
        public DbSet<CeidMapping> CeidMappings { get; set; }
        public DbSet<AlidMapping> AlidMappings { get; set; }
        public DbSet<EcidMapping> EcidMappings { get; set; }
        public DbSet<RptidMapping> RptidMappings { get; set; }
        public DbSet<RptidSvidMapping> RptidSvidMappings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 应用实体配置
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdMappingDbContext).Assembly);

            // 全局配置
            ConfigureAuditableEntities(modelBuilder);
            ConfigureStringProperties(modelBuilder);

            base.OnModelCreating(modelBuilder);
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            UpdateAuditableEntities();
            return await base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges()
        {
            UpdateAuditableEntities();
            return base.SaveChanges();
        }

        /// <summary>
        /// 配置可审计实体的通用属性
        /// </summary>
        private void ConfigureAuditableEntities(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(IAuditableEntity).IsAssignableFrom(entityType.ClrType))
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(nameof(IAuditableEntity.CreatedAt))
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");

                    modelBuilder.Entity(entityType.ClrType)
                        .Property(nameof(IAuditableEntity.UpdatedAt))
                        .HasDefaultValueSql("CURRENT_TIMESTAMP");
                }
            }
        }

        /// <summary>
        /// 配置字符串属性的通用设置
        /// </summary>
        private void ConfigureStringProperties(ModelBuilder modelBuilder)
        {
            foreach (var property in modelBuilder.Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties())
                .Where(p => p.ClrType == typeof(string)))
            {
                if (property.GetMaxLength() == null)
                {
                    property.SetMaxLength(255);
                }
            }
        }

        /// <summary>
        /// 更新可审计实体的审计字段
        /// </summary>
        private void UpdateAuditableEntities()
        {
            var entries = ChangeTracker.Entries<IAuditableEntity>();

            foreach (var entry in entries)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedAt = DateTime.UtcNow;
                        entry.Entity.UpdatedAt = DateTime.UtcNow;
                        entry.Entity.CreatedBy = "System";
                        entry.Entity.UpdatedBy = "System";
                        break;

                    case EntityState.Modified:
                        entry.Entity.UpdatedAt = DateTime.UtcNow;
                        entry.Entity.UpdatedBy = "System";
                        break;
                }
            }
        }
    }
}