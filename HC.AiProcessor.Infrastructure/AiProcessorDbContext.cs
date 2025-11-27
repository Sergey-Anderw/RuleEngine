using HC.AiProcessor.Entity.Ai;
using HC.AiProcessor.Entity.Catalog;
using HC.Packages.Persistent;
using HC.Packages.Common.Contracts.V1;
using Attribute = HC.AiProcessor.Entity.Catalog.Attribute;

namespace HC.AiProcessor.Infrastructure;

public class AiProcessorDbContext(
    DbContextOptions<AiProcessorDbContext> options,
    ISystemClock systemClock,
    IIdentityContextUser identityContextUser)
    : BaseDbContext<AiProcessorDbContext>(options, systemClock, identityContextUser)
{
    #region ai

    public DbSet<AiEmbedding> AiEmbeddings => Set<AiEmbedding>();
    public DbSet<AiProcessorTask> AiProcessorTasks => Set<AiProcessorTask>();
    public DbSet<AiProductAttributeEmbedding> AiProductAttributeEmbeddings => Set<AiProductAttributeEmbedding>();
    public DbSet<AiProduct> AiProducts => Set<AiProduct>();
    public DbSet<AiSettings> AiSettings => Set<AiSettings>();

    #endregion

    #region catalog

    public DbSet<Attribute> Attributes => Set<Attribute>();
    public DbSet<Family> Families => Set<Family>();
    public DbSet<FamilyAttribute> FamilyAttributes => Set<FamilyAttribute>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();

    #endregion

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        base.OnModelCreating(modelBuilder);
    }
}
