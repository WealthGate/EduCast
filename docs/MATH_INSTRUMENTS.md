# On-board math and physics instruments

Choose **Instruments…** above the board and select a tool. An instrument appears over the lesson. The handles sit outside the measuring faces. Drag the instrument body or its **+** handle to move it, use **↻** to rotate, and use **↔** where available to resize. Hold **Shift** while moving for board-grid alignment or while rotating for 15° steps. Switch to **Pen** to write freehand around the instrument. Select **Use tool** to return to manipulation. **Esc** cancels the active adjustment or trace.

## Demonstrate to students

Choose **Show on Program** for the active instrument. The student display, PNG export, and recording use the instrument's clean view. Handles, calibration lines, keypad, and history stay in the teacher workspace. In manual mode, use **Take preview** after adjusting an already published instrument. Freeze holds its current pose. Clearing the checkbox or removing the tool hides it immediately, including while frozen. Blackout covers all Program layers.

The instrument stays on the board while you use it. Completed constructions become editable lesson objects, follow the normal object visibility and answer settings, and can be undone. Use **Remove tool** to clear the temporary instrument.

## Draw and measure

| Instrument | Classroom workflow |
| --- | --- |
| Ruler | Align the zero tick with the starting point. The small pointer above the ruler marks zero. Enable **Trace** and drag near the straight edge. Hold **Shift** while tracing to snap to the main scale marks. Select **Label length** to add a measurement to the line. **Inches** switches the scale between metric and imperial units |
| Ruler calibration | Enter a known length in the selected unit, select **Calibrate…**, and drag between the reference endpoints on the board. Calibration applies to the current session. The default scale is one centimetre per 32 board units, which matches the grid spacing. Use **Reset scale** to restore it |
| Compass | Place the pointed pivot on the starting point. With **Trace** off, drag the pointed pencil tip onto the second point. The pivot stays fixed as the radius and direction change. **Draw circle** uses that radius. To draw an arc, enable **Trace** and sweep from the dashed guide around the pivot. Moving the compass keeps its radius |
| 180° or 360° protractor | Align the open center with the vertex and rotate the baseline into position. Drag the **∠** handle beyond the scale to move the measuring ray. Hold **Shift** for 15° angle steps. **Draw angle** commits both rays, the arc, and the degree label together |
| 45° or 30°/60° set square | Move and rotate the triangle to align an edge. Enable **Trace** and drag near any edge for a constrained line. **Draw triangle** commits all three edges together |
| French curve | Move the vertex, use **↔** to change the span, and use **±** to change or flip the rise. The guide follows a quadratic curve in the instrument's local coordinates. Enable **Trace** to draw part of it or choose **Draw full curve**. Use the separate **Graph…** tool for function plots with axes |

Calibrated lengths refer to the board reference. The displayed physical size varies with zoom, display scaling, and projector placement.

## Vernier caliper and micrometer

Open either instrument from **Instruments…** or the **Physics** page’s tool list. Drag the body to move it and the **↻** handle to rotate it. The two contact marks show where the instrument is measuring. **Measure board points…** aligns those marks with the two endpoints you drag between and rounds the distance to the instrument’s graduation. It uses the ruler’s board calibration.

- **Vernier caliper** measures from 0 to 150 mm in steps of 0.02 mm. Drag the sliding carriage. Use **Outside** for the large jaws, **Inside** for the small jaws, and **Depth** for the rear rod. Read the last whole millimetre before vernier zero, then add the number of the matching vernier division multiplied by 0.02 mm. The illustrated scale has 50 vernier divisions across 49 mm. [Mitutoyo caliper scale](https://www.mitutoyo.com/webfoo/wp-content/uploads/E12029-Calipers.pdf)
- **Micrometer screw gauge** measures from 0 to 25 mm in steps of 0.01 mm. Drag the thimble upward to open and downward to close. A full turn moves the spindle 0.50 mm. Read the last exposed whole or half millimetre on the sleeve, then add the thimble division at the index line multiplied by 0.01 mm. [Mitutoyo micrometer guide](https://www.mitutoyo.com/webfoo/wp-content/uploads/E11003-Quick-Guide.pdf)

Hold **Shift** during adjustment for a finer drag. Scroll over the carriage or thimble, or use **−** and **+**, for one graduation. **Shift** with the wheel steps 1 mm on the caliper or one full turn on the micrometer. Pick a practice specimen and adjust until the measuring faces contact it. **Close to specimen** or **Advance to contact** seats the tool in one step. The simulation assumes a correct zero and stops at ideal contact. [NPL guide to callipers and micrometers](https://eprintspublications.npl.co.uk/2043/)

Keep **Scale close-up** on and turn **Show reading** off to have students read the graduations themselves. Turning off **Show reading** also hides the calculated answer from a frozen Program view. Use **Add reading…** and click the board to place the calculation as a lesson label. The label follows the board’s visibility and answer settings.

## Calculator

Choose **Calculator** to place a movable readout. Use the expression field or enable the optional keypad. Try `(3/4)^2`, `sqrt(81)`, or `sin(30)` in degree mode. Clear **Degrees** for radian trigonometry. **Evaluate** or **Enter** updates the readout. Select a recent calculation to put its expression back in the input field.

**Show on Program** shares the readout with the most recently evaluated expression and result. Students can follow the calculation while the teacher enters the next expression privately.

OpenBoard's documented on-board instruments informed the placement and manipulation workflow. [OpenBoard documentation](https://www.openboard.ch/en/documentation)
