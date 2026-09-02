using MesDataManager.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace MesDataManager.Infrastructure.Persistence;

/// <summary>
/// Contesto EF Core sul database MES esistente. Le entita' sono mappate a mano sulle colonne
/// reali perche' il database e' pre-esistente e non va toccato: nessuna migration parte da qui.
/// Le convenzioni di naming C# (PascalCase senza suffisso ID maiuscolo) restano interne e
/// vengono ricondotte ai nomi fisici tramite <c>HasColumnName</c>.
/// </summary>
public sealed class MesDbContext(DbContextOptions<MesDbContext> options) : DbContext(options)
{
    /// <summary>Schema che ospita tutte le tabelle di anagrafica.</summary>
    public const string MasterDataSchema = "MasterData";

    public DbSet<ModuleRepairReason> ModuleRepairReasons => Set<ModuleRepairReason>();

    public DbSet<ModuleScrapReason> ModuleScrapReasons => Set<ModuleScrapReason>();

    public DbSet<ModuleTransRouteReason> ModuleTransRouteReasons => Set<ModuleTransRouteReason>();

    public DbSet<PaintingDowntimeReason> PaintingDowntimeReasons => Set<PaintingDowntimeReason>();

    public DbSet<PressBatchClosingReason> PressBatchClosingReasons => Set<PressBatchClosingReason>();

    public DbSet<PressDowntimeReason> PressDowntimeReasons => Set<PressDowntimeReason>();

    public DbSet<PressFailureType> PressFailureTypes => Set<PressFailureType>();

    public DbSet<PressReducedProdReason> PressReducedProdReasons => Set<PressReducedProdReason>();

    public DbSet<DieCorrectionIssue> DieCorrectionIssues => Set<DieCorrectionIssue>();

    public DbSet<Module> Modules => Set<Module>();

    public DbSet<OvenRecipe> OvenRecipes => Set<OvenRecipe>();

    public DbSet<Worker> Workers => Set<Worker>();

    public DbSet<EmailRecipient> EmailRecipients => Set<EmailRecipient>();

    public DbSet<Press> Presses => Set<Press>();

    public DbSet<Oven> Ovens => Set<Oven>();

    public DbSet<HeatThreatment> HeatThreatments => Set<HeatThreatment>();

    public DbSet<Company> Companies => Set<Company>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(MasterDataSchema);

        modelBuilder.Entity<ModuleRepairReason>(e =>
        {
            e.ToTable("ModuleRepairReason");
            e.HasKey(x => x.ModuleRepairReasonId);
            e.Property(x => x.ModuleRepairReasonId).HasColumnName("ModuleRepairReasonID").ValueGeneratedNever();
            e.Property(x => x.Description).HasMaxLength(50).IsRequired();
            e.Property(x => x.IsActiveMaster).HasColumnName("IsActive_Master");
        });

