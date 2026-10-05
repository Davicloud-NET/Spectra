namespace OpenAlFilterSpike.Checks;

/// <summary>Something done to a playing source between two render calls.</summary>
internal delegate void Change(Rig rig, uint source, uint filter);
