using MesDataManager.Application.Archives;

namespace MesDataManager.Tests;

/// <summary>
/// Matrice di scrivibilita': e' il punto in cui la policy dell'anagrafica e l'editabilita' del
/// singolo campo si combinano, e da cui la UI decide cosa bloccare.
/// </summary>
public sealed class ArchiveDescriptorTests
{
    private static readonly ArchiveField Description =
        new("Description", ArchiveFieldKind.Text) { Editability = ArchiveFieldEditability.Always };

    private static readonly ArchiveField Position =
        new("Position", ArchiveFieldKind.Integer) { Editability = ArchiveFieldEditability.Always };

    private static readonly ArchiveField IsActive =
        new("IsActive", ArchiveFieldKind.Boolean) { Editability = ArchiveFieldEditability.Always };

    private static readonly ArchiveField AssignedKey =
        new("Id", ArchiveFieldKind.Integer) { IsKey = true, Editability = ArchiveFieldEditability.OnInsert };

    private static readonly ArchiveField Computed =
        new("IsActiveMaster", ArchiveFieldKind.Boolean) { Editability = ArchiveFieldEditability.ReadOnly };

    [Theory]
    [InlineData(ArchiveEditPolicy.Full, true, true, true)]
    [InlineData(ArchiveEditPolicy.MasterControlled, false, false, true)]
    [InlineData(ArchiveEditPolicy.ReadOnly, false, false, false)]
    public void Le_operazioni_ammesse_seguono_la_policy(
        ArchiveEditPolicy policy,
        bool insert,
        bool delete,
        bool update)
    {
        var descriptor = Describe(policy);

        Assert.Equal(insert, descriptor.AllowsInsert);
        Assert.Equal(delete, descriptor.AllowsDelete);
        Assert.Equal(update, descriptor.AllowsUpdate);
    }

    [Fact]
    public void A_gestione_piena_si_scrive_tutto_tranne_i_campi_in_sola_lettura()
    {
        var descriptor = Describe(ArchiveEditPolicy.Full);

        Assert.True(descriptor.IsWritable(Description, isNewRecord: false));
        Assert.True(descriptor.IsWritable(Position, isNewRecord: false));
        Assert.False(descriptor.IsWritable(Computed, isNewRecord: false));
    }

    [Fact]
    public void Sulle_tabelle_allineate_dall_ERP_si_scrivono_solo_posizione_e_attivazione()
    {
        var descriptor = Describe(ArchiveEditPolicy.MasterControlled);

        Assert.True(descriptor.IsWritable(Position, isNewRecord: false));
        Assert.True(descriptor.IsWritable(IsActive, isNewRecord: false));
        Assert.False(descriptor.IsWritable(Description, isNewRecord: false));
        Assert.False(descriptor.IsWritable(Computed, isNewRecord: false));
    }

    [Fact]
    public void In_sola_lettura_non_si_scrive_niente_nemmeno_in_inserimento()
    {
        var descriptor = Describe(ArchiveEditPolicy.ReadOnly);

        Assert.False(descriptor.IsWritable(Description, isNewRecord: false));
        Assert.False(descriptor.IsWritable(Description, isNewRecord: true));
        Assert.False(descriptor.IsWritable(AssignedKey, isNewRecord: true));
    }

    [Fact]
    public void Una_chiave_assegnata_a_mano_si_scrive_solo_in_inserimento()
    {
        var descriptor = Describe(ArchiveEditPolicy.Full);

        Assert.True(descriptor.IsWritable(AssignedKey, isNewRecord: true));
        Assert.False(descriptor.IsWritable(AssignedKey, isNewRecord: false));
    }

    [Fact]
    public void Il_divieto_di_eliminazione_non_tocca_inserimento_e_modifica()
    {
        var descriptor = Describe(ArchiveEditPolicy.Full, preventDelete: true);

        Assert.False(descriptor.AllowsDelete);
        Assert.True(descriptor.AllowsInsert);
        Assert.True(descriptor.AllowsUpdate);
        Assert.True(descriptor.IsWritable(Description, isNewRecord: false));
    }

    [Fact]
    public void L_etichetta_ripiega_sul_nome_del_campo()
    {
        Assert.Equal("Field.Description", Description.ResolvedLabelKey);
        Assert.Equal(
            "Field.IsActive_Master",
            (Computed with { LabelKey = "IsActive_Master" }).ResolvedLabelKey);
    }

    private static ArchiveDescriptor Describe(ArchiveEditPolicy policy, bool preventDelete = false) => new()
    {
        Key = "Prova",
        EntityType = typeof(object),
        Group = ArchiveGroup.MasterData,
        EditPolicy = policy,
        PreventDelete = preventDelete,
        Fields = [AssignedKey, Position, Description, IsActive, Computed],
    };
}
