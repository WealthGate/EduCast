# Classroom display and teacher guide

## Prepare the room

1. Plug in the classroom display and press **Win+P**, then select **Extend**.
2. Open Windows **Settings → System → Display** and use **Identify** to match display numbers with the physical screens.
3. Keep the teacher workspace on the laptop. In EduCast, select the projector by its display number and resolution, then choose **Start display**.
4. Choose **16:9** or **4:3** under Program. The lesson keeps its drawing proportions in both modes. Windows controls the physical display resolution.
5. Use **Windowed output** to open the clean Program window on a one-screen setup. When sharing through a meeting client, share that window explicitly.

## Teach

- **Pen** and **Highlighter** use mouse, touch or Windows ink input. Color, thickness, smoothing and visibility are set at the start of the stroke. In **Live updates**, approved ink appears as you write. Press **Esc** to cancel an active stroke. Freeze, manual Take and PNG export use committed strokes.
- Use the board's zoom buttons or **Ctrl+wheel** to zoom the teacher view. Drag with the middle mouse button to pan a zoomed board.
- **Ruler line** draws straight between drag endpoints. Hold **Shift** for 15° increments. **Compass circle** uses the drag length as radius. **Compass arc** uses the first drag to set center and radius, then a click to set the end. Hold **Shift** at the end for a counterclockwise sweep.
- In the equation editor, try `x^2 + 5x + 6 = 0`, `(a+b)/(c+d)`, `sqrt(x^2+y^2)`, `H_2SO_4`, or `matrix(1,2;3,4)`. Choose **Place on board**, then click the board. Use the object list and **Edit…** to revise a placed equation, text or graph. The graph editor accepts an expression in `x`, with optional `y =` and radian trigonometry.
- Use **Select** or the object list to change an existing object's visibility. To move a visible object, drag it with **Select**. Program receives the move after release in Live updates or after Take in manual mode. With nothing selected the visibility controls apply to the next object. **Teacher only** keeps an object private, **Student only** shows it only on Program, and **Hidden** excludes it from both rendered views. The object list remains available to the teacher in all four states.
- Mark objects **Answer** and use **Reveal answers** or **Hide answers** for the page. In manual mode, take the draft after revealing. Hiding an answer removes it from existing Program and held frames immediately.
- **Save** preserves editable objects and teacher notes. **PNG…** exports only the current clean Program frame, including freeze or blackout state.

| Shortcut | Action |
| --- | --- |
| F5 / Shift+F5 | Start or move student output / stop output |
| F6 | Freeze or resume |
| F7 | Toggle blackout |
| F8 | Take preview |
| Alt+Left / Alt+Right | Previous or next page |
| Ctrl+N / Ctrl+O / Ctrl+S | New, open, or save a lesson |
| Ctrl+Z / Ctrl+Y | Undo or redo a board edit outside text fields |
| Esc | Cancel an active stroke, object drag, geometry or placement action |

## Physical verification checklist

| Scenario | Expected result |
| --- | --- |
| Connect a projector in extended mode, including one positioned left of the laptop in Settings | The selector lists it and the student window occupies the chosen monitor while the teacher stays on the laptop |
| Test 100% and 150% scaling on different displays | Output fits its monitor, with accurate proportions and clean letterboxing |
| Write with a mouse and stylus, then draw a straight line | Approved ink appears in Program during input in Live updates. A light-to-heavy stylus stroke varies in width and stays consistent after pen-up. The line appears on release |
| Select and drag an object while Program is visible | Teacher sees the drag preview. Program shows the previous position until release |
| Show the equation dialog and edit private notes with the projector active | The projector keeps showing only the approved composition |
| Freeze, prepare a draft, revoke an existing answer, then resume | Draft edits stay held, the revoked answer disappears immediately, and the current Program frame resumes correctly |
| Unplug the selected output while writing, then reconnect | Student window closes and project remains intact; select the projector and start output again |
| Save, reopen, and export PNG | Project restores editable data; the PNG contains only the Program composition |
| Verify closed-room workload | Record CPU, GPU, working set and measured ink latency on the actual computer during idle, pen and classroom output workloads |

Record the display models, driver and Windows version, projector connection, scale factors and stylus model with each hardware result.
