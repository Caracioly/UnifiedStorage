namespace UnifiedStorage.Mod.Domain;

public enum StorageScanContext
{
    Unknown = 0,
    SessionRefresh = 1,
    AuthoritySnapshot = 2,
    HoverPreview = 3,
    InteractCheck = 4,
    NearbyCount = 5
}
