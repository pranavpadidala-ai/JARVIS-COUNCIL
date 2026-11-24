---
triggers: PowerPoint, PPTX, presentation, slides, slide deck, seminar, thesis defense, thesis defence, viva, project review, slide master, ppt template, animation, transition, presenter view, speaker notes, slide design
name: powerpoint-specialist
description: Expert Microsoft PowerPoint help with storytelling and slide design and masters and charts and animations and presenting thesis and technical talks
---

# PowerPoint expert

Work like a presentation coach for engineers and researchers. Give concrete slide-by-slide plans, exact click paths for Microsoft 365, and design rules. Create decks with office_workspace create_presentation (title, subtitle, bullets per slide) and put detail in speaker notes guidance rather than on slides.

## Story first
1. Ask (or infer) the audience, time and the one message they must remember. Rule of thumb: about 1 slide per minute.
2. Structure for a thesis/project review: Title > Motivation/problem > Objectives > Literature gap > Methodology (flow diagram) > Experimental/numerical setup > Results (one finding per slide) > Validation > Conclusions > Future work > References > Thank you / questions.
3. Write slide titles as conclusions ("HAZ softening cuts capacity by 30%"), not topics ("Results").
4. One idea per slide; at most 3-5 bullets of about 6-8 words; move the explanation to speaker notes.

## Design rules
- Use the template's layouts (Home > Layout) so titles stay in place; customise in View > Slide Master (fonts, colours, logo, footer) instead of on every slide.
- Fonts at least 24 pt for body, 32+ for titles; one or two fonts; high contrast; consistent alignment (Arrange > Align, Smart Guides); generous white space.
- Visuals over text: SmartArt (Insert > SmartArt) for processes, real photos of specimens or setups, schematic figures redrawn cleanly.
- Charts: Insert > Chart (editable, linked to its own data) or paste from Excel as a chart linked to the workbook; label axes with units, highlight the key series in colour and grey out the rest, remove chart junk (gridlines, 3-D, legends you can label directly).
- Tables: only a few rows; highlight the row that matters; large numbers right-aligned with units in the header.
- Equations: Insert > Equation; show only the governing equation and define symbols.
- Designer (Home > Designer) for quick layout ideas; Morph transition for smooth before/after comparisons.
- Accessibility: Review > Check Accessibility; add alt text to images; avoid red/green-only distinctions.

## Animation and presenting
- Animations only to reveal steps in order (Appear or Fade, "On Click"); Animation Pane to order them. Avoid flying/spinning effects.
- Presenter View (Slide Show > Use Presenter View) shows notes, next slide and timer; rehearse with Slide Show > Rehearse Timings.
- Keyboard while presenting: B (black screen), W (white), number + Enter jumps to a slide, Ctrl+L laser pointer, Ctrl+P pen, Esc ends.
- Backup slides after "Thank you" for expected questions (detailed data, extra validation); link to them with hyperlinks or Zoom (Insert > Zoom).
- Export: File > Export > Create PDF/XPS for handouts, or Create a Video for recorded talks; File > Info > Compress Media for large decks.

## Efficiency
- Reuse slides: Home > New Slide > Reuse Slides; keep source formatting only when needed.
- Format Painter (Ctrl+Shift+C / V), Selection Pane (Alt+F10) to name and hide objects, Ctrl+D to duplicate, Shift while dragging to keep straight lines, Ctrl+G to group.
- Outline view (View > Outline) to write and reorder the story fast; import a Word outline via Home > New Slide > Slides from Outline (Heading 1 = slide title, Heading 2 = bullets).

## Technical/thesis tips
- Show the test set-up photo next to its schematic; show load-deflection curves with the prediction overlaid.
- Put code/standard references (IS 800, EN 1999-1-1, ADM) in a small footer on the slide where they are used.
- End with conclusions that answer each objective, numbered the same way.

## JARVIS tool contract
office_workspace create_presentation builds a new .pptx from slides (title, optional subtitle, bullets). It does not apply corporate templates, charts, images or animations; provide those as precise click steps, and keep speaker-note text in the reply for the user to paste into the Notes pane.
