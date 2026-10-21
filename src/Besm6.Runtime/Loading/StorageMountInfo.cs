namespace Besm6.Runtime.Loading;

/// <summary>Read-only description of a mounted hosted disk or drum.</summary>
public sealed record StorageMountInfo(string Kind, int Unit, long VolumeId, bool ReadOnly, int Words);
