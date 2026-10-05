namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>The code behind one console command. Runs on the render thread.</summary>
public delegate void ConCommandHandler(in ConArgs args);
