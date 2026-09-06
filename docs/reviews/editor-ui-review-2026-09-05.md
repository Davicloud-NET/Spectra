# Spectra editor UI review — 5 September 2026

The editor has a coherent visual foundation, but its space allocation and incomplete authoring workflows make it feel less capable than the engine underneath. The next pass should focus on viewport space, usable asset workflows, and trustworthy feedback. Retain the scene tree, inspector, command infrastructure, and established theme.

This review used a fresh successful build of the current working tree and the running editor. I opened the existing Demo project from Recent projects and inspected the start page, Build and View ribbons, wall and light selections, material browser, Output, and command palette. I also traced the corresponding source. No application source or authored scene content was changed. This was a visual and source review, not a large-scene performance benchmark or a full accessibility audit. Existing working-tree changes were present during the build.

The activities assessed were blockout, changing an existing object, finding and applying an asset, and understanding a broken scene. A first-time user should be able to insert a block, resize it, assign a material, and try the scene using visible controls. A frequent user should reach the same actions through direct manipulation, shortcuts, and searchable commands. Selection should identify the same object in the tree and inspector; an edit should visibly succeed or explain its refusal; one gesture should remain one undo operation.

## 1. Give the viewport substantially more room

**Observed.** The render area was reported as 876 × 442 in an approximately 1482 × 952 captured window: about 28% of the window. Two wide sidebars, the expanded ribbon, several header rows, and a 236-pixel bottom dock consume most of the space. The View ribbon uses only a small portion of its available width while retaining the full ribbon height. Content initially spends its large area on four folder icons. Levels occupies a separate dock for a single level.

**Adjustment.** Make a compact building workspace the default. Keep Move/Rotate/Size, insertion, and snap state immediately available in a shorter command surface. Put view controls beside the viewport, where their effect occurs. Default the bottom dock to a smaller height or a quickly summoned drawer, and offer a one-key viewport maximize/restore action. Keep an expanded ribbon and persistent Content workspace available for users who prefer them. A level selector/document strip can replace the separate Levels dock in the compact workspace.

Existing ribbon collapse and docking are useful foundations; the issue is the initial allocation and the number of adjustments needed to obtain a useful working area. Evaluate the result at both the normal size and the declared 1180 × 640 minimum, plus common display scaling. The smaller sizes were not successfully exercised in this review.

Source: [main layout](D:/Projekte/Spectra/SpectraEngine.Editor/MainWindow.axaml:683), [ribbon host](D:/Projekte/Spectra/SpectraEngine.Editor/MainWindow.axaml:606).

## 2. Complete the material assignment workflow

**Observed and source-confirmed.** Selecting WallNorth exposes Position, Rotation, brush Kind, Operation, and Size. There is no material section. Materials are visible in Content, but asset drops accept only models, and activating a material reveals its file in Explorer. The ordinary task “put this material on that wall” therefore has no complete in-editor route on these surfaces.

**Rework.** Add a material slot with a visual preview, searchable picker, and reveal-in-Content action. Distinguish applying to the whole brush from applying to a selected face. Show the receiving face or object before accepting a material drop. Follow with face alignment, UV scale, rotation, and offset. Every assignment and drag must use the existing undo contract.

This is a larger capability gap than the appearance of the material tiles. Improving thumbnails alone will still leave the task unfinished.

Source: [inspected properties](D:/Projekte/Spectra/SpectraEngine.Core/Inspection/NodeInspector.cs:92), [brush fields](D:/Projekte/Spectra/SpectraEngine.Core/Inspection/NodeInspector.cs:279), [drop policy](D:/Projekte/Spectra/SpectraEngine.Editor/Shell/AssetDropPolicy.cs:62), [asset activation](D:/Projekte/Spectra/SpectraEngine.Editor/MainWindow.axaml.cs:1725).

## 3. Turn Content into an asset browser that supports finding and choosing

**Observed.** Material tiles share one cube icon. Names such as `checker_gray...` and `pbr_plastic_r...` truncate in the fixed narrow tiles, while byte sizes receive their own line. The header offers a path, Up, and Refresh, with no asset search or type filter. Double-clicking a non-folder reveals it in Explorer rather than opening a preview/editor.

