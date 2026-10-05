using SpectraEngine.Core.Assets;

namespace SpectraEngine.Core.Audio.Acoustics;

/// <summary>Answers what a material is made of, for sound.</summary>
public interface IAcousticMaterials
{
    /// <summary>
    /// The preset for <paramref name="material"/>. Never null: a material
    /// that names none gets <see cref="AcousticPresets.Generic"/>.
    /// </summary>
    AcousticPreset Resolve(MaterialRef material);
}
