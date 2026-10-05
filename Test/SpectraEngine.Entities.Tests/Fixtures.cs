namespace SpectraEngine.Entities.Tests;

// Source the generator tests compile. Each diagnostic fixture is wrong in one way.
internal static class Fixtures
{
    // Input of the committed snapshot: one keyvalue of every binding shape,
    // two inputs, two outputs.
    public const string RepresentativeEntity = """
        using SpectraEngine.Core.Entities;
        using System;
        using System.Numerics;

        namespace TestGame.Entities;

        [SpectraEntity("func_door", Display = "Door", Group = "Brush", Placement = EntityPlacement.Brush)]
        public sealed partial class FuncDoor : Entity
        {
            [EntityOutput]
            public const string OnOpened = nameof(OnOpened);

            [EntityOutput]
            public const string OnClosed = nameof(OnClosed);

            [Keyvalue("startopen", Display = "Start open", Default = "0")]
            public bool StartOpen { get; set; }

            [Keyvalue("speed", Display = "Speed", Tooltip = "Units a second.", Default = "100",
                Min = 1f, Max = 1000f, Widget = KeyvalueWidget.Slider)]
            public float Speed { get; set; } = 100f;

            [Keyvalue("glow", Display = "Glow", Default = "1 0.5 0.25", Type = KeyvalueType.Color)]
            public Vector3 Glow { get; set; }

            [Keyvalue("opensound", Display = "Open sound", Type = KeyvalueType.AssetSound)]
            public string OpenSound { get; set; } = "";

            [Keyvalue("linked", Display = "Linked node", Type = KeyvalueType.NodeRef)]
            public Guid Linked { get; set; }

            [EntityInput("Open")]
            private void Open(ref EntityInputContext context) => FireOnOpened(context.Activator);

            [EntityInput("Close")]
            private void Close(ref EntityInputContext context) => FireOnClosed(context.Activator);
        }
        """;

    public const string NotPartial = """
        using SpectraEngine.Core.Entities;

        namespace TestGame.Entities;

        [SpectraEntity("logic_thing")]
        public sealed class LogicThing : Entity
        {
        }
        """;

    public const string DuplicateClassName = """
        using SpectraEngine.Core.Entities;

        namespace TestGame.Entities;

        [SpectraEntity("logic_thing")]
        public sealed partial class FirstThing : Entity
        {
        }

        [SpectraEntity("logic_thing")]
        public sealed partial class SecondThing : Entity
        {
        }
        """;

    public const string UnsupportedKeyvalueType = """
        using SpectraEngine.Core.Entities;
        using System.Collections.Generic;

        namespace TestGame.Entities;

        [SpectraEntity("logic_thing")]
        public sealed partial class LogicThing : Entity
        {
            [Keyvalue("names")]
            public List<string> Names { get; set; } = new();
        }
        """;

    public const string KeyvalueTypeMismatch = """
        using SpectraEngine.Core.Entities;

        namespace TestGame.Entities;

        [SpectraEntity("logic_thing")]
        public sealed partial class LogicThing : Entity
        {
            [Keyvalue("tint", Type = KeyvalueType.Color)]
            public float Tint { get; set; }
        }
        """;

    // Get-only property: nothing for the binder to assign.
    public const string KeyvalueNotAssignable = """
        using SpectraEngine.Core.Entities;

        namespace TestGame.Entities;

        [SpectraEntity("logic_thing")]
        public sealed partial class LogicThing : Entity
        {
            [Keyvalue("speed")]
            public float Speed => 100f;
        }
        """;

    public const string InvalidInputSignature = """
        using SpectraEngine.Core.Entities;

        namespace TestGame.Entities;

        [SpectraEntity("logic_thing")]
        public sealed partial class LogicThing : Entity
        {
            [EntityInput("Trigger")]
            private bool Trigger(string parameter) => true;
        }
        """;

    public const string ReservedKeyvalueName = """
        using SpectraEngine.Core.Entities;

        namespace TestGame.Entities;

        [SpectraEntity("logic_thing")]
        public sealed partial class LogicThing : Entity
        {
            [Keyvalue("targetname")]
            public string Who { get; set; } = "";
        }
        """;

    // A cast is the only way to spell a placement the enum does not have.
    public const string UnknownPlacement = """
        using SpectraEngine.Core.Entities;

        namespace TestGame.Entities;

        [SpectraEntity("trigger_thing", Placement = (EntityPlacement)9)]
        public sealed partial class TriggerThing : Entity
        {
        }
        """;

    public const string CachedEntity = """
        using SpectraEngine.Core.Entities;

        namespace TestGame.Entities;

        [SpectraEntity("logic_cached", Group = "Logic")]
        public sealed partial class LogicCached : Entity
        {
            [EntityOutput]
            public const string OnFired = nameof(OnFired);

            [Keyvalue("count", Display = "Count", Default = "0")]
            public int Count { get; set; }

            [EntityInput("Fire")]
            private void Fire(ref EntityInputContext context) => FireOnFired(context.Activator);
        }
        """;

    // CachedEntity plus a member the generator ignores, appended last so no
    // recorded span moves. The edit must be inside the class: Roslyn reuses
    // an unchanged class node and the transform would not re-run.
    public const string CachedEntityWithSpareMember = """
        using SpectraEngine.Core.Entities;

        namespace TestGame.Entities;

        [SpectraEntity("logic_cached", Group = "Logic")]
        public sealed partial class LogicCached : Entity
        {
            [EntityOutput]
            public const string OnFired = nameof(OnFired);

            [Keyvalue("count", Display = "Count", Default = "0")]
            public int Count { get; set; }

            [EntityInput("Fire")]
            private void Fire(ref EntityInputContext context) => FireOnFired(context.Activator);

            private int Spare() => 1;
        }
        """;

    // CachedEntity with one more keyvalue: the caching tests' control.
    public const string CachedEntityWithExtraKeyvalue = """
        using SpectraEngine.Core.Entities;

        namespace TestGame.Entities;

        [SpectraEntity("logic_cached", Group = "Logic")]
        public sealed partial class LogicCached : Entity
        {
            [EntityOutput]
            public const string OnFired = nameof(OnFired);

            [Keyvalue("count", Display = "Count", Default = "0")]
            public int Count { get; set; }

            [EntityInput("Fire")]
            private void Fire(ref EntityInputContext context) => FireOnFired(context.Activator);

            [Keyvalue("label", Display = "Label")]
            public string Label { get; set; } = "";
        }
        """;

    public const string UnrelatedFile = """
        namespace TestGame.Support;

        internal static class Unrelated
        {
            public static int Value => 1;
        }
        """;

    public const string UnrelatedFileEdited = """
        namespace TestGame.Support;

        internal static class Unrelated
        {
            public static int Value => 2;

            public static int Other => 3;
        }
        """;
}
