using MesDataManager.Application.Archives;
using MesDataManager.Application.Lookups;
using MesDataManager.Domain.Entities;

namespace MesDataManager.Infrastructure.Archives;

/// <summary>
/// Dichiarazione delle anagrafiche gestite. Questo file e' l'unico posto da toccare per
/// aggiungere una tabella, una colonna o cambiare cosa e' modificabile: griglia, form,
/// validazione e menu si adeguano da soli.
/// <para>
/// Tipi, lunghezze e nullabilita' sono allineati alle colonne di SQL Server nello schema
/// <c>MasterData</c>, ricavati dal modello EDMX dell'applicazione WinForms.
/// </para>
/// </summary>
public sealed class ArchiveCatalog : IArchiveCatalog
{
    private readonly Dictionary<string, ArchiveDescriptor> _byKey;

    public ArchiveCatalog()
    {
        var descriptors = Build();
        All = descriptors;
        _byKey = descriptors.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ArchiveDescriptor> All { get; }

    public ArchiveDescriptor? Find(string key) => _byKey.GetValueOrDefault(key);

    // ------------------------------------------------------------------ helper di costruzione

    private static ArchiveField GeneratedKey(string name) => new(name, ArchiveFieldKind.Integer)
    {
        IsKey = true,
        Editability = ArchiveFieldEditability.ReadOnly,
        ShowInGrid = false,
    };

    private static ArchiveField AssignedKey(string name, ArchiveFieldKind kind, int? maxLength = null) =>
        new(name, kind)
        {
            IsKey = true,
            IsRequired = true,
            MaxLength = maxLength,
            Editability = ArchiveFieldEditability.OnInsert,
        };

    private static ArchiveField Text(string name, int maxLength, bool required = true, bool wide = false) =>
        new(name, ArchiveFieldKind.Text) { IsRequired = required, MaxLength = maxLength, IsWide = wide };

    private static ArchiveField Flag(string name) => new(name, ArchiveFieldKind.Boolean) { IsRequired = true };

    private static ArchiveField Number(string name, bool required = true) =>
        new(name, ArchiveFieldKind.Integer) { IsRequired = required };

    private static ArchiveField Amount(string name, bool required = true, int digits = 2) =>
        new(name, ArchiveFieldKind.Decimal) { IsRequired = required, DecimalDigits = digits };

    private static ArchiveField Lookup(string name, string lookupKey, int maxLength, bool required = true) =>
        new(name, ArchiveFieldKind.Lookup)
        {
            IsRequired = required,
            MaxLength = maxLength,
            LookupKey = lookupKey,
        };

    /// <summary>Posizione di presentazione, decisa dallo stabilimento.</summary>
    private static ArchiveField Position() => new(nameof(ModuleRepairReason.Position), ArchiveFieldKind.Integer)
    {
        IsRequired = true,
    };

    /// <summary>Attivazione locale.</summary>
    private static ArchiveField IsActive() => Flag(nameof(ModuleRepairReason.IsActive));

    /// <summary>
    /// Abilitazione a livello ERP: sempre in sola lettura, l'applicazione la mostra soltanto
    /// perche' spiega all'operatore perche' il flag "Attivo" e' bloccato.
    /// </summary>
    private static ArchiveField IsActiveMaster() =>
        new(nameof(ModuleRepairReason.IsActiveMaster), ArchiveFieldKind.Boolean)
        {
            LabelKey = "IsActive_Master",
            Editability = ArchiveFieldEditability.ReadOnly,
        };

    // ------------------------------------------------------------------ anagrafiche

    private static List<ArchiveDescriptor> Build() =>
    [
        // === Archivi generali: elenchi allineati dall'ERP =============================
        // Su queste tabelle lo stabilimento regola solo ordine di presentazione e
        // attivazione locale; descrizioni e codici arrivano da monte.

        new ArchiveDescriptor
        {
            Key = "ModuleRepairReason",
            EntityType = typeof(ModuleRepairReason),
            Group = ArchiveGroup.MasterData,
            EditPolicy = ArchiveEditPolicy.MasterControlled,
            SupportsActiveFilter = true,
            HasMasterFlag = true,
            DefaultSort = [nameof(ModuleRepairReason.Position), nameof(ModuleRepairReason.Description)],
            SearchableFields = [nameof(ModuleRepairReason.Description)],
            Fields =
            [
                AssignedKey(nameof(ModuleRepairReason.ModuleRepairReasonId), ArchiveFieldKind.Integer),
                Position(),
                Text(nameof(ModuleRepairReason.Description), 50, wide: true),
                IsActive(),
                IsActiveMaster(),
            ],
        },

        new ArchiveDescriptor
        {
            Key = "ModuleScrapReason",
            EntityType = typeof(ModuleScrapReason),
            Group = ArchiveGroup.MasterData,
            EditPolicy = ArchiveEditPolicy.MasterControlled,
            SupportsActiveFilter = true,
            HasMasterFlag = true,
            DefaultSort = [nameof(ModuleScrapReason.Position), nameof(ModuleScrapReason.Description)],
            SearchableFields =
            [
                nameof(ModuleScrapReason.Description),
                nameof(ModuleScrapReason.Code),
                nameof(ModuleScrapReason.OprId),
            ],
            Fields =
            [
                GeneratedKey(nameof(ModuleScrapReason.ModuleScrapReasonId)),
                Position(),
                Text(nameof(ModuleScrapReason.Code), 20),
                Text(nameof(ModuleScrapReason.OprId), 20),
                Text(nameof(ModuleScrapReason.Description), 50, wide: true),
                IsActive(),
                IsActiveMaster(),
            ],
        },

        new ArchiveDescriptor
        {
            Key = "ModuleTransRouteReason",
            EntityType = typeof(ModuleTransRouteReason),
            Group = ArchiveGroup.MasterData,
            EditPolicy = ArchiveEditPolicy.MasterControlled,
            SupportsActiveFilter = true,
            HasMasterFlag = true,
            DefaultSort = [nameof(ModuleTransRouteReason.Position), nameof(ModuleTransRouteReason.Description)],
            SearchableFields =
            [
                nameof(ModuleTransRouteReason.Description),
                nameof(ModuleTransRouteReason.Code),
                nameof(ModuleTransRouteReason.OprId),
            ],
            Fields =
            [
                GeneratedKey(nameof(ModuleTransRouteReason.ModuleTransRouteReasonId)),
                Position(),
                Text(nameof(ModuleTransRouteReason.Code), 20),
                Text(nameof(ModuleTransRouteReason.OprId), 20),
                Text(nameof(ModuleTransRouteReason.Description), 50, wide: true),
                IsActive(),
                IsActiveMaster(),
            ],
        },

        new ArchiveDescriptor
        {
            Key = "PaintingDowntimeReason",
            EntityType = typeof(PaintingDowntimeReason),
            Group = ArchiveGroup.MasterData,
            EditPolicy = ArchiveEditPolicy.MasterControlled,
            SupportsActiveFilter = true,
            HasMasterFlag = true,
            DefaultSort = [nameof(PaintingDowntimeReason.Position), nameof(PaintingDowntimeReason.Description)],
            SearchableFields = [nameof(PaintingDowntimeReason.Description)],
            Fields =
            [
                AssignedKey(nameof(PaintingDowntimeReason.PaintingDowntimeReasonId), ArchiveFieldKind.Integer),
                Position(),
                Text(nameof(PaintingDowntimeReason.Description), 50, wide: true),
                IsActive(),
                IsActiveMaster(),
            ],
        },

        new ArchiveDescriptor
        {
            Key = "PressBatchClosingReason",
            EntityType = typeof(PressBatchClosingReason),
            Group = ArchiveGroup.MasterData,
            EditPolicy = ArchiveEditPolicy.MasterControlled,
            SupportsActiveFilter = true,
            HasMasterFlag = true,
            DefaultSort = [nameof(PressBatchClosingReason.Position), nameof(PressBatchClosingReason.Description)],
            SearchableFields =
            [
                nameof(PressBatchClosingReason.Description),
                nameof(PressBatchClosingReason.Result),
            ],
            Fields =
            [
                AssignedKey(nameof(PressBatchClosingReason.PressBatchClosingReasonId), ArchiveFieldKind.Integer),
                Position(),
                Text(nameof(PressBatchClosingReason.Description), 50, wide: true),
                Text(nameof(PressBatchClosingReason.Result), 50, wide: true),
                IsActive(),
                IsActiveMaster(),
            ],
        },

        new ArchiveDescriptor
        {
            Key = "PressDowntimeReason",
            EntityType = typeof(PressDowntimeReason),
            Group = ArchiveGroup.MasterData,
            EditPolicy = ArchiveEditPolicy.MasterControlled,
            SupportsActiveFilter = true,
            HasMasterFlag = true,
            DefaultSort = [nameof(PressDowntimeReason.Position), nameof(PressDowntimeReason.Description)],
            SearchableFields = [nameof(PressDowntimeReason.Description)],
            Fields =
            [
                AssignedKey(nameof(PressDowntimeReason.PressDowntimeReasonId), ArchiveFieldKind.Integer),
                Position(),
                Text(nameof(PressDowntimeReason.Description), 50, wide: true),
                Flag(nameof(PressDowntimeReason.IsElt)),
                Flag(nameof(PressDowntimeReason.IsMec)),
                Flag(nameof(PressDowntimeReason.IsProd)),
                Number(nameof(PressDowntimeReason.OeeClass)),
                Number(nameof(PressDowntimeReason.AvailabilityClass)),
                Flag(nameof(PressDowntimeReason.IsPressUnavailable)),
                IsActive(),
                IsActiveMaster(),
            ],
        },

        // Compare fra gli archivi generali ma non ha ne' Position ne' IsActive:
        // e' una tabella gestita interamente dallo stabilimento.
        //
        // Eliminazione disabilitata: la tabella e' referenziata da
        // Press.BatchDowntime.FailureType e History._BatchDowntime.FailureType (milioni di
        // righe di fermi macchina) ma senza vincolo di chiave esterna, e con tipi di colonna
        // diversi — tinyint da un lato, smallint dall'altro. Un DELETE riuscirebbe e
        // lascerebbe orfano lo storico dei fermi. Vedi docs/decisioni-aperte.md, voce A5.
        new ArchiveDescriptor
        {
            Key = "PressFailureType",
            EntityType = typeof(PressFailureType),
            Group = ArchiveGroup.MasterData,
            PreventDelete = true,
            DefaultSort = [nameof(PressFailureType.Description)],
            SearchableFields = [nameof(PressFailureType.Description)],
            Fields =
            [
                AssignedKey(nameof(PressFailureType.PressFailureTypeId), ArchiveFieldKind.Integer),
                Text(nameof(PressFailureType.Description), 50, wide: true),
            ],
        },

        new ArchiveDescriptor
        {
            Key = "PressReducedProdReason",
            EntityType = typeof(PressReducedProdReason),
            Group = ArchiveGroup.MasterData,
            EditPolicy = ArchiveEditPolicy.MasterControlled,
            SupportsActiveFilter = true,
            HasMasterFlag = true,
            DefaultSort = [nameof(PressReducedProdReason.Position), nameof(PressReducedProdReason.Description)],
            SearchableFields = [nameof(PressReducedProdReason.Description)],
            Fields =
            [
                AssignedKey(nameof(PressReducedProdReason.PressReducedProdReasonId), ArchiveFieldKind.Integer),
                Position(),
                Text(nameof(PressReducedProdReason.Description), 50, wide: true),
                IsActive(),
                IsActiveMaster(),
            ],
        },

        // === Archivi di stabilimento: gestiti interamente dall'applicazione ===========

        new ArchiveDescriptor
        {
            Key = "DieCorrectionIssue",
            EntityType = typeof(DieCorrectionIssue),
            Group = ArchiveGroup.Plant,
            DefaultSort = [nameof(DieCorrectionIssue.Name)],
            SearchableFields = [nameof(DieCorrectionIssue.Name)],
            Fields =
            [
                GeneratedKey(nameof(DieCorrectionIssue.DieCorrectionIssueId)),
                Text(nameof(DieCorrectionIssue.Name), 255, wide: true),
            ],
        },

        new ArchiveDescriptor
        {
            Key = "Module",
            EntityType = typeof(Module),
            Group = ArchiveGroup.Plant,
            DefaultSort = [nameof(Module.ModuleId)],
            SearchableFields = [nameof(Module.ModuleId), nameof(Module.ModuleGroupId)],
            Fields =
            [
                AssignedKey(nameof(Module.ModuleId), ArchiveFieldKind.Text, 10),
                Text(nameof(Module.ModuleGroupId), 10),
            ],
        },

        new ArchiveDescriptor
        {
            Key = "OvenRecipe",
            EntityType = typeof(OvenRecipe),
            Group = ArchiveGroup.Plant,
            DefaultSort = [nameof(OvenRecipe.OvenId), nameof(OvenRecipe.RecipeId)],
            SearchableFields = [nameof(OvenRecipe.Description), nameof(OvenRecipe.OvenId)],
            Fields =
            [
                GeneratedKey(nameof(OvenRecipe.OvenRecipeId)),
                Lookup(nameof(OvenRecipe.OvenId), LookupKeys.Ovens, 4),
                Number(nameof(OvenRecipe.RecipeId)),
                Text(nameof(OvenRecipe.Description), 500, wide: true),
                Amount(nameof(OvenRecipe.Temperature), required: false, digits: 1),
                Number(nameof(OvenRecipe.MinRamp), required: false),
                Number(nameof(OvenRecipe.MinCycle), required: false),
                Number(nameof(OvenRecipe.MinCycleMax), required: false),
                Number(nameof(OvenRecipe.MinCooling), required: false),
            ],
        },

        new ArchiveDescriptor
        {
            Key = "Worker",
            EntityType = typeof(Worker),
            Group = ArchiveGroup.Plant,
            SupportsActiveFilter = true,
            DefaultSort = [nameof(Worker.Description)],
            SearchableFields =
            [
                nameof(Worker.Description),
                nameof(Worker.EmplId),
                nameof(Worker.FirstName),
                nameof(Worker.LastName),
            ],
            Fields =
            [
                AssignedKey(nameof(Worker.WorkerId), ArchiveFieldKind.Integer),
                Text(nameof(Worker.EmplId), 20),
                Text(nameof(Worker.FirstName), 25),
                Text(nameof(Worker.MiddleName), 25, required: false),
                Text(nameof(Worker.LastName), 25),
                Text(nameof(Worker.Description), 50, wide: true),
                Lookup(nameof(Worker.CompanyId), LookupKeys.Companies, 4) with { ShowInGrid = false },
                // Le dieci abilitazioni di mansione, piu' il flag di attivazione: in griglia
                // sarebbero un muro di spunte largo quanto lo schermo. Restano nel form, e
                // le voci non attive si riconoscono comunque dalla riga in secondo piano.
                Flag(nameof(Worker.IsLineSupervisor)) with { ShowInGrid = false },
                Flag(nameof(Worker.IsPressSupervisor)) with { ShowInGrid = false },
                Flag(nameof(Worker.IsPressOperator)) with { ShowInGrid = false },
                Flag(nameof(Worker.IsSawOperator)) with { ShowInGrid = false },
                Flag(nameof(Worker.IsOvenOperator)) with { ShowInGrid = false },
                Flag(nameof(Worker.IsRollingOperator)) with { ShowInGrid = false },
                Flag(nameof(Worker.IsPaintOperator)) with { ShowInGrid = false },
                Flag(nameof(Worker.IsPackingOperator)) with { ShowInGrid = false },
                Flag(nameof(Worker.IsCorrectionOperator)) with { ShowInGrid = false },
                Flag(nameof(Worker.IsMachiningOperator)) with { ShowInGrid = false },
                IsActive() with { ShowInGrid = false },
            ],
        },

        new ArchiveDescriptor
        {
            Key = "EmailRecipient",
            EntityType = typeof(EmailRecipient),
            Group = ArchiveGroup.Plant,
            SupportsActiveFilter = true,
            DefaultSort = [nameof(EmailRecipient.MessageType), nameof(EmailRecipient.Name)],
            SearchableFields =
            [
                nameof(EmailRecipient.Name),
                nameof(EmailRecipient.Email),
                nameof(EmailRecipient.MessageType),
            ],
            Fields =
            [
                GeneratedKey(nameof(EmailRecipient.EmailRecipientId)),
                Text(nameof(EmailRecipient.MessageType), 50),
                Text(nameof(EmailRecipient.Name), 255, wide: true),
                Text(nameof(EmailRecipient.Email), 255, required: false, wide: true),
                IsActive(),
            ],
        },

        // === Impianti ================================================================
        // Nell'applicazione WinForms queste tre tabelle avevano gia' il codice di lettura
        // e le etichette tradotte, ma erano state rimosse dall'albero di navigazione e
        // risultavano quindi irraggiungibili. Qui sono esposte in sola lettura: valutare
        // se aprirle alla modifica (EditPolicy = Full) o togliere il descrittore.

        new ArchiveDescriptor
        {
            Key = "Press",
            EntityType = typeof(Press),
            Group = ArchiveGroup.Plant,
            EditPolicy = ArchiveEditPolicy.ReadOnly,
            SupportsActiveFilter = true,
            DefaultSort = [nameof(Press.PressId)],
            SearchableFields = [nameof(Press.PressId), nameof(Press.Description)],
            Fields =
            [
                AssignedKey(nameof(Press.PressId), ArchiveFieldKind.Text, 3),
                Text(nameof(Press.Description), 50, wide: true),
                Lookup(nameof(Press.CompanyId), LookupKeys.Companies, 4),
                Amount(nameof(Press.BilletDiameter), digits: 1),
                Amount(nameof(Press.BilletMeterWeight), digits: 3),
                Number(nameof(Press.BilletDeadTime)),
                Amount(nameof(Press.MinHourKg)),
                Amount(nameof(Press.BackMillimeterWeight), digits: 3),
                Amount(nameof(Press.LogWeightTolerancePerc)),
                Flag(nameof(Press.HasMes)),
                IsActive(),
                Text(nameof(Press.Note), 100, required: false) with { ShowInGrid = false },
                Text(nameof(Press.SawOprId), 20) with { ShowInGrid = false },
                Text(nameof(Press.SawWrkCtrId), 20) with { ShowInGrid = false },
                Text(nameof(Press.SawPlcIp), 15, required: false) with { ShowInGrid = false },
                Number(nameof(Press.SawPlcPort), required: false) with { ShowInGrid = false },
                Flag(nameof(Press.PressMonitorIsActive)) with
                {
                    LabelKey = "PressMonitor_IsActive",
                    ShowInGrid = false,
                },
                Amount(nameof(Press.PressMonitorMin), required: false) with
                {
                    LabelKey = "PressMonitor_Min",
                    ShowInGrid = false,
                },
                Amount(nameof(Press.PressMonitorMax), required: false) with
                {
                    LabelKey = "PressMonitor_Max",
                    ShowInGrid = false,
                },
                Amount(nameof(Press.PressMonitorStep), required: false) with
                {
                    LabelKey = "PressMonitor_Step",
                    ShowInGrid = false,
                },
                Amount(nameof(Press.PressMonitorThreshold1), required: false) with
                {
                    LabelKey = "PressMonitor_Threshold1",
                    ShowInGrid = false,
                },
                Amount(nameof(Press.PressMonitorThreshold2), required: false) with
                {
                    LabelKey = "PressMonitor_Threshold2",
                    ShowInGrid = false,
                },
            ],
        },

        new ArchiveDescriptor
        {
            Key = "Oven",
            EntityType = typeof(Oven),
            Group = ArchiveGroup.Plant,
            EditPolicy = ArchiveEditPolicy.ReadOnly,
            DefaultSort = [nameof(Oven.OvenId)],
            SearchableFields = [nameof(Oven.OvenId), nameof(Oven.Description)],
            Fields =
            [
                AssignedKey(nameof(Oven.OvenId), ArchiveFieldKind.Text, 4),
                Text(nameof(Oven.Description), 50, wide: true),
                Lookup(nameof(Oven.CompanyId), LookupKeys.Companies, 4),
                Text(nameof(Oven.AreaId), 50),
                Number(nameof(Oven.ModuleCapacity)),
                Text(nameof(Oven.OvenOprId), 20) with { ShowInGrid = false },
                Text(nameof(Oven.OvenWrkCtrId), 20) with { ShowInGrid = false },
            ],
        },

        new ArchiveDescriptor
        {
            Key = "HeatThreatment",
            EntityType = typeof(HeatThreatment),
            Group = ArchiveGroup.Plant,
            EditPolicy = ArchiveEditPolicy.ReadOnly,
            DefaultSort = [nameof(HeatThreatment.HeatThreatmentId)],
            SearchableFields = [nameof(HeatThreatment.HeatThreatmentId), nameof(HeatThreatment.Description)],
            Fields =
            [
                AssignedKey(nameof(HeatThreatment.HeatThreatmentId), ArchiveFieldKind.Text, 10),
                Text(nameof(HeatThreatment.Description), 50, wide: true),
                Amount(nameof(HeatThreatment.DurationMinutes), digits: 1),
            ],
        },
    ];
}
