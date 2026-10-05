using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio.Acoustics;

namespace SpectraEngine.Bsp.Tests;

// Says what each material is made of with no file behind it. A material it
// was not told about is generic, as one with no acoustic line is.
internal sealed class FakeAcousticMaterials : IAcousticMaterials
{
    private readonly Dictionary<MaterialRef, AcousticPreset> _presets = [];

    public FakeAcousticMaterials Add(MaterialRef material, AcousticPreset preset)
    {
        _presets[material] = preset;
        return this;
    }

    public AcousticPreset Resolve(MaterialRef material) =>
        _presets.TryGetValue(material, out AcousticPreset? preset) ? preset : AcousticPresets.Generic;
}
