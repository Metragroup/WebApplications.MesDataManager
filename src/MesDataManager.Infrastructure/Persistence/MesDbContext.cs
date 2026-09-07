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

    public DbSet<PressDowntimeType> PressDowntimeTypes => Set<PressDowntimeType>();

    /// <summary>Schema Press.BatchDowntime: 4,59M righe, fuori dallo schema MasterData.</summary>
    public DbSet<BatchDowntime> BatchDowntimes => Set<BatchDowntime>();

    /// <summary>Schema Press.Batch: i lotti di estrusione, 240 mila righe.</summary>
    public DbSet<Batch> Batches => Set<Batch>();

    /// <summary>Schema Press.BatchBillet: le billette dei lotti, 4,95M righe.</summary>
    public DbSet<BatchBillet> BatchBillets => Set<BatchBillet>();

    /// <summary>
    /// Lotti scomposti per lunghezza barra e turno, dalla funzione tabellare
    /// <c>EF.ufn_BatchByLengthShift</c>. E' una funzione e non una vista: il periodo e' un
    /// parametro, non un filtro, quindi va passato qui e non nella <c>Where</c>.
    /// <para>
    /// Mappata come funzione componibile: <c>Where</c>, <c>OrderBy</c> e la paginazione si
    /// aggiungono alla chiamata e finiscono nella stessa query, senza portare in memoria le
    /// righe scartate.
    /// </para>
    /// </summary>
    public IQueryable<BatchByLengthShift> BatchesByLengthShift(DateTime startTs, DateTime stopTs) =>
        FromExpression(() => BatchesByLengthShift(startTs, stopTs));

    /// <summary>
    /// Turni di una pressa in un periodo, dalla funzione <c>Press.ufn_GetShifts</c>. E' la
    /// definizione di turno dell'impianto: va usata al posto di ricavare i turni dalle billette.
    /// </summary>
    public IQueryable<PressShift> PressShifts(string pressId, DateTime from, DateTime to) =>
        FromExpression(() => PressShifts(pressId, from, to));

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

        modelBuilder.Entity<PressDowntimeType>(e =>
        {
            e.ToTable("PressDowntimeType");
            e.HasKey(x => x.PressDowntimeTypeId);
            e.Property(x => x.PressDowntimeTypeId).HasColumnName("PressDowntimeTypeID").ValueGeneratedNever();
            e.Property(x => x.Description).HasMaxLength(50).IsRequired();
        });

        // Schema Press, non MasterData: nessuna migration parte da qui e nessuna FK e'
        // dichiarata verso Press/PressDowntimeReason/PressDowntimeType, per lo stesso motivo di
        // PressFailureType (tipi di colonna disallineati, vedi docs/decisioni-aperte.md).
        //
        // Il trigger va dichiarato: la tabella ne ha uno su INSERT/UPDATE/DELETE
        // (TR_Press_BatchDowntime, che alimenta lo storico) e da EF Core 7 il salvataggio usa
        // una clausola OUTPUT per rileggere la chiave generata. SQL Server rifiuta OUTPUT senza
        // INTO su una tabella con trigger, quindi senza questa riga ogni scrittura falliva con
        // "the target table ... cannot have any enabled triggers". Le letture non se ne
        // accorgono, e nemmeno i test su SQLite, che non ha trigger: vedi il test
        // Il_trigger_di_BatchDowntime_e_dichiarato_nel_modello.
        modelBuilder.Entity<BatchDowntime>(e =>
        {
            e.ToTable("BatchDowntime", schema: "Press", t => t.HasTrigger("TR_Press_BatchDowntime"));
            e.HasKey(x => x.BatchDowntimeId);
            e.Property(x => x.BatchDowntimeId).HasColumnName("BatchDowntimeID").ValueGeneratedOnAdd();
            e.Property(x => x.BatchDowntimeRawId).HasColumnName("BatchDowntimeRawID");
            e.Property(x => x.PressId).HasColumnName("PressID").HasColumnType("char(3)").IsRequired();
            e.Property(x => x.DowntimeReasonId).HasColumnName("DowntimeReasonID");
            e.Property(x => x.DowntimeCode).HasMaxLength(4000).IsRequired();
            e.Property(x => x.EditStatusId).HasColumnName("EditStatusID").HasColumnType("char(1)");
        });

        // Lotti e billette, schema Press. Lo schema va sempre esplicito: nel database esistono
        // omonimi negli schemi History e ML, e leggere lo storico al posto del dato corrente
        // sarebbe un errore silenzioso. Nessun trigger su queste due tabelle (verificato), quindi
        // a differenza di BatchDowntime non serve dichiararne alcuno.
        modelBuilder.Entity<Batch>(e =>
        {
            e.ToTable("Batch", schema: "Press");
            e.HasKey(x => x.BatchId);

            // La chiave la compone l'applicazione (pressa + yyMMddHHmmss): nessuna identity.
            e.Property(x => x.BatchId).HasColumnName("BatchID").HasColumnType("char(15)").ValueGeneratedNever();
            e.Property(x => x.BatchStatusId).HasColumnName("BatchStatusID");
            e.Property(x => x.PressId).HasColumnName("PressID").HasColumnType("char(3)").IsRequired();
            e.Property(x => x.DieId).HasColumnName("DieID").HasColumnType("varchar(20)");
            e.Property(x => x.DieCode).HasColumnType("varchar(20)");
            e.Property(x => x.LockTs).HasColumnName("Lock_Ts");
            e.Property(x => x.LockUsr).HasColumnName("Lock_Usr").HasMaxLength(50);
            e.Property(x => x.PressBatchClosingReasonId).HasColumnName("PressBatchClosingReasonID");
            e.Property(x => x.KgRaw).HasPrecision(15, 5);
            e.Property(x => x.KgSheared).HasPrecision(15, 5);
            e.Property(x => x.KgExtruded).HasPrecision(15, 5);
            e.Property(x => x.KgCut).HasPrecision(15, 5);
            e.Property(x => x.ItemMeterWeightMasterData).HasColumnName("ItemMeterWeight_MasterData").HasPrecision(15, 5);
            e.Property(x => x.ItemMeterWeightMes).HasColumnName("ItemMeterWeight_Mes").HasPrecision(15, 5);
            e.Property(x => x.ItemMeterWeightTest).HasColumnName("ItemMeterWeight_Test").HasPrecision(15, 5);
            e.Property(x => x.ItemMeterWeight).HasPrecision(15, 5);
            e.Property(x => x.DiagnosticsStatus).HasColumnType("char(3)");
            e.Property(x => x.DiagnosticsMsg).HasMaxLength(2000);
            e.Property(x => x.EditStatusId).HasColumnName("EditStatusID").HasColumnType("char(1)");
            e.HasOne<Press>().WithMany().HasForeignKey(x => x.PressId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BatchBillet>(e =>
        {
            e.ToTable("BatchBillet", schema: "Press");
            e.HasKey(x => x.BatchBilletId);

            e.Property(x => x.BatchBilletId).HasColumnName("BatchBilletID").ValueGeneratedOnAdd();
            e.Property(x => x.BatchBilletRawId).HasColumnName("BatchBilletRawID");
            e.Property(x => x.BatchId).HasColumnName("BatchID").HasColumnType("char(15)").IsRequired();
            e.Property(x => x.PressId).HasColumnName("PressID").HasColumnType("char(3)").IsRequired();
            e.Property(x => x.DieId).HasColumnName("DieID").HasColumnType("varchar(20)").IsRequired();
            e.Property(x => x.TypeId).HasColumnName("TypeID");
            e.Property(x => x.ShiftId).HasColumnName("ShiftID").HasColumnType("char(10)");
            e.Property(x => x.MmBarSet).HasPrecision(15, 5);
            e.Property(x => x.KgSheared).HasPrecision(15, 5);
            e.Property(x => x.KgExtruded).HasPrecision(15, 5);
            e.Property(x => x.Billet1CastingId).HasColumnName("Billet1_CastingID").HasColumnType("char(20)");
            e.Property(x => x.Billet1AlloyId).HasColumnName("Billet1_AlloyID").HasMaxLength(20);
            e.Property(x => x.Billet1Kg).HasColumnName("Billet1_Kg").HasPrecision(15, 5);
            e.Property(x => x.Billet2CastingId).HasColumnName("Billet2_CastingID").HasColumnType("char(20)");
            e.Property(x => x.Billet2AlloyId).HasColumnName("Billet2_AlloyID").HasMaxLength(20);
            e.Property(x => x.Billet2Kg).HasColumnName("Billet2_Kg").HasPrecision(15, 5);
            e.Property(x => x.ProdId).HasColumnName("ProdID").HasColumnType("varchar(20)");
            e.Property(x => x.EditStatusId).HasColumnName("EditStatusID").HasColumnType("char(1)");

            // La chiave esterna verso il lotto esiste davvero a database
            // (FK_Press_BatchBillet_BatchID) ma manca dall'EDMX del vecchio progetto, che faceva
            // i join a mano. Qui e' dichiarata, senza proprieta' di navigazione come le altre.
            e.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Press>().WithMany().HasForeignKey(x => x.PressId).OnDelete(DeleteBehavior.Restrict);
        });

        // La funzione tabellare che alimenta il "dettaglio lunghezza": senza chiave, non
        // scrivibile, e con i nomi fisici delle colonne come per le tabelle.
        modelBuilder.Entity<BatchByLengthShift>(e =>
        {
            e.HasNoKey();
            e.ToView(null);
            e.Property(x => x.BatchId).HasColumnName("BatchID");
            e.Property(x => x.PressId).HasColumnName("PressID");
            e.Property(x => x.DieId).HasColumnName("DieID");
            e.Property(x => x.ShiftId).HasColumnName("ShiftID");
            e.Property(x => x.PressBatchClosingReasonId).HasColumnName("PressBatchClosingReasonID");
            e.Property(x => x.DiagnosticsStatus).HasColumnName("DiagnosticsStatus");
        });

        modelBuilder
            .HasDbFunction(typeof(MesDbContext).GetMethod(
                nameof(BatchesByLengthShift),
                [typeof(DateTime), typeof(DateTime)])!)
            .HasName("ufn_BatchByLengthShift")
            .HasSchema("EF");

        // I turni delle presse: funzione a piu' istruzioni, senza chiave e non scrivibile.
        modelBuilder.Entity<PressShift>(e =>
        {
            e.HasNoKey();
            e.ToView(null);
            e.Property(x => x.PressId).HasColumnName("PressID");
            e.Property(x => x.ShiftId).HasColumnName("ShiftID");
        });

        modelBuilder
            .HasDbFunction(typeof(MesDbContext).GetMethod(
                nameof(PressShifts),
                [typeof(string), typeof(DateTime), typeof(DateTime)])!)
            .HasName("ufn_GetShifts")
            .HasSchema("Press");
    }
}
