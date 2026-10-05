using System.Collections.Generic;
using SpectraEngine.Core.Entities;

namespace Spectra.Kitchen.Tests;

// Logs every keyvalue it is offered, so a test can compare what two entity
// worlds parsed and in what order.
internal sealed class KeyvalueLogEntity(List<string> log) : Entity
{
    public override bool ParseKeyValue(string key, string value)
    {
        log.Add($"{TargetName}: {key} = {value}");
        return true;
    }
}
