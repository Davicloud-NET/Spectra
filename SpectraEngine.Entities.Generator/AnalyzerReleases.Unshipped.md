; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
; The ids are frozen once they appear here: they travel in build logs and NoWarn
; lists, so a renumber changes somebody's build without anything being edited.

### New Rules

Rule ID | Category | Severity | Notes
--------|-----------------|----------|-------------------------------------------------------------
SPE001  | SpectraEntities | Error    | Entity class must be partial
SPE002  | SpectraEntities | Error    | Duplicate entity class name
SPE003  | SpectraEntities | Error    | Unsupported keyvalue member type
SPE004  | SpectraEntities | Error    | Entity input has the wrong signature
SPE005  | SpectraEntities | Error    | Reserved keyvalue name (targetname IS SceneNode.Name)
SPE006  | SpectraEntities | Error    | Keyvalue type does not match the member
SPE007  | SpectraEntities | Error    | Keyvalue member cannot be assigned
SPE008  | SpectraEntities | Error    | Unknown entity placement
SPE009  | SpectraEntities | Error    | Parameter type no wire carries
SPE010  | SpectraEntities | Error    | Input has no parameter to leave out
SPS001  | SpectraScriptApi | Error   | Script API class must be partial
SPS002  | SpectraScriptApi | Error   | Script API member has no summary
SPS003  | SpectraScriptApi | Error   | Type no script value is made from
SPS004  | SpectraScriptApi | Error   | Text does not read as Luau writes it
SPS005  | SpectraScriptApi | Error   | Member does not take the object
SPS006  | SpectraScriptApi | Error   | Duplicate script API name
SPS007  | SpectraScriptApi | Error   | Property cannot be set through the door
SPS008  | SpectraScriptApi | Error   | Script attribute does not belong here
SPS009  | SpectraScriptApi | Error   | Script API member has the wrong shape
