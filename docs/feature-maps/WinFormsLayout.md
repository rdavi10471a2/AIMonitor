# WinForms Layout

## Human Notes

This map captures the AIMonitor WinForms layout rules learned from the Solution Index source-browser work. The goal is not decorative UI; it is dense operator tooling that stays readable, resizable, and predictable when nested controls, splitters, trees, tabs, and status rows interact.

Microsoft's WinForms layout guidance is the baseline:

- Use `TableLayoutPanel` and `FlowLayoutPanel` for dynamic layouts instead of hand-positioning controls when the form must resize or contents can change.
- Use `Dock` and `Anchor` intentionally; docking order and autosizing behavior affect the final layout.
- Set `SplitContainer.Panel1MinSize`, `Panel2MinSize`, `SplitterWidth`, and runtime `SplitterDistance` after layout when real sizes are available.
- Treat automatic scaling and high-DPI behavior as part of layout correctness, not a separate polish pass.

Reference docs:

- https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/walkthrough-arranging-controls-on-windows-forms-using-a-tablelayoutpanel
- https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-dock-and-anchor
- https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.splitcontainer.splitterdistance
- https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.splitcontainer.panel1minsize
- https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.splitcontainer.panel2minsize
- https://learn.microsoft.com/en-us/dotnet/desktop/winforms/forms/autoscale

## AIMonitor Rules

- Prefer one stateful command button for tree expansion. The button should flip between `Expand All` and `Collapse All` based on the current tree state.
- Do not stretch command buttons across a dense tree or reference panel. Put them in a short padded `FlowLayoutPanel` command row with stable button width.
- Do not create command buttons with `Dock = Fill` and then move them into a flow/padded command row. Clear docking or construct them undocked, then set a stable size and anchor.
- Keep explanatory hints short and close to the surface they explain. Use `ToolTip` for dense tree semantics that should not consume permanent screen space.
- Use tabs when two related but directionally different views compete for the same space. For the Solution Index source browser, label file/symbol direction as `Uses` and `Used By`; reserve `reference` for the underlying indexed row concept.
- At file scope, `Uses` can include symbols declared in the same file. This is correct because it means the selected file points at those symbols. `Used By` means source locations that point at symbols declared by the selected item.
- For reference trees, make grouping match the selected node. File/folder/project selections should group by target symbol, then source file, then line. Symbol/member selections should group by source file, then line, because the target symbol is already the selected item.
- Do not depend on designer/default splitter distances. In nested splitters, set panel minimums and calculate the initial splitter distance after layout.
- Give splitters a friendly width in operator tools where users are expected to resize panes repeatedly.
- Avoid adding extra rows without fixed heights or percent ownership. Every row in a nested `TableLayoutPanel` should have an explicit reason to be absolute or percent-sized.

## Manual Verification Checklist

- Resize the main window horizontally and vertically; no command row should crowd the tree or source editor.
- Drag each splitter to minimum and maximum practical positions; both panels should remain usable.
- Collapse and expand the Solution Explorer and reference trees; the stateful toggle label should remain truthful.
- Select a file node and verify `Uses` reads as things the file points at, including same-file symbols where applicable.
- Select a file node and verify `Used By` reads as source locations that point at symbols declared in the file.
- Select a Razor reference and verify source navigation works without promising full Visual Studio-level Razor binding.
