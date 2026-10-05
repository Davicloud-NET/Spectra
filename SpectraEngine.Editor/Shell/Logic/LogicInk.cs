namespace SpectraEngine.Editor.Shell.Logic;

// The ways the graph's text is set: a face, a size and a colour each.
internal enum LogicInk
{
    // A card's name.
    Name,

    // The name on the card of a target nothing answers to.
    StubName,

    // The name on the activator's card.
    QuietName,

    // The class under a name.
    ClassLine,

    // What is wrong with a target, under its name.
    StubLine,

    // A wired input or output.
    Port,

    // An input or output the class declares and nothing is wired to.
    QuietPort,

    // An input or output a wire names and the class does not have.
    WrongPort,

    // The line about outputs a card does not list.
    Note,

    // What a running entity's state line is about.
    StateLabel,

    // The value on that line.
    StateValue,

    // A wire's label.
    Label,

    // A label that carries a parameter.
    MonoLabel,

    // The label of a wire that is firing or waiting.
    LitLabel,

    // The label of a wire that cannot deliver.
    BrokenLabel,

    // The name on a card seen from far out.
    FarName,

    // The same on the card of a target nothing answers to.
    FarStubName,
}
