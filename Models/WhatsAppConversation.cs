using System.ComponentModel.DataAnnotations;
using HaliYikamaAPI.Models.Enums;
using HaliYikamaAPI.Services.WhatsApp;

namespace HaliYikamaAPI.Models;

/// <summary>
/// Kalıcı WhatsApp konuşma durumu (yeniden başlatma ve çoklu sunucu desteği için veritabanında saklanır).
/// </summary>
public class WhatsAppConversation
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    [StringLength(30)]
    public string PhoneNumber { get; set; } = string.Empty;

    [StringLength(100)]
    public string? CustomerName { get; set; }

    [Required]
    public ConversationStep Step { get; set; } = ConversationStep.WaitingServiceChoice;

    public ServiceType? ServiceType { get; set; }

    public int? CarpetCount { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
}