**Rework.** Add project-wide search, type filters, clickable breadcrumbs, and a list/grid switch. Offer material and model previews; allow larger tiles or a readable name/details view. Make selection, preview, insertion, and reveal explicit actions with keyboard equivalents. Keep “Show in Explorer” as a context action.

There is also a concrete inconsistency: model double-click reports that placement is not built yet, although `OnViewportAssetDropped` calls `InsertModel`. Native viewports have a separate drag limitation and currently explain it through a launch argument. Supply an Insert action that works through the appropriate supported path, and make capability messages reflect the actual session.

**Source-only scale concern.** The current browser uses a nonvirtualized ItemsControl/WrapPanel, enumerates the folder synchronously, and starts a thumbnail task for every texture. Virtualize the result view and bound thumbnail work before targeting large asset libraries. This is an architectural concern, not a measured latency claim.

Source: [tiles](D:/Projekte/Spectra/SpectraEngine.Editor/Shell/ContentPanel.axaml:44), [enumeration and thumbnails](D:/Projekte/Spectra/SpectraEngine.Editor/Shell/ContentBrowserModel.cs:257), [activation](D:/Projekte/Spectra/SpectraEngine.Editor/MainWindow.axaml.cs:1725), [placement path](D:/Projekte/Spectra/SpectraEngine.Editor/MainWindow.axaml.cs:1929).

## 4. Make diagnostics describe the actual scene state

**Observed.** Many objects displayed magenta checkerboards, but Output said “no problems” and contained only the opening messages. The cause of those rendered checkerboards was not isolated in this UI review and may involve the loaded content or current engine changes.

**Confirmed feedback gap.** Engine logging goes to Serilog's console, debug, and file sinks. The Output collection is populated separately through shell messages and selected operation reports. It is not a complete view of asset/engine warnings. Furthermore, its 500-entry cap removes old warnings and decrements the problem count, so enough later informational messages can turn the summary back into “no problems” without resolving anything.

**Rework.** Route actionable asset, map, shader, and engine diagnostics into a Problems view with object/asset links, severity filters, and a persistent count visible when the panel is closed. Deduplicate repeated failures. Keep active problems separate from bounded log history. Until that exists, describe the current panel accurately as shell output rather than making a broad health claim.

Source: [logger sinks](D:/Projekte/Spectra/SpectraEngine.Editor/Program.cs:24), [shell reporting](D:/Projekte/Spectra/SpectraEngine.Editor/Shell/ShellModel.cs:1127), [summary and eviction](D:/Projekte/Spectra/SpectraEngine.Editor/Shell/OutputLog.cs:118).

## 5. Finish the inspector's input feedback and specialized editors

**Observed.** The light color control is a swatch beside a hex field. The swatch is a Border with a tooltip, not a color picker. Choosing a warmer or less saturated light requires knowing a color code or leaving the editor.

**Source-confirmed.** Invalid numeric, vector, and hex input calls `Revert()` without an inline validation message. The underlying state remains safe, but a user cannot tell why their entry disappeared. Entity Outputs provides an output dropdown, while Target and Input remain free text; the source explicitly defers target picking and cross-entity input validation.

**Adjustment.** Make the color swatch open a picker with live preview and one undo transaction. Mark invalid drafts beside the field and explain the accepted format/range while preserving the scene's last valid value. For wiring, provide a searchable target picker and target-dependent input choices, retaining a deliberate free-text route for unresolved or unknown classes. Use “Forever” as the visible label for the supported unlimited count.

Preserve the good existing behavior: focused fields resist snapshot refreshes, edits commit on Enter/blur, Escape reverts, and mixed vector components remain independent.

Source: [color control](D:/Projekte/Spectra/SpectraEngine.Editor/Shell/PropertiesPanel.axaml:412), [parse rejection](D:/Projekte/Spectra/SpectraEngine.Editor/Shell/PropertyPanelModel.cs:430), [field contract](D:/Projekte/Spectra/SpectraEngine.Editor/Shell/PropertyFieldModel.cs:136), [wiring fields](D:/Projekte/Spectra/SpectraEngine.Editor/Shell/EntityWiringModel.cs:77).