        modelBuilder.Entity<ModuleScrapReason>(e =>
        {
            e.ToTable("ModuleScrapReason");
            e.HasKey(x => x.ModuleScrapReasonId);
            e.Property(x => x.ModuleScrapReasonId).HasColumnName("ModuleScrapReasonID").ValueGeneratedOnAdd();
            e.Property(x => x.Description).HasMaxLength(50).IsRequired();
            e.Property(x => x.OprId).HasColumnName("OprID").HasMaxLength(20).IsRequired();
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.IsActiveMaster).HasColumnName("IsActive_Master");
        });

        modelBuilder.Entity<ModuleTransRouteReason>(e =>
        {
            e.ToTable("ModuleTransRouteReason");
            e.HasKey(x => x.ModuleTransRouteReasonId);
            e.Property(x => x.ModuleTransRouteReasonId)
                .HasColumnName("ModuleTransRouteReasonID")
                .ValueGeneratedOnAdd();
            e.Property(x => x.Description).HasMaxLength(50).IsRequired();
            e.Property(x => x.OprId).HasColumnName("OprID").HasMaxLength(20).IsRequired();
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.IsActiveMaster).HasColumnName("IsActive_Master");
        });

        modelBuilder.Entity<PaintingDowntimeReason>(e =>
        {
            e.ToTable("PaintingDowntimeReason");
            e.HasKey(x => x.PaintingDowntimeReasonId);
            e.Property(x => x.PaintingDowntimeReasonId)
                .HasColumnName("PaintingDowntimeReasonID")
                .ValueGeneratedNever();
            e.Property(x => x.Description).HasMaxLength(50).IsRequired();
            e.Property(x => x.IsActiveMaster).HasColumnName("IsActive_Master");
        });

        modelBuilder.Entity<PressBatchClosingReason>(e =>
        {
            e.ToTable("PressBatchClosingReason");
            e.HasKey(x => x.PressBatchClosingReasonId);
            e.Property(x => x.PressBatchClosingReasonId)
                .HasColumnName("PressBatchClosingReasonID")
                .ValueGeneratedNever();
            e.Property(x => x.Description).HasMaxLength(50).IsRequired();
            e.Property(x => x.Result).HasMaxLength(50).IsRequired();
            e.Property(x => x.IsActiveMaster).HasColumnName("IsActive_Master");
        });

        modelBuilder.Entity<PressDowntimeReason>(e =>
        {
            e.ToTable("PressDowntimeReason");
            e.HasKey(x => x.PressDowntimeReasonId);
            e.Property(x => x.PressDowntimeReasonId).HasColumnName("PressDowntimeReasonID").ValueGeneratedNever();
            e.Property(x => x.Description).HasMaxLength(50).IsRequired();
            e.Property(x => x.IsActiveMaster).HasColumnName("IsActive_Master");
        });

        modelBuilder.Entity<PressFailureType>(e =>
        {
            e.ToTable("PressFailureType");
            e.HasKey(x => x.PressFailureTypeId);
            e.Property(x => x.PressFailureTypeId).HasColumnName("PressFailureTypeID").ValueGeneratedNever();
            e.Property(x => x.Description).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<PressReducedProdReason>(e =>
        {
            e.ToTable("PressReducedProdReason");
            e.HasKey(x => x.PressReducedProdReasonId);
            e.Property(x => x.PressReducedProdReasonId)
                .HasColumnName("PressReducedProdReasonID")
                .ValueGeneratedNever();
            e.Property(x => x.Description).HasMaxLength(50).IsRequired();
            e.Property(x => x.IsActiveMaster).HasColumnName("IsActive_Master");
        });

        modelBuilder.Entity<DieCorrectionIssue>(e =>
        {
            e.ToTable("DieCorrectionIssue");
            e.HasKey(x => x.DieCorrectionIssueId);
            e.Property(x => x.DieCorrectionIssueId).HasColumnName("DieCorrectionIssueID").ValueGeneratedOnAdd();
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
        });

        modelBuilder.Entity<Module>(e =>
        {
            e.ToTable("Module");
            e.HasKey(x => x.ModuleId);
            e.Property(x => x.ModuleId).HasColumnName("ModuleID").HasMaxLength(10).ValueGeneratedNever();
            e.Property(x => x.ModuleGroupId).HasColumnName("ModuleGroupID").HasMaxLength(10).IsRequired();
        });

        modelBuilder.Entity<OvenRecipe>(e =>
        {
            e.ToTable("OvenRecipe");
            e.HasKey(x => x.OvenRecipeId);
            e.Property(x => x.OvenRecipeId).HasColumnName("OvenRecipeID").ValueGeneratedOnAdd();
            e.Property(x => x.OvenId).HasColumnName("OvenID").HasMaxLength(4).IsRequired();
            e.Property(x => x.RecipeId).HasColumnName("RecipeID");
            e.Property(x => x.Temperature).HasPrecision(15, 5);
            e.Property(x => x.Description).HasMaxLength(500).IsRequired();
            e.HasOne<Oven>().WithMany().HasForeignKey(x => x.OvenId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Worker>(e =>
        {
            e.ToTable("Worker");
            e.HasKey(x => x.WorkerId);
            e.Property(x => x.WorkerId).HasColumnName("WorkerID").ValueGeneratedNever();
            e.Property(x => x.EmplId).HasColumnName("EmplID").HasColumnType("varchar(20)").IsRequired();
            e.Property(x => x.FirstName).HasColumnType("varchar(25)").IsRequired();
            e.Property(x => x.MiddleName).HasColumnType("varchar(25)");
            e.Property(x => x.LastName).HasColumnType("varchar(25)").IsRequired();
            e.Property(x => x.Description).HasColumnType("varchar(50)").IsRequired();
            e.Property(x => x.CompanyId).HasColumnName("CompanyID").HasColumnType("char(4)").IsRequired();
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EmailRecipient>(e =>
        {
            e.ToTable("EmailRecipient");
            e.HasKey(x => x.EmailRecipientId);
            e.Property(x => x.EmailRecipientId).HasColumnName("EmailRecipientID").ValueGeneratedOnAdd();
            e.Property(x => x.MessageType).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.Email).HasMaxLength(255);
        });

        modelBuilder.Entity<Press>(e =>
        {
            e.ToTable("Press");
            e.HasKey(x => x.PressId);
            e.Property(x => x.PressId).HasColumnName("PressID").HasColumnType("char(3)").ValueGeneratedNever();
            e.Property(x => x.Description).HasMaxLength(50).IsRequired();
            e.Property(x => x.CompanyId).HasColumnName("CompanyID").HasColumnType("char(4)").IsRequired();
            e.Property(x => x.BilletDiameter).HasPrecision(28, 12);
            e.Property(x => x.BilletMeterWeight).HasPrecision(28, 12);
            e.Property(x => x.MinHourKg).HasPrecision(28, 12);
            e.Property(x => x.BackMillimeterWeight).HasPrecision(28, 12);
            e.Property(x => x.LogWeightTolerancePerc).HasPrecision(15, 5);
            e.Property(x => x.Note).HasMaxLength(100);
            e.Property(x => x.PressMonitorIsActive).HasColumnName("PressMonitor_IsActive");
            e.Property(x => x.PressMonitorMin).HasColumnName("PressMonitor_Min").HasPrecision(28, 12);
            e.Property(x => x.PressMonitorMax).HasColumnName("PressMonitor_Max").HasPrecision(28, 12);
            e.Property(x => x.PressMonitorStep).HasColumnName("PressMonitor_Step").HasPrecision(28, 12);
            e.Property(x => x.PressMonitorThreshold1).HasColumnName("PressMonitor_Threshold1").HasPrecision(28, 12);
            e.Property(x => x.PressMonitorThreshold2).HasColumnName("PressMonitor_Threshold2").HasPrecision(28, 12);
            e.Property(x => x.SawPlcIp).HasColumnName("Saw_PlcIP").HasColumnType("char(15)");
            e.Property(x => x.SawPlcPort).HasColumnName("Saw_PlcPort");
            e.Property(x => x.SawOprId).HasColumnName("SawOprID").HasMaxLength(20).IsRequired();
            e.Property(x => x.SawWrkCtrId).HasColumnName("SawWrkCtrID").HasMaxLength(20).IsRequired();
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Oven>(e =>
        {
            e.ToTable("Oven");
            e.HasKey(x => x.OvenId);
            e.Property(x => x.OvenId).HasColumnName("OvenID").HasMaxLength(4).ValueGeneratedNever();
            e.Property(x => x.CompanyId).HasColumnName("CompanyID").HasColumnType("char(4)").IsRequired();
            e.Property(x => x.AreaId).HasColumnName("AreaID").HasMaxLength(50).IsRequired();
            e.Property(x => x.Description).HasMaxLength(50).IsRequired();
            e.Property(x => x.OvenOprId).HasColumnName("OvenOprID").HasMaxLength(20).IsRequired();
            e.Property(x => x.OvenWrkCtrId).HasColumnName("OvenWrkCtrID").HasMaxLength(20).IsRequired();
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HeatThreatment>(e =>
        {
            e.ToTable("HeatThreatment");
            e.HasKey(x => x.HeatThreatmentId);
            e.Property(x => x.HeatThreatmentId)
                .HasColumnName("HeatThreatmentID")
                .HasColumnType("varchar(10)")
                .ValueGeneratedNever();
            e.Property(x => x.Description).HasMaxLength(50).IsRequired();
            e.Property(x => x.DurationMinutes).HasPrecision(15, 5);
        });

        modelBuilder.Entity<Company>(e =>
        {
            e.ToTable("Company");
            e.HasKey(x => x.CompanyId);
            e.Property(x => x.CompanyId).HasColumnName("CompanyID").HasColumnType("char(4)").ValueGeneratedNever();
            e.Property(x => x.Description).HasMaxLength(50).IsRequired();
        });
    }
}
