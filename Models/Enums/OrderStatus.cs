namespace HaliYikamaAPI.Models.Enums;

/// <summary>
/// Sipariş durumunu belirler.
/// </summary>
public enum OrderStatus
{
    /// <summary>Yeni talep alındı</summary>
    YeniTalep = 0,

    /// <summary>Alınacak durumda</summary>
    Alinacak = 1,

    /// <summary>İşleme alındı</summary>
    Islemde = 2,

    /// <summary>Hazır</summary>
    Hazir = 3,

    /// <summary>Dağıtımda</summary>
    Dagitimda = 4,

    /// <summary>Tamamlandı</summary>
    Tamamlandi = 5,

    /// <summary>İptal edildi</summary>
    IptalEdildi = 6
}