## 6. Simplify the vocabulary and teach the gesture in context

**Observed.** A selected Block becomes Kind = World and Operation = Additive in the inspector. The ribbon also presents Part, Cut, Panel, Handles = Studio, Axes = world, and `su`. Their relationships require prior engine knowledge. Camera mode appears both above and below the viewport, while the bottom bar spends room on node count, compiles, viewport dimensions, and frame rate.

**Adjustment.** Use the same display names across insertion, selection headers, inspector choices, and commands, with short explanations where the distinction matters. For example, explain the static-world role of a block and the independent-object role of a part at creation and conversion. Label Panel as a surface light where space permits. Put handle style and backend/navigation alternatives in preferences or an advanced view menu.

Use the status bar for current gestures: select, add to selection, orbit, freelook, snap override, and cancel. Put detailed engine counters behind a diagnostics toggle. Make instructional hints dismissible and remember dismissal.

Source: [ribbon vocabulary](D:/Projekte/Spectra/SpectraEngine.Editor/Shell/Ribbon/RibbonLayout.cs:240), [brush terminology](D:/Projekte/Spectra/SpectraEngine.Core/Inspection/NodeInspector.cs:279), [status content](D:/Projekte/Spectra/SpectraEngine.Editor/MainWindow.axaml:1103).

## 7. Add precision view controls for blockout work

**Observed and source-confirmed.** View offers framing, ground grid modes, and debug overlays. There is no visible perspective/top/front/side selector; the current Camera constructs a perspective projection. The orientation widget gives orientation feedback but does not provide the complete precision-view workflow.

**Rework.** Add orthographic top/front/side views, a clear view selector, and viewport maximize/restore. Consider a four-view workspace after the individual orthographic views work. Keep these controls adjacent to the scene. This is a capability addition for the intended level-design workflow, not a small theme adjustment.

Source: [view commands](D:/Projekte/Spectra/SpectraEngine.Editor/Shell/Ribbon/RibbonLayout.cs:320), [projection](D:/Projekte/Spectra/SpectraEngine.Core/Scene/Camera.cs:151).

## 8. Finish command discovery and recent-project identification

**Observed and source-confirmed.** Ctrl+P opens a working palette with shortcut labels. Its roster covers editing and viewport commands but omits project open/save, Play, and panel/layout actions. An empty query shows twelve alphabetically ranked commands without indicating that more exist. Recent projects includes many identical Demo names; the path's identifying final directory is ellipsized away, although a tooltip provides the full path.

**Adjustment.** Include the application-level commands in the same registry, add useful search aliases, and make result limits apparent. Give recent projects a readable final folder name or disambiguating path suffix, preserving full paths in tooltips. These are relatively small improvements to two already useful surfaces.

Source: [palette roster/search](D:/Projekte/Spectra/SpectraEngine.Editor/Shell/CommandTable.cs:54), [recent-project columns](D:/Projekte/Spectra/SpectraEngine.Editor/Shell/StartPage.axaml:252).

## Visual direction and order of work

Keep the dark warm-gray palette, restrained red selection, amber tool state, colored object glyphs, and compact inspector grouping. The tree selection and inspector identity read clearly. The largest visual weakness is the allocation of empty space around a small scene, followed by tiny truncated asset labels and the large brightness jump from dark chrome to the blue viewport background. Evaluate a quieter editor background and stronger gizmo contrast separately from runtime sky rendering; no contrast-compliance measurement was performed here.

1. Repair misleading diagnostics and activation messages; improve inspector rejection feedback and add the color picker.
2. Deliver the compact workspace and viewport maximize/restore, preserving the expanded workspace as an option.
3. Complete material assignment together with asset search, previews, and explicit actions.
4. Add orthographic views and schema-assisted wiring.
5. Refine typography, secondary text, and icon balance against the resulting layout.

The application build passed with three existing obsolete-API warnings. No automated tests were added or run for this review, and no claim is made about large-library performance, all display scales, play/stop restoration, or drag-placement correctness across both viewport implementations.
