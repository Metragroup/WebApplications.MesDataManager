using MesDataManager.Domain.Abstractions;

namespace MesDataManager.Domain.Entities;

/// <summary>Destinatari delle notifiche e-mail (MasterData.EmailRecipient).</summary>
public sealed class EmailRecipient : IActivatable
{
    public int EmailRecipientId { get; set; }
    public string MessageType { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Email { get; set; }
    public bool IsActive { get; set; }
}
